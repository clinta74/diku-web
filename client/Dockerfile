# Multi-stage build for Muwbta React client
# Stage 1: Build
#
# Node 24 is the current Active LTS. Nothing from this stage is shipped - only /src/dist crosses
# into the runtime image below - so a vulnerability here is a supply-chain concern about what
# built the bundle, not something reachable in a running container.
FROM node:24-alpine AS build

WORKDIR /src

# Copy package files
COPY package.json package-lock.json ./

# Install dependencies
RUN npm ci

# Copy source. All three tsconfigs, not just the root one: it is a solution-style file that does
# nothing but reference tsconfig.app.json and tsconfig.node.json, so copying it alone fails in
# `tsc -b` with TS5083 rather than with anything that names the real problem.
COPY tsconfig*.json vite.config.ts index.html ./
COPY src ./src
COPY public ./public

# Build React app
RUN npm run build

# Stage 2: Runtime (nginx)
#
# This is the image that actually ships, so it is the one whose CVE count means anything. 1.27 was
# four mainline releases behind, and most of what a scanner flags on these is the Alpine base
# rather than nginx itself - which is why moving forward a minor is worth more than it looks.
# Mainline (odd minor) to match what this file already tracked; 1.30-alpine is the stable branch
# if that is ever preferred.
FROM nginx:1.31-alpine

# For the certificate the entrypoint self-signs when none is mounted (docker-entrypoint.d).
RUN apk add --no-cache openssl

WORKDIR /usr/share/nginx/html

# Copy built app from build stage
COPY --from=build /src/dist .

# Copy nginx configuration as a template. The image's entrypoint runs envsubst over
# /etc/nginx/templates and writes the result to NGINX_ENVSUBST_OUTPUT_DIR, which defaults to
# conf.d - pointing it at /etc/nginx is what turns nginx.conf.template into nginx.conf.
ENV NGINX_ENVSUBST_OUTPUT_DIR=/etc/nginx

# Where /api is proxied. Override it in compose when the API service is not called `web`.
ENV BACKEND_ORIGIN=http://web:8080

# Docker's embedded DNS. Declared here rather than relying on the base image's own
# NGINX_LOCAL_RESOLVERS, which is exported too late for envsubst to see and left the placeholder
# sitting in the rendered config.
ENV DNS_RESOLVER=127.0.0.11

COPY nginx.conf.template /etc/nginx/templates/nginx.conf.template

# Runs before nginx starts, as the container user. Executable bit set here because the image's
# entrypoint skips a .sh it cannot execute - with a warning nobody reads, and then nginx fails to
# find the certificate the script did not write.
COPY --chmod=755 docker-entrypoint.d/05-selfsign.sh /docker-entrypoint.d/05-selfsign.sh

# Create non-root user for security.
#
# /etc/nginx has to be writable by it, which is easy to miss: the entrypoint renders the template
# above *as this user*, before nginx starts. Without it the render fails with nothing worse than an
# "info:" line, and nginx then starts on whatever config was baked in - so the container comes up
# healthy while serving a configuration nobody asked for.
RUN addgroup -g 1000 nginx-app && \
    adduser -D -u 1000 -G nginx-app nginx-app && \
    mkdir -p /etc/nginx/tls && \
    chown -R nginx-app:nginx-app \
        /usr/share/nginx/html /var/cache/nginx /var/log/nginx /etc/nginx

USER nginx-app

# 80 for the container healthcheck and for a deployment that terminates TLS nowhere; 443 for the
# hop from a front proxy, so the scheme this nginx forwards is the one the caller actually used.
EXPOSE 80 443

HEALTHCHECK --interval=30s --timeout=10s --retries=3 --start-period=10s \
    CMD wget -q -O - http://localhost/health || exit 1

CMD ["nginx", "-g", "daemon off;"]