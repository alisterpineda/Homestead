# Homestead

A document management application with a .NET 10 backend API and Angular frontend. Features OCR processing, full-text search, document versioning, tagging, and multi-user support with OIDC authentication.

## Docker Deployment

Homestead ships as a single Docker image containing both the API and frontend. It requires a PostgreSQL database and optionally an OIDC provider (e.g., Authentik, Keycloak, Auth0) for single sign-on.

### Quick Start

1. Create a `docker-compose.yml`:

```yaml
services:
  homestead:
    image: homestead:latest
    build: .
    restart: unless-stopped
    ports:
      - 8080:8080
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      DB_HOST: postgres
      DB_PASSWORD: changeme
    volumes:
      - media:/media
      - cache:/cache

  postgres:
    image: postgres:17-alpine
    restart: unless-stopped
    environment:
      POSTGRES_DB: homestead
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: changeme
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d homestead"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  media:
  cache:
  pgdata:
```

2. Start the stack:

```bash
docker compose up -d
```

3. Visit `https://homestead.example.com` — you'll be greeted by the setup page to create your admin account.

### Single Sign-On (OIDC)

Homestead supports any OpenID Connect provider. To enable SSO, add the OIDC environment variables to the `homestead` service:

```yaml
services:
  homestead:
    # ... same as above, plus:
    environment:
      DB_HOST: postgres
      DB_PASSWORD: changeme
      OIDC_AUTHORITY: https://idp.example.com/...
      OIDC_CLIENT_ID: your-client-id
      OIDC_CLIENT_SECRET: your-client-secret
      OIDC_DISPLAY_NAME: Sign in with SSO
```

#### Provider Setup

1. In your OIDC provider, create a new client/application:
   - **Redirect / Callback URI:** `https://homestead.example.com/api/auth/oidc/callback`
   - **Scopes:** `openid`, `email`, `profile`
   - Note the **Client ID** and **Client Secret**

2. Set `OIDC_AUTHORITY` to the provider's issuer URL (the base URL where `/.well-known/openid-configuration` is served).

3. (Optional) To map a provider group/role to the Homestead admin role, set `OIDC_ROLE_CLAIM` to the claim type your provider uses (e.g., `groups`, `roles`) and `OIDC_ADMIN_CLAIM_VALUE` to the value that should grant admin access. Users matching that claim will automatically receive admin privileges on login.

Users logging in via OIDC are automatically provisioned — no manual account creation needed.

### Environment Variables

#### Database (required)

| Variable | Default | Description |
|---|---|---|
| `DB_PASSWORD` | — | PostgreSQL password **(required)** |
| `DB_HOST` | `localhost` | PostgreSQL hostname |
| `DB_PORT` | `5432` | PostgreSQL port |
| `DB_NAME` | `homestead` | Database name |
| `DB_USER` | `postgres` | PostgreSQL username |

#### OIDC (optional)

All three of `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, and `OIDC_CLIENT_SECRET` must be set to enable OIDC. If any are missing, OIDC is disabled and only local login is available.

| Variable | Default | Description |
|---|---|---|
| `OIDC_AUTHORITY` | — | OIDC provider authority URL |
| `OIDC_CLIENT_ID` | — | OIDC client ID |
| `OIDC_CLIENT_SECRET` | — | OIDC client secret |
| `OIDC_DISPLAY_NAME` | `SSO` | Label shown on the login button |
| `OIDC_ROLE_CLAIM` | — | Claim type used for role mapping (e.g., `groups`) |
| `OIDC_ADMIN_CLAIM_VALUE` | — | Claim value that grants the admin role |
| `OIDC_DISABLE_LOCAL_LOGIN` | `false` | Set to `true` to hide the local login form |
| `OIDC_ADDITIONAL_SCOPES` | — | Comma-separated additional OAuth scopes |

#### Other

| Variable | Default | Description |
|---|---|---|
| `FileStorage__MediaPath` | `/media` | Path for document storage |
| `FileStorage__CachePath` | `/cache` | Path for generated thumbnail cache |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | — | OpenTelemetry collector endpoint (optional) |

### Volumes

| Path | Purpose |
|---|---|
| `/media` | Uploaded documents — **back this up** |
| `/cache` | Generated document thumbnails (can be regenerated on demand) |

### Health Checks

| Endpoint | Purpose |
|---|---|
| `GET /health` | Full health check (includes database connectivity) |
| `GET /alive` | Liveness probe |

### First-Time Setup

On the first visit, the application detects that no users exist and presents a setup page. Create your admin account here — this endpoint is automatically disabled once the first user is created.

After setup, additional users can be created from the admin panel at `/admin`, or they can log in via OIDC and be auto-provisioned.

### Notes

- The container listens on port **8080** (HTTP). TLS termination is handled by your reverse proxy.
- Forwarded headers are enabled by default, so `X-Forwarded-For` / `X-Forwarded-Proto` from your reverse proxy are trusted automatically.
- Database migrations run automatically on startup.

## Development

### Dev Container Setup

**Prerequisites:** [Docker Desktop](https://www.docker.com/products/docker-desktop/) and [VS Code](https://code.visualstudio.com/) with the [Dev Containers](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers) extension.

1. Ensure your SSH key is loaded so Git works inside the container:
   ```bash
   ssh-add --apple-use-keychain ~/.ssh/id_ed25519
   ```
2. Open the repository in VS Code and click **"Reopen in Container"** when prompted.
3. Once the container is ready, start the full stack:
   ```bash
   cd Homestead.AppHost && dotnet run
   ```

See [CLAUDE.md](CLAUDE.md) for project structure and test commands.
