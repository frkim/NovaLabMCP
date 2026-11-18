#!/usr/bin/env bash
set -euo pipefail

uuid='[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
failed=0

require_match() {
  local value=$1 pattern=$2 message=$3
  if [[ ! $value =~ $pattern ]]; then
    printf '::error::%s\n' "$message" >&2
    failed=1
  fi
}

require_match "${RESOURCE_GROUP:-}" '^[a-zA-Z0-9_.()-]{1,90}$' \
  'Set AZURE_RESOURCE_GROUP or enter resource_group when dispatching.'
require_match "${ENVIRONMENT_NAME:-}" '^[a-z][a-z0-9-]{1,14}[a-z0-9]$' \
  'Set NOVALABMCP_ENVIRONMENT_NAME or enter environment_name (3-16 lowercase letters, digits, or single interior hyphens; start with a letter).'
if [[ ${ENVIRONMENT_NAME:-} == *--* ]]; then
  printf '::error::The environment prefix cannot contain consecutive hyphens.\n' >&2
  failed=1
fi
require_match "${REGISTRY_NAME:-}" '^[a-z0-9]{5,50}$' \
  'Set NOVALABMCP_REGISTRY_NAME or enter registry_name (5–50 lowercase letters and digits).'
require_match "${CLIENT_ID:-}" "^${uuid}$" \
  'Set ENTRA_CLIENT_ID or enter entra_client_id (the SPA application ID).'
require_match "${API_SCOPE:-}" '^(api|https)://[^[:space:]]+/[^/[:space:]]+$' \
  'Set ENTRA_API_SCOPE or enter entra_api_scope (the full delegated API scope, including the scope name).'
require_match "${AUDIENCE:-}" "^${uuid}$" \
  'Set ENTRA_API_AUDIENCE or enter entra_api_audience (the API application ID for v2 tokens).'

exit "$failed"
