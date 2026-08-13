# Docker Setup Guide

This project uses Docker for the database in local development, and for full stack deployment in production.

## Local Development Setup

### Quick Start

1. Copy the environment template:
   ```bash
   cp .env.example .env
   ```
   (Customize `.env` if needed for different database credentials)

2. Start the PostgreSQL database:
   ```bash
   docker-compose up
   ```

3. Run the application locally:
   ```bash
   # Terminal 1: Start .NET backend
   dotnet run --project src/DikuWeb.Server

   # Terminal 2: Start React frontend
   cd client && npm run dev
   ```

4. Access the application:
   - **Web UI**: http://localhost:5173 (Vite dev server)
   - **API**: http://localhost:5050
   - **Database Admin**: http://localhost:8080 (Adminer)
   - **Swagger Docs**: http://localhost:5050/swagger

### Local Development Services

#### PostgreSQL (`:5432`)
- Development database running in container
- Adminer UI available at http://localhost:8080
- Credentials from `.env`
- Data persists in `pgdata` volume between runs

#### Adminer (`:8080`)
- Optional web-based PostgreSQL admin tool
- Browse tables, run queries, edit data
- Comment out in `docker-compose.yml` if not needed

### Development Commands

```bash
# Start database
docker-compose up

# Start in background
docker-compose up -d

# View logs
docker-compose logs -f postgres

# Stop services
docker-compose down

# Remove volume (deletes database)
docker-compose down -v

# Shell into database container
docker-compose exec postgres psql -U dikuweb -d dikuweb
```

### Running the Application

```bash
# Terminal 1: Backend
cd /path/to/diku-web
dotnet run --project src/DikuWeb.Server

# Terminal 2: Frontend
cd /path/to/diku-web/client
npm run dev
```

## Production Deployment

For production, use `docker-compose.prod.yml` which includes the full application stack:

```bash
# Set environment variables
export REGISTRY=ghcr.io
export IMAGE_REPO=your-org/diku-web
export DIKUWEB_IMAGE_TAG=v1.0.0

# Start production services
docker-compose -f docker-compose.prod.yml up -d
```

### Production Stack Includes

- **PostgreSQL** — Database
- **Web API** — ASP.NET Core backend (built)
- **Client** — React frontend (built + nginx)
- **No Adminer** — Database access via CLI only
- **No Swagger** — API documentation disabled
- **Hardened logging** — Warnings and errors only
- **Internal networking** — No debug ports exposed

## Configuration

### Development (.env)

```
# Database for local development
DB_NAME=dikuweb
DB_USER=dikuweb
DB_PASSWORD=password
```

### Production (.env or command-line)

```
# Pre-built images from registry
REGISTRY=ghcr.io
IMAGE_REPO=your-org/diku-web
DIKUWEB_IMAGE_TAG=latest
```

## Database Connection

Local development database connection string:

```
Server=localhost;Port=5432;Database=dikuweb;User Id=dikuweb;Password=password;
```

Used by:
- `appsettings.Development.json` in .NET backend
- `dotnet ef` migrations

## Troubleshooting

### Database won't start
```bash
# Check if port 5432 is in use
lsof -i :5432

# Remove and recreate volume
docker-compose down -v
docker-compose up postgres
```

### Can't connect to database from backend
```bash
# Verify connection string points to localhost:5432
# Check that docker-compose postgres service is healthy
docker-compose ps

# Test connection manually
docker-compose exec postgres psql -U dikuweb -d dikuweb
```

### Adminer won't load
- Ensure postgres service is healthy
- Check postgres credentials match `.env`
- Wait 15-20 seconds for database to initialize

### Port 5432 already in use
Edit `docker-compose.yml` to use different port:
```yaml
ports:
  - "127.0.0.1:5433:5432"  # Changed from 5432 to 5433
```

Then update connection string to use port 5433.

## Local Development vs Production

| Feature | Local Dev | Production |
|---------|-----------|------------|
| Database | Docker container | Docker container |
| Backend | Local (`dotnet run`) | Docker container |
| Frontend | Local (`npm run dev`) | Docker container |
| Database Admin (Adminer) | ✅ Included | ❌ Removed |
| Swagger API Docs | ✅ Enabled | ❌ Disabled |
| Logging | Debug verbose | Warnings only |
| Port Exposure | ✅ All ports | ❌ Internal only |

## Building Production Images

```bash
# Build .NET image
docker build -t ghcr.io/your-org/diku-web:v1.0.0 \
  -f Dockerfile \
  .

# Build client image
docker build -t ghcr.io/your-org/diku-web-client:v1.0.0 \
  -f client/Dockerfile \
  .

# Push to registry
docker push ghcr.io/your-org/diku-web:v1.0.0
docker push ghcr.io/your-org/diku-web-client:v1.0.0
```

## See Also

- [README.md](../README.md) - Project overview
- [PLAN.md](PLAN.md) - Architecture and design
