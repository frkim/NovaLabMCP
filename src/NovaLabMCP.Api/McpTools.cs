using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace NovaLabMCP.Api;

[McpServerToolType]
public sealed class ItsmReadOnlyTools
{
    [McpServerTool(Name = "readonly_itsm_list_tickets"), Description("READONLY: Lists IT service tickets with bounded pagination and search.")]
    public static string ListTickets(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "itsm", "tickets", "readonly_itsm_list_tickets", page, pageSize, query);

    [McpServerTool(Name = "readonly_itsm_get_ticket"), Description("READONLY: Retrieves an IT service ticket by ID.")]
    public static string GetTicket(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id) =>
        McpToolCalls.Get(store, activity, caller, "itsm", "tickets", id, "readonly_itsm_get_ticket");

    [McpServerTool(Name = "readonly_itsm_list_employees"), Description("READONLY: Lists service desk employees.")]
    public static string ListEmployees(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "itsm", "employees", "readonly_itsm_list_employees", page, pageSize, query);

    [McpServerTool(Name = "readonly_itsm_list_teams"), Description("READONLY: Lists support teams.")]
    public static string ListTeams(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "itsm", "teams", "readonly_itsm_list_teams", page, pageSize, query);

    [McpServerTool(Name = "readonly_itsm_list_assets"), Description("READONLY: Lists managed service desk assets.")]
    public static string ListAssets(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "itsm", "assets", "readonly_itsm_list_assets", page, pageSize, query);

    [McpServerTool(Name = "readonly_itsm_search_knowledge"), Description("READONLY: Searches published service desk knowledge articles.")]
    public static string SearchKnowledge(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string query, int page = 1, int pageSize = 10) =>
        McpToolCalls.List(store, activity, caller, "itsm", "knowledge", "readonly_itsm_search_knowledge", page, pageSize, query);

    [McpServerTool(Name = "readonly_itsm_get_dashboard"), Description("READONLY: Returns service desk ticket counts by status and priority.")]
    public static string GetDashboard(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller) =>
        activity.Track(caller, "READONLY", "itsm", "readonly_itsm_get_dashboard", "{}", () =>
        {
            McpToolCalls.RequireViewer(caller);

            var tickets = store.List("itsm", "tickets", 1, 50, null, null, null, null, null)["items"]!.AsArray();
            return McpActivityBuffer.Summary(new
            {
                total = store.Count("itsm", "tickets"),
                open = tickets.Count(ticket => ticket?["status"]?.ToString() is not "Resolved" and not "Closed"),
                critical = tickets.Count(ticket => ticket?["priority"]?.ToString() == "P1")
            });
        });
}

[McpServerToolType]
public sealed class ItsmReadWriteTools
{
    [McpServerTool(Name = "readwrite_itsm_create_ticket"), Description("READWRITE: Creates a ticket after validation.")]
    public static string CreateTicket(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject ticket) =>
        McpToolCalls.Write(store, activity, caller, "itsm", "tickets", "create", "readwrite_itsm_create_ticket", fields: ticket);

    [McpServerTool(Name = "readwrite_itsm_update_ticket"), Description("READWRITE: Updates a ticket after validation.")]
    public static string UpdateTicket(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, JsonObject changes) =>
        McpToolCalls.Write(store, activity, caller, "itsm", "tickets", "update", "readwrite_itsm_update_ticket", id, changes);

    [McpServerTool(Name = "readwrite_itsm_assign_ticket"), Description("READWRITE: Assigns an incident to an active employee and support team.")]
    public static string AssignTicket(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, string employeeId, string teamId) =>
        McpToolCalls.Write(store, activity, caller, "itsm", "tickets", "assign", "readwrite_itsm_assign_ticket", id, new JsonObject { ["employeeId"] = employeeId, ["teamId"] = teamId });

