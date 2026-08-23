# The generation model behind the builder assist.
#
# This file exists for one parameter. Ollama's default context is 4096 tokens, and the canon
# prefix this design depends on does not fit in it - docs/WORLD.md sections 1-9 alone are about
# 8,900 tokens. A prompt longer than num_ctx is not refused; it is truncated, so the failure
# looks like a model that has read the world and misremembered it rather than like a model that
# was handed two thirds of a page.
#
# Everything in it is a LOAD-TIME parameter, and that is the rule for what belongs here.
# Sampling settings - temperature, top_p, seed - are applied per request and cost nothing to
# vary, so they stay in the caller where the prose task and the JSON task can disagree about
# them. num_ctx cannot: a request that asks for a different one reloads the model, and a reload
# discards the KV cache for the prefix, which is the single thing docker-compose.truenas.yml is
# arranged to protect (OLLAMA_KEEP_ALIVE -1, one parallel slot, models on the SSD pool). Baking
# it means a caller that forgets to send it gets the right window instead of quietly getting
# 4096 back and taking the cache down with it.

FROM gemma3:12b

# 16k, from the budget rather than from the round number:
#
#   canon prefix        ~9,000   WORLD.md sections 1-9, which is the canon and not the process
#   role + schema       ~1,500   what it is being asked for, and the shape of the answer
#   request context     ~2,000   the entity, its siblings, the zone's own rooms as exemplars
#   generation          ~2,000   a room description is a paragraph; a zone shape is larger
#   headroom            ~1,900
#
# 32k was the alternative and is rejected on memory, not on need - see the KV cache arithmetic
# in README.md. If the prefix grows past about 11k the budget above is what to re-run; the
# number to change is here and the reason it changed belongs beside it.
PARAMETER num_ctx 16384

# Ollama's default already, set explicitly because it is the parameter that decides what
# truncation does when it happens anyway. Leading tokens are what get kept, and the canon prefix
# is the leading tokens - so an over-long request loses the tail it can afford to lose rather
# than the world it cannot.
PARAMETER num_keep 4

# A conservative default for a tool that proposes text a human then edits. Callers override it
# per request - lower for anything that has to satisfy a schema, higher for prose - and doing so
# does not reload the model. It is here so that a caller which sends nothing gets something
# sober rather than Ollama's 0.8.
PARAMETER temperature 0.6
