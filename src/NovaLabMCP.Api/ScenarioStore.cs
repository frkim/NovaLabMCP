using System.Text.Json;
using System.Text.Json.Nodes;

namespace NovaLabMCP.Api;

public sealed class ScenarioStore
{
    private readonly object gate = new();
    private Dictionary<string, Dictionary<string, List<JsonObject>>> records = [];

    public ScenarioStore() => Reset();

    public static IReadOnlyDictionary<string, string[]> Entities { get; } =
        new Dictionary<string, string[]>
        {
            ["itsm"] = ["tickets", "employees", "teams", "assets", "comments", "knowledge"],
            ["crm"] = ["accounts", "contacts", "leads", "opportunities", "activities", "products", "sales-teams"],
            ["projects"] = ["portfolios", "projects", "milestones", "tasks", "team-members", "risks", "status-updates"]
        };

    public void Reset()
    {
        lock (gate)
        {
            records = SeedData.Create();
        }
    }

    public JsonObject Scenarios() => new()
    {
        ["items"] = new JsonArray(
            Scenario("itsm", "IT Service Desk", "Contoso IT Service Desk"),
            Scenario("crm", "CRM & Sales", "Contoso CRM & Sales"),
            Scenario("projects", "Project Management", "Contoso Project Management"))
    };

    public JsonObject List(string scenario, string entity, int page, int pageSize, string? query, string? sort, string? order, string? filterBy, string? filterValue)
    {
        ValidateCollection(scenario, entity);
        if (page < 1 || pageSize is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be positive and pageSize must be between 1 and 50.");
        }

        lock (gate)
        {
            IEnumerable<JsonObject> result = records[scenario][entity];
            if (!string.IsNullOrWhiteSpace(query))
            {
                result = result.Where(item => item.ToJsonString().Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(filterBy) && !string.IsNullOrWhiteSpace(filterValue))
            {
                if (!records[scenario][entity].Any(item => item.ContainsKey(filterBy)))
                {
                    throw new ArgumentException("The selected filter is not supported.");
                }

                result = result.Where(item => string.Equals(item[filterBy]?.ToString(), filterValue, StringComparison.OrdinalIgnoreCase));
            }

            var items = result.ToList();
            if (!string.IsNullOrWhiteSpace(sort))
            {
                if (!records[scenario][entity].Any(item => item.ContainsKey(sort)))
                {
                    throw new ArgumentException("The selected sort field is not supported.");
                }

                items = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase)
                    ? items.OrderByDescending(item => item[sort]?.ToString(), StringComparer.OrdinalIgnoreCase).ToList()
                    : items.OrderBy(item => item[sort]?.ToString(), StringComparer.OrdinalIgnoreCase).ToList();
            }

            var total = items.Count;
            return new JsonObject
            {
                ["items"] = new JsonArray(items.Skip((page - 1) * pageSize).Take(pageSize).Select(item => item.DeepClone()).ToArray()),
                ["page"] = page,
                ["pageSize"] = pageSize,
                ["totalCount"] = total,
                ["totalPages"] = (int)Math.Ceiling(total / (double)pageSize)
            };
        }
    }

    public JsonObject? Get(string scenario, string entity, string id)
    {
        ValidateCollection(scenario, entity);
        lock (gate)
        {
            return records[scenario][entity].FirstOrDefault(item => item["id"]?.ToString() == id)?.DeepClone().AsObject();
        }
    }

    public JsonObject Create(string scenario, string entity, JsonObject input)
    {
        ValidateCollection(scenario, entity);
        lock (gate)
        {
            var item = input.DeepClone().AsObject();
            item["id"] = Guid.NewGuid().ToString("D");
            item["createdAt"] ??= DateTimeOffset.UtcNow;
            item["updatedAt"] = DateTimeOffset.UtcNow;
            ValidateRecord(scenario, entity, item, null);
            records[scenario][entity].Add(item);
            RecalculateProjectProgress(scenario, entity, item);
            return item.DeepClone().AsObject();
        }
    }

