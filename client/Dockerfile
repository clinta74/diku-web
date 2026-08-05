# Multi-stage build for DikuWeb React client
# Stage 1: Build
FROM node:22-alpine AS build

WORKDIR /src

# Copy package files
COPY package.json package-lock.json ./

# Install dependencies
RUN npm ci

# Copy source
COPY tsconfig.json vite.config.ts index.html ./
COPY src ./src
COPY public ./public

# Build React app
RUN npm run build

# Stage 2: Runtime (nginx)
FROM nginx:1.27-alpine

WORKDIR /usr/share/nginx/html

# Copy built app from build stage
COPY --from=build /src/dist .

# Copy nginx configuration
COPY nginx.conf /etc/nginx/nginx.conf

# Create non-root user for security
RUN addgroup -g 1000 nginx-app && \
    adduser -D -u 1000 -G nginx-app nginx-app && \
    chown -R nginx-app:nginx-app /usr/share/nginx/html /var/cache/nginx /var/log/nginx

USER nginx-app

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=10s --retries=3 --start-period=10s \
    CMD wget -q -O - http://localhost/health || exit 1

CMD ["nginx", "-g", "daemon off;"]