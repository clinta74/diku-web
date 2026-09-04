# API Security Review

> Status: findings for evaluation, 2026-09-03. Branch `harden-api`. Nothing here has been
> changed in code; every item cites the line it was read from so it can be re-verified.
>
> Deployment facts confirmed by the operator after the first draft: an HTTPS proxy (Nginx
> Proxy Manager, on its own server) fronts the beta and forwards only to port 7180; ports
> 5434 and 3000 are LAN-only. A1, A2 and A3 were revised on that basis.

Scope: the HTTP surface of `Muwbta.Server` (auth, characters, game, builder, admin, assist,
operator routes), the deployment in front of it (nginx, compose, the beta NAS), and the
in-game verbs a player can reach. Four questions were asked — can the server be made to
reveal passwords, can one player get into another's account, can a ban be bypassed, can the
database be exploited — plus social engineering.

The short version: **the code is in good shape and most of the risk is in the deployment
around it.** Authorization is checked on every route that needs it, ownership is checked on
every character-scoped route, the database is reached only through parameterized EF Core,
passwords are hashed with PBKDF2 and never serialized, and the client renders text without
HTML. What lets the code down is one missing middleware, a proxy hop that overwrites the
header the middleware would need, and a game that gives a stranger everything they need to
pass as staff.

## Severity

| | Meaning |
|---|---|
| **High** | Exploitable today by an unauthenticated stranger, or defeats a control the design relies on |
| **Medium** | Exploitable with a normal account, or a missing control with a realistic path to harm |
| **Low** | Real, narrow, or needs a second weakness to matter |
| **Info** | Verified sound; recorded so nobody re-audits it |

## Do this first — done

**Confirm who owns the first account on beta.** The database was wiped and redeployed
today. [AuthEndpoints.cs:106](../src/Muwbta.Server/Auth/AuthEndpoints.cs) promotes the
first registration on an empty database to Admin — by design, so a fresh install needs no
SQL. If the beta URL was reachable before you registered, the first account might not be
yours. One query settles it:

```sql
select username, role, created_at from accounts order by created_at limit 3;
```

**Confirmed by the operator on 2026-09-03: the first account is theirs.** Kept here because
it is the check to repeat after any future wipe; C1 is the fix that makes it unnecessary.

---

## A. Transport and deployment

### A1 — Forwarded headers are not honoured — **High**

nginx sets `X-Forwarded-For` and `X-Forwarded-Proto` on every proxied request
([nginx.conf.template:119-122](../client/nginx.conf.template)). Kestrel never reads them:
there is no `UseForwardedHeaders()` anywhere in
[Program.cs](../src/Muwbta.Server/Program.cs), and the deployment note at line 248 says so.
So behind the front end, `HttpContext.Connection.RemoteIpAddress` is always the nginx
container and the request scheme is always `http`. Three controls depend on those two
values:

1. **The auth rate limiter is a site-wide cap, not a per-visitor one.**
   [RateLimiting.cs](../src/Muwbta.Server/Infrastructure/RateLimiting.cs) partitions
   `/register`, `/login` and `/password` by address, ten per minute. With one address for
   everybody, **ten requests a minute from anyone locks every player out of signing in,
   registering, and changing their password.** That is a denial of service that costs an
   attacker one `curl` in a loop.

2. **The session cookie is never marked `Secure`.** See A2.

3. **Moderation has no IP.** Nothing can record where an account registered or logged in
   from, so a banned player's second account cannot be correlated with their first. See D2.

**There are two hops, and the inner one undoes the outer.** Nginx Proxy Manager
terminates TLS and, by its stock template, sends `X-Forwarded-For`, `X-Forwarded-Proto:
https` and `X-Real-IP` to port 7180. The compose nginx then does this
([nginx.conf.template:119-122](../client/nginx.conf.template)):

```nginx
proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;   # appends - fine
proxy_set_header X-Forwarded-Proto $scheme;                      # overwrites with "http"
```

`$scheme` on that hop is `http`, so NPM's `https` is replaced before Kestrel ever sees it.
Even with the middleware below in place, the request would still read as HTTP and the
cookie would still not be `Secure` (A2). Nothing needs changing on NPM for this; the fix
is entirely on this side.