    public JsonObject? Update(string scenario, string entity, string id, JsonObject changes)
    {
        ValidateCollection(scenario, entity);
        lock (gate)
        {
            var index = records[scenario][entity].FindIndex(item => item["id"]?.ToString() == id);
            if (index < 0)
            {
                return null;
            }

            var updated = records[scenario][entity][index].DeepClone().AsObject();
            foreach (var (key, value) in changes)
            {
                if (key is not "id" and not "createdAt")
                {
                    updated[key] = value?.DeepClone();
                }
            }

            updated["updatedAt"] = DateTimeOffset.UtcNow;
            ValidateRecord(scenario, entity, updated, id);
            records[scenario][entity][index] = updated;
            RecalculateProjectProgress(scenario, entity, updated);
            return updated.DeepClone().AsObject();
        }
    }

    public bool Delete(string scenario, string entity, string id)
    {
        ValidateCollection(scenario, entity);
        lock (gate)
        {
            var item = records[scenario][entity].FirstOrDefault(record => record["id"]?.ToString() == id);
            if (item is null)
            {
                return false;
            }

            EnsureDeleteAllowed(scenario, entity, id);
            records[scenario][entity].Remove(item);
            RecalculateProjectProgress(scenario, entity, item);
            return true;
        }
    }

    public int Count(string scenario, string entity)
    {
        ValidateCollection(scenario, entity);
        lock (gate)
        {
            return records[scenario][entity].Count;
        }
    }

    public static void ValidateCollection(string scenario, string entity)
    {
        if (!Entities.TryGetValue(scenario, out var entities) || !entities.Contains(entity, StringComparer.Ordinal))
        {
            throw new KeyNotFoundException("The requested scenario or entity does not exist.");
        }
    }

    private void ValidateRecord(string scenario, string entity, JsonObject item, string? currentId)
    {
        var required = entity switch
        {
            "tickets" => new[] { "title", "status", "priority", "requesterId" },
            "employees" or "teams" or "assets" or "knowledge" or "accounts" or "contacts" or "leads" or "opportunities" or
                "activities" or "products" or "sales-teams" or "portfolios" or "projects" or "milestones" or "tasks" or
                "team-members" or "risks" or "status-updates" => new[] { "name" },
            "comments" => new[] { "ticketId", "body", "author" },
            _ => Array.Empty<string>()
        };

        foreach (var key in required)
        {
            if (string.IsNullOrWhiteSpace(item[key]?.ToString()))
            {
                throw new ArgumentException($"{key} is required.");
            }
        }

        if (scenario == "itsm" && entity == "tickets")
        {
            var priority = item["priority"]?.ToString();
            var status = item["status"]?.ToString();
            if (priority == "P1" && (string.IsNullOrWhiteSpace(item["impactStatement"]?.ToString()) || !Exists("itsm", "teams", item["supportTeamId"]?.ToString())))
            {
                throw new ArgumentException("P1 tickets require an impact statement and assigned support team.");
            }

            if (item["assigneeId"] is JsonNode assignee && !IsActiveEmployee(assignee.ToString()))
            {
                throw new ArgumentException("Assignment requires an active employee.");
            }

            if (status == "Resolved" && string.IsNullOrWhiteSpace(item["resolutionSummary"]?.ToString()))
            {
                throw new ArgumentException("Resolving a ticket requires a resolution summary.");
            }

            if (status == "Closed" && records["itsm"]["tickets"].FirstOrDefault(ticket => ticket["id"]?.ToString() == currentId)?["status"]?.ToString() != "Resolved")
            {
                throw new ArgumentException("Only a resolved ticket can be closed.");
            }
        }

        if (scenario == "crm" && entity == "opportunities")
        {
            if (!Exists("crm", "accounts", item["accountId"]?.ToString()) || !IsActiveEmployee(item["ownerId"]?.ToString()))
            {
                throw new ArgumentException("Opportunities require an existing account and active owner.");
            }

            if (!decimal.TryParse(item["amount"]?.ToString(), out var amount) || amount < 0)
            {
                throw new ArgumentException("Opportunity amount must be non-negative.");
            }

            var stage = item["stage"]?.ToString();
            if (stage is "Closed Won" or "Closed Lost" && string.IsNullOrWhiteSpace(item["closeDate"]?.ToString()))
            {
                throw new ArgumentException("Closed opportunities require a close date.");
            }

            if (stage == "Closed Lost" && string.IsNullOrWhiteSpace(item["lossReason"]?.ToString()))
            {
                throw new ArgumentException("Closed Lost opportunities require a loss reason.");
            }
        }

        if (scenario == "projects" && entity == "tasks")
        {
            if (!Exists("projects", "projects", item["projectId"]?.ToString()))
            {
                throw new ArgumentException("Tasks require an existing project.");
            }

            var dependencies = item["dependencies"] as JsonArray ?? [];
            var dependencyIds = dependencies.Select(dependency => dependency?.ToString()).Where(value => value is not null).ToHashSet();
            if (currentId is not null && dependencyIds.Contains(currentId) ||
                dependencyIds.Any(dependency => dependency == item["id"]?.ToString() || HasDependencyPath(dependency!, currentId ?? item["id"]?.ToString() ?? string.Empty)))
            {
                throw new ArgumentException("Task dependencies cannot contain cycles.");
            }

            if (item["status"]?.ToString() == "Completed")
            {
                if (string.IsNullOrWhiteSpace(item["completedAt"]?.ToString()))
                {
                    throw new ArgumentException("Completed tasks require a completion timestamp.");
                }

                foreach (var dependency in dependencies)
                {
                    var blocker = records["projects"]["tasks"].FirstOrDefault(task => task["id"]?.ToString() == dependency?.ToString());
                    if (blocker?["status"]?.ToString() != "Completed")
                    {
                        throw new ArgumentException("Tasks cannot be completed while blocking dependencies remain unresolved.");
                    }
                }
            }
        }

        if (scenario == "projects" && entity == "risks" &&
            (!decimal.TryParse(item["probability"]?.ToString(), out var probability) || !decimal.TryParse(item["impact"]?.ToString(), out var impact) ||
             probability is < 1 or > 5 || impact is < 1 or > 5))
        {
            throw new ArgumentException("Risk probability and impact must be values from 1 to 5.");
        }
    }

