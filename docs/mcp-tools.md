# MCP tools

The official `ModelContextProtocol.AspNetCore` SDK exposes Streamable HTTP
servers. Each capability/scenario is a separate endpoint:

| Capability | IT Service Desk | CRM & Sales | Project Management |
|---|---|---|---|
| READONLY | `/mcp/readonly/itsm` | `/mcp/readonly/crm` | `/mcp/readonly/projects` |
| READWRITE | `/mcp/readwrite/itsm` | `/mcp/readwrite/crm` | `/mcp/readwrite/projects` |

`tools/list` returns only the registered catalog for that endpoint. Names are
prefixed `readonly_itsm_`/`readwrite_itsm_`,
`readonly_crm_`/`readwrite_crm_`, or
`readonly_pm_`/`readwrite_pm_`. The REST catalog routes expose the corresponding
tool names for the UI and client setup.

## Representative tools

- ITSM: `readonly_itsm_list_tickets`, `readonly_itsm_get_dashboard`,
  `readwrite_itsm_create_ticket`, `readwrite_itsm_assign_ticket`,
  `readwrite_itsm_resolve_ticket`, `readwrite_itsm_close_ticket`.
- CRM: `readonly_crm_list_accounts`, `readonly_crm_get_pipeline_summary`,
  `readwrite_crm_create_lead`, `readwrite_crm_qualify_lead`,
  `readwrite_crm_update_opportunity`.
- Projects: `readonly_pm_list_projects`, `readonly_pm_get_project_health`,
  `readwrite_pm_create_task`, `readwrite_pm_assign_task`,
  `readwrite_pm_complete_task`, `readwrite_pm_add_status_update`.

Each tool validates its bounded input through the shared store. Write tools
require `DemoEditor` or `DemoAdmin`; read tools require `DemoViewer` or higher.
The role is derived from the authenticated principal, not an MCP argument.
Local Development may set `X-Demo-Role`; production ignores demo headers and
requires a bearer token with the configured Entra audience and role claim.
High-impact operations require an explicit `confirm` argument.

## Local test

Start the API in Development, then list the ITSM read-only tools:

```bash
curl -sS http://localhost:5290/mcp/readonly/itsm \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

Invoke a tool:

```bash
curl -sS http://localhost:5290/mcp/readonly/itsm \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"readonly_itsm_list_tickets","arguments":{"page":1,"pageSize":10}}}'
```

Run `bash tests/smoke-test.sh` to verify REST seed data, all six catalogs,
read-only calls, authorized writes, a denied write, and activity capture across
all three domains.
