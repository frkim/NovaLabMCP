using System.Security.Claims;
using System.Text.Json.Nodes;
using NovaLabMCP.Api;

namespace NovaLabMCP.Tests;

public sealed class ScenarioStoreTests
{
    [Fact]
    public void should_seed_all_required_entities_and_minimum_counts()
    {
        var store = new ScenarioStore();

        Assert.Equal(40, store.Count("itsm", "tickets"));
        Assert.Equal(30, store.Count("itsm", "employees"));
        Assert.Equal(5, store.Count("itsm", "teams"));
        Assert.Equal(25, store.Count("itsm", "assets"));
        Assert.Equal(60, store.Count("itsm", "comments"));
        Assert.Equal(15, store.Count("itsm", "knowledge"));
        Assert.Equal(25, store.Count("crm", "accounts"));
        Assert.Equal(50, store.Count("crm", "contacts"));
        Assert.Equal(35, store.Count("crm", "leads"));
        Assert.Equal(25, store.Count("crm", "opportunities"));
        Assert.Equal(50, store.Count("crm", "activities"));
        Assert.Equal(12, store.Count("crm", "products"));
        Assert.Equal(3, store.Count("crm", "sales-teams"));
        Assert.Equal(4, store.Count("projects", "portfolios"));
        Assert.Equal(12, store.Count("projects", "projects"));
        Assert.Equal(25, store.Count("projects", "milestones"));
        Assert.Equal(80, store.Count("projects", "tasks"));
        Assert.Equal(20, store.Count("projects", "team-members"));
        Assert.Equal(18, store.Count("projects", "risks"));
        Assert.Equal(20, store.Count("projects", "status-updates"));
    }

    [Fact]
    public void should_return_defensive_copies_when_a_record_is_read()
    {
        var store = new ScenarioStore();
        var copy = store.Get("itsm", "tickets", "itsm-tickets-001")!;
        copy["title"] = "Changed by caller";

        Assert.NotEqual("Changed by caller", store.Get("itsm", "tickets", "itsm-tickets-001")!["title"]!.ToString());
    }

    [Fact]
    public void should_page_search_sort_and_reject_unsupported_sort_fields()
    {
        var store = new ScenarioStore();
        var page = store.List("crm", "accounts", 1, 1, "market", "name", "asc", null, null);

        Assert.Equal(1, page["page"]!.GetValue<int>());
        Assert.Equal(1, page["pageSize"]!.GetValue<int>());
        Assert.Equal(2, page["totalCount"]!.GetValue<int>());
        Assert.Throws<ArgumentException>(() => store.List("crm", "accounts", 1, 10, null, "DROP TABLE", "asc", null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.List("crm", "accounts", 1, 500, null, null, null, null, null));
    }

    [Fact]
    public void should_require_impact_and_team_for_critical_tickets()
    {
        var store = new ScenarioStore();
        var ticket = new JsonObject
        {
            ["title"] = "Critical payroll access incident",
            ["status"] = "New",
            ["priority"] = "P1",
            ["requesterId"] = "itsm-employees-001"
        };

        var error = Assert.Throws<ArgumentException>(() => store.Create("itsm", "tickets", ticket));

        Assert.Contains("impact statement", error.Message);
    }

    [Fact]
    public void should_block_deleting_accounts_with_linked_contacts()
    {
        var store = new ScenarioStore();

        Assert.Throws<InvalidOperationException>(() => store.Delete("crm", "accounts", "crm-accounts-001"));
    }

    [Fact]
    public void should_reject_circular_or_unresolved_task_dependencies()
    {
        var store = new ScenarioStore();
        var firstTask = store.Get("projects", "tasks", "projects-tasks-001")!;
        firstTask["dependencies"] = new JsonArray("projects-tasks-001");

        Assert.Throws<ArgumentException>(() => store.Update("projects", "tasks", "projects-tasks-001", firstTask));

        var blockedTask = new JsonObject
        {
            ["name"] = "Complete validated integration",
            ["projectId"] = "projects-projects-001",
            ["status"] = "Completed",
            ["completedAt"] = "2026-03-01T12:00:00Z",
            ["dependencies"] = new JsonArray("projects-tasks-002")
        };
        Assert.Throws<ArgumentException>(() => store.Create("projects", "tasks", blockedTask));
    }

    [Fact]
    public void should_keep_readonly_and_readwrite_mcp_catalogs_separate()
    {
        var readonlyTools = McpCatalog.Tools.Where(name => name.StartsWith("readonly_itsm_", StringComparison.Ordinal)).ToArray();
        var readwriteTools = McpCatalog.Tools.Where(name => name.StartsWith("readwrite_itsm_", StringComparison.Ordinal)).ToArray();

        Assert.Contains("readonly_itsm_list_tickets", readonlyTools);
        Assert.DoesNotContain(readonlyTools, name => name.StartsWith("readwrite_", StringComparison.Ordinal));
        Assert.Contains("readwrite_itsm_create_ticket", readwriteTools);
        Assert.DoesNotContain(readwriteTools, name => name.StartsWith("readonly_", StringComparison.Ordinal));
    }

    [Fact]
    public void should_log_one_redacted_activity_event_for_success_and_failure()
    {
        var activity = new McpActivityBuffer(10);
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "demo-user"), new Claim(ClaimTypes.Role, "DemoViewer")], "test"));

        Assert.Equal("safe", activity.Track(caller, "READONLY", "itsm", "readonly_itsm_list_tickets", "{\"query\":\"omitted\"}", () => "safe"));
        Assert.Throws<ArgumentException>(() => activity.Track<string>(caller, "READWRITE", "crm", "readwrite_crm_create_lead", "{}", () => throw new ArgumentException("invalid")));

        var events = activity.List();
        Assert.Equal(2, events.Count);
        Assert.Equal("ValidationError", events[0].Outcome);
        Assert.Equal("Success", events[1].Outcome);
        Assert.DoesNotContain("omitted", events[1].InputSummary, StringComparison.Ordinal);
    }

}