    private bool HasDependencyPath(string from, string target)
    {
        var visited = new HashSet<string>();
        var pending = new Stack<string>();
        pending.Push(from);
        while (pending.TryPop(out var id))
        {
            if (id == target)
            {
                return true;
            }

            if (visited.Add(id))
            {
                var task = records["projects"]["tasks"].FirstOrDefault(item => item["id"]?.ToString() == id);
                foreach (var dependency in task?["dependencies"]?.AsArray() ?? [])
                {
                    if (dependency is not null)
                    {
                        pending.Push(dependency.ToString());
                    }
                }
            }
        }

        return false;
    }

    private bool Exists(string scenario, string entity, string? id) =>
        id is not null && records[scenario][entity].Any(item => item["id"]?.ToString() == id);

    private bool IsActiveEmployee(string? id) =>
        id is not null && records["itsm"]["employees"].Concat(records["projects"]["team-members"])
            .Any(item => item["id"]?.ToString() == id && item["active"]?.ToString() != "false");

    private void EnsureDeleteAllowed(string scenario, string entity, string id)
    {
        var blocked = scenario switch
        {
            "itsm" when entity == "employees" => records["itsm"]["tickets"].Any(item => item["assigneeId"]?.ToString() == id && item["status"]?.ToString() is not "Resolved" and not "Closed"),
            "itsm" when entity == "teams" => records["itsm"]["tickets"].Any(item => item["supportTeamId"]?.ToString() == id && item["status"]?.ToString() is not "Resolved" and not "Closed"),
            "crm" when entity == "accounts" => records["crm"]["contacts"].Any(item => item["accountId"]?.ToString() == id) || records["crm"]["opportunities"].Any(item => item["accountId"]?.ToString() == id),
            "projects" when entity == "projects" => records["projects"]["tasks"].Any(item => item["projectId"]?.ToString() == id && item["status"]?.ToString() != "Completed") ||
                records["projects"]["risks"].Any(item => item["projectId"]?.ToString() == id && item["status"]?.ToString() != "Closed"),
            _ => false
        };

        if (blocked)
        {
            throw new InvalidOperationException("The record has active related records and cannot be deleted.");
        }
    }

    private void RecalculateProjectProgress(string scenario, string entity, JsonObject item)
    {
        if (scenario != "projects" || entity != "tasks")
        {
            return;
        }

        var projectId = item["projectId"]?.ToString();
        var tasks = records["projects"]["tasks"].Where(task => task["projectId"]?.ToString() == projectId).ToArray();
        var project = records["projects"]["projects"].FirstOrDefault(project => project["id"]?.ToString() == projectId);
        if (project is not null && tasks.Length > 0)
        {
            project["completionPercentage"] = (int)Math.Round(tasks.Count(task => task["status"]?.ToString() == "Completed") * 100d / tasks.Length);
        }
    }

