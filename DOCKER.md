# Docker & Production Deployment Guide

This guide explains how to build, deploy, and manage DikuWeb using Docker and GitHub Actions.

## Architecture Overview

```
GitHub Actions (CI/CD)
  ├─→ Builds Backend (Dockerfile)
  │    └─→ ghcr.io/your-org/diku-web:latest
  │
  └─→ Builds Client (client/Dockerfile)
       └─→ ghcr.io/your-org/diku-web-client:latest

Production Stack (docker-compose)
  ├─→ nginx (React SPA + reverse proxy) [port 80]
  │    └─→ Proxies /api to backend
  │
  ├─→ ASP.NET Core Backend [port 5000, internal only]
  │    └─→ Depends on PostgreSQL
  │
  └─→ PostgreSQL 18 [port 5432, internal only]
       └─→ Data persistence (pgdata volume)
```

Key points:
- **nginx** serves static React files and proxies API calls to the backend
- **Backend** only handles API requests (no static file serving)
- **PostgreSQL** stores all game data
- All services communicate via internal Docker network (172.25.0.0/16)
- Public traffic only hits nginx on port 80

## Prerequisites

- Docker & Docker Compose 2.20+
- GitHub CLI (for authentication to private registries)
- `.env` file with configuration (copy from `.env.example`)

## Local Development with Docker

### 1. Build images locally

```bash
# Build backend
docker build -t diku-web:dev .

# Build client
docker build -t diku-web-client:dev client/

# Or build both via docker-compose
docker-compose build
```

### 2. Set up environment

```bash
# Copy and customize
cp .env.example .env

# For local dev, set:
POSTGRES_PASSWORD=dev_password
DIKUWEB_IMAGE_TAG=dev
CLIENT_PORT=80
```

### 3. Start the stack

```bash
# First time: builds and runs all services
docker-compose up -d

# View logs
docker-compose logs -f

# Follow specific service
docker-compose logs -f client
docker-compose logs -f web
docker-compose logs -f postgres
```

### 4. Access services

- **Application**: http://localhost (nginx, serves React + proxies /api)
- **Database Admin**: http://localhost:8080 (Adminer, optional)
  - Server: `postgres`
  - User: `dikuweb`
  - Password: (from .env POSTGRES_PASSWORD)

### 5. How it works locally

1. Browser requests http://localhost
2. nginx (port 80) serves React static files from dist/
3. React app loads and makes API calls to http://localhost/api
4. nginx proxies /api requests to http://web:5000 (internal)
5. Backend processes request and returns response
6. Browser displays result

### 6. Stop everything

```bash
docker-compose down
```

To also remove the database volume:

```bash
docker-compose down -v
```

---

## Production Deployment

### 1. Prerequisites

- Server with Docker & Docker Compose
- GitHub Container Registry access (if using private repo)

### 2. Authenticate to GHCR

```bash
# Generate a GitHub Personal Access Token (PAT) with 'read:packages' scope
# https://github.com/settings/tokens

# Log in to GHCR
echo $PAT | docker login ghcr.io -u <github_username> --password-stdin

# Or use GitHub CLI
gh auth login  # Follow prompts
```

### 3. Configure environment

```bash
# Copy and secure
cp .env.example .env

# Edit for production (IMPORTANT!)
POSTGRES_PASSWORD=<strong-random-password>
DIKUWEB_ENV=Production
DIKUWEB_IMAGE_TAG=v1.0.0  # or latest
REGISTRY=ghcr.io
IMAGE_REPO=your-org/diku-web
CLIENT_PORT=80
LOG_LEVEL=Warning
SWAGGER_ENABLED=false
# Comment out or remove Adminer in production
```

### 4. Deploy

```bash
# Pull latest images
docker-compose pull

# Start services (creates volumes, networks, containers)
docker-compose up -d

# Verify health
docker-compose ps
docker-compose logs client --tail=20
docker-compose logs web --tail=20
```

Expected output:
```
NAME                 IMAGE                                    PORTS
dikuweb-postgres     postgres:18                              (internal)
dikuweb-web          ghcr.io/your-org/diku-web:latest         (internal)
dikuweb-client       ghcr.io/your-org/diku-web-client:latest  0.0.0.0:80->80/tcp
```

### 5. Test the deployment

```bash
# Check health
curl http://localhost/health

# Visit the app
curl http://localhost/

# Check API health
curl http://localhost/api/health
```

