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

#### AI (optional)

AI-assisted document classification/summarization and statement parsing are optional — define no providers and the features are a graceful no-op. AI providers are **named config instances** under `Ai:Providers:{name}` (same shape as the OCR engines), selected via `Ai:DefaultProvider`.

> **Breaking (2026-07):** the flat `AI_ENDPOINT` / `AI_MODEL` / `AI_API_KEY` / `AI_IMAGE_INPUT` / `AI_IMAGE_RENDER_MAX_EDGE` env vars are **no longer read**. Because unconfigured AI is a silent no-op, a deployment that misses this rename keeps running but quietly stops classifying/summarizing new documents — migrate to the variables below.

| Variable | Default | Description |
|---|---|---|
| `Ai__Providers__{name}__Type` | — | **Required** for every provider: `OpenAiCompatible` or `AzureFoundry` |
| `Ai__Providers__{name}__Endpoint` | — | OpenAI-compatible base URL (e.g. `http://localhost:11434/v1`) or the Azure resource endpoint (e.g. `https://<res>.openai.azure.com`) |
| `Ai__Providers__{name}__Model` | — | Model tag (e.g. `gemma-4-e4b`) or the Azure **deployment name** |
| `Ai__Providers__{name}__ApiKey` | — | API key; optional for `OpenAiCompatible` (ignored by Ollama), **required** for `AzureFoundry` |
| `Ai__Providers__{name}__ImageInput` | `true` | Attach a rendered page-1 image to classify/parse calls (turn off for text-only models) |
| `Ai__Providers__{name}__ImageRenderMaxEdge` | `1024` | Long-edge pixel budget for that page render (256–4096) |
| `Ai__Providers__{name}__MaxInputChars` | `12000` | Character budget for document text in the classification prompt |
| `Ai__Providers__{name}__StatementMaxInputChars` | `120000` | Hard cap for statement text sent whole to statement parsing |
| `Ai__DefaultProvider` | — | Which provider the app uses. Optional when exactly one provider is defined; required with two or more |

A misconfigured provider never stops the app from starting — it shows as *unavailable, with the reason* on the admin **AI** page, which also has a per-provider **Test connection** probe.

For local Aspire dev, set these in **AppHost user-secrets** — AppHost forwards the whole `Ai:` section verbatim as the env vars above:

```bash
cd Homestead.AppHost
dotnet user-secrets set "Ai:Providers:local:Type" "OpenAiCompatible"
dotnet user-secrets set "Ai:Providers:local:Endpoint" "http://localhost:11434/v1"
dotnet user-secrets set "Ai:Providers:local:Model" "gemma-4-e4b"
# a second provider, e.g. Azure AI Foundry:
# dotnet user-secrets set "Ai:Providers:foundry:Type" "AzureFoundry"
# dotnet user-secrets set "Ai:Providers:foundry:Endpoint" "https://<res>.openai.azure.com"
# dotnet user-secrets set "Ai:Providers:foundry:ApiKey" "<key>"
# dotnet user-secrets set "Ai:Providers:foundry:Model" "<deployment-name>"
dotnet user-secrets set "Ai:DefaultProvider" "local"
```

#### Other