    private static JsonObject Scenario(string id, string name, string description) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["description"] = description,
        ["entities"] = new JsonArray(Entities[id].Select(entity => (JsonNode?)JsonValue.Create(entity)).ToArray())
    };
}

internal static class SeedData
{
    private delegate void SeedAdder(string scenario, string entity, int index, params (string Key, object? Value)[] fields);

    private static readonly string[] Names =
    [
        "Avery Chen", "Jordan Rivera", "Morgan Patel", "Riley Thompson", "Casey Nguyen", "Taylor Brooks",
        "Jamie Kim", "Cameron Davis", "Quinn Foster", "Drew Wilson", "Skylar Bennett", "Alex Morgan",
        "Reese Sullivan", "Parker Shah", "Rowan Mitchell", "Emerson Lee", "Dakota Clark", "Finley Adams",
        "Sage Turner", "Elliot Bailey", "Harper Diaz", "Logan Murphy", "Charlie Ross", "Kendall Price",
        "Blake Cooper", "Robin James", "Payton Reed", "Sam Ellis", "Micah Ward", "Devon Hughes"
    ];

    private static readonly string[] TicketTitles =
    [
        "VPN certificate renewal fails after device restart", "Conditional Access loop blocks finance sign-in",
        "BitLocker recovery prompt appears after firmware update", "ERP invoice search exceeds response target",
        "Boardroom camera loses connection during hybrid meetings", "North campus Wi-Fi drops on the third floor",
        "Payroll export fails for employees with multiple cost centers", "New laptop cannot enroll in endpoint management",
        "Shared mailbox permissions are missing for the acquisition team", "Warehouse scanner cannot reach inventory service",
        "Teams calls have one-way audio from the London office", "Engineering workstation reports low disk space"
    ];

    private static readonly string[] AccountNames =
    [
        "Northstar Components", "Juniper Market Group", "Harborview Credit Union", "Clearwater Health Partners", "Redstone Energy",
        "Summit Advisory Works", "Bluehaven Manufacturing", "Cedar & Finch Retail", "Westbridge Financial", "Everwell Clinics",
        "Solstice Grid Systems", "Meridian Legal Group", "Pioneer Industrial Supply", "Mapleline Grocers", "Stonepath Capital",
        "Brightline Medical Network", "Crown Peak Renewables", "Linden Consulting Partners", "Ironwood Robotics", "MarketSquare Foods",
        "Granite Bay Insurance", "Silverleaf Care Network", "Easton Power Cooperative", "Oakwell Strategy Group", "Vector Forge Labs"
    ];

    private static readonly string[] ProjectNames =
    [
        "Global ERP Finance Rollout", "Customer Data Platform", "Identity Governance Transformation",
        "Retail Store Network Modernization", "Sales Operations Automation", "Manufacturing Analytics Foundation",
        "Clinical Scheduling Renewal", "Energy Market Forecasting", "Enterprise Collaboration Refresh",
        "Supplier Risk Management", "Digital Workplace Accessibility", "Regional Data Center Exit"
    ];

    public static Dictionary<string, Dictionary<string, List<JsonObject>>> Create()
    {
        var data = ScenarioStore.Entities.ToDictionary(
            scenario => scenario.Key,
            scenario => scenario.Value.ToDictionary(entity => entity, _ => new List<JsonObject>(), StringComparer.Ordinal),
            StringComparer.Ordinal);

        void Add(string scenario, string entity, int index, params (string Key, object? Value)[] fields)
        {
            var record = new JsonObject { ["id"] = $"{scenario}-{entity}-{index + 1:000}" };
            foreach (var (key, value) in fields)
            {
                record[key] = JsonSerializer.SerializeToNode(value);
            }

            data[scenario][entity].Add(record);
        }

        SeedItsm(Add);
        SeedCrm(Add);
        SeedProjects(Add);
        return data;
    }

