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

### Connect your Claude (MCP)

Homestead can optionally serve an [MCP](https://modelcontextprotocol.io) server at `/mcp`, so household members can ask their own Claude — claude.ai, Claude Desktop or Claude Code — to search and organise their documents. Claude signs in through the same identity provider as the web app, using a second OAuth client you register for it, and acts as the signed-in user: same role, same document access, and every change shows in the activity stream as theirs. The steps below are for Authentik; another IdP works the same way if it issues signed JWT access tokens whose `aud` is the client id and whose `sub` matches the web client's. The product design is in [docs/product-design/shared/mcp.md](docs/product-design/shared/mcp.md).

#### Authentik provider setup

1. Create an **OAuth2/OpenID Provider** named "Homestead MCP":
   - **Client type:** Confidential. **Flow:** authorization code (the same authorization flow your web provider uses).
   - **Redirect URIs:**
     - `https://claude.ai/api/mcp/auth_callback`
     - `https://claude.com/api/mcp/auth_callback`
     - `http://localhost:8765/callback`
     - `http://127.0.0.1:8765/callback`

     The first two serve claude.ai and Claude Desktop (Anthropic may move the callback between them, so register both); the last two serve Claude Code.
   - **Scopes:** the `openid`, `profile` and `email` mappings **plus `offline_access`**. Without `offline_access` Authentik issues no refresh token, and Claude loses the connection at every token expiry.
   - **Signing key:** an RS256 or ES256 certificate. This is **required** — with no signing key Authentik signs tokens with HS256 keyed by the client secret (which every connected Claude holds), and Homestead rejects HS256.
   - **Subject mode:** "hashed user ID" — the same as your web provider, so both providers give a user the same `sub`. Homestead links Claude to an account by that subject.
   - **Issuer mode:** per provider (the issuer is derived from the application slug).
   - **Access token validity:** leave it at the default (1 hour). Raise it only if the refresh check in [Verify a token](#verify-a-token) still fails with `offline_access` selected, and never above 1 hour — it is how long a removed user keeps access.
   - Note the **Client ID** and **Client Secret**.
2. Create an **Application** for that provider. Its **slug** fixes the issuer URL (`https://<authentik>/application/o/<slug>/`), so choose it once. Bind it to household users only — anyone the binding admits can try to connect, and only users with a Homestead account get further.
3. Configure Homestead (see [MCP (optional)](#mcp-optional)):

   ```yaml
   environment:
     Mcp__Authority: https://authentik.example.com/application/o/homestead-mcp/
     Mcp__Audience: <the provider's client id>
     Mcp__PublicUrl: https://homestead.example.com/mcp
   ```

   `Mcp__Authority` is the `issuer` from `https://<authentik>/application/o/<slug>/.well-known/openid-configuration`, **copied verbatim** — trailing slash included; Claude clients compare it byte-for-byte. `Mcp__PublicUrl` is `https://<host>/mcp` exactly as users will type it. On restart the log shows `MCP server enabled at /mcp` with the audience, issuer and resource it is using.

#### Reverse proxy

- **Preserve the `Host` header** on requests to Homestead. The sign-in challenge Homestead sends points Claude at its metadata document on the host the request arrived on.
- **Do not block Anthropic's IP range `160.79.104.0/21`** — for Homestead *or* Authentik. claude.ai and Claude Desktop connectors run on Anthropic's servers, which fetch Homestead's metadata and Authentik's discovery document and exchange tokens from that range. A WAF rule, geo-block or access gateway (forward auth, VPN-only access) in front of either breaks the flow. Claude Code connects from your own machine instead.
- Authentik's **discovery and token endpoints must answer within 10 seconds**, or claude.ai gives up.

#### Verify a token

Decode one access token from the MCP provider (it is a JWT) and check:

- the header's `alg` is `RS256` or `ES256`;
- `aud` equals the provider's client id (`Mcp__Audience`);
- `sub` and `email` are present;
- `iss` equals `Mcp__Authority` exactly.

Then connect a client and call `whoami`, wait past the access-token lifetime, and call it again: it must answer without a new sign-in prompt. If it does not, the provider is missing its `offline_access` mapping.

#### First-time users

Each user must **sign in to the Homestead web app once** before connecting Claude — that sign-in is what records their identity, and the MCP door only ever looks accounts up, never creates them. Until they do, the connector reports "No Homestead account is linked to this identity. Sign in to Homestead in the browser first." Deactivated accounts get the usual "account inactive" refusal.

#### Access changes

- **Deactivating or demoting a user on Homestead's `/admin` page is immediate** on both the web app and Claude.
- **Removing the user from the Authentik application** ends their Claude access when their current access token expires (at most the token validity, 1 hour by default).
- **Changing their Authentik groups alone does not change their Homestead role** — the admin role is re-synced from the groups claim only at their next web sign-in. Demote them in `/admin` as well if it must take effect now.

#### Connect from claude.ai / Claude Desktop

In claude.ai (Claude Desktop uses the same connectors): **Settings → Connectors → Add custom connector**, enter a name and the URL `https://homestead.example.com/mcp`, and under the OAuth client options choose **Use your own OAuth client** and enter the MCP provider's client id and secret. Save, then **Connect** and sign in with Authentik.

#### Connect from Claude Code

```bash
claude mcp add --transport http homestead https://homestead.example.com/mcp \
  --client-id <client id> --client-secret --callback-port 8765
```

`--client-secret` prompts for the secret. Then run `/mcp` in Claude Code, pick `homestead` and sign in in the browser it opens. Port 8765 must be free on your machine — it is the callback port registered on the provider. `claude mcp login homestead` and `claude mcp logout homestead` sign in again or forget the tokens.

#### What Claude can do

Claude can see who it is signed in as (`whoami`), search documents with the same search as the web app plus date, expiry, untagged and pending-review filters (`search_documents`), read a document's metadata, AI suggestions and, on request, its extracted text (`get_document`), and list tags and parties (`list_tags`, `list_parties`). Contributors and Admins can also edit a document's name, dates, notes, tags and parties (`update_document`), add or remove tags and parties on up to 100 documents at once (`bulk_update_documents`), approve or decline AI suggestions (`resolve_suggestions`), create a tag or party (`create_tag`, `create_party`), leave a comment (`add_comment`) and move up to 20 documents to the trash (`move_to_trash`); Viewers and Guests get the read tools only. Edits apply directly, as the user's own; nothing is deleted permanently — Claude cannot upload, permanently delete or empty the trash, and anything it trashes can be restored in the web app. Tag paths and party names must already exist unless the user asks Claude for a new one. Document text is passed to Claude as data, marked untrusted, never as instructions.

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

AI-assisted document classification/summarization, statement parsing and payee suggestions run on the **Claude API** (Anthropic) and are optional — leave `Ai__ApiKey` / `Ai__Model` unset and the features are a graceful no-op.

> **Breaking (2026-10):** AI is Claude-only, configured by one `Ai:*` section. The named-provider shape — `Ai__Providers__{name}__*` and `Ai__DefaultProvider` — is **no longer read**, and with it the OpenAI-compatible and Azure provider types and the `Temperature` / `StructuredOutput` knobs. Migrate by setting `Ai__ApiKey` and `Ai__Model`. Because unconfigured AI is a silent no-op, the app detects the old keys: while they are set and the new ones aren't, the admin **AI** page shows *Legacy AI config found* and the WebApi logs a warning at startup. Both shapes can be set side by side during the move (the new keys win), so keep the old ones until you're happy.
>
> (The older flat `AI_ENDPOINT` / `AI_MODEL` / `AI_API_KEY` / `AI_IMAGE_INPUT` / `AI_IMAGE_RENDER_MAX_EDGE` vars, retired in 2026-07, are not read either.)

| Variable | Default | Description |
|---|---|---|
| `Ai__ApiKey` | — | **Required.** A Claude API key |
| `Ai__Model` | — | **Required.** A Claude model ID, e.g. `claude-opus-5-5` |
| `Ai__Endpoint` | — | Optional base-URL override (an absolute URI); unset uses the Claude API |
| `Ai__Effort` | `medium` | How much the model thinks per request: `low`, `medium`, `high`, `xhigh` or `max`. Lowering it to `low` is the first thing to try if cost matters |
| `Ai__RefusalFallback` | `true` | When the model declines a request (a safety-classifier refusal), re-run it on a fallback model chosen by Anthropic, inside the same call. Both attempts are billed. `false` makes a refusal final |
| `Ai__ImageInput` | `true` | Attach a rendered page-1 image to classify/parse calls (the image is most of a classify's input tokens) |
| `Ai__ImageRenderMaxEdge` | `1024` | Long-edge pixel budget for that page render (256–4096) |
| `Ai__MaxInputChars` | `12000` | Character budget for document text in the classification prompt |
| `Ai__StatementMaxInputChars` | `120000` | Hard cap for statement text sent whole to statement parsing |
| `Ai__PdfInput` | `true` | Classify a PDF by sending the PDF itself (Claude reads every page's text and layout) instead of the OCR text and page image. Only for background classifies whose scope includes name, parties or summary, and only within the two caps below; anything else — and a PDF the API rejects — uses the OCR text + page image. Watch the token meter on your first bulk classify |
| `Ai__PdfInputMaxPages` | `20` | Longest PDF sent as a PDF (1–100; pages drive tokens) |
| `Ai__PdfUpload` | `true` | Send a PDF by uploading it to the Claude API's Files API and referencing it, instead of base64 inside the request. The upload is deleted after the call and set to expire after an hour in case the delete never runs. Turn it off for an `Ai__Endpoint` proxy that doesn't serve `/v1/files` (with it on, such a proxy makes every PDF classify fall back to the OCR text + page image, with a warning), or if you rely on zero data retention (see *Privacy of uploaded PDFs* below). An inline PDF holds roughly 20× the file size in memory while it is sent |
| `Ai__PdfInputMaxBytes` | 20 MiB uploaded, 4 MiB inline | Largest PDF sent as a PDF (64 KiB–20 MiB). Unset, it follows `Ai__PdfUpload`: the inline default stays small because of that memory cost |

A misconfigured section never stops the app from starting — it shows as *unavailable, with the reason* on the admin **AI** page, which also has a **Test connection** probe. The probe sends a real structured-output request at the configured effort, so a wrong key or model ID fails there with the API's own message.

**Privacy of uploaded PDFs.** An uploaded PDF sits in the Files API storage of the key's Anthropic workspace for the length of the call (an hour at most, if the delete fails), where any other API key in that workspace could reference it. Give Homestead's key a workspace of its own. Files API storage is not covered by zero-data-retention arrangements; inline PDFs are.

**Cost.** Every classify, summary, statement parse and payee pass is a paid call. Put a **workspace spend limit** on the key in the Anthropic Console — that is the real guard; the Console is also the authoritative usage view. The Aspire dashboard shows per-call token counts (`homestead.ai.tokens`) for a quick look.

For local Aspire dev, set these in **AppHost user-secrets** — AppHost forwards the whole `Ai:` section verbatim as the env vars above:

```bash
cd Homestead.AppHost
dotnet user-secrets set "Ai:ApiKey" "<key>"
dotnet user-secrets set "Ai:Model" "claude-opus-5-5"
# optional:
# dotnet user-secrets set "Ai:Effort" "low"
```

In Docker, keep the key out of the compose file: put `Ai__ApiKey` in an `env_file` that is not committed (or a secrets mechanism that ends up as that env var).

#### Other

| Variable | Default | Description |
|---|---|---|
| `FileStorage__MediaPath` | `/media` | Path for document storage |
| `FileStorage__CachePath` | `/cache` | Path for generated thumbnail cache |
| `Ocr__Engines__tesseract__TessDataPath` | `/usr/share/tessdata` | Directory of pinned Tesseract `.traineddata` models — see [OCR Models](#ocr-models-tesseract) |
| `Ocr__Engines__{key}__*` | — | Defines an additional text-extraction engine instance — see [Text extraction engines](#text-extraction-engines) |
| `Ocr__DefaultEngine` | `tesseract` | Which engine uploads and un-pinned reprocess runs use — an `Ocr__Engines__{key}` name. A name matching no engine falls back to `tesseract` and records the substitution in the version's provenance |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | — | OpenTelemetry collector endpoint (optional) |

#### Price sources (optional)

External commodity price sources (Bank of Canada FX rates, Alpha Vantage security prices) are **off by default**. Enablement and credentials are deployment facts, so they live in config under `PriceSources:{Source}` — never in the database. The Finances → Price sources pages report each source's state read-only; what they still edit is product data (the watch-list and the import schedule).

| Variable | Default | Description |
|---|---|---|
| `PriceSources__BankOfCanada__Enabled` | `false` | Enable Bank of Canada Valet FX imports (no key needed) |
| `PriceSources__BankOfCanada__BaseUrl` | `https://www.bankofcanada.ca/valet/` | Valet API root |
| `PriceSources__AlphaVantage__Enabled` | `false` | Enable Alpha Vantage security-price imports |
| `PriceSources__AlphaVantage__ApiKey` | — | **Required when enabled.** Get one free at <https://www.alphavantage.co/support/#api-key> |
| `PriceSources__AlphaVantage__BaseUrl` | `https://www.alphavantage.co/query` | Alpha Vantage query endpoint |

An enabled source with invalid config (e.g. Alpha Vantage without an `ApiKey`) never stops the app from starting — its page shows **Not configured, with the reason**, Run now is disabled, and scheduled imports fail with `source_not_configured`. Enablement alone still admits the source's *existing* prices to valuation.

For local Aspire dev, set these in **AppHost user-secrets** — AppHost forwards the whole `PriceSources:` section verbatim as the env vars above:

```bash
cd Homestead.AppHost
dotnet user-secrets set "PriceSources:BankOfCanada:Enabled" "true"
dotnet user-secrets set "PriceSources:AlphaVantage:Enabled" "true"
dotnet user-secrets set "PriceSources:AlphaVantage:ApiKey" "<key>"
```

> **Breaking (2026-09):** the in-app **Enabled** toggle and the API key / base URL fields on each price source's page are **no longer read**, and the `IsEnabled` / `Config` columns are dropped from `finances.commodity_price_source_settings`. Re-enter them as the config above — until you do, every external source is disabled and its prices are excluded from valuation.

#### Backup targets

Backup is off until at least one target is defined. `{key}` is any name you choose; it identifies the
target on `/admin/backup` and is what backup pairings are stored against, so renaming it orphans them.

| Variable | Default | Description |
|---|---|---|
| `Backup__Targets__{key}__Kind` | — | **Required**: `Local` or `GoogleDrive` |
| `Backup__Targets__{key}__Path` | — | `Local` only: the mirror root. Must not overlap `FileStorage__MediaPath` or `__CachePath` |
| `Backup__Targets__{key}__ClientId` | — | `GoogleDrive` only: your own OAuth client id — see [Google Drive backup setup](docs/google-drive-backup-setup.md) |
| `Backup__Targets__{key}__ClientSecret` | — | `GoogleDrive` only: that client's secret. Google shows it **once**, at creation |
| `Backup__Targets__{key}__RootFolderPath` | `Homestead/Backup` | `GoogleDrive` only: the app-managed folder in My Drive, as a `/`-separated path. Applied when the folder is first created; editing it later does not move an existing mirror |
| `Backup__Targets__{key}__MaxFilesPerBatch` | per-kind | Override how many file copies one batch attempts |
| `Backup__Targets__{key}__MaxBytesPerBatch` | per-kind | Override the byte budget for one batch — lower this on a slow uplink |
| `Backup__Targets__{key}__MaxOpsPerMetadataBatch` | per-kind | Override how many moves/deletes one batch attempts |

A `GoogleDrive` target also has to be **connected** — config only names the OAuth client; the grant
itself is minted by an admin at `google.com/device` from the target's card on `/admin/backup`. See
[Google Drive backup setup](docs/google-drive-backup-setup.md) for the one-time Google Cloud steps.

#### MCP (optional)

The MCP server for Claude clients is **off** unless `Mcp__Authority` and `Mcp__Audience` (and, in production, `Mcp__PublicUrl`) are set — see [Connect your Claude (MCP)](#connect-your-claude-mcp) for the IdP side. With nothing set, `/mcp` answers 404 and the startup log says so in one line. A section that is present but invalid never stops the app from starting: `/mcp` stays a 404 and the startup log has one warning with the reason.

| Variable | Default | Description |
|---|---|---|
| `Mcp__Authority` | — | **Required.** The MCP provider's issuer — the `issuer` value from its `/.well-known/openid-configuration`, copied verbatim (Authentik's ends in `/`) |
| `Mcp__Audience` | — | **Required.** The MCP OAuth client's id; access tokens must carry it as `aud` |
| `Mcp__PublicUrl` | — | **Required in production.** The exact public URL of the endpoint, as users will type it into Claude, e.g. `https://homestead.example.com/mcp`. Claude clients reject the server unless it matches byte-for-byte. Only Development infers it from the request |
| `Mcp__MetadataAddress` | — | Explicit discovery-document URL, if it isn't at the authority's `/.well-known/openid-configuration` |
| `Mcp__AdditionalScopes__0`, `__1`, … | — | Scopes to advertise on top of `openid profile email`. Never `offline_access`: Claude clients request it themselves when the IdP offers it, and Homestead drops it from this list (with a log line) |

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
and no API keys in a `pg_dump`. Which engine to *use* is also config: `Ocr:DefaultEngine` names the
default (shown read-only on the Documents settings page); the reprocess dialogs can override it per run.

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

> ⚠️ **Breaking (2026-09):** the household default engine saved on the Documents settings page is
> **no longer read**. Set `Ocr__DefaultEngine` instead — unset means `tesseract`, so a deployment that
> had picked an Azure engine in-app silently goes back to local OCR until the variable is set.

For local Aspire dev, set these in **AppHost user-secrets** — AppHost forwards the whole `Ocr:` section
verbatim as the env vars above:

```bash
cd Homestead.AppHost
dotnet user-secrets set "Ocr:Engines:azure-layout:Type" "AzureDocumentIntelligence"
dotnet user-secrets set "Ocr:Engines:azure-layout:Endpoint" "https://<res>.cognitiveservices.azure.com"
dotnet user-secrets set "Ocr:Engines:azure-layout:ApiKey" "<key>"
dotnet user-secrets set "Ocr:DefaultEngine" "azure-layout"
```

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
   Backend hot reload is on by default via the committed `aspire.config.json` (`features.defaultWatchEnabled=true`) — most C# edits are hot-reloaded into the running WebApi in place, and edits watch can't patch restart just the WebApi; either way, no manual stop/re-run. The Angular dev server already live-reloads on its own. The AppHost runs in CLI-bundle mode (`AspireUseCliBundle=true`): DCP and the dashboard come from the installed Aspire CLI's bundle (`~/.aspire/bundle`) rather than NuGet packages, so the CLI must be installed — the devcontainer's Aspire feature already does that. In bundle mode a plain `dotnet run` delegates to `aspire run --no-build`, which the CLI rejects while `defaultWatchEnabled` is on, so to start the stack **without** watch use `cd Homestead.AppHost && ASPIRE_SUPPRESS_CLI_RUN_HOOK=1 dotnet run` (the same bypass the CLI sets on its own launches); that runs the AppHost in-process with the bundle's DCP and dashboard.

See [CLAUDE.md](CLAUDE.md) for project structure and test commands.