**Fix, three parts.**

1. Make the NPM → 7180 leg TLS, so `$scheme` on that hop is `https` and the existing
   `X-Forwarded-Proto $scheme` sends the right value unchanged. NPM does not verify
   upstream certificates, so nothing needs signing or managing: a script in
   `/docker-entrypoint.d/` self-signs a throwaway certificate at container start unless one
   is mounted, the server block adds `listen 443 ssl` beside the existing `listen 80` (kept
   for the healthcheck and the dev stack), and the compose publishes `7180:443`. NPM's proxy
   host switches to scheme `https`.

   What this buys: the LAN leg is encrypted against passive listening. What it does not
   buy: authentication of either end, or any restriction on who may connect to 7180 — NPM
   accepts any certificate, and any LAN host can still reach the port. Step 2's known-proxy
   list therefore remains the only lock on header spoofing. If a real lock is wanted later,
   a private CA (two `openssl` commands; both ends are yours, so no public CA is involved)
   lets the inner nginx require NPM's client certificate and makes 7180 unreachable to
   anything else; NPM takes `proxy_ssl_certificate` in a proxy host's Advanced tab.

   The plain-HTTP alternative, if the TLS leg is deferred, is a passthrough map:

   ```nginx
   map $http_x_forwarded_proto $fwd_proto { default $scheme; https https; }
   proxy_set_header X-Forwarded-Proto $fwd_proto;   # in both proxy locations
   ```

2. Kestrel honours the headers, trusting exactly the two hops. `X-Forwarded-For` arrives
   as `client, <npm>` — the rightmost entry is what compose nginx appended — so the walk
   has to be allowed two steps, and NPM's address has to be known or the walk stops at it
   and every player still shares one address:

   ```csharp
   builder.Services.Configure<ForwardedHeadersOptions>(o =>
   {
       o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
       o.ForwardLimit = 2;
       o.KnownNetworks.Clear();
       o.KnownProxies.Clear();
       o.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("172.25.0.0"), 16)); // muwbta-network
       o.KnownProxies.Add(IPAddress.Parse(config["Proxy:NpmAddress"]!));         // the NPM host
   });
   // ...
   app.UseForwardedHeaders();
   app.UseAuthentication();
   ```

   The pinning is not optional. Honouring the header from *any* source is worse than
   ignoring it: a caller could then set a fresh `X-Forwarded-For` per request and the auth
   limiter would never fire at all. Port 7180 is reachable on the LAN, so a LAN caller
   bypassing NPM is exactly the case the known list exists for.

3. HSTS, on NPM: it is a toggle on the proxy host's SSL tab, and that is where a
   TLS-terminating header belongs.

### A2 — The session cookie is never `Secure` — **Medium**

TLS terminates at Nginx Proxy Manager, so passwords and cookies are not crossing the
network in cleartext — that half of the first draft is withdrawn. What remains is the flag.

The cookie is issued with `CookieSecurePolicy.SameAsRequest`
([Program.cs:149](../src/Muwbta.Server/Program.cs)). The comment says that "still sets
Secure in production", but the request reaches Kestrel as HTTP twice over — no forwarded
headers are read, and the one that would say `https` is overwritten on the way in (A1) —
so the policy resolves to "not secure" on this deployment and on every deployment shaped
like it. A cookie without `Secure` is offered on any plain-HTTP request to the same host.
With HSTS on and nothing but 443 exposed that is a narrow window, which is why this is
Medium rather than High; it is still a flag that should be on and is not.

**Fix.** A1's proto passthrough makes `SameAsRequest` correct. The safer setting in
Production does not depend on the chain being right:

```csharp
options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;
```

The cookie is already `HttpOnly` and `SameSite=Lax`, which is right.

### A3 — Postgres and Grafana are published on the NAS host — **Low**

`5434:5432` and `3000:3000`
([docker-compose.truenas.beta.yml:153, :88](../tmp/docker-compose.truenas.beta.yml)) bind
to every interface on the NAS. Confirmed LAN-only, both password-protected, and — the
correction from the first draft — *reachable from the LAN on purpose*: the operator works
on Grafana and the database from other machines on it. Binding to loopback, as first
suggested, would have removed exactly that access, and the prod example's "SSH tunnel"
note describes a different operating model from this one.

