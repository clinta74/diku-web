#!/bin/sh
# Self-signs a certificate for the 443 listener when none has been mounted.
#
# The nginx image runs everything in /docker-entrypoint.d before it starts nginx, as the container
# user, which is why /etc/nginx/tls has to be writable by nginx-app (see the Dockerfile).
#
# Why a throwaway certificate is enough: the only thing that connects to 443 is a front proxy that
# terminates the public certificate and re-encrypts to here without validating what it gets. So the
# leg is encrypted against anything listening on the LAN, and nothing checks who signed it. A
# certificate nobody validates does not need to be managed, rotated, or kept anywhere - it can be
# minted fresh on every start.
#
# What this does NOT do: authenticate either end, or restrict who may connect to 443. Any host on
# the LAN can still reach the port; the server's Proxy:KnownProxies list is what stops such a host
# from spoofing forwarded headers. A deployment that wants the port locked to the front proxy mounts
# a certificate here and requires a client certificate - that is a different configuration, not this
# one.
#
# Mounting a real pair at /etc/nginx/tls/{cert,key}.pem is honoured and skips the self-signing.

set -e

dir=/etc/nginx/tls

if [ -s "$dir/cert.pem" ] && [ -s "$dir/key.pem" ]; then
    echo "tls: using the certificate mounted at $dir"
    exit 0
fi

mkdir -p "$dir"

openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -nodes -days 3650 \
    -subj "/CN=${TLS_COMMON_NAME:-muwbta-client}" \
    -keyout "$dir/key.pem" -out "$dir/cert.pem" >/dev/null 2>&1

echo "tls: self-signed a certificate for the 443 listener (nothing validates it - see 05-selfsign.sh)"