    private static void SeedItsm(SeedAdder add)
    {
        string[] teams = ["Service Desk", "Identity & Access", "Network Operations", "Endpoint Engineering", "Business Applications"];
        for (var i = 0; i < 30; i++)
        {
            add("itsm", "employees", i, ("name", Names[i]), ("email", $"{Names[i].ToLowerInvariant().Replace(' ', '.')}@example.com"),
                ("department", new[] { "Finance", "Engineering", "Operations", "Human Resources", "Sales" }[i % 5]),
                ("jobTitle", new[] { "Analyst", "Engineer", "Manager", "Coordinator" }[i % 4]), ("location", new[] { "Seattle", "London", "Toronto", "Dublin" }[i % 4]),
                ("active", true));
        }

        for (var i = 0; i < teams.Length; i++)
        {
            add("itsm", "teams", i, ("name", teams[i]), ("queue", teams[i].Replace(' ', '-').ToLowerInvariant()), ("description", $"Handles {teams[i].ToLowerInvariant()} requests across Contoso."),
                ("teamLead", Names[i]), ("active", true));
        }

        for (var i = 0; i < 25; i++)
        {
            add("itsm", "assets", i, ("name", $"{new[] { "Latitude 7450", "ThinkPad X1", "Surface Laptop 7", "Precision 5690" }[i % 4]} · {i + 1:000}"),
                ("assetTag", $"CTO-{i + 1001:0000}"), ("type", new[] { "Laptop", "Mobile", "Network switch", "Meeting room kit" }[i % 4]),
                ("manufacturer", new[] { "Dell", "Lenovo", "Microsoft", "Cisco" }[i % 4]), ("ownerId", $"itsm-employees-{i % 30 + 1:000}"),
                ("location", new[] { "Seattle", "London", "Toronto", "Dublin" }[i % 4]), ("lifecycleStatus", "In service"));
        }

        for (var i = 0; i < 40; i++)
        {
            var priority = i < 4 ? "P1" : new[] { "P2", "P3", "P4" }[i % 3];
            var status = new[] { "New", "In Progress", "Pending", "Resolved", "Closed" }[i % 5];
            add("itsm", "tickets", i, ("name", $"INC-{41000 + i}"), ("ticketNumber", $"INC-{41000 + i}"),
                ("title", TicketTitles[i % TicketTitles.Length]), ("description", $"Investigate {TicketTitles[i % TicketTitles.Length].ToLowerInvariant()} affecting the {new[] { "finance", "field operations", "engineering", "customer support" }[i % 4]} team."),
                ("type", i < 4 ? "Incident" : new[] { "Incident", "Service Request", "Problem" }[i % 3]), ("status", status), ("priority", priority),
                ("category", new[] { "Network", "Identity", "Endpoint", "Business Applications" }[i % 4]), ("requesterId", $"itsm-employees-{i % 30 + 1:000}"),
                ("assigneeId", i % 6 == 0 ? null : $"itsm-employees-{(i + 5) % 30 + 1:000}"), ("supportTeamId", $"itsm-teams-{i % 5 + 1:000}"),
                ("relatedAssetId", $"itsm-assets-{i % 25 + 1:000}"), ("createdAt", "2026-01-15T10:00:00Z"),
                ("dueAt", DateTimeOffset.Parse("2026-01-15T10:00:00Z").AddHours(priority switch { "P1" => 4, "P2" => 8, "P3" => 24, _ => 72 })),
                ("impactStatement", priority == "P1" ? "Payroll processing for 180 employees is blocked across two offices." : null),
                ("resolutionSummary", status is "Resolved" or "Closed" ? "Restored service and verified with the requester." : null));
        }

        for (var i = 0; i < 60; i++)
        {
            add("itsm", "comments", i, ("name", $"Incident update {i + 1}"), ("ticketId", $"itsm-tickets-{i % 40 + 1:000}"),
                ("author", Names[i % Names.Length]), ("body", new[] { "Confirmed affected device and captured the latest diagnostic timestamp.", "Validated service recovery with the requester and monitored for recurrence.", "Found a certificate renewal delay on the affected network segment.", "Coordinated the next update with the on-call support team." }[i % 4]),
                ("visibility", i % 3 == 0 ? "Internal" : "Public"), ("createdAt", "2026-01-15T11:00:00Z"));
        }

        string[] articles = ["Restore a VPN certificate after device renewal", "Troubleshoot Entra conditional access sign-in loops", "Recover BitLocker after approved firmware updates",
            "Diagnose ERP search latency", "Prepare a conference room for hybrid calls", "Reconnect a managed laptop to corporate Wi-Fi",
            "Request access to shared mailboxes", "Enroll a replacement endpoint", "Resolve payroll export validation", "Use the secure file transfer portal",
            "Report a suspicious authentication prompt", "Restart a managed print queue", "Configure a mobile authenticator", "Request a temporary admin elevation", "Check service health during an outage"];
        for (var i = 0; i < articles.Length; i++)
        {
            add("itsm", "knowledge", i, ("name", articles[i]), ("title", articles[i]), ("category", new[] { "Identity", "Network", "Endpoint", "Applications" }[i % 4]),
                ("summary", $"Steps and owner guidance for {articles[i].ToLowerInvariant()}."), ("body", "Confirm the affected service, follow the approved recovery sequence, and record the outcome in the related incident."),
                ("tags", new[] { "support", "recovery" }), ("publicationStatus", "Published"), ("updatedAt", "2026-01-14T16:00:00Z"));
        }
    }