**Fix.** Grafana stays as it is. The Postgres publish is the one worth removing, and only
once the backup exports are trusted enough that nobody needs `psql` from outside the
compose network — the operator's call and timing. Until then, it is a password-protected
port on a private LAN, which is what Low means.

### A4 — Security headers — **Low**

nginx sends `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy` and the obsolete
`X-XSS-Protection`. Missing: a `Content-Security-Policy` and (once TLS exists)
`Strict-Transport-Security`. The client loads nothing cross-origin, so a strict CSP is
cheap: `default-src 'self'; img-src 'self' data:; connect-src 'self'` covers it and turns
the "no HTML injection" property in F7 from a code review finding into an enforced one.
Drop `X-XSS-Protection`; modern browsers ignore it and older ones misbehaved with it on.

---

## B. Revealing passwords

**Verified sound:** hashes are ASP.NET Identity's `PasswordHasher` (PBKDF2-HMAC-SHA256);
`AccountResponse` and `AccountSummary` never carry the hash; no log message takes a password
or a cookie ([ServerLog.cs](../src/Muwbta.Server/ServerLog.cs)); `/health/ready` omits
exception text by design; nothing in the repo is a live secret — the example files carry
`change_this_password_in_production` and the beta file is untracked. Changing a password
requires the current one and evicts every other session
([AuthEndpoints.cs:175-220](../src/Muwbta.Server/Auth/AuthEndpoints.cs)); an admin reset
does the same through `PasswordChangedAt`.

### B1 — Registration does not use `PasswordPolicy` — **Low**

[PasswordPolicy.cs](../src/Muwbta.Server/Auth/PasswordPolicy.cs) exists so that three
surfaces agree, and its own comment says "a rule enforced at only two of them is not a
rule". Registration is the one that does not call it
([AuthEndpoints.cs:85](../src/Muwbta.Server/Auth/AuthEndpoints.cs)): it has a private
`MinPasswordLength` and no maximum at all. The maximum is the part that matters — the policy
comment explains it bounds PBKDF2 CPU per request, and registration is the surface a
stranger can reach. Bounded in practice by nginx's 20 MB body cap, which is not the bound
anyone intended.

**Fix.** Replace the inline check with `PasswordPolicy.IsAcceptable`.

### B2 — Login leaks whether a username exists, by timing — **Low**

[AuthEndpoints.cs:146](../src/Muwbta.Server/Auth/AuthEndpoints.cs) returns the same 401
for "no such user" and "wrong password", and the comment says that is to prevent
enumeration. But the `account is null ||` short-circuits *before* `VerifyHashedPassword`,
so an unknown name answers in microseconds and a known one after ~100 ms of PBKDF2. The
difference is measurable from across the internet.

**Fix.** When the account is missing, verify the supplied password against a fixed dummy
hash anyway, so both paths pay the same cost.

### B3 — Registration reveals whether an email is taken — **Low**

The 409 "username or email is already registered" confirms the email exists in the
system. Usernames are public in-game anyway; emails are not. A trade-off against UX that is
reasonable to keep, provided A1 makes the rate limit real.

### B4 — The ban reason is shown to the banned player — **Low**

[AuthEndpoints.cs:156](../src/Muwbta.Server/Auth/AuthEndpoints.cs) returns `BanReason`
verbatim on login. Admins will write notes there — "same IP as the account we banned
Tuesday" — that are for the log, not the player. Either document that the field is
player-facing or split it into a public reason and a private note.

---

## C. Getting into another account

**Verified sound:** every `/api/game/{characterId}/…` route resolves the character through
`sessions.Find(accountId, characterId)` or `LoadOwnedCharacterAsync`, which filter by the
caller's account ([GameEndpoints.cs](../src/Muwbta.Server/Game/GameEndpoints.cs)); a wrong
account gets the same answer as an unopened session, so it cannot probe. `/api/characters`
lists by account. `/api/game/sessions` is scoped to the caller. Admin routes require the
Admin role; builder routes the Builder role, derived from one `Satisfies` table so HTTP and
in-game checks cannot disagree. CSRF is covered by `SameSite=Lax` plus the absence of any
CORS policy — a cross-site page cannot send the cookie with a POST.

