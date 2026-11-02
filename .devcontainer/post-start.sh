#!/usr/bin/env bash
# Runs on every container start (devcontainer.json postStartCommand).
set -uo pipefail

# Trust the ASP.NET HTTPS dev cert. On Linux this writes the PEM to ~/.aspnet/dev-certs/trust, which SSL_CERT_DIR
# (devcontainer.json remoteEnv) already lists, so OpenSSL-based tools (curl, .NET) trust it. It also prints a
# warning about system-store trust that needs root; harmless here.
dotnet dev-certs https --trust || true

# Node ignores SSL_CERT_DIR and the system store; it only reads NODE_EXTRA_CA_CERTS, and that must be one file.
# Aspire serves Keycloak (and the dashboard) with this same dev cert on https, so Claude Code's MCP client — Node —
# needs it to reach the realm's token endpoint when connecting to the local /mcp. Bundle whatever the trust dir
# holds into the stable path NODE_EXTRA_CA_CERTS points at (remoteEnv again).
trust_dir="$HOME/.aspnet/dev-certs/trust"
bundle="$HOME/.aspnet/dev-certs/trust-bundle.pem"
if [ -d "$trust_dir" ]; then
  find "$trust_dir" -maxdepth 1 -type f -name '*.pem' -exec cat {} + > "$bundle"
  chmod 600 "$bundle"
fi