    [McpServerTool(Name = "readwrite_itsm_add_comment"), Description("READWRITE: Adds a public or internal ticket comment.")]
    public static string AddComment(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string ticketId, string body, string visibility = "Public") =>
        McpToolCalls.Write(store, activity, caller, "itsm", "comments", "create", "readwrite_itsm_add_comment",
            fields: new JsonObject { ["ticketId"] = ticketId, ["body"] = body, ["author"] = caller.Identity?.Name ?? "Demo editor", ["visibility"] = visibility });

    [McpServerTool(Name = "readwrite_itsm_resolve_ticket"), Description("READWRITE: Resolves a ticket with a required resolution summary.")]
    public static string ResolveTicket(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, string resolutionSummary) =>
        McpToolCalls.Write(store, activity, caller, "itsm", "tickets", "resolve", "readwrite_itsm_resolve_ticket", id,
            new JsonObject { ["resolutionSummary"] = resolutionSummary });

    [McpServerTool(Name = "readwrite_itsm_close_ticket"), Description("READWRITE: Closes a resolved ticket; explicit confirmation is required.")]
    public static string CloseTicket(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, bool confirm) =>
        McpToolCalls.Write(store, activity, caller, "itsm", "tickets", "close", "readwrite_itsm_close_ticket", id, confirm: confirm);

    [McpServerTool(Name = "readwrite_itsm_delete_ticket"), Description("READWRITE: Deletes a ticket after explicit confirmation.")]
    public static string DeleteTicket(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, bool confirm) =>
        McpToolCalls.Write(store, activity, caller, "itsm", "tickets", "delete", "readwrite_itsm_delete_ticket", id, confirm: confirm);
}

[McpServerToolType]
public sealed class CrmReadOnlyTools
{
    [McpServerTool(Name = "readonly_crm_list_accounts"), Description("READONLY: Lists CRM accounts.")]
    public static string ListAccounts(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "crm", "accounts", "readonly_crm_list_accounts", page, pageSize, query);

    [McpServerTool(Name = "readonly_crm_get_account"), Description("READONLY: Retrieves a CRM account by ID.")]
    public static string GetAccount(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id) =>
        McpToolCalls.Get(store, activity, caller, "crm", "accounts", id, "readonly_crm_get_account");

    [McpServerTool(Name = "readonly_crm_list_contacts"), Description("READONLY: Lists CRM contacts.")]
    public static string ListContacts(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "crm", "contacts", "readonly_crm_list_contacts", page, pageSize, query);

    [McpServerTool(Name = "readonly_crm_list_leads"), Description("READONLY: Lists CRM leads.")]
    public static string ListLeads(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "crm", "leads", "readonly_crm_list_leads", page, pageSize, query);

    [McpServerTool(Name = "readonly_crm_list_opportunities"), Description("READONLY: Lists CRM opportunities.")]
    public static string ListOpportunities(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "crm", "opportunities", "readonly_crm_list_opportunities", page, pageSize, query);

    [McpServerTool(Name = "readonly_crm_get_pipeline_summary"), Description("READONLY: Summarizes sales pipeline totals by stage.")]
    public static string GetPipelineSummary(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller) =>
        activity.Track(caller, "READONLY", "crm", "readonly_crm_get_pipeline_summary", "{}", () =>
        {
            McpToolCalls.RequireViewer(caller);
            var opportunities = store.List("crm", "opportunities", 1, 50, null, null, null, null, null)["items"]!.AsArray();
            return McpActivityBuffer.Summary(opportunities.GroupBy(item => item?["stage"]?.ToString() ?? "Unknown")
                .Select(group => new { stage = group.Key, count = group.Count(), value = group.Sum(item => decimal.TryParse(item?["amount"]?.ToString(), out var amount) ? amount : 0) }));
        });

    [McpServerTool(Name = "readonly_crm_list_activities"), Description("READONLY: Lists account and opportunity activities.")]
    public static string ListActivities(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "crm", "activities", "readonly_crm_list_activities", page, pageSize, query);