### C1 — First registration on an empty database becomes Admin — **Low** (see *Do this first*)

Two things about [AuthEndpoints.cs:106](../src/Muwbta.Server/Auth/AuthEndpoints.cs). The
`AnyAsync` check and the insert are separate statements with no transaction, so two
registrations racing on an empty database both see "first" and both become Admin. And more
practically: on any deployment that is reachable before its owner registers, the owner is
whoever gets there first.

**Fix.** Either require a bootstrap token for the first registration
(`Auth__BootstrapToken`, checked only while the accounts table is empty), or do the check
and insert in a serializable transaction and accept the race window is closed but the
"first stranger" problem is not. The token is the better answer.

### C2 — No per-account throttle on failed logins — **Medium**

The only brake on guessing is the per-address window, which today is global (A1). Once A1
is fixed, a distributed attacker gets ten guesses a minute *per address* against any one
account, indefinitely. The password floor is eight characters with no breached-list check.

**Fix.** Track failed attempts per account and back off — a growing delay after the fifth
failure, reset on success — surfaced to the admin panel so a lockout can be seen and lifted.
Consider checking new passwords against a known-breached list; this is what makes an
8-character floor defensible.

### C3 — Any admin can reset any other admin's password — **Low**

[AccountAdminService.cs:366](../src/Muwbta.Server/Admin/AccountAdminService.cs) refuses
self-reset and nothing else. With two admins, either can take over the other. The audit
row records it, which is the right mitigation for a small team; it is noted so the flat
model is a decision rather than an oversight.

### C4 — No "sign out everywhere" — **Low**

The only way to invalidate other sessions is to change the password. A `POST /api/auth/
sessions/revoke` that bumps the stamp without a password change would give a player who
suspects a borrowed laptop a cheaper reaction.

### C5 — `CharacterPath` accepts an integer — **Low**

`Enum.TryParse<CharacterPath>("42", …)` succeeds
([CharacterEndpoints.cs:70](../src/Muwbta.Server/Characters/CharacterEndpoints.cs)) and
creates a character whose path is not a path. Every switch on it has a non-throwing default
and the loop catches per-pulse exceptions, so this is not a crash — it is an out-of-model
character with no abilities and a `Path: "42"` in every payload. The admin role endpoint
already does this correctly with `Enum.IsDefined`; copy it.

---

## D. Bypassing a ban or mute

**Verified sound:** login refuses a banned account; banning a connected account evicts its
sessions through the loop ([AdminLiveEffects.cs:30](../src/Muwbta.Server/Admin/AdminLiveEffects.cs));
the cookie is revalidated against the row at most 60 s later
([PrincipalRevalidator.cs](../src/Muwbta.Server/Auth/PrincipalRevalidator.cs)), after
which every request including heartbeats fails and the liveness reaper closes the session
within `HeartbeatTimeoutSeconds` (60). A mute is enforced at one gate, `RefusedForMute`,
called by say, tell, reply (through `Deliver`), chat, emote and party — every free-text
verb — and is pushed to the live actor when changed. The residual window is: an account
banned while *offline* that still holds a fresh cookie can enter and act for up to 60 s, and
watch for up to another 60 s. Acceptable, and worth knowing.

### D1 — Ban evasion by re-registering is trivial — **Medium**

A ban is per account. Registration needs a username, a string containing `@`, and a
password. Nothing ties a second account to the first: the email is not verified, and
because of A1 no IP is ever seen or stored.

**Fix, in order of cost.** Fix A1 and record the address at registration and each login
(with a retention window — this is personal data). Add an admin query "accounts from this
address". Then decide whether email verification is worth the friction; for a game with
invite-only or small-community ambitions it may not be.

### D2 — No player-side ignore — **Low**

