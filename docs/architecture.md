# Architecture

NovaLabMCP is a single-process demo application with an in-memory store. The
ASP.NET Core host exposes REST endpoints, six separate MCP Streamable HTTP
endpoints, health checks, and an SSE activity stream. The Next.js app calls the
REST API and proxies same-origin `/api/*` requests to the configured backend in
Container Apps.

## Data and domain boundaries

`ScenarioStore` owns three explicitly keyed datasets: `itsm`, `crm`, and
`projects`. Collection routes and tool methods always specify a scenario and
entity; there is no mutable process-wide active-scenario setting. Seed data is
created deterministically on startup and can be reset by a demo admin in
Development. All records disappear on process restart.

The store serializes in-process reads and writes with a lock and returns deep
copies for reads. Shared CRUD and pagination code is currently implemented in
`src/NovaLabMCP.Api/ScenarioStore.cs`; MCP adapters are in
`src/NovaLabMCP.Api/McpTools.cs`.

## API surfaces

| Surface | Endpoint |
|---|---|
| Scenarios and entity CRUD | `/api/v1/{scenario}/{entity}` |
| MCP tool catalog | `/api/v1/mcp/catalog/{capability}/{scenario}` |
| MCP activity list/detail | `/api/v1/mcp-activity` |
| MCP event stream | `/api/v1/mcp-activity/stream` |
| Liveness/readiness | `/health/live`, `/health/ready` |
| OpenAPI | `/openapi/v1.json` |

Collection endpoints support page/pageSize, global search, allowlisted
field-based sorting, and a single field filter. Page sizes are bounded at 50.
The API uses a development-only demo role header and bearer-token validation
outside Development.

## MCP isolation and event handling

Each MCP endpoint has its own SDK server and registered tool class, so the
read-only catalog does not register write tools and vice versa. MCP handlers
delegate data changes to `ScenarioStore` and apply role checks; destructive
operations require confirmation. Tool invocation wrappers record bounded,
redacted summaries in `McpActivityBuffer`, which fans out events over SSE.

The activity buffer is in-process and ephemeral. It is not audit storage.
REST/UI changes do not create MCP events. A single Container App replica and
single active revision are used to avoid serving divergent process-local state.
Deployments/restarts may interrupt requests and reset both records and logs.

## Azure topology

`infra/bicep/main.bicep` provisions a Basic ACR, user-assigned managed
identities with `AcrPull`, a Container Apps environment, and (after bootstrap)
the API and frontend apps. Container Apps use HTTPS ingress and one replica.
The Next.js server-side proxy forwards the browser's bearer token to the API;
the browser never receives an internal container hostname.