    [McpServerTool(Name = "readonly_crm_search_records"), Description("READONLY: Searches CRM accounts, contacts, leads, and opportunities.")]
    public static string SearchRecords(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string query, int page = 1, int pageSize = 10) =>
        activity.Track(caller, "READONLY", "crm", "readonly_crm_search_records", McpActivityBuffer.Summary(new { page, pageSize, queryLength = query.Length }), () =>
        {
            McpToolCalls.RequireViewer(caller);
            var results = new JsonArray();
            foreach (var entity in new[] { "accounts", "contacts", "leads", "opportunities" })
            {
                var records = store.List("crm", entity, 1, 50, query, null, null, null, null)["items"]!.AsArray();
                foreach (var item in records)
                {
                    results.Add(new JsonObject { ["entity"] = entity, ["record"] = item?.DeepClone() });
                }
            }

            return new JsonObject { ["items"] = new JsonArray(results.Skip((page - 1) * pageSize).Take(pageSize).Select(item => item?.DeepClone()).ToArray()), ["totalCount"] = results.Count }.ToJsonString();
        });
}

[McpServerToolType]
public sealed class CrmReadWriteTools
{
    [McpServerTool(Name = "readwrite_crm_create_account"), Description("READWRITE: Creates a CRM account.")]
    public static string CreateAccount(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject account) =>
        McpToolCalls.Write(store, activity, caller, "crm", "accounts", "create", "readwrite_crm_create_account", fields: account);

    [McpServerTool(Name = "readwrite_crm_update_account"), Description("READWRITE: Updates a CRM account.")]
    public static string UpdateAccount(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, JsonObject changes) =>
        McpToolCalls.Write(store, activity, caller, "crm", "accounts", "update", "readwrite_crm_update_account", id, changes);

    [McpServerTool(Name = "readwrite_crm_create_contact"), Description("READWRITE: Creates a contact linked to an existing account.")]
    public static string CreateContact(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject contact) =>
        McpToolCalls.Write(store, activity, caller, "crm", "contacts", "create", "readwrite_crm_create_contact", fields: contact);

    [McpServerTool(Name = "readwrite_crm_update_contact"), Description("READWRITE: Updates a CRM contact.")]
    public static string UpdateContact(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, JsonObject changes) =>
        McpToolCalls.Write(store, activity, caller, "crm", "contacts", "update", "readwrite_crm_update_contact", id, changes);

    [McpServerTool(Name = "readwrite_crm_create_lead"), Description("READWRITE: Creates a CRM lead.")]
    public static string CreateLead(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject lead) =>
        McpToolCalls.Write(store, activity, caller, "crm", "leads", "create", "readwrite_crm_create_lead", fields: lead);

    [McpServerTool(Name = "readwrite_crm_qualify_lead"), Description("READWRITE: Qualifies a lead and records its conversion associations.")]
    public static string QualifyLead(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, string accountId, string contactId, bool confirm) =>
        McpToolCalls.Write(store, activity, caller, "crm", "leads", "qualify", "readwrite_crm_qualify_lead", id,
            new JsonObject { ["accountId"] = accountId, ["contactId"] = contactId }, confirm: confirm);

    [McpServerTool(Name = "readwrite_crm_create_opportunity"), Description("READWRITE: Creates an opportunity linked to an account and active owner.")]
    public static string CreateOpportunity(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject opportunity) =>
        McpToolCalls.Write(store, activity, caller, "crm", "opportunities", "create", "readwrite_crm_create_opportunity", fields: opportunity);

    [McpServerTool(Name = "readwrite_crm_update_opportunity"), Description("READWRITE: Updates an opportunity.")]
    public static string UpdateOpportunity(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, JsonObject changes) =>
        McpToolCalls.Write(store, activity, caller, "crm", "opportunities", "update", "readwrite_crm_update_opportunity", id, changes);