Harassment relief is a moderator's mute. There is no `ignore <name>`, so a player being
harassed while no admin is online has no recourse but to log out. Cheap to add — it is a
per-character set checked in `Deliver` and the room broadcast — and it takes pressure off
the moderation queue.

---

## E. Database

**Verified sound:** every query is LINQ over EF Core, parameterized; the only raw SQL in
the tree is two static migrations; there is no `FromSqlRaw`, `ExecuteSqlRaw` or `NpgsqlCommand`
in application code. `citext` handles case on usernames, emails and character names. Keys
arrive through `RoomKey.TryParse`; enums are parsed by name; `System.Text.Json` has its
default 64-level depth limit; the admin search uses `Contains`, which Npgsql translates to
`strpos`, so `%` and `_` are not wildcards. The connection string is logged as host and
database only. Migrations run at startup under EF's advisory lock.

### E1 — Postgres reachable from the LAN — **Low** (A3)

### E2 — `/enter` and `/leave` are not rate limited — **Low**

Only `/command` has a limiter. Entering does three queries and a loop message; leaving
flushes two save queues with a five-second wait
([GameEndpoints.cs:66, :453](../src/Muwbta.Server/Game/GameEndpoints.cs)). A player
alternating the two in a tight loop is a cheap way to load the database and the writers.
Put both under the `Commands` policy (it already falls back to the account key).

### E3 — Builder import is a trusted-role write of the whole world — **Info**

A Builder can POST a 20 MB bundle that merges into the live world. That is the role's
purpose, the importer never deletes, and the change feed records it. It is listed because
it is the largest single write path and should stay behind the Builder role, never a
lesser one.

---

## F. Social engineering

This is where the real exposure is. None of it is a code bug; all of it is the game handing
a stranger the props they need.

### F1 — Anyone can be "Admin" — **High**

Three facts combine:

- **No reserved names.** `^[A-Za-z]{3,16}$`
  ([CharacterEndpoints.cs:123](../src/Muwbta.Server/Characters/CharacterEndpoints.cs))
  accepts `Admin`, `Administrator`, `Moderator`, `System`, `Staff`, `Support`, `Muwbta`,
  `Reaches`. Usernames likewise.
- **Staff are invisible.** `who` prints name, level and path
  ([CommandRegistry.cs:367](../src/Muwbta.Engine/Commands/CommandRegistry.cs)) — a real
  admin looks exactly like a level-1 Warden.
- **Tells carry no marker.** A tell renders as `{Name} tells you, '…'`
  ([ChannelCommands.cs:102](../src/Muwbta.Engine/Commands/ChannelCommands.cs)).

So `Admin tells you, 'We're migrating accounts — reply with your password to keep your
characters'` is byte-for-byte what a genuine staff tell would look like, and there is no
genuine staff tell to compare it with. This is the most likely way a player loses an
account on this server.

**Fix.** Three parts, all small:

1. A reserved-name check at character and account creation, case-insensitive, covering
   both exact names and substrings (`admin`, `moderator`, `staff`, `system`, `support`,
   `gm`, the product and engine names).
2. A visible staff tag wherever a name appears to another player — `who`, tells, says,
   the room occupant list: `[Admin] Kael tells you, …`. The role is already on the actor.
3. One line in the welcome text: *Staff will never ask for your password.*

### F2 — `emote` forges any line of speech — **Medium**

An emote renders as `{Name} {free text}`
([CommandRegistry.cs:1404](../src/Muwbta.Engine/Commands/CommandRegistry.cs)). So
`emote tells you, 'your account is flagged — send your password to Support'` produces
`Kael tells you, 'your account is flagged…'` — the exact shape of a tell, in emote colour.
Colour is not a defence; players do not read colour under pressure.

**Fix.** Render emotes with a marker the other verbs cannot produce — `* Kael waves` is
the MUD convention — and refuse emote text that begins with a speech verb (`says`, `tells`,
`asks`, `whispers`, `shouts`). With F1's staff tag in place the forgery also loses its
badge.

### F3 — Password recovery goes through a human — **Medium**

There is no self-service reset. A player who forgets their password asks an admin, and the
admin uses `POST /api/admin/accounts/{username}/password` or the in-game verb. That makes
the admin the target: *"Hi, it's Kael, I'm locked out, can you reset me?"* — sent from a
brand-new account, or as a `tell` from a character named to look like Kael's friend.

