# Production Deployment Guide (No .env Files)

This guide covers deploying DikuWeb in production environments that don't support `.env` files.

## Quick Start

All configuration is done via **environment variables** - no `.env` file needed.

```bash
# Set required environment variables
export POSTGRES_DB=dikuweb
export POSTGRES_USER=dikuweb
export POSTGRES_PASSWORD=<strong-random-password>
export DIKUWEB_ENV=Production
export DIKUWEB_IMAGE_TAG=v1.0.0
export REGISTRY=ghcr.io
export IMAGE_REPO=your-org/diku-web
export CLIENT_PORT=80
export LOG_LEVEL=Warning
export MAX_CHARACTERS=5
export MAX_SESSIONS=100
export SESSION_TIMEOUT=20160
export LINKDEAD_GRACE=30
export STARTING_ROOM=hall@0.0.0
export SWAGGER_ENABLED=false
export ADMINER_PORT=""  # Empty to disable

# Deploy
docker-compose pull
docker-compose up -d
```

## Environment Variables Reference

**REQUIRED** (must be set):

| Variable | Purpose | Example |
|----------|---------|---------|
| `POSTGRES_DB` | Database name | `dikuweb` |
| `POSTGRES_USER` | Database user | `dikuweb` |
| `POSTGRES_PASSWORD` | Database password | `pG9x8#kL2mP@rT5` |
| `REGISTRY` | Container registry | `ghcr.io` |
| `IMAGE_REPO` | Image repository | `my-org/diku-web` |
| `DIKUWEB_IMAGE_TAG` | Image version | `v1.0.0` or `latest` |
| `DIKUWEB_ENV` | ASP.NET environment | `Production` |
| `CLIENT_PORT` | Frontend port | `80` |

**OPTIONAL** (reasonable defaults shown):

| Variable | Purpose | Default |
|----------|---------|---------|
| `LOG_LEVEL` | Backend log level | `Information` |
| `MAX_CHARACTERS` | Characters per account | `5` |
| `MAX_SESSIONS` | Concurrent sessions | `100` |
| `SESSION_TIMEOUT` | Session timeout (min) | `20160` |
| `LINKDEAD_GRACE` | Link-dead grace (sec) | `30` |
| `STARTING_ROOM` | New character spawn point | `hall@0.0.0` |
| `SWAGGER_ENABLED` | Enable OpenAPI docs | `false` |
| `ADMINER_PORT` | DB admin UI port | `` (empty = disabled) |
| `WEB_DEBUG_PORT` | Backend debug port | `` (empty = disabled) |

---

## Deployment Methods

### 1. Docker CLI with Environment Variables

```bash
# Inline export + docker-compose
export POSTGRES_PASSWORD=secret && \
export DIKUWEB_IMAGE_TAG=v1.0.0 && \
docker-compose up -d
```

### 2. Shell Script (Recommended)

Create `deploy.sh`:

```bash
#!/bin/bash
set -e

# Load configuration from environment or use defaults
REGISTRY="${REGISTRY:-ghcr.io}"
IMAGE_REPO="${IMAGE_REPO:-your-org/diku-web}"
DIKUWEB_IMAGE_TAG="${DIKUWEB_IMAGE_TAG:-latest}"
POSTGRES_DB="${POSTGRES_DB:-dikuweb}"
POSTGRES_USER="${POSTGRES_USER:-dikuweb}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-}"
LOG_LEVEL="${LOG_LEVEL:-Warning}"
CLIENT_PORT="${CLIENT_PORT:-80}"
DIKUWEB_ENV="${DIKUWEB_ENV:-Production}"

# Validate required variables
if [ -z "$POSTGRES_PASSWORD" ]; then
    echo "ERROR: POSTGRES_PASSWORD must be set"
    exit 1
fi

# Export for docker-compose
export REGISTRY IMAGE_REPO DIKUWEB_IMAGE_TAG POSTGRES_DB POSTGRES_USER
export POSTGRES_PASSWORD LOG_LEVEL CLIENT_PORT DIKUWEB_ENV
export MAX_CHARACTERS="${MAX_CHARACTERS:-5}"
export MAX_SESSIONS="${MAX_SESSIONS:-100}"
export SESSION_TIMEOUT="${SESSION_TIMEOUT:-20160}"
export LINKDEAD_GRACE="${LINKDEAD_GRACE:-30}"
export STARTING_ROOM="${STARTING_ROOM:-hall@0.0.0}"
export SWAGGER_ENABLED="${SWAGGER_ENABLED:-false}"
export ADMINER_PORT="${ADMINER_PORT:-}"

echo "Deploying DikuWeb..."
echo "  Registry: $REGISTRY"
echo "  Image: $IMAGE_REPO:$DIKUWEB_IMAGE_TAG"
echo "  Database: $POSTGRES_DB"
echo "  Environment: $DIKUWEB_ENV"

docker-compose pull
docker-compose up -d

echo "✓ Deployment complete"
docker-compose ps
```