### 6. Database backups

```bash
# Backup the database
docker-compose exec postgres pg_dump \
  -U dikuweb dikuweb > backup-$(date +%Y%m%d).sql

# Or use the /backups volume
docker-compose exec postgres pg_dump \
  -U dikuweb dikuweb > /backups/backup-$(date +%Y%m%d).sql

# Restore from backup
docker-compose exec -T postgres psql \
  -U dikuweb dikuweb < backup-20240115.sql
```

### 7. Updates

```bash
# Pull new images (both backend and client)
docker-compose pull

# Stop and remove old containers (volumes persist)
docker-compose down

# Start with new images (migrations run automatically)
docker-compose up -d

# Check logs
docker-compose logs client --tail=50
docker-compose logs web --tail=50
```

### 8. Monitoring

```bash
# View all logs
docker-compose logs -f

# Follow specific service
docker-compose logs -f client
docker-compose logs -f web
docker-compose logs -f postgres

# Check container stats
docker stats dikuweb-client dikuweb-web dikuweb-postgres

# Verify services are healthy
docker-compose ps
```

### 9. Scaling considerations

- **Frontend (nginx)**: Stateless, easy to scale horizontally
- **Backend**: Handles game loop (must be single instance for now)
- **Database**: Use managed PostgreSQL for high availability

To scale frontend independently:
```bash
# Run multiple nginx containers behind a load balancer
docker run -d --name client-2 -p 8081:80 ghcr.io/your-org/diku-web-client:latest
```

### 10. Stop/restart

```bash
# Graceful stop (containers persist)
docker-compose stop

# Restart (fast)
docker-compose start

# Restart specific service
docker-compose restart client
docker-compose restart web

# Full restart
docker-compose down && docker-compose up -d
```

---

## GitHub Actions CI/CD

### Build Workflow

Two jobs run in parallel:
1. **build-backend**: Builds and pushes backend image
2. **build-client**: Builds and pushes client image

### Build Triggers

Images are built and pushed automatically when:
- Code pushed to `main` branch
  - Backend tagged as: `latest`, `main-{sha}`
  - Client tagged as: `latest`, `main-{sha}`
- Code pushed to `develop` branch
  - Backend tagged as: `develop-{sha}`
  - Client tagged as: `develop-{sha}`
- Tag created (e.g., `v1.0.0`)
  - Backend tagged as: `v1.0.0`, `1.0`, `1`
  - Client tagged as: `v1.0.0`, `1.0`, `1`
- Pull request opened
  - Both images built but NOT pushed (test only)

### Images published

- **Backend**: `ghcr.io/{owner}/{repo}:tag`
- **Client**: `ghcr.io/{owner}/{repo}-client:tag`

### Required Secrets

GitHub Actions uses `GITHUB_TOKEN` automatically (no setup needed).

### Manual Workflow Trigger (optional)

```bash
# List workflows
gh workflow list

# Trigger Docker build workflow
gh workflow run docker-build.yml --ref main
```

---

## Reverse Proxy Setup (HTTPS)

For production with HTTPS, use a reverse proxy in front of nginx:

### Nginx Example

```nginx
upstream diku_web {
    server localhost:80;
}

server {
    listen 80;
    server_name diku-web.example.com;
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name diku-web.example.com;

    ssl_certificate /etc/ssl/certs/cert.pem;
    ssl_certificate_key /etc/ssl/private/key.pem;

    location / {
        proxy_pass http://diku_web;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_http_version 1.1;
        proxy_set_header Connection "";
        proxy_buffering off;
    }
}
```

### Caddy Example (simpler)

```caddy
diku-web.example.com {
    reverse_proxy localhost:80 {
        header_up X-Forwarded-Proto https
    }
}
```

---

## Troubleshooting

### Client won't load

```bash
# Check nginx is running and healthy
docker-compose ps client

# Check logs
docker-compose logs client --tail=50

# Verify it can reach backend
docker-compose exec client curl -v http://web:5000/health
```

### API calls failing

```bash
# Verify backend is healthy
docker-compose ps web

# Check backend logs
docker-compose logs web --tail=50

# Test backend directly
docker-compose exec client curl -v http://web:5000/api/health

# Check database connection
docker-compose logs web | grep -i database
```

### Database connection fails

```bash
# Check postgres health
docker-compose ps postgres

# Verify database is ready
docker-compose exec postgres psql \
  -U dikuweb -d dikuweb -c "SELECT 1"

# Check connection string (from .env)
echo $POSTGRES_PASSWORD
```