The email on the account cannot help, because it was never verified (F6): an admin who
"sends the new password to the email on file" may be sending it to whoever registered
second with the victim's address.

**Fix.** A written protocol before it is needed, not after. Options: recovery codes shown
once at registration and stored hashed; or verify email at registration so it can be
trusted for recovery; or — cheapest — a rule that resets are never done on in-game or
new-account request, only through a channel the operator already knows the player on.
Whatever is chosen, the admin panel should show the requester's account age and IP (after
A1) next to the reset button.

### F4 — Content is a trusted channel — **Medium**

Builders write room descriptions, quest dialogue, and the login welcome message. Quest
dialogue renders with *clickable command buttons*
([QuestCommands.cs:294](../src/Muwbta.Engine/Commands/QuestCommands.cs)) — verified safe:
the command is constructed server-side from the quest key, not from the text, so a
builder cannot make a button that runs an arbitrary command. But the *prose* is theirs, and
it arrives styled as the world rather than as a player. A compromised Builder account can
put *"Your session has expired. Say your password to continue."* into the room every new
player starts in, and it will be believed.

The welcome message has no length or content validation that I could find.

**Fix.** Treat the Builder role as the privileged role it is — F1's staff tag helps here
too, since a Builder is staff. Cap the welcome message and keep it plain text. The change
feed already records who wrote what; make sure someone reads it.

### F5 — Lookalike names — **Low**

The ASCII-only regex blocks Unicode homoglyphs, which is the important half. `Kael` and
`KaeI` (capital I) both pass and are indistinguishable in most fonts. A similarity check at
creation against existing names — fold `I`/`l`/`1` and `O`/`0` before comparing — closes
the rest.

### F6 — Email is collected but never verified — **Low** on its own, load-bearing for F3

Registration accepts any string with an `@`. A player can register with *your* address,
and the unique index then prevents you from using it. Nothing today depends on the email
being real, which is exactly why it should not be used for recovery until it is.

### F7 — The client renders text, not HTML — **Info**

No `innerHTML`, no `dangerouslySetInnerHTML`, no URL linkification anywhere in
`client/src`. Player text becomes a text node in a `<span>`; server-supplied style classes
come only from the server. A phishing URL in a tell is a string a player would have to copy
by hand. This is a property worth writing down and worth a test, because it is the kind
of thing a well-meaning "make links clickable" PR removes in an afternoon. A4's CSP
enforces it.

---

## G. Availability of the single loop

**Verified sound:** the loop catches per-pulse exceptions and per-mutation exceptions
([GameLoop.cs:121, :398](../src/Muwbta.Engine/GameLoop.cs)), so a throwing handler costs
one pulse, not the world; commands are capped at 512 characters and limited per character;
SSE is bounded to three characters per account and a newer stream displaces the older; the
assist has its own limiter and queue depth. The remaining gaps are E2 and the global auth
cap in A1.

---

## Priority

| # | Item | Effort | Unlocks |
|---|---|---|---|
| 0 | ~~Confirm the first beta account is yours~~ — done | — | — |
| 1 | A1 TLS on the inner hop (self-signed at boot); forwarded headers in Kestrel, both hops pinned | 3 hr | A2, C2, D1 |
| 2 | A2 `Secure` cookie unconditional in Production; HSTS toggle on NPM | 1 hr | — |
| 3 | A3 remove the Postgres publish once exports are trusted (operator's timing) | 5 min | — |
| 4 | F1 reserved names + staff tag + welcome line | ½ day | F2, F4 |
| 5 | F2 emote marker | 1 hr | — |
| 6 | C2 per-account login backoff | ½ day | — |
| 7 | F3 write the recovery protocol | 1 hr, no code | — |
| 8 | B1, B2, C5, E2 — four small code fixes | 2 hr | — |
| 9 | D1 record addresses; D2 `ignore`; F6 email verification | later | — |

Items 1–3 are the deployment. Items 4–5 are the ones that stop a player losing their
account to a stranger. The rest is hardening.