Run it:

```bash
# With all settings
POSTGRES_PASSWORD=secret DIKUWEB_IMAGE_TAG=v1.0.0 ./deploy.sh

# Or with environment file sourced first
source /etc/diku-web/config.sh && ./deploy.sh
```

### 3. Systemd Service

Create `/etc/systemd/system/docker-diku-web.service`:

```ini
[Unit]
Description=DikuWeb Container Stack
Requires=docker.service
After=docker.service network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory=/opt/diku-web

# Load environment from config file
EnvironmentFile=/etc/diku-web/config.env
EnvironmentFile=-/etc/diku-web/config.override

ExecStartPre=/usr/bin/docker-compose pull
ExecStart=/usr/bin/docker-compose up --no-log-prefix

Restart=always
RestartSec=10s

User=docker-user
Group=docker

StandardOutput=journal
StandardError=journal
SyslogIdentifier=diku-web

[Install]
WantedBy=multi-user.target
```

Create `/etc/diku-web/config.env`:

```bash
POSTGRES_PASSWORD=your-secure-password
DIKUWEB_IMAGE_TAG=v1.0.0
REGISTRY=ghcr.io
IMAGE_REPO=your-org/diku-web
CLIENT_PORT=80
LOG_LEVEL=Warning
```

Start/stop:

```bash
# Start
sudo systemctl start docker-diku-web

# Stop
sudo systemctl stop docker-diku-web

# Logs
sudo journalctl -u docker-diku-web -f

# Status
sudo systemctl status docker-diku-web
```

### 4. Docker Swarm

Initialize swarm:

```bash
docker swarm init
```

Create secret for password (never in compose or config files):

```bash
echo "your-secure-password" | docker secret create postgres_password -
```

Create `docker-compose-swarm.yml`:

```yaml
version: '3.8'

services:
  postgres:
    image: postgres:18
    environment:
      POSTGRES_DB: dikuweb
      POSTGRES_USER: dikuweb
      POSTGRES_PASSWORD_FILE: /run/secrets/postgres_password
    secrets:
      - postgres_password
    # ... rest of config

secrets:
  postgres_password:
    external: true
```

Deploy:

```bash
docker stack deploy -c docker-compose-swarm.yml diku-web
```

### 5. Kubernetes

Create `configmap.yaml`:

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: diku-web-config
data:
  REGISTRY: ghcr.io
  IMAGE_REPO: your-org/diku-web
  DIKUWEB_IMAGE_TAG: v1.0.0
  LOG_LEVEL: Warning
  # ... other settings
```

Create `secret.yaml`:

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: diku-web-secrets
type: Opaque
stringData:
  POSTGRES_PASSWORD: "your-secure-password"
  POSTGRES_DB: "dikuweb"
  POSTGRES_USER: "dikuweb"
```

Reference in pods:

```yaml
envFrom:
  - configMapRef:
      name: diku-web-config
  - secretRef:
      name: diku-web-secrets
```

---

## GitOps Example (ArgoCD)

Create `kustomization.yaml`:

```yaml
apiVersion: kustomize.config.k8s.io/v1beta1
kind: Kustomization

configMapGenerator:
  - name: diku-web-config
    literals:
      - REGISTRY=ghcr.io
      - IMAGE_REPO=your-org/diku-web
      - DIKUWEB_IMAGE_TAG=v1.0.0

secretGenerator:
  - name: diku-web-secrets
    envs:
      - secrets.env
```

