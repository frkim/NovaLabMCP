# NovaLabMCP — Implementation Specification

**Status:** Authoritative implementation specification  
**Product name:** NovaLabMCP  
**Audience:** GitHub Copilot (agent mode), developers, and reviewers

## 1. Product overview

NovaLabMCP is a realistic, multi-domain MCP demo simulator. It demonstrates how AI assistants and agents can interact with enterprise business systems through the Model Context Protocol (MCP).

The application must implement **all three business use cases** as first-class, fully working scenarios in the same release:

- **Use case A — Contoso IT Service Desk**
- **Use case B — Contoso CRM & Sales**
- **Use case C — Contoso Project Management**

These are not alternatives from which the developer or user should choose one or two. All three are mandatory deliverables. None may be treated as optional, deferred, a mock-up, or a placeholder.

The implementation is considered incomplete until all three domains have their own realistic seeded data, business rules, REST CRUD capabilities, READONLY and READWRITE MCP tools, frontend data views/editing, tests, and demo flows.

Users select a scenario in the web application. The selected scenario determines the active dataset, dashboard, entities, CRUD operations, MCP tools, and demo prompts.

The same MCP service must be consumable by compatible clients such as GitHub Copilot, Microsoft Copilot Studio, Microsoft Foundry agents, and other MCP-compatible clients. Verify each client's current transport, authentication, and feature support before claiming compatibility.

NovaLabMCP is a simulator, not a production business system. Use convincing fictional data and an **in-memory database only**. A process restart restores the deterministic seed data.

### Core principles

1. All three business use cases implemented in one application, with strict domain boundaries.
2. MCP-first interoperability, independent of any one AI client.
3. Strict separation between **READONLY** and **READWRITE** MCP capabilities.
4. Realistic, relational, varied fictional business data.
5. In-memory-only business data; no external database.
6. Professional web UI for browsing and editing records.
7. Central logging of every MCP command and a live, filterable activity panel.
8. Server-side validation and authorization.
9. Incremental implementation with tests and documentation.

## 2. Required technology stack

| Layer | Requirement |
|---|---|
| Backend | **C# / ASP.NET Core on .NET 10 LTS**; verify current supported SDK/runtime |
| MCP | Official, maintained C# MCP SDK; verify current stable package and API |
| Frontend | Next.js + TypeScript |
| UI | Material UI (MUI), with a custom theme and reusable components |
| Data | **In-memory only**; no SQL, PostgreSQL, Cosmos DB, Redis, ORM, migrations, or database connection strings |
| Realtime | ASP.NET Core SignalR or Server-Sent Events (SSE) |
| API | ASP.NET Core REST API + OpenAPI |
| Hosting target | Azure Container Apps |
| Infrastructure | Azure Bicep |
| CI/CD | GitHub Actions |
| Telemetry | Structured .NET logging and OpenTelemetry-compatible traces/metrics where useful |

The MCP host and REST API may initially run in the same ASP.NET Core process/container, but keep their responsibilities separate. MCP handlers must be thin adapters over application services. The frontend must call the REST API and must never access repositories directly.

## 3. Main application UX

Build a polished enterprise SaaS-style application with:

- NovaLabMCP branding and clear navigation.
- All use cases A, B, C Scenarios run simultanaously however it may be nice to create a selector (Option A: CRM, B=Service Desk, C=Project Management) to adapt the UX/UI accordingly
- Domain-specific overview/dashboard.
- Navigation for the entities in the selected scenario.
- Data explorer with paginated tables and record detail views.
- Create/edit forms and delete confirmation.
- Dedicated **MCP Command Center** showing live MCP activity.
- Visible selected-scenario and in-memory/demo-mode indicators.
- Dark/light mode toggle. Respect OS preference on first visit and persist the user's choice in browser storage.
- Responsive desktop, tablet, and mobile layouts.

Switching scenario must clearly change the active dataset and MCP tool catalog. Never silently mix data across domains. Scenario context must be request/session scoped; do not use a mutable process-wide “current scenario” variable shared by users.

All three scenarios must be accessible from the application. Do not hide or disable a scenario because another scenario has been implemented first.

### 3.1 Paginated data tables

Every entity list must use a reusable table pattern with:

- API-backed pagination (default 10 rows; options 10, 25, 50).
- Current page, total count, total pages, first/previous/next/last navigation.
- Global search across relevant fields.
- Column sorting.
- Relevant column filters (status, priority, owner, stage, dates, etc.).
- Clear-filters action.
- Row actions: View, Edit, Delete, subject to permissions.
- Loading skeleton, empty state, error state, retry, and success feedback.
- Keyboard accessibility and visible focus.
- Stable sorting and pagination after mutations.
- Page/filter/sort state in URL query parameters where practical.