    [McpServerTool(Name = "readwrite_crm_log_activity"), Description("READWRITE: Logs a sales activity.")]
    public static string LogActivity(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject entry) =>
        McpToolCalls.Write(store, activity, caller, "crm", "activities", "create", "readwrite_crm_log_activity", fields: entry);

    [McpServerTool(Name = "readwrite_crm_delete_record"), Description("READWRITE: Deletes a CRM record after checking relationships and explicit confirmation.")]
    public static string DeleteRecord(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string entity, string id, bool confirm) =>
        McpToolCalls.Write(store, activity, caller, "crm", entity, "delete", "readwrite_crm_delete_record", id, confirm: confirm);
}

[McpServerToolType]
public sealed class ProjectReadOnlyTools
{
    [McpServerTool(Name = "readonly_pm_list_portfolios"), Description("READONLY: Lists project portfolios.")]
    public static string ListPortfolios(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "projects", "portfolios", "readonly_pm_list_portfolios", page, pageSize, query);

    [McpServerTool(Name = "readonly_pm_list_projects"), Description("READONLY: Lists projects.")]
    public static string ListProjects(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "projects", "projects", "readonly_pm_list_projects", page, pageSize, query);

    [McpServerTool(Name = "readonly_pm_get_project"), Description("READONLY: Retrieves one project by ID.")]
    public static string GetProject(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id) =>
        McpToolCalls.Get(store, activity, caller, "projects", "projects", id, "readonly_pm_get_project");

    [McpServerTool(Name = "readonly_pm_list_tasks"), Description("READONLY: Lists project tasks and dependencies.")]
    public static string ListTasks(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "projects", "tasks", "readonly_pm_list_tasks", page, pageSize, query);

    [McpServerTool(Name = "readonly_pm_list_milestones"), Description("READONLY: Lists project milestones.")]
    public static string ListMilestones(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "projects", "milestones", "readonly_pm_list_milestones", page, pageSize, query);

    [McpServerTool(Name = "readonly_pm_list_risks"), Description("READONLY: Lists project risks and mitigations.")]
    public static string ListRisks(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, int page = 1, int pageSize = 10, string? query = null) =>
        McpToolCalls.List(store, activity, caller, "projects", "risks", "readonly_pm_list_risks", page, pageSize, query);

    [McpServerTool(Name = "readonly_pm_get_project_health"), Description("READONLY: Returns a project's health, task progress, and open risks.")]
    public static string GetProjectHealth(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id) =>
        activity.Track(caller, "READONLY", "projects", "readonly_pm_get_project_health", McpActivityBuffer.Summary(new { id }), () =>
        {
            McpToolCalls.RequireViewer(caller);
            var project = store.Get("projects", "projects", id) ?? throw new KeyNotFoundException("Project not found.");
            var tasks = store.List("projects", "tasks", 1, 50, null, null, null, "projectId", id)["items"]!.AsArray();
            var risks = store.List("projects", "risks", 1, 50, null, null, null, "projectId", id)["items"]!.AsArray();
            return McpActivityBuffer.Summary(new { project, openTasks = tasks.Count(task => task?["status"]?.ToString() != "Completed"), openRisks = risks.Count(risk => risk?["status"]?.ToString() != "Closed") });
        });

    [McpServerTool(Name = "readonly_pm_get_portfolio_summary"), Description("READONLY: Summarizes portfolio project count and health.")]
    public static string GetPortfolioSummary(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller) =>
        activity.Track(caller, "READONLY", "projects", "readonly_pm_get_portfolio_summary", "{}", () =>
        {
            McpToolCalls.RequireViewer(caller);
            var projects = store.List("projects", "projects", 1, 50, null, null, null, null, null)["items"]!.AsArray();
            return McpActivityBuffer.Summary(new { totalProjects = projects.Count, byHealth = projects.GroupBy(project => project?["health"]?.ToString()).Select(group => new { health = group.Key, count = group.Count() }) });
        });
}