In `secrets.env`:

```
POSTGRES_PASSWORD=your-secure-password
```

Deploy with ArgoCD:

```bash
argocd app create diku-web \
  --repo https://github.com/your-org/diku-web \
  --path k8s \
  --dest-server https://kubernetes.default.svc
```

---

## CI/CD Integration

### GitHub Actions Example

Create `.github/workflows/deploy.yml`:

```yaml
name: Deploy to Production

on:
  push:
    tags:
      - "v*"

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Deploy to production server
        env:
          SSH_KEY: ${{ secrets.DEPLOY_SSH_KEY }}
          DEPLOY_HOST: ${{ secrets.DEPLOY_HOST }}
          POSTGRES_PASSWORD: ${{ secrets.POSTGRES_PASSWORD }}
        run: |
          mkdir -p ~/.ssh
          echo "$SSH_KEY" > ~/.ssh/id_rsa
          chmod 600 ~/.ssh/id_rsa
          ssh-keyscan $DEPLOY_HOST >> ~/.ssh/known_hosts
          
          ssh deployer@$DEPLOY_HOST << 'EOF'
          export POSTGRES_PASSWORD="$POSTGRES_PASSWORD"
          export DIKUWEB_IMAGE_TAG="${GITHUB_REF#refs/tags/}"
          export REGISTRY=ghcr.io
          export IMAGE_REPO=your-org/diku-web
          
          cd /opt/diku-web
          docker-compose pull
          docker-compose up -d
          EOF
```

### GitLab CI Example

Create `.gitlab-ci.yml`:

```yaml
deploy:production:
  stage: deploy
  script:
    - apt-get update && apt-get install -y docker-compose
    - export POSTGRES_PASSWORD="$POSTGRES_PASSWORD_PROD"
    - export DIKUWEB_IMAGE_TAG="$CI_COMMIT_TAG"
    - docker-compose pull
    - docker-compose up -d
  only:
    - tags
  environment:
    name: production
```

---

## Verification

After deployment, verify the stack is running:

```bash
# Check containers
docker-compose ps

# Check logs
docker-compose logs -f

# Test health endpoints
curl http://localhost/health           # Frontend health
curl http://localhost/api/health       # API health

# Test database
docker-compose exec postgres psql \
  -U dikuweb -d dikuweb -c "SELECT 1"
```

---

## Updating

To update to a new image version:

```bash
export DIKUWEB_IMAGE_TAG=v1.1.0
docker-compose pull
docker-compose up -d
```

Or in a script that reads from secrets:

```bash
DIKUWEB_IMAGE_TAG=$(cat /run/secrets/image_version) \
docker-compose pull && docker-compose up -d
```

---

## Troubleshooting

### Missing environment variable

**Error:** `variable is not set. Substitution key 'POSTGRES_PASSWORD' not found`

**Solution:** Set the missing variable:

```bash
export POSTGRES_PASSWORD=secret
docker-compose up -d
```

### Variables not being substituted

Verify they're exported:

```bash
env | grep POSTGRES_PASSWORD
```

Or pass explicitly:

```bash
docker-compose -e POSTGRES_PASSWORD=secret up -d
```

### Different values per environment

Use different config files:

```bash
# Production
source /etc/diku-web/prod.env
docker-compose up -d

# Staging
source /etc/diku-web/staging.env
docker-compose up -d
```

---

## Security Best Practices

✓ **Never** commit secrets to Git  
✓ Store passwords in secrets manager (1Password, Vault, AWS Secrets Manager)  
✓ Use separate databases for prod/staging  
✓ Rotate `POSTGRES_PASSWORD` periodically  
✓ Use TLS/HTTPS via reverse proxy (not shown here)  
✓ Audit container image sources  
✓ Keep images updated (automatic scanning recommended)  

Example with AWS Secrets Manager:

```bash
POSTGRES_PASSWORD=$(aws secretsmanager get-secret-value \
  --secret-id diku-web/postgres-password \
  --query SecretString --output text)
export POSTGRES_PASSWORD
docker-compose up -d
```