Every collection endpoint must accept validated pagination parameters and return:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0,
  "totalPages": 0
}
```

Default maximum page size is 50. Validate sort fields against an allowlist.

### 3.2 CRUD UX

- Forms require labels, validation, sensible defaults, Save/Cancel, field-level errors, and clear server error messages.
- Warn before navigating away from a dirty form.
- Delete requires a confirmation dialog naming the record.
- Enforce referential integrity in the service layer; block unsafe deletes or require explicit handling of related records.
- After a mutation, refresh affected tables and dashboard metrics.
- Show success/error notifications.
- UI-originated changes must not be misrepresented as MCP commands in the MCP log.

### 3.3 Professional and appealing design

The interface must look like a polished enterprise product, not a default scaffold or generic admin template.

- Strong information hierarchy, intentional typography, consistent spacing, restrained semantic color, and readable density.
- Reusable design tokens for surfaces, text, borders, status, and focus.
- Consistent, accessible status and priority badges.
- Charts only when they clarify useful information; label units, time ranges, and axes.
- Avoid clutter, excessive gradients, decorative animation, and oversized empty hero sections.
- Responsive layout; avoid accidental horizontal overflow.
- Clear loading, empty, error, disabled, and success states.
- Sufficient contrast; never communicate status by color alone.
- Subtle motion only when it clarifies state; respect `prefers-reduced-motion`.
- Inspect and reuse existing components before creating duplicates.

Follow the supplied AI coding standards, including the design-quality and appealing-UI guidance. Read the referenced standards before implementation.

## 4. Architecture and scenario isolation

```text
MCP Clients (GitHub Copilot | Copilot Studio | Foundry | other MCP hosts)
                             |
                    HTTPS / Streamable HTTP
                             |
+----------------------------v--------------------------------+
|                    NovaLabMCP MCP Host                      |
|  READONLY endpoints/catalogs | READWRITE endpoints/catalogs |
+----------------------------+--------------------------------+
                             |
                    Application services
                             |
+----------------------------v--------------------------------+
|                 ASP.NET Core REST API                      |
| Validation | Business rules | Authorization | MCP logging   |
+----------------------------+--------------------------------+
                             |
+----------------------------v--------------------------------+
|                Thread-safe in-memory repositories           |
| Seeded domain collections | Relationships | Reset           |
+-------------------------------------------------------------+