[McpServerToolType]
public sealed class ProjectReadWriteTools
{
    [McpServerTool(Name = "readwrite_pm_create_project"), Description("READWRITE: Creates a project in an existing portfolio.")]
    public static string CreateProject(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject project) =>
        McpToolCalls.Write(store, activity, caller, "projects", "projects", "create", "readwrite_pm_create_project", fields: project);

    [McpServerTool(Name = "readwrite_pm_update_project"), Description("READWRITE: Updates a project.")]
    public static string UpdateProject(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, JsonObject changes) =>
        McpToolCalls.Write(store, activity, caller, "projects", "projects", "update", "readwrite_pm_update_project", id, changes);

    [McpServerTool(Name = "readwrite_pm_create_task"), Description("READWRITE: Creates a task with validated project and dependencies.")]
    public static string CreateTask(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject task) =>
        McpToolCalls.Write(store, activity, caller, "projects", "tasks", "create", "readwrite_pm_create_task", fields: task);

    [McpServerTool(Name = "readwrite_pm_update_task"), Description("READWRITE: Updates a project task.")]
    public static string UpdateTask(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, JsonObject changes) =>
        McpToolCalls.Write(store, activity, caller, "projects", "tasks", "update", "readwrite_pm_update_task", id, changes);

    [McpServerTool(Name = "readwrite_pm_assign_task"), Description("READWRITE: Assigns a task to a team member.")]
    public static string AssignTask(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, string assigneeId) =>
        McpToolCalls.Write(store, activity, caller, "projects", "tasks", "update", "readwrite_pm_assign_task", id, new JsonObject { ["assigneeId"] = assigneeId });

    [McpServerTool(Name = "readwrite_pm_complete_task"), Description("READWRITE: Completes an unblocked task; explicit confirmation is required.")]
    public static string CompleteTask(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, bool confirm) =>
        McpToolCalls.Write(store, activity, caller, "projects", "tasks", "complete", "readwrite_pm_complete_task", id, confirm: confirm);

    [McpServerTool(Name = "readwrite_pm_create_milestone"), Description("READWRITE: Creates a milestone in an existing project.")]
    public static string CreateMilestone(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject milestone) =>
        McpToolCalls.Write(store, activity, caller, "projects", "milestones", "create", "readwrite_pm_create_milestone", fields: milestone);

    [McpServerTool(Name = "readwrite_pm_update_milestone"), Description("READWRITE: Updates a project milestone.")]
    public static string UpdateMilestone(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, JsonObject changes) =>
        McpToolCalls.Write(store, activity, caller, "projects", "milestones", "update", "readwrite_pm_update_milestone", id, changes);

    [McpServerTool(Name = "readwrite_pm_create_risk"), Description("READWRITE: Creates a project risk with 1-5 probability and impact.")]
    public static string CreateRisk(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject risk) =>
        McpToolCalls.Write(store, activity, caller, "projects", "risks", "create", "readwrite_pm_create_risk", fields: risk);

    [McpServerTool(Name = "readwrite_pm_update_risk"), Description("READWRITE: Updates a project risk and mitigation.")]
    public static string UpdateRisk(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string id, JsonObject changes) =>
        McpToolCalls.Write(store, activity, caller, "projects", "risks", "update", "readwrite_pm_update_risk", id, changes);

    [McpServerTool(Name = "readwrite_pm_add_status_update"), Description("READWRITE: Adds a project status update.")]
    public static string AddStatusUpdate(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, JsonObject update) =>
        McpToolCalls.Write(store, activity, caller, "projects", "status-updates", "create", "readwrite_pm_add_status_update", fields: update);

    [McpServerTool(Name = "readwrite_pm_delete_record"), Description("READWRITE: Deletes a project record after relationship validation and explicit confirmation.")]
    public static string DeleteRecord(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string entity, string id, bool confirm) =>
        McpToolCalls.Write(store, activity, caller, "projects", entity, "delete", "readwrite_pm_delete_record", id, confirm: confirm);
}