    private static void SeedCrm(SeedAdder add)
    {
        var industries = new[] { "Manufacturing", "Retail", "Financial Services", "Healthcare", "Energy", "Professional Services" };
        for (var i = 0; i < 25; i++)
        {
            add("crm", "accounts", i, ("name", AccountNames[i]), ("company", AccountNames[i]), ("industry", industries[i % industries.Length]),
                ("website", $"https://{AccountNames[i].ToLowerInvariant().Replace(' ', '-').Replace("&", "and")}.example.com"), ("headquarters", new[] { "Seattle", "Austin", "Toronto", "Dublin", "London" }[i % 5]),
                ("employeeRange", new[] { "201-500", "501-1,000", "1,001-5,000" }[i % 3]), ("annualRevenueBand", new[] { "$50M-$100M", "$100M-$500M", "$500M+" }[i % 3]),
                ("ownerId", $"itsm-employees-{i % 30 + 1:000}"), ("relationshipStatus", new[] { "Prospect", "Customer", "Expansion" }[i % 3]));
        }

        for (var i = 0; i < 50; i++)
        {
            add("crm", "contacts", i, ("name", Names[i % Names.Length]), ("email", $"contact{i + 1}@example.com"),
                ("jobTitle", new[] { "Chief Information Officer", "VP Operations", "Director of Data", "Procurement Lead", "Security Architect" }[i % 5]),
                ("phone", $"+1-555-{210 + i / 10:000}-{1000 + i:0000}"), ("accountId", $"crm-accounts-{i % 25 + 1:000}"),
                ("buyingCommitteeRole", new[] { "Decision maker", "Technical evaluator", "Champion", "Finance" }[i % 4]), ("contactPreference", i % 2 == 0 ? "Email" : "Phone"));
        }

        var sources = new[] { "Referral", "Industry event", "Website enquiry", "Partner referral", "Outbound" };
        for (var i = 0; i < 35; i++)
        {
            add("crm", "leads", i, ("name", $"{Names[i % Names.Length]} · {AccountNames[i % 25]}"), ("source", sources[i % sources.Length]),
                ("company", AccountNames[i % 25]), ("email", $"lead{i + 1}@example.com"), ("qualificationStatus", new[] { "New", "Working", "Qualified", "Disqualified" }[i % 4]),
                ("score", 54 + i % 46), ("ownerId", $"itsm-employees-{i % 30 + 1:000}"), ("nextAction", new[] { "Schedule discovery", "Share architecture brief", "Confirm budget owner", "Review event notes" }[i % 4]),
                ("createdAt", "2026-01-10T09:00:00Z"));
        }

        string[] stages = ["Qualification", "Discovery", "Solution Design", "Proposal", "Negotiation", "Closed Won", "Closed Lost"];
        for (var i = 0; i < 25; i++)
        {
            var stage = stages[i % stages.Length];
            add("crm", "opportunities", i, ("name", new[] { "Azure Data Platform Modernization", "Enterprise Identity Consolidation", "Customer Service Copilot Pilot", "Retail Analytics Foundation", "Clinical Scheduling Renewal" }[i % 5] + $" · {AccountNames[i % 25]}"),
                ("accountId", $"crm-accounts-{i % 25 + 1:000}"), ("primaryContactId", $"crm-contacts-{i % 50 + 1:000}"),
                ("ownerId", $"itsm-employees-{i % 30 + 1:000}"), ("amount", 85000 + i * 17250), ("currency", "USD"), ("stage", stage),
                ("probability", stage switch { "Qualification" => 10, "Discovery" => 25, "Solution Design" => 45, "Proposal" => 65, "Negotiation" => 85, "Closed Won" => 100, _ => 0 }),
                ("expectedCloseDate", "2026-09-30"), ("closeDate", stage is "Closed Won" or "Closed Lost" ? "2026-02-20" : null),
                ("forecastCategory", stage switch { "Closed Won" => "Closed", "Closed Lost" => "Omitted", "Negotiation" => "Commit", "Proposal" => "Best Case", _ => "Pipeline" }),
                ("competitors", new[] { "Northwind Systems", "Fabrikam Digital" }), ("nextStep", "Confirm solution scope with the buying committee."),
                ("lossReason", stage == "Closed Lost" ? "Customer deferred the program to the next planning cycle." : null));
        }

        for (var i = 0; i < 50; i++)
        {
            add("crm", "activities", i, ("name", new[] { "Discovery call", "Solution workshop", "Budget review", "Technical follow-up", "Executive briefing" }[i % 5] + $" · {AccountNames[i % 25]}"), ("type", new[] { "Call", "Meeting", "Email", "Task" }[i % 4]),
                ("subject", "Advance the enterprise modernization discussion"), ("notes", "Reviewed the current operating model, key stakeholders, and decision milestones."),
                ("dueDate", "2026-03-15"), ("status", i % 4 == 0 ? "Completed" : "Open"), ("ownerId", $"itsm-employees-{i % 30 + 1:000}"),
                ("accountId", $"crm-accounts-{i % 25 + 1:000}"), ("opportunityId", $"crm-opportunities-{i % 25 + 1:000}"));
        }

        var productNames = new[] { "Azure Data Platform Modernization", "Identity Governance Suite", "Customer Service Copilot", "Retail Analytics", "Secure Access Review", "Cloud Migration Advisory",
            "Data Quality Accelerator", "Endpoint Security Assessment", "Clinical Workflow Optimization", "Energy Forecasting Toolkit", "Sales Operations Automation", "Managed Adoption Services" };
        for (var i = 0; i < productNames.Length; i++)
        {
            add("crm", "products", i, ("name", productNames[i]), ("family", new[] { "Cloud", "Security", "AI", "Advisory" }[i % 4]), ("sku", $"NOVA-{i + 1:000}"),
                ("description", $"Contoso {productNames[i].ToLowerInvariant()} engagement."), ("listPrice", 12000 + i * 6500), ("active", true));
        }

        for (var i = 0; i < 3; i++)
        {
            add("crm", "sales-teams", i, ("name", new[] { "North America Enterprise", "EMEA Strategic Accounts", "Growth & Partner Sales" }[i]),
                ("region", new[] { "North America", "EMEA", "Global" }[i]), ("manager", Names[i]), ("quota", 4200000 + i * 1250000), ("members", new[] { Names[i], Names[i + 3], Names[i + 6] }));
        }
    }

