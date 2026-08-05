#!/bin/bash
set -e

##############################################################################
# DikuWeb Deployment Script (No .env Files)
# 
# Usage:
#   ./deploy.sh [environment]
#
# Environments:
#   production (default)
#   staging
#   dev
#
# Requirements:
#   - Docker & Docker Compose installed
#   - POSTGRES_PASSWORD set in environment or config file
#   - Appropriate permissions to manage Docker
#
##############################################################################

ENVIRONMENT="${1:-production}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Source environment-specific config
if [ -f "$SCRIPT_DIR/deploy.$ENVIRONMENT.conf" ]; then
    set -a
    source "$SCRIPT_DIR/deploy.$ENVIRONMENT.conf"
    set +a
else
    echo "ERROR: Configuration file not found: deploy.$ENVIRONMENT.conf"
    exit 1
fi

# Validate required variables
required_vars=(
    "POSTGRES_DB"
    "POSTGRES_USER"
    "POSTGRES_PASSWORD"
    "REGISTRY"
    "IMAGE_REPO"
    "DIKUWEB_IMAGE_TAG"
    "DIKUWEB_ENV"
    "CLIENT_PORT"
)

for var in "${required_vars[@]}"; do
    if [ -z "${!var}" ]; then
        echo "ERROR: Required variable not set: $var"
        exit 1
    fi
done

echo "=========================================="
echo "DikuWeb Deployment"
echo "=========================================="
echo "Environment:  $ENVIRONMENT"
echo "Registry:     $REGISTRY"
echo "Image:        $IMAGE_REPO:$DIKUWEB_IMAGE_TAG"
echo "Database:     $POSTGRES_DB"
echo "Frontend:     Port $CLIENT_PORT"
echo "Backend Env:  $DIKUWEB_ENV"
echo "=========================================="
echo ""

# Export all variables for docker-compose
export POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD
export REGISTRY IMAGE_REPO DIKUWEB_IMAGE_TAG DIKUWEB_ENV
export CLIENT_PORT LOG_LEVEL MAX_CHARACTERS MAX_SESSIONS
export SESSION_TIMEOUT LINKDEAD_GRACE STARTING_ROOM SWAGGER_ENABLED
export ADMINER_PORT WEB_DEBUG_PORT

# Pull latest images
echo "[1/3] Pulling latest images..."
docker-compose pull

# Stop old containers (if any)
echo "[2/3] Stopping old containers..."
docker-compose down || true

# Start new containers
echo "[3/3] Starting containers..."
docker-compose up -d

echo ""
echo "✓ Deployment complete!"
echo ""
echo "Status:"
docker-compose ps

echo ""
echo "Container logs:"
docker-compose logs --tail=5

echo ""
echo "Waiting for health checks..."
sleep 5

echo ""
echo "Verifying health endpoints..."
if curl -sf http://localhost/health > /dev/null 2>&1; then
    echo "✓ Frontend health: OK"
else
    echo "✗ Frontend health: FAILED"
fi

if curl -sf http://localhost/api/health > /dev/null 2>&1; then
    echo "✓ API health: OK"
else
    echo "✗ API health: FAILED"
fi

echo ""
echo "Deployment Summary:"
echo "  Frontend: http://$(hostname -I | awk '{print $1}'):$CLIENT_PORT"
echo "  API:      http://$(hostname -I | awk '{print $1}'):$CLIENT_PORT/api"
echo "  Logs:     docker-compose logs -f"
echo ""