### Stuck in CrashLoopBackOff

```bash
# Check which service is failing
docker-compose ps

# View logs to see error
docker-compose logs service-name --tail=100

# Common issues:
# - POSTGRES_PASSWORD not set or wrong
# - Database not ready (wait 15-20s)
# - Port already in use
# - Image not found (pull failed)
```

### Performance issues

```bash
# Check resource usage
docker stats

# Check connection pool saturation
docker-compose exec web curl http://localhost:5000/health

# Monitor nginx error log
docker-compose exec client tail -f /var/log/nginx/error.log
```

### Need to rebuild from scratch

```bash
# WARNING: This deletes all data!
docker-compose down -v

# Start fresh
docker-compose pull
docker-compose up -d
```

---

## Environment Variables Reference

| Variable | Purpose | Default |
|----------|---------|---------|
| `DIKUWEB_ENV` | ASP.NET environment | `Production` |
| `DIKUWEB_IMAGE_TAG` | Image version tag | `latest` |
| `REGISTRY` | Container registry host | `ghcr.io` |
| `IMAGE_REPO` | Image repository path | `your-org/diku-web` |
| `POSTGRES_DB` | Database name | `dikuweb` |
| `POSTGRES_USER` | Database user | `dikuweb` |
| `POSTGRES_PASSWORD` | Database password | (must set in prod) |
| `POSTGRES_PORT` | Database port binding | `127.0.0.1:5432` |
| `CLIENT_PORT` | Public frontend port | `80` |
| `LOG_LEVEL` | Backend log level | `Information` |
| `MAX_CHARACTERS` | Chars per account | `5` |
| `MAX_SESSIONS` | Max concurrent sessions | `100` |
| `SESSION_TIMEOUT` | Session timeout (min) | `20160` |
| `ADMINER_PORT` | DB admin UI port | `127.0.0.1:8080` |
| `SWAGGER_ENABLED` | Enable Swagger docs | `false` |

See `.env.example` for all options.

---

## File Structure

```
diku-web/
├── Dockerfile                    # Backend multi-stage build
├── client/
│   ├── Dockerfile               # Client multi-stage build
│   ├── nginx.conf               # nginx configuration
│   ├── .dockerignore            # Build context optimization
│   ├── package.json
│   ├── vite.config.ts
│   └── src/
│       └── (React components)
├── docker-compose.yml           # Full stack definition
├── .env.example                 # Configuration template
├── .dockerignore                # Backend build context
├── .github/workflows/
│   └── docker-build.yml         # GitHub Actions CI/CD
└── src/
    ├── DikuWeb.Server/          # ASP.NET Core API
    ├── DikuWeb.Engine/          # Game engine
    ├── DikuWeb.Persistence/     # Database layer
    └── DikuWeb.Domain/          # Domain models
```

---

## Performance Tips

1. **Use named volumes for database** - Already configured, avoid :Z or :z on bind mounts
2. **Enable build cache** - GitHub Actions workflow uses registry cache
3. **Gzip compression** - Configured in nginx.conf
4. **Static asset caching** - React assets cached for 1 year (Vite hashes filenames)
5. **Connection pooling** - Configured in backend connection string
6. **Multi-stage builds** - Keeps final images small

---

## Security Considerations

✓ Non-root users in containers  
✓ Secrets in .env (never in images)  
✓ Health checks for readiness  
✓ Security headers in nginx (X-Frame-Options, etc.)  
✓ HttpOnly cookies for auth  
✓ Database only accessible internally  
✓ Adminer removed in production  
✓ SQL injection protection via EF Core  

**Recommended for production:**
- Use a managed database (AWS RDS, Azure Database for PostgreSQL)
- Set up automated database backups
- Use TLS/HTTPS with a reverse proxy
- Enable audit logging
- Regular security scanning (Trivy, etc.)

---

## Next Steps

1. **Customize** `.env` for your deployment
2. **Push** to GitHub to trigger builds in Actions
3. **Monitor** workflow progress in GitHub Actions tab
4. **Deploy** with `docker-compose pull && docker-compose up -d`
5. **Test** the full stack
6. **Monitor** logs and health checks
7. **Backup** database regularly
8. **Scale** as needed

For questions or issues, check the troubleshooting section or review logs with:
```bash
docker-compose logs -f
```