    private static void SeedProjects(SeedAdder add)
    {
        for (var i = 0; i < 20; i++)
        {
            add("projects", "team-members", i, ("name", Names[i]), ("email", $"{Names[i].ToLowerInvariant().Replace(' ', '.')}@example.com"),
                ("role", new[] { "Project Manager", "Product Owner", "Engineer", "Analyst", "Change Lead" }[i % 5]),
                ("department", new[] { "Technology", "Finance", "Operations", "Security" }[i % 4]), ("skills", new[] { "Delivery", "Data", "Security" }),
                ("capacity", 60 + i % 5 * 5), ("availability", new[] { "Available", "Assigned", "Limited" }[i % 3]), ("active", true));
        }

        for (var i = 0; i < 4; i++)
        {
            add("projects", "portfolios", i, ("name", new[] { "Connected Enterprise", "Trusted Digital Workplace", "Customer Growth", "Resilient Operations" }[i]),
                ("businessUnit", new[] { "Corporate", "Technology", "Sales", "Operations" }[i]), ("sponsor", Names[i]), ("strategicObjective", "Modernize core capabilities while improving reliability."),
                ("status", "Active"));
        }

        for (var i = 0; i < 12; i++)
        {
            add("projects", "projects", i, ("name", ProjectNames[i]), ("code", $"PRJ-{620 + i}"),
                ("description", $"Coordinate the {ProjectNames[i].ToLowerInvariant()} across regional business teams."),
                ("portfolioId", $"projects-portfolios-{i % 4 + 1:000}"), ("projectManagerId", $"projects-team-members-{i % 20 + 1:000}"),
                ("sponsorId", $"projects-team-members-{(i + 3) % 20 + 1:000}"), ("status", new[] { "Active", "Active", "Planning", "On Hold" }[i % 4]),
                ("health", new[] { "Green", "Amber", "Red" }[i % 3]), ("startDate", "2026-01-05"), ("endDate", "2026-10-30"),
                ("budget", 500000 + i * 85000), ("currency", "USD"), ("completionPercentage", 0));
        }

        for (var i = 0; i < 25; i++)
        {
            add("projects", "milestones", i, ("name", new[] { "Architecture approved", "Pilot ready", "Regional rollout", "Security review", "Operational handover" }[i % 5] + $" · {ProjectNames[i % 12]}"),
                ("projectId", $"projects-projects-{i % 12 + 1:000}"), ("description", "Validate deliverables with accountable business owners."),
                ("plannedDate", "2026-06-30"), ("actualDate", i % 6 == 0 ? "2026-06-25" : null), ("status", i % 5 == 0 ? "In Progress" : "Planned"),
                ("ownerId", $"projects-team-members-{i % 20 + 1:000}"));
        }

        for (var i = 0; i < 80; i++)
        {
            var status = i % 4 == 0 ? "Completed" : i % 4 == 1 ? "In Progress" : i % 9 == 0 ? "Blocked" : "To Do";
            var projectIndex = i % 12;
            var localIndex = i / 12;
            var dependency = localIndex > 0 && i % 3 == 0 ? $"projects-tasks-{i - 11:000}" : null;
            add("projects", "tasks", i, ("name", new[] { "Confirm data owners", "Complete interface mapping", "Validate access model", "Prepare regional test plan", "Review supplier readiness", "Document support transition" }[i % 6] + $" · {ProjectNames[projectIndex]}"),
                ("projectId", $"projects-projects-{projectIndex + 1:000}"), ("milestoneId", $"projects-milestones-{i % 25 + 1:000}"),
                ("description", "Coordinate the agreed delivery outcome with the project team and business owner."),
                ("assigneeId", $"projects-team-members-{i % 20 + 1:000}"), ("priority", new[] { "Critical", "High", "Medium", "Low" }[i % 4]), ("status", status),
                ("startDate", "2026-02-01"), ("dueDate", i % 7 == 0 ? "2026-02-10" : "2026-04-15"), ("estimateHours", 8 + i % 5 * 4),
                ("actualHours", status == "Completed" ? 12 + i % 6 : 0), ("dependencies", dependency is null ? Array.Empty<string>() : new[] { dependency }),
                ("completedAt", status == "Completed" ? "2026-03-01T12:00:00Z" : null));
        }

        for (var i = 0; i < 18; i++)
        {
            add("projects", "risks", i, ("name", new[] { "Regional data residency approval", "Legacy interface ownership gap", "Supplier lead-time variation", "Change capacity during quarter close", "Identity policy rollout conflicts", "Incomplete source data mapping" }[i % 6] + $" · {ProjectNames[i % 12]}"),
                ("projectId", $"projects-projects-{i % 12 + 1:000}"), ("description", "A delivery dependency may affect the approved schedule without early mitigation."),
                ("probability", 2 + i % 4), ("impact", 2 + (i + 1) % 4), ("score", (2 + i % 4) * (2 + (i + 1) % 4)),
                ("ownerId", $"projects-team-members-{i % 20 + 1:000}"), ("mitigation", "Review dependency ownership weekly and agree a fallback date with the sponsor."),
                ("status", i % 5 == 0 ? "Closed" : "Open"));
        }

        for (var i = 0; i < 20; i++)
        {
            add("projects", "status-updates", i, ("name", $"Weekly delivery update · {ProjectNames[i % 12]} · Week {i + 1}"),
                ("projectId", $"projects-projects-{i % 12 + 1:000}"), ("reportingPeriod", "2026-W08"),
                ("summary", "Delivery remains focused on the next integration checkpoint and risk owners."),
                ("accomplishments", "Completed the business process review and confirmed pilot participants."),
                ("nextSteps", "Finish interface validation and circulate the readiness checklist."),
                ("blockers", i % 4 == 0 ? "Awaiting regional security approval." : null), ("health", new[] { "Green", "Amber", "Red" }[i % 3]),
                ("authorId", $"projects-team-members-{i % 20 + 1:000}"));
        }
    }
}
