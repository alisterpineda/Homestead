# Homestead

A household management application for documents and personal finances, with a .NET 10 backend API and Angular frontend. Features OCR processing, full-text search, document versioning, tagging, net-worth tracking across multi-currency accounts, and multi-user support with OIDC authentication.

## Docker Deployment

Homestead ships as a single Docker image containing both the API and frontend. It requires a PostgreSQL database and an OIDC provider (e.g., Authentik, Keycloak, Auth0). OIDC is the only sign-in method — the application refuses to start without it.

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
      OIDC_AUTHORITY: https://idp.example.com/...
      OIDC_CLIENT_ID: your-client-id
      OIDC_CLIENT_SECRET: your-client-secret
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

3. Visit `https://homestead.example.com` and sign in with your OIDC provider — the first user to sign in is automatically activated and granted the admin role.

### Single Sign-On (OIDC)

Homestead authenticates exclusively through OpenID Connect; any compliant provider works. The three core variables (`OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, `OIDC_CLIENT_SECRET`) are required — see the Quick Start compose above. Optional variables customize the integration:

```yaml
services:
  homestead:
    # ... same as above, plus optionally:
    environment:
      OIDC_DISPLAY_NAME: Sign in with SSO
      OIDC_ROLE_CLAIM: groups
      OIDC_ADMIN_CLAIM_VALUES: homestead-admins
```

#### Provider Setup

1. In your OIDC provider, create a new client/application:
   - **Redirect / Callback URI:** `https://homestead.example.com/signin-oidc`
   - **Post-logout Redirect URI** (if your provider requires one): `https://homestead.example.com/signout-callback-oidc`
   - **Scopes:** `openid`, `email`, `profile`
   - Note the **Client ID** and **Client Secret**

2. Set `OIDC_AUTHORITY` to the provider's issuer URL (the base URL where `/.well-known/openid-configuration` is served).

3. To map provider groups/roles to the Homestead admin role, set `OIDC_ROLE_CLAIM` to the claim type your provider uses (e.g., `groups`, `roles`) and `OIDC_ADMIN_CLAIM_VALUES` to a comma-separated list of values that should grant admin access.

   > **The provider is the source of truth for the admin role on every sign-in:** a matching claim grants Admin, a missing one revokes it (the user drops back to Contributor). This sync runs even when the mapping is **not** configured — without `OIDC_ROLE_CLAIM`/`OIDC_ADMIN_CLAIM_VALUES`, every admin (including the auto-promoted first user, and anyone promoted from `/admin`) is demoted to Contributor on their next sign-in. Configure the mapping in any deployment that needs a durable admin.

Users signing in via OIDC are automatically provisioned — no manual account creation needed. New accounts start **deactivated** — sign-in is refused with an "account inactive" notice on the login page — until an admin activates them from `/admin`; only the very first user is activated automatically.

### Environment Variables

#### Database (required)

| Variable | Default | Description |
|---|---|---|
| `DB_PASSWORD` | — | PostgreSQL password **(required)** |
| `DB_HOST` | `localhost` | PostgreSQL hostname |
| `DB_PORT` | `5432` | PostgreSQL port |
| `DB_NAME` | `homestead` | Database name |
| `DB_USER` | `postgres` | PostgreSQL username |

#### OIDC (required)

All three of `OIDC_AUTHORITY`, `OIDC_CLIENT_ID`, and `OIDC_CLIENT_SECRET` must be set — OIDC is the only sign-in method, and the application fails at startup if any of them are missing.

| Variable | Default | Description |
|---|---|---|
| `OIDC_AUTHORITY` | — | OIDC provider authority URL **(required)** |
| `OIDC_CLIENT_ID` | — | OIDC client ID **(required)** |
| `OIDC_CLIENT_SECRET` | — | OIDC client secret **(required)** |
| `OIDC_METADATA_ADDRESS` | — | Explicit metadata document URL, if it isn't at the authority's `/.well-known/openid-configuration` |
| `OIDC_DISPLAY_NAME` | `SSO` | Label shown on the login button |
| `OIDC_ROLE_CLAIM` | — | Claim type used for role mapping (e.g., `groups`) |
| `OIDC_ADMIN_CLAIM_VALUES` | — | Comma-separated claim values that grant the admin role |
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

There is no setup wizard. The first user to sign in via OIDC bootstraps the system: that account is automatically activated and granted the admin role. (To *keep* the admin role across sign-ins, configure the role-claim mapping — see the note in Provider Setup.) Every subsequent OIDC sign-in is provisioned **deactivated** — as a Contributor, or as an Admin when their claim matches `OIDC_ADMIN_CLAIM_VALUES` — and must be activated by an admin from `/admin`.

Admins can also pre-create users from the admin panel at `/admin` — pre-created accounts are matched by email (an exact, case-sensitive match against the email claim your IdP sends) and are active immediately when the user first signs in via OIDC.

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