Next.js frontend -> REST API + SignalR/SSE -> ASP.NET Core
```

Use three separate domain modules:

- `ItServiceDesk`
- `CrmSales`
- `ProjectManagement`

Each of the three domains is mandatory and owns its entities, DTOs, validation, services, repositories, REST routes, MCP tool registrations, seed data, and demo scenarios.

Share infrastructure, pagination, logging, authorization, and UI primitives.

Do not omit or stub any domain to simplify the initial delivery.

Suggested REST routes:

- `/api/scenarios`
- `/api/itsm/...`
- `/api/crm/...`
- `/api/projects/...`

Suggested MCP routes:

- `/mcp/readonly/itsm`
- `/mcp/readwrite/itsm`
- `/mcp/readonly/crm`
- `/mcp/readwrite/crm`
- `/mcp/readonly/projects`
- `/mcp/readwrite/projects`

Prefer distinct MCP endpoints/servers so a client can be granted only one capability area. They may share the same ASP.NET Core host and application services. Verify exact endpoint/session behavior against the current official C# MCP SDK. If separate endpoints are not practical, preserve strict separate tool catalogs and explain the alternative.

Never use a global mutable active-scenario setting; scenario must be explicit in the endpoint, route, or validated tool context.

## 5. In-memory data store

Implement repository interfaces backed by thread-safe in-memory collections (for example, `ConcurrentDictionary<Guid,T>` or another appropriate design).

Requirements:

- Seed deterministically once at startup.
- All CRUD operations update the in-memory collections.
- Reads reflect successful writes immediately.
- Protect multi-step and related-entity mutations against race conditions.
- Return immutable snapshots/defensive copies; never expose mutable repository objects.
- Generate IDs server-side.
- Store timestamps in UTC and serialize as ISO 8601.
- Provide `POST /api/demo/reset`, restricted to an administrator/demo-reset role. Reset restores original seed data and follows documented log-reset behavior.
- Do not expose reset as a general MCP tool by default.
- Display a persistent notice: **“In-memory demo data — resets when the service restarts.”**

**Azure Container Apps constraint:** process-local data is non-durable. A restart, redeployment, scale-out, or scale-in can reset data or cause replicas to diverge. Configure a single backend replica for the demo and document that restarts reset all business data and in-memory logs. Do not claim high availability or persistence.

If multiple replicas are enabled, clearly disclose independent per-replica state; do not silently deploy that configuration.

## 6. Use case A — Contoso IT Service Desk

### Entities

- **Ticket:** ticket number, title, description, type, status, priority, category, requester, assignee, support team, related asset, created/updated/due timestamps, resolution summary.
- **Employee:** name, email, department, job title, location, manager, active status.
- **Support Team:** name, queue, description, supported categories, team lead.
- **Asset:** asset tag, type, manufacturer/model, fictional serial number, owner, location, lifecycle status.
- **Comment:** ticket, author, body, public/internal visibility, created timestamp.
- **Knowledge Article:** title, category, summary, body, tags, publication status, updated timestamp.

### Business rules

- Tickets have one requester, optional assignee/team, and optional asset; a ticket may have multiple comments.
- Internal comments require appropriate authorization.
- Status: New, In Progress, Pending, Resolved, Closed.
- Priority: P1 Critical, P2 High, P3 Medium, P4 Low.
- P1 tickets require an impact statement and assigned support team.
- Resolution requires a resolution summary; closing requires Resolved status.
- Assignment must reference an active employee/team.
- Do not delete employees/teams referenced by active tickets without reassignment.
- SLA due dates must be plausible for the priority.

### Seed data minimum

30 employees; 5 support teams (Service Desk, Identity & Access, Network Operations, Endpoint Engineering, Business Applications); 40 tickets including at least 3 P1 incidents; 25 assets; 60 comments (public and internal); 15 knowledge articles.

Use believable cases: VPN certificate failures, Entra ID conditional-access lockouts, laptop disk-encryption issues, ERP latency, meeting-room equipment failures, and Wi-Fi outages. No lorem ipsum or repetitive placeholder records.

### READONLY tools

- `readonly_itsm_list_tickets`
- `readonly_itsm_get_ticket`
- `readonly_itsm_list_employees`
- `readonly_itsm_list_teams`
- `readonly_itsm_list_assets`
- `readonly_itsm_search_knowledge`
- `readonly_itsm_get_dashboard`

### READWRITE tools

- `readwrite_itsm_create_ticket`
- `readwrite_itsm_update_ticket`
- `readwrite_itsm_assign_ticket`
- `readwrite_itsm_add_comment`
- `readwrite_itsm_resolve_ticket`
- `readwrite_itsm_close_ticket`
- `readwrite_itsm_delete_ticket`

## 7. Use case B — Contoso CRM & Sales

### Entities

- **Account:** company, industry, website, headquarters, employee range, annual-revenue band, owner, relationship status.
- **Contact:** name, business email, job title, fictional phone, account, buying-committee role, contact preference.
- **Lead:** source, company/contact details, qualification status, score, owner, created date, next action.
- **Opportunity:** name, account, primary contact, owner, amount, currency, stage, probability, expected close date, forecast category, competitors, next step.
- **Activity:** call/meeting/email/task, subject, notes, due date, completion status, owner, related CRM record(s).
- **Product:** name, product family, SKU, description, list price, active status.
- **Sales Team:** name, region, manager, members, quota.

### Business rules

- Contacts belong to an account; accounts can have multiple contacts and opportunities.
- Opportunities reference an existing account and owner.
- Lead qualification may create/associate an account and contact, and optionally an opportunity.
- Stages: Qualification, Discovery, Solution Design, Proposal, Negotiation, Closed Won, Closed Lost.
- Probability/forecast defaults must be consistent with stage; allow documented overrides.
- Closed Won/Lost requires close date; Closed Lost requires a loss reason.
- Amount is non-negative and has a currency code.
- Do not delete accounts with linked opportunities/contacts until relationships are handled.

### Seed data minimum

25 accounts across manufacturing, retail, financial services, healthcare, energy, and professional services; 50 contacts; 35 leads from referrals, events, website enquiries, partner referrals, and outbound; 25 opportunities across all stages, including won/lost; 50 activities; 12 products/services; 3 regional sales teams with quotas.

Use plausible deal names and amounts, e.g. “Azure Data Platform Modernization”, “Enterprise Identity Consolidation”, and “Customer Service Copilot Pilot”. Use fictional companies and `example.com` email addresses.

### READONLY tools

- `readonly_crm_list_accounts`
- `readonly_crm_get_account`
- `readonly_crm_list_contacts`
- `readonly_crm_list_leads`
- `readonly_crm_list_opportunities`
- `readonly_crm_get_pipeline_summary`
- `readonly_crm_list_activities`
- `readonly_crm_search_records`

### READWRITE tools

- `readwrite_crm_create_account`
- `readwrite_crm_update_account`
- `readwrite_crm_create_contact`
- `readwrite_crm_update_contact`
- `readwrite_crm_create_lead`
- `readwrite_crm_qualify_lead`
- `readwrite_crm_create_opportunity`
- `readwrite_crm_update_opportunity`
- `readwrite_crm_log_activity`
- `readwrite_crm_delete_record`

Generic delete must validate entity type, record ID, relationships, authorization, and explicit confirmation. Prefer entity-specific delete tools if a generic tool weakens validation or authorization.

## 8. Use case C — Contoso Project Management

### Entities

- **Portfolio:** name, business unit, sponsor, strategic objective, status.
- **Project:** code, name, description, portfolio, project manager, sponsor, status, Red/Amber/Green health, dates, budget/currency, completion percentage.
- **Milestone:** project, name, description, planned/actual date, status, owner.
- **Task:** project, optional milestone, title, description, assignee, priority, status, dates, estimate, actual effort, dependencies.
- **Team Member:** name, email, role, department, skills, capacity, availability.
- **Risk:** project, title, description, probability, impact, score, owner, mitigation, status.
- **Status Update:** project, reporting period, summary, accomplishments, next steps, blockers, health, author.

### Business rules

- Projects belong to a portfolio and have project manager and sponsor.
- Milestones/tasks belong to a project.
- Reject circular task dependencies.
- Completed tasks require completion timestamp and cannot have unresolved blocking dependencies.
- Project completion is derived from task completion unless a documented authorized override exists.
- A milestone cannot complete before required tasks unless an authorized override is recorded.
- Risk score = probability × impact on a documented scale.
- End date must be on/after start date; budget and effort are non-negative.
- Deleting a project with active tasks/risks is blocked or requires explicit cascade confirmation.

### Seed data minimum

4 portfolios; 12 projects with varied statuses and health; 25 milestones; 80 tasks with priorities, owners, and dependencies; 20 team members with varied roles/capacities; 18 risks with mitigation plans; 20 status updates.

Use credible examples: “Global ERP Finance Rollout”, “Customer Data Platform”, “Identity Governance Transformation”, “Retail Store Network Modernization”, and “Sales Operations Automation”. Include schedule variance, blocked tasks, budget consumption, and realistic risks.

### READONLY tools

- `readonly_pm_list_portfolios`
- `readonly_pm_list_projects`
- `readonly_pm_get_project`
- `readonly_pm_list_tasks`
- `readonly_pm_list_milestones`
- `readonly_pm_list_risks`
- `readonly_pm_get_project_health`
- `readonly_pm_get_portfolio_summary`

### READWRITE tools

- `readwrite_pm_create_project`
- `readwrite_pm_update_project`
- `readwrite_pm_create_task`
- `readwrite_pm_update_task`
- `readwrite_pm_assign_task`
- `readwrite_pm_complete_task`
- `readwrite_pm_create_milestone`
- `readwrite_pm_update_milestone`
- `readwrite_pm_create_risk`
- `readwrite_pm_update_risk`
- `readwrite_pm_add_status_update`
- `readwrite_pm_delete_record`

As in CRM, generic delete must validate entity type, relationships, authorization, and explicit confirmation.

## 9. MCP contract — READONLY / READWRITE

This split is a mandatory architectural and security requirement.

### READONLY area

- List, search, retrieve, aggregate, and report only.
- Must never mutate business data.
- May expose read-only resources and prompts.
- Must not register create, update, delete, assign, resolve, close, qualify, complete, or any other mutation tool.

### READWRITE area

- Explicit create, update, delete, assign, resolve, close, qualify, complete, log, and other required mutations.
- Validate schemas, business rules, relationships, and caller permissions.
- Return structured results with operation, affected record ID, and outcome.
- Destructive/high-impact actions require explicit confirmation.

Use names:

- `readonly_itsm_*` / `readwrite_itsm_*`
- `readonly_crm_*` / `readwrite_crm_*`
- `readonly_pm_*` / `readwrite_pm_*`

Tool descriptions must clearly state READONLY or READWRITE behavior. Input schemas must be typed, bounded, and precise. Prefer structured results with stable IDs and human-readable summaries.

Where supported by the official SDK, annotate read tools accurately (`readOnlyHint=true`, `destructiveHint=false`, `idempotentHint=true` only when true). Write tools must set `readOnlyHint=false`; destructive tools must set `destructiveHint=true`. Annotations are hints, not security controls.

For every write: validate input; authenticate and authorize; require explicit confirmation for destructive/high-impact operations; call the domain service; log outcome; return affected ID and result. Never expose arbitrary code execution, shell access, unrestricted query execution, or direct collection mutation.

### Resources and prompts

READONLY may expose domain glossaries, status definitions, team ownership, ITSM SLA policy, CRM stage guidance, project health definitions, and risk scoring.

Prompts should be domain-specific and guide the model to use READONLY tools for analysis before proposing a mutation. Prompts must never bypass READWRITE permissions or confirmation.

Suggested prompts:

- ITSM: `daily_incident_review`, `analyze_major_incident`, `team_workload_summary`.
- CRM: `weekly_pipeline_review`, `opportunity_risk_review`, `prepare_account_briefing`.
- Project Management: `portfolio_health_review`, `project_risk_assessment`, `weekly_status_report`.

## 10. MCP command logging and live MCP Command Center

### 10.1 Central logging — every MCP command

Every MCP tool invocation must create a structured event, including successful calls, validation failures, authorization denials, timeouts, and unexpected errors. Implement logging centrally around MCP dispatch so newly added tools are covered automatically.

Capture:

- Event ID.
- UTC start and completion timestamps.
- Correlation/trace ID.
- Capability area: READONLY or READWRITE.
- Scenario: ITSM, CRM, or PROJECTS.
- Tool/command name.
- Caller/client identity when available (safe identifier only).
- Redacted, size-limited input summary.
- Outcome: Success, ValidationError, Unauthorized, Forbidden, Timeout, Error.
- Duration in milliseconds.
- Safe result summary.
- Error code and safe error message, where applicable.

Never log access tokens, credentials, secrets, or unnecessary personal/sensitive data. Do not log full business payloads by default. Apply field-level redaction and truncation.

### 10.2 In-memory activity buffer

Keep live MCP activity in a bounded, thread-safe in-memory buffer (default capacity 2,000 events, configurable). Prevent unbounded memory growth. Events are ephemeral and reset on process restart. Operational telemetry, if configured, must not be presented as durable business data.

### 10.3 Real-time UI panel

Expose live events through SignalR or SSE. The **MCP Command Center** must update without page refresh and provide:

- Live/paused indicator and pause/resume control.
- Event count retained in the current buffer.
- Newest-first event list with timestamp, capability badge, scenario, tool, caller, duration, and outcome.
- Search by tool, scenario, caller, correlation ID, or text.
- Filters for READONLY/READWRITE, scenario, outcome, and time range.
- Toggle to hide/show successful calls.
- Expandable detail view with safe input/result summaries and error information.
- Copy event JSON and correlation ID.
- Clear filters.
- Empty, disconnected, reconnecting, and error states.

“Full log” means all event objects retained in the bounded in-memory buffer, not unlimited history. Show the retention limit and ephemeral nature in the UI. Avoid disruptive reordering while a user inspects an event; allow pause or pinning.

Protect the activity stream and event details with authentication and authorization. READONLY-only users must not automatically see sensitive READWRITE event details. Redact payload summaries.

Suggested endpoints:

- `GET /api/mcp-activity?page=1&pageSize=50&scenario=...&capability=...&outcome=...`
- `GET /api/mcp-activity/{eventId}`
- SignalR hub or SSE endpoint: `/api/mcp-activity/stream`

The list API must paginate, filter, and sort newest-first. Clients must not be able to forge MCP invocation events.

## 11. REST API requirements

For every domain implement paginated list, get-by-ID, create, update, delete (where allowed), search, and summary endpoints.

Use consistent HTTP semantics:

- `200` successful read/update
- `201` created
- `204` deleted
- `400` validation error
- `401` unauthenticated
- `403` forbidden
- `404` not found
- `409` business conflict
- `500` unexpected error

Use typed DTOs, validate all API input, and never bind requests directly to internal mutable entities. Paginate every collection. Validate page sizes and sort-column allowlists. Publish OpenAPI. Add `/health/live` and `/health/ready`. Configure CORS for the frontend origin; do not use wildcard origins with credentials.

Example validation error:

```json
{
  "type": "https://novalabmcp/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "errors": { "priority": ["Priority is required."] },
  "traceId": "..."
}
```

## 12. Frontend screens

### Overview dashboards

**IT Service Desk:** open tickets, P1/P2, overdue tickets, tickets by status/priority/team, recent incidents, SLA risk.

**CRM & Sales:** open and weighted pipeline value, opportunities by stage, expected close value, won/lost summary, overdue activities, top opportunities/accounts.

**Project Management:** active projects, RAG health counts, overdue tasks, milestone status, high risks, budget summary, projects needing attention.

All charts need labels, units, time ranges, accessible alternatives, and meaningful empty states.

### Entity pages

For every entity, provide a paginated list and record detail view. Support create/edit/delete where domain rules permit. Keep domain-specific columns and forms explicit and type-safe while reusing common table, filter, pagination, form, and detail components.

### MCP Command Center

Make MCP observability a first-class navigation item, not a hidden developer console. Include filters, main event list, detail drawer, live state, and clear READONLY/READWRITE distinction. A compact recent-activity widget may also appear on dashboards.

## 13. Security and authorization

Suggested roles:

- `DemoViewer`: view UI and invoke READONLY MCP tools.
- `DemoEditor`: viewer plus permitted READWRITE operations.
- `DemoAdmin`: editor plus reset data and view all log details.

Requirements:

- Authenticate remote MCP requests when deployed.
- Prefer Microsoft Entra ID for Azure-hosted deployments; use managed identity for Azure resource access.
- Local demo authentication may use fake identities/roles, but must be isolated and disabled in production.
- Enforce permissions in backend services and MCP endpoints.
- READONLY endpoint must reject mutation requests even if a client crafts them manually.
- READWRITE endpoint must expose only the write catalog if strict endpoint separation is used.
- Never trust caller-supplied role/user IDs.
- Protect activity stream and log detail endpoints.
- Require confirmation for destructive actions and reset.
- Never log tokens, secrets, or full sensitive business records.

## 14. Azure Container Apps deployment

Deploy the ASP.NET Core API/MCP host and Next.js frontend as separate Azure Container Apps, with images in Azure Container Registry (ACR). Use Azure Bicep for infrastructure and GitHub Actions for builds and deployments. Do not provision an external database or other persistent business-data store.

### Infrastructure as code

- Place a resource-group-scoped Bicep entry point and any necessary modules under `infra/bicep/`. Provision the Container Apps environment, both apps, ACR, and the logging resources required by the chosen environment configuration. Add Application Insights only if useful.
- Parameterize the environment name, region, resource names, image references, and non-secret settings. Use sensible demo-sized defaults, validate parameters, and document costs. Keep environment-specific values outside source control and credentials out of templates, parameters, outputs, and logs.
- Use managed identity and least-privilege role assignments for the apps to pull from ACR; do not enable the ACR admin account or use registry passwords. Account for identity and role-assignment propagation before deploying images.
- Make initial deployment and subsequent deployments repeatable. Document the bootstrap sequence: provision the registry and required identities/permissions, build and push both images, then deploy or update the Container Apps with those image references. Do not depend on an image that does not yet exist.
- Expose the frontend and the authenticated REST/MCP endpoints over HTTPS. Configure the actual application ports, health probes using `/health/live` and `/health/ready` as appropriate, frontend-to-API connectivity, and an explicit CORS origin. Ensure browser-accessible API URLs are not confused with internal container hostnames. Configure SignalR/SSE for the selected ingress and client authentication.
- Integrate the application's Microsoft Entra ID authentication and authorization for deployed REST, MCP, and activity endpoints. Configure tenant, audience, issuer, frontend identity, and redirect URIs as required by the implemented auth flow. Disable local demo authentication in Azure and fail deployment or startup on missing required security configuration; do not treat network exposure or CORS as authentication.
- Configure the backend with a minimum and maximum of one replica and a single active revision. Prevent overlapping backend revisions from serving independent state during rollout; accept and document downtime if needed to guarantee that invariant. Restarts and deployments reset business data and in-memory MCP activity. Never claim persistence or high availability.

### GitHub Actions

- Add workflows under `.github/workflows/` for pull-request validation and deployment. The validation workflow must restore, build, lint/type-check, and test the actual .NET and Next.js projects, validate Bicep, and build both container images without requiring Azure credentials. Use the repository's actual project paths and supported tool versions once implemented.
- Deploy from an explicitly configured protected branch or manual dispatch, only after validation succeeds. Authenticate to Azure using GitHub OIDC and a narrowly scoped federated identity; document the required Azure role assignments, GitHub environment protection rules, variables, and secrets. Restrict workflow permissions, grant `id-token: write` only to the deployment job, and never expose deployment credentials to untrusted pull-request code.
- Push uniquely tagged images for the commit being deployed; resolve and deploy their exact image digests rather than relying on mutable tags such as `latest`. Serialize deployments to the same environment and surface build, provisioning, deployment, and smoke-test failures.
- After deployment, verify both application health and the intended authenticated REST/MCP access, including rejection of unauthenticated access. Exercise frontend-to-API connectivity and the live activity transport where feasible. Do not report a successful deployment when required checks fail.
- Document initial setup, federated credential subject/environment configuration, environment variables, manual deployment, smoke tests, troubleshooting, costs, and teardown in `docs/deployment.md`. Warn that teardown deletes the selected resource group and its resources; require explicit confirmation and never delete unrelated resources.

## 15. Repository structure

Adjust after inspecting the existing repository:

```text
NovaLabMCP/
├── AGENTS.md
├── README.md
├── docs/
│   ├── architecture.md
│   ├── mcp-tools.md
│   ├── mcp-logging.md
│   ├── data-models.md
│   ├── authentication.md
│   ├── deployment.md
│   ├── demo-scenarios.md
│   └── client-configuration.md
├── src/
│   ├── NovaLabMCP.Api/
│   │   ├── Endpoints/
│   │   ├── Middleware/
│   │   ├── Realtime/
│   │   └── Program.cs
│   ├── NovaLabMCP.Application/
│   │   ├── Abstractions/
│   │   ├── Common/
│   │   └── Security/
│   ├── NovaLabMCP.Domain/
│   │   ├── ItServiceDesk/
│   │   ├── CrmSales/
│   │   └── ProjectManagement/
│   ├── NovaLabMCP.Infrastructure/
│   │   ├── InMemory/
│   │   ├── Seeding/
│   │   └── Observability/
│   ├── NovaLabMCP.Mcp/
│   │   ├── ReadOnly/
│   │   ├── ReadWrite/
│   │   ├── ItServiceDesk/
│   │   ├── CrmSales/
│   │   └── ProjectManagement/
│   └── novalabmcp-web/
│       ├── app/
│       ├── components/
│       ├── features/
│       ├── lib/
│       └── theme/
├── tests/
│   ├── NovaLabMCP.UnitTests/
│   ├── NovaLabMCP.IntegrationTests/
│   └── NovaLabMCP.McpTests/
├── infra/bicep/
└── .github/workflows/
```

Keep domain logic independent of ASP.NET Core and MCP SDK types where practical.

## 16. Testing strategy

Write tests with each feature.

**Backend/domain**

- Business rules for all three scenarios.
- Referential integrity and deletion constraints.
- In-memory repository concurrency and defensive-copy behavior.
- Deterministic seeding and reset.
- Pagination/filter/sort boundaries.
- Authorization for each role and endpoint.

**MCP**

- READONLY endpoint registers only READONLY tools.
- READWRITE endpoint registers only READWRITE tools.
- All schemas discoverable and valid.
- Read tools never mutate data.
- Write tools mutate only after validation and authorization.
- Destructive tools require explicit confirmation.
- Every invocation creates exactly one terminal log event, including failures.
- Redaction works.
- Realtime event emitted for each completed invocation.
- Tool naming and scenario isolation are correct.

**Frontend**

- Scenario switching updates navigation and data.
- Tables support pagination, sorting, filtering, search, and page-size changes.
- Create/edit/delete works with validation/error/success states.
- Theme toggle respects OS preference and persists.
- Command Center filters, pauses/resumes, reconnects, and displays events.
- Responsive layout and keyboard accessibility.

**End-to-end**

- At least one READONLY and one READWRITE flow per scenario.
- Verify MCP changes are visible in UI and UI changes are visible to MCP.
- Run lint, build, tests, and exercise the feature. Report exact commands and results.

## 17. Demo scenarios

**IT Service Desk**

1. Find unresolved P1 incidents and summarize impact/teams.
2. Assign an unassigned VPN incident to Network Operations.
3. Add an internal investigation note.
4. Resolve an incident with a resolution summary.
5. Inspect corresponding READONLY and READWRITE log events.

**CRM & Sales**

1. Summarize pipeline by stage and forecast.
2. Find opportunities closing this quarter with no recent activity.
3. Create a lead from a partner referral.
4. Qualify a lead and create an opportunity.
5. Update an opportunity stage and next step.
6. Inspect MCP events and outcomes.

**Project Management**

1. Identify red-health projects and overdue milestones.
2. Summarize high risks and mitigation owners.
3. Find blocked tasks and dependencies.
4. Assign a task to a team member.
5. Complete a task and verify project progress recalculates.
6. Inspect MCP events in real time.

## 18. Configuration

Example local settings:

```text
ASPNETCORE_ENVIRONMENT=Development
NOVALABMCP_DEMO_AUTH_ENABLED=true
NOVALABMCP_MCP_LOG_CAPACITY=2000
NOVALABMCP_DEFAULT_PAGE_SIZE=10
NOVALABMCP_MAX_PAGE_SIZE=50
NOVALABMCP_FRONTEND_ORIGIN=http://localhost:3000
```

Do not add database configuration. Production/Azure must force demo auth off, require configured authentication, use a single backend replica, and keep environment-specific settings outside source control.

## 19. Milestones

Implement incrementally. Do not generate the entire application in one pass.

The milestones are an implementation sequence, not a reduction in scope: **Milestones 2, 3, and 4 are all required**, and all three domains must be completed before the product can be declared feature-complete or released as the requested demo.

### Milestone 0 — Repository and standards

- Inspect repository and existing conventions.
- Read supplied `AGENTS.md` and relevant coding/security/testing/architecture/UI standards.
- Verify current .NET 10 LTS SDK/runtime and official C# MCP SDK package/API.
- Propose solution structure, package choices, frontend setup, and commands.
- Establish build, lint, format, and test commands.

### Milestone 1 — Shared foundation and in-memory infrastructure

- Common API contracts, errors, pagination, auth, logging abstractions.
- Thread-safe in-memory repositories.
- Deterministic seed/reset infrastructure.
- Tests.

### Milestone 2 — IT Service Desk domain (mandatory)

- Fully implement the Contoso IT Service Desk domain.
- Model, business rules, services, realistic relational seed data, REST CRUD endpoints, READONLY/READWRITE MCP tools, tests, and repeatable demo flows.
- Do not proceed with a placeholder-only implementation.

### Milestone 3 — CRM & Sales domain (mandatory)

- Fully implement the Contoso CRM & Sales domain.
- Model, business rules, services, realistic relational seed data, REST CRUD endpoints, READONLY/READWRITE MCP tools, tests, and repeatable demo flows.
- Preserve CRM-specific validation and relationships; do not reuse ITSM entities as substitutes.

### Milestone 4 — Project Management domain (mandatory)

- Fully implement the Contoso Project Management domain.
- Model, business rules, services, realistic relational seed data, REST CRUD endpoints, READONLY/READWRITE MCP tools, tests, and repeatable demo flows.
- Preserve project, milestone, task, dependency, risk, and team relationships; do not reuse CRM or ITSM entities as substitutes.

**Domain completion gate:** Before moving to the frontend integration/release sign-off, verify that A, B, and C each work end-to-end through both REST and MCP, have their own seeded data and UI navigation, and pass their domain tests. A milestone may be developed in sequence, but no domain is optional and the overall delivery is not complete until all three pass this gate.

### Milestone 5 — Frontend and data explorer

- Application shell and scenario selector.
- Overview dashboards and reusable paginated tables.
- View/create/edit/delete flows.
- Responsive UI and dark/light mode.
- Design-quality verification.

### Milestone 6 — MCP Command Center

- Central invocation logging and bounded in-memory buffer.
- Filterable/paginated log API.
- SignalR/SSE stream.
- Live panel with event details, filters, pause/resume, reconnect states.
- Redaction and authorization tests.

### Milestone 7 — Azure deployment and interoperability

- Implement and validate the Bicep infrastructure, container builds, GitHub Actions validation and OIDC deployment workflows described in section 14.
- Verify single-replica/single-active-revision behavior, ephemeral-state limitations, deployed authentication, and post-deployment smoke tests.
- Document bootstrap, operations, costs, and teardown.
- Test at least two compatible MCP clients; target three where supported.
- Record actual client versions, transport, auth, and limitations.

### Milestone 8 — Demo hardening

- Run quality gates and all tests.
- Verify all three scenarios, command logging, separation, and authorization.
- Prepare repeatable scripts and reset instructions.
- Document limitations.

## 20. Definition of Done

- [ ] Product is named NovaLabMCP in UI, docs, config, and solution.
- [ ] All three required use cases are implemented: A — Contoso IT Service Desk, B — Contoso CRM & Sales, and C — Contoso Project Management.
- [ ] Each of the three domains works end-to-end; none is optional, deferred, mocked, or represented by a placeholder.
- [ ] User can select all three use cases in the application.
- [ ] All three domains have realistic relational fictional seed data.
- [ ] Data store is in-memory only; no external database is used.
- [ ] UI clearly states data resets on process restart.
- [ ] Backend is C# / ASP.NET Core on .NET 10 LTS.
- [ ] MCP READONLY and READWRITE are distinct capability surfaces.
- [ ] READONLY exposes no mutation tools.
- [ ] READWRITE exposes explicit mutation tools with server-side authorization.
- [ ] Every MCP command, including failures and denied calls, is logged centrally.
- [ ] MCP activity is streamed to the UI in real time.
- [ ] Command Center displays retained event details with filters and pagination.
- [ ] Frontend is professional, responsive, and appealing.
- [ ] Every entity has paginated tables with search, sorting, and relevant filters.
- [ ] Users can view/edit records; create/delete are supported where allowed.
- [ ] Dark/light mode toggle respects OS preference and persists.
- [ ] Validation, error, loading, empty, and success states exist.
- [ ] Relevant tests pass.
- [ ] At least two MCP clients tested where current product support permits.
- [ ] Bicep provisions the required Azure resources without an external business-data store.
- [ ] GitHub Actions validates, builds, and deploys both applications using OIDC and immutable image references.
- [ ] Azure deployment uses one backend replica and one active revision, with no overlapping revisions serving independent state.
- [ ] Deployed authentication and post-deployment smoke tests pass.
- [ ] No secrets or real personal data committed.
- [ ] Reset behavior, deployment limitations, and client compatibility documented.

## 21. Instructions to GitHub Copilot

This document is the implementation contract and supersedes earlier single-domain, TypeScript-backend, and persistent-database assumptions.

1. Inspect the repository before making changes.
2. Read the supplied coding standards and relevant referenced instructions.
3. Before adding a library, SDK, or runtime, research its current stable version and verify compatibility with .NET 10 LTS and the frontend.
4. Backend must be C# / ASP.NET Core. Do not implement it in TypeScript or Node.js.
5. Do not introduce a database, ORM, migration system, or external persistence.
6. Implement all three required use cases: IT Service Desk, CRM & Sales, and Project Management. Keep their domain modules separate while sharing common infrastructure; do not drop or defer any domain.
7. Keep READONLY and READWRITE MCP catalogs strictly separate; test the catalogs themselves.
8. Centralize MCP command logging so every tool invocation is captured automatically.
9. Keep business rules in application/domain services; REST and MCP handlers are adapters.
10. Enforce authorization on the server; annotations and prompts are not security controls.
11. Use realistic, coherent fictional seed data only.
12. Build a real, professional frontend, not a placeholder.
13. Every entity list must have pagination, sorting, filtering, and global search.
14. Command Center must show live MCP events and support filtering and inspection.
15. Test each milestone and exercise the feature before declaring it complete.
16. Keep documentation synchronized with implementation.
17. Do not claim a client integration works until tested with the actual client/version.
18. Explain deviations from this specification.
19. Implement one milestone at a time; do not proceed until the current milestone's plan and exit criteria have been reviewed. This sequencing does not make any use case optional: Milestones 2, 3, and 4 are all mandatory.
20. Do not declare the requested NovaLabMCP implementation complete until all three use cases pass the domain completion gate and the Definition of Done.

### First task — Milestone 0 only

- Inspect the repository and report its current state.
- Read/apply the supplied coding standards.
- Verify current .NET 10 LTS SDK/runtime and official C# MCP SDK version.
- Propose solution/project structure and frontend setup.
- List exact local build, run, lint, format, and test commands.
- Identify material risks, especially process-local state on Azure Container Apps and MCP endpoint separation.