| Variable | Default | Description |
|---|---|---|
| `FileStorage__MediaPath` | `/media` | Path for document storage |
| `FileStorage__CachePath` | `/cache` | Path for generated thumbnail cache |
| `Ocr__Engines__tesseract__TessDataPath` | `/usr/share/tessdata` | Directory of pinned Tesseract `.traineddata` models — see [OCR Models](#ocr-models-tesseract) |
| `Ocr__Engines__{key}__*` | — | Defines an additional text-extraction engine instance — see [Text extraction engines](#text-extraction-engines) |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | — | OpenTelemetry collector endpoint (optional) |

### Volumes

| Path | Purpose |
|---|---|
| `/media` | Uploaded documents — **back this up** |
| `/cache` | Generated document thumbnails (can be regenerated on demand) |

The paths above are the container defaults; back the `/media` volume with durable storage. For **local Aspire dev** the AppHost instead points the WebApi at `.homestead-data/` at the repo root (auto-provisioned on first upload, gitignored). That directory lives under the mounted workspace, so it survives reboots, devcontainer rebuilds, and `/tmp` reaping — dev no longer writes uploads to `/tmp`.

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

### Text extraction engines

Text extraction engines are **defined in configuration** under `Ocr:Engines:{key}`, where `{key}` is
an arbitrary instance name you choose. Endpoints and API keys are deployment facts, so they live in
env vars / user-secrets where your existing secret tooling already works — there is no engines table
and no API keys in a `pg_dump`. Which engine to *use* is product data: the Documents settings page
sets a household-wide default, and the reprocess dialogs can override it per run.

Two types are supported:

| `Type` | Cost | Notes |
|---|---|---|
| `Tesseract` | Free, local | Always available. For PDFs, reads any embedded text layer and only OCRs text-less pages. |
| `AzureDocumentIntelligence` | Billed per page | Optional. Sends the whole file; with `prebuilt-layout` returns Markdown with HTML tables, which is what makes table-heavy documents (e.g. bank statements) parse well. |

Engines are **named instances**, so one Azure resource can back several engines that differ only by
model:

```jsonc
"Ocr": {
  "Engines": {
    "tesseract":    { "Type": "Tesseract", "TessDataPath": "/usr/share/tessdata",
                      "Languages": ["Eng"], "Dpi": 300, "Psm": 3, "Oem": 3 },
    "azure-layout": { "Type": "AzureDocumentIntelligence", "Endpoint": "https://<res>.cognitiveservices.azure.com",
                      "ApiKey": "<key>", "ModelId": "prebuilt-layout", "OutputContentFormat": "Markdown" },
    "azure-read":   { "Type": "AzureDocumentIntelligence", "Endpoint": "https://<res>.cognitiveservices.azure.com",
                      "ApiKey": "<key>", "ModelId": "prebuilt-read" }
  }
}
```

As env vars, each leaf is a stock double-underscore key, e.g.
`Ocr__Engines__azure-layout__Endpoint`. A `tesseract` instance is always present with the defaults
above even if you configure nothing at all. `OutputContentFormat` defaults to `Markdown` for
`prebuilt-layout` and `Text` otherwise; `Markdown` with any other model (e.g. `prebuilt-read`) is
invalid and the engine will report itself unavailable with that reason rather than failing at
extraction time. The model id is matched exactly, so a near-miss like `prebuilt-layout-v2` counts as
"any other model": it defaults to `Text`, and asking it for `Markdown` is rejected. Only that explicit
pairing is caught — nothing here knows Azure's model list, so a mistyped id on its own still reports
as configured and fails at the Azure call.

**`Type` is required for every engine except `tesseract`** (whose type is implied by its name, so the
image can override only its `TessDataPath`). An engine with no `Type` is reported as unavailable
rather than being assumed to be Tesseract — otherwise forgetting it on an Azure engine would silently
run local OCR under the name you gave it.

An engine can be defined but unusable — a missing endpoint, a bad model/format pair, a missing
`Type`. Nothing is validated at startup (a typo must not stop the app booting), so such an engine
shows up in the engine pickers as unavailable with the reason attached. Selecting one explicitly is
rejected with a 400; the *default* path falls back to built-in Tesseract and records the substitution
in the version's extraction provenance, so it is never silent.

> ⚠️ **Breaking change:** the Tesseract config moved from `Ocr:Tesseract:*` to
> `Ocr:Engines:tesseract:*`. If you set `Ocr__Tesseract__TessDataPath`, rename it to
> `Ocr__Engines__tesseract__TessDataPath` — the old name is now ignored, which silently falls back
> to the relative `tessdata` default and breaks OCR.

> ⚠️ **Azure free tier (F0) analyzes only the first 2 pages** of a document and reports success. It
> is not detectable from config, so a 30-page statement yields 2 pages of text. The app logs a
> warning when the analyzed page count is short of the source, and records the analyzed count in the
> version's extraction provenance.

### OCR Models (Tesseract)

OCR uses Tesseract (invoked as an external CLI) with models pinned to an exact
[`tessdata_best`](https://github.com/tesseract-ocr/tessdata_best) commit, so OCR output stays
reproducible across dev, test, and prod and over time — it does not float with whatever the OS
packager happens to ship. Three committed files are the source of truth:

| File | Purpose |
|---|---|
| `Homestead.WebApi/tessdata.manifest` | Languages to provision, one code per line (`eng`, `fra`) |
| `Homestead.WebApi/tessdata.ref` | The pinned `tessdata_best` commit SHA |
| `scripts/fetch-tessdata.sh` | Downloads each manifest language at that ref into a destination directory |

The single runtime knob is the **`Ocr__Engines__tesseract__TessDataPath`** env var, which points Tesseract at
the provisioned directory. Every environment provisions the same way and converges on
`/usr/share/tessdata`:

- **Prod** — the Docker image runs the script at build time and sets the env var (nothing to do).
- **Dev container** — `post-create.sh` installs the Tesseract binary and runs the script; the env
  var is set in `devcontainer.json`'s `remoteEnv`.
- **CI** — the `integration-test` job installs the binary, runs the script, and sets the env var.

`dotnet build` / `dotnet test` never touch the network — provisioning happens only in those hooks.

**Enabled vs. available languages.** The script provisions every manifest language (`eng` + `fra`),
but only the languages listed in `Ocr:Engines:tesseract:Languages` (default `["Eng"]`) are used at
extraction time. The French models ship but stay dormant unless a self-hoster enables `Fra`.

> ⚠️ **Every enabled language must also be in `tessdata.manifest`.** Tesseract is invoked once with
> the combined `-l eng+fra` form, so a single missing model makes the *whole* call fail and **all**
> OCR for that deployment silently yields no extracted text (it's logged as a warning, not surfaced
> as an error). **Upgrade note:** the image now provisions only `eng`+`fra`; it previously bundled
> ~20 language packs via the OS packager. If you had enabled any other language in
> `Ocr:Engines:tesseract:Languages`, add it to `tessdata.manifest` (and re-provision) before upgrading.

**Adding a language.** Append its `tessdata_best` filename stem to `tessdata.manifest` (e.g. `deu`),
re-run the provisioning hook, and enable it in `Ocr:Engines:tesseract:Languages`. Optionally bump
`tessdata.ref` to a newer commit to refresh every model at once. The manifest stem must match the
lowercased `TesseractLanguage` enum name the engine resolves (e.g. `Fra` → `fra`). CJK is a known
exception: the enum's `Zho` resolves to `zho`, but the actual file is `chi_sim`, so adding Chinese
requires both a `chi_sim` manifest line **and** an explicit `Zho → "chi_sim"` mapping in
`TesseractTextExtractionEngine.ResolveLanguage`.

**Bare-metal dev** (running the WebApi natively, outside the dev container — e.g. macOS): run the
script once into a writable directory and point the env var there:

```bash
sh scripts/fetch-tessdata.sh "$HOME/.local/share/tessdata"
export Ocr__Engines__tesseract__TessDataPath="$HOME/.local/share/tessdata"
```

## Development

### Dev Container Setup

**Prerequisites:** [Docker Desktop](https://www.docker.com/products/docker-desktop/) and [VS Code](https://code.visualstudio.com/) with the [Dev Containers](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers) extension.

1. Ensure your SSH key is loaded so Git works inside the container:
   ```bash
   ssh-add --apple-use-keychain ~/.ssh/id_ed25519
   ```
2. Open the repository in VS Code and click **"Reopen in Container"** when prompted.
3. Once the container is ready, start the full stack (from the repo root):
   ```bash
   aspire run   # auto-detects the AppHost
   ```
   Backend hot reload is on by default via the committed `aspire.config.json` (`features.defaultWatchEnabled=true`) — most C# edits are hot-reloaded into the running WebApi in place, and edits watch can't patch restart just the WebApi; either way, no manual stop/re-run. The Angular dev server already live-reloads on its own. `cd Homestead.AppHost && dotnet run` starts the stack without watch.

See [CLAUDE.md](CLAUDE.md) for project structure and test commands.
