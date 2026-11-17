#!/usr/bin/env bash
set -euo pipefail

api_base="${API_BASE_URL:-http://localhost:5290}"
role="${DEMO_ROLE:-DemoEditor}"

request() {
  curl --fail-with-body --silent --show-error "$@"
}

mcp_call() {
  local endpoint="$1"
  local payload="$2"
  local response
  response="$(request \
    -H "X-Demo-Role: ${role}" \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json, text/event-stream' \
    -d "$payload" \
    "${api_base}/mcp/${endpoint}")"
  printf '%s\n' "$response" |
    sed -n 's/^data: //p' |
    head -n 1 |
    jq -e '.result | has("isError") | not' >/dev/null
}

request "${api_base}/health/live" >/dev/null

for scenario_entity in \
  'itsm tickets 40' \
  'crm accounts 25' \
  'projects projects 12'
do
  read -r scenario entity minimum <<<"$scenario_entity"
  count="$(request \
    -H "X-Demo-Role: ${role}" \
    "${api_base}/api/v1/${scenario}/${entity}?page=1&pageSize=1" |
    jq -r '.totalCount')"
  if (( count < minimum )); then
    printf 'Expected at least %s %s/%s records; found %s\n' \
      "$minimum" "$scenario" "$entity" "$count" >&2
    exit 1
  fi
done

for capability_scenario in \
  'readonly itsm readonly_itsm_ readwrite_itsm_' \
  'readwrite itsm readwrite_itsm_ readonly_itsm_' \
  'readonly crm readonly_crm_ readwrite_crm_' \
  'readwrite crm readwrite_crm_ readonly_crm_' \
  'readonly projects readonly_pm_ readwrite_pm_' \
  'readwrite projects readwrite_pm_ readonly_pm_'
do
  read -r capability scenario expected forbidden <<<"$capability_scenario"
  response="$(request \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json, text/event-stream' \
    -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' \
    "${api_base}/mcp/${capability}/${scenario}")"
  printf '%s\n' "$response" |
    sed -n 's/^data: //p' |
    head -n 1 |
    jq -e --arg expected "$expected" --arg forbidden "$forbidden" \
      '.result.tools | length > 0 and all(.name | startswith($expected)) and all(.name | startswith($forbidden) | not)' \
      >/dev/null
done

mcp_call readonly/itsm \
  '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"readonly_itsm_list_tickets","arguments":{"page":1,"pageSize":1}}}'
mcp_call readonly/crm \
  '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"readonly_crm_list_accounts","arguments":{"page":1,"pageSize":1}}}'
mcp_call readonly/projects \
  '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"readonly_pm_list_projects","arguments":{"page":1,"pageSize":1}}}'

denied_write="$(request \
  -H 'X-Demo-Role: DemoViewer' \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"readwrite_itsm_create_ticket","arguments":{"ticket":{"title":"Denied smoke test","status":"New","priority":"P3","requesterId":"itsm-employees-001"}}}}' \
  "${api_base}/mcp/readwrite/itsm")"
printf '%s\n' "$denied_write" |
  sed -n 's/^data: //p' |
  head -n 1 |
  jq -e '.result.isError == true' >/dev/null

ticket_name="Smoke-${RANDOM}-ticket"
lead_name="Smoke-${RANDOM}-lead"
task_name="Smoke-${RANDOM}-task"
project_id="$(request \
  -H "X-Demo-Role: ${role}" \
  "${api_base}/api/v1/projects/projects?page=1&pageSize=1" |
  jq -r '.items[0].id')"

mcp_call readwrite/itsm \
  "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"readwrite_itsm_create_ticket\",\"arguments\":{\"ticket\":{\"title\":\"${ticket_name}\",\"status\":\"New\",\"priority\":\"P3\",\"requesterId\":\"itsm-employees-001\"}}}}"
mcp_call readwrite/crm \
  "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\",\"params\":{\"name\":\"readwrite_crm_create_lead\",\"arguments\":{\"lead\":{\"name\":\"${lead_name}\"}}}}"
mcp_call readwrite/projects \
  "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"readwrite_pm_create_task\",\"arguments\":{\"task\":{\"name\":\"${task_name}\",\"projectId\":\"${project_id}\"}}}}"

request -H "X-Demo-Role: ${role}" \
  "${api_base}/api/v1/mcp-activity?page=1&pageSize=50" |
  jq -e '.totalCount >= 7 and any(.items[]; .outcome == "Forbidden")' >/dev/null

printf 'REST and MCP smoke checks passed for ITSM, CRM, and Projects.\n'
