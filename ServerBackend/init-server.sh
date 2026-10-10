#!/usr/bin/env bash
set -euo pipefail

cd -- "$(dirname -- "$0")"

export DOTNET_ENVIRONMENT=Development
export ASPNETCORE_ENVIRONMENT=Development

if ! grep -q '<UserSecretsId>' ServerBackend.csproj; then
    dotnet user-secrets init --project ServerBackend.csproj
fi

jwt_secrets="$(dotnet user-secrets list --project ServerBackend.csproj)"
if ! grep -q '^Jwt:Key = ' <<< "$jwt_secrets"; then
    jwt_key="$(openssl rand -base64 32)"
    dotnet user-secrets set "Jwt:Key" "$jwt_key" --project ServerBackend.csproj >/dev/null
fi
unset jwt_secrets jwt_key

dotnet dev-certs https --trust
exec dotnet run --project ServerBackend.csproj --no-launch-profile
