# Multi-stage build for DikuWeb React client
# Stage 1: Build
FROM node:22-alpine AS build

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
FROM nginx:1.27-alpine

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

# Create non-root user for security.
#
# /etc/nginx has to be writable by it, which is easy to miss: the entrypoint renders the template
# above *as this user*, before nginx starts. Without it the render fails with nothing worse than an
# "info:" line, and nginx then starts on whatever config was baked in - so the container comes up
# healthy while serving a configuration nobody asked for.
RUN addgroup -g 1000 nginx-app && \
    adduser -D -u 1000 -G nginx-app nginx-app && \
    chown -R nginx-app:nginx-app \
        /usr/share/nginx/html /var/cache/nginx /var/log/nginx /etc/nginx

USER nginx-app

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=10s --retries=3 --start-period=10s \
    CMD wget -q -O - http://localhost/health || exit 1

CMD ["nginx", "-g", "daemon off;"]