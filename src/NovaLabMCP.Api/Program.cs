using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using NovaLabMCP.Api;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_000_000);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration.GetValue("NOVALABMCP_FRONTEND_ORIGIN", "http://localhost:3000")!)
        .AllowAnyHeader()
        .AllowAnyMethod()));

var demoAuthEnabled = builder.Configuration.GetValue<bool>("NOVALABMCP_DEMO_AUTH_ENABLED");
var authority = builder.Configuration["NOVALABMCP_AUTHORITY"];
var audience = builder.Configuration["NOVALABMCP_AUDIENCE"];
if (demoAuthEnabled && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException("Demo authentication is only permitted in the Development environment.");
}

if (!demoAuthEnabled)
{
    if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(audience))
    {
        throw new InvalidOperationException("Configure Entra authority and audience before running outside demo mode.");
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.RequireHttpsMetadata = true;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = "name",
                RoleClaimType = "roles"
            };
        });
}
builder.Services.AddAuthorization();

var store = new ScenarioStore();
var activity = new McpActivityBuffer(builder.Configuration.GetValue("NOVALABMCP_MCP_LOG_CAPACITY", 2000));
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(activity);

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseRouting();
app.UseCors();
if (!demoAuthEnabled)
{
    app.UseAuthentication();
}
#pragma warning disable ASP0001 // The main app uses minimal endpoint mappings; UseEndpoints is only used by the isolated MCP app.
app.UseAuthorization();
#pragma warning restore ASP0001

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
    if (demoAuthEnabled)
    {
        var role = context.Request.Headers["X-Demo-Role"].FirstOrDefault();
        if (role is not ("DemoViewer" or "DemoEditor" or "DemoAdmin"))
        {
            role = "DemoViewer";
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "local-demo"), new Claim(ClaimTypes.Name, "Local demo user"), new Claim(ClaimTypes.Role, role)],
            "LocalDemo"));
    }

    await next();
});

app.MapOpenApi("/openapi/{documentName}.json");
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

var api = app.MapGroup("/api/v1");
if (!demoAuthEnabled)
{
    api.RequireAuthorization();
}

api.MapGet("/scenarios", () => Results.Ok(store.Scenarios()))
    .WithName("ListScenarios")
    .WithSummary("Lists available isolated demo scenarios.");

api.MapGet("/mcp/catalog/{capability}/{scenario}", (string capability, string scenario) =>
{
    if (!ScenarioStore.Entities.ContainsKey(scenario) || capability is not ("readonly" or "readwrite"))
    {
        return Results.NotFound();
    }

    var prefix = capability == "readonly" ? $"readonly_{(scenario == "projects" ? "pm" : scenario)}_" :
        $"readwrite_{(scenario == "projects" ? "pm" : scenario)}_";
    var tools = McpCatalog.Tools.Where(tool => tool.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
    return Results.Ok(new { scenario, capability = capability.ToUpperInvariant(), tools });
})
.WithName("GetMcpCatalog")
.WithSummary("Returns the distinct MCP tool names registered for a capability endpoint.");

api.MapGet("/{scenario}/{entity}", (HttpContext context, string scenario, string entity) =>
{
    if (!Authorized(context.User, "DemoViewer"))
    {
        return Results.Forbid();
    }

    try
    {
        var page = int.TryParse(context.Request.Query["page"], out var parsedPage) ? parsedPage : 1;
        var pageSize = int.TryParse(context.Request.Query["pageSize"], out var parsedSize) ? parsedSize : 10;
        var filter = context.Request.Query["filter"].FirstOrDefault();
        var filterBy = context.Request.Query["filterBy"].FirstOrDefault();
        return Results.Ok(store.List(scenario, entity, page, pageSize, context.Request.Query["q"], context.Request.Query["sort"],
            context.Request.Query["order"], filterBy, filter));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid collection query");
    }
})
.WithName("ListRecords")
.WithSummary("Returns a validated, searchable, sortable, paginated entity collection.");

api.MapGet("/{scenario}/{entity}/{id}", (HttpContext context, string scenario, string entity, string id) =>
{
    if (!Authorized(context.User, "DemoViewer"))
    {
        return Results.Forbid();
    }

    try
    {
        return store.Get(scenario, entity, id) is { } record ? Results.Ok(record) : Results.NotFound();
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
})
.WithName("GetRecord")
.WithSummary("Returns one entity record.");

api.MapPost("/{scenario}/{entity}", async (HttpContext context, string scenario, string entity) =>
{
    if (!Authorized(context.User, "DemoEditor"))
    {
        return Results.Forbid();
    }

    try
    {
        var body = await context.Request.ReadFromJsonAsync<JsonObject>();
        if (body is null)
        {
            return Results.BadRequest(new { error = "A JSON object is required." });
        }

        var record = store.Create(scenario, entity, body);
        return Results.Created($"/api/v1/{scenario}/{entity}/{record["id"]}", record);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Request body must be valid JSON." });
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Validation failed");
    }
})
.WithName("CreateRecord")
.WithSummary("Creates a validated entity record.");

api.MapPatch("/{scenario}/{entity}/{id}", async (HttpContext context, string scenario, string entity, string id) =>
{
    if (!Authorized(context.User, "DemoEditor"))
    {
        return Results.Forbid();
    }

    try
    {
        var body = await context.Request.ReadFromJsonAsync<JsonObject>();
        if (body is null)
        {
            return Results.BadRequest(new { error = "A JSON object is required." });
        }

        var record = store.Update(scenario, entity, id, body);
        return record is null ? Results.NotFound() : Results.Ok(record);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Request body must be valid JSON." });
    }
    catch (ArgumentException exception)
    {
        return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest, title: "Validation failed");
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
})
.WithName("UpdateRecord")
.WithSummary("Updates a validated entity record.");

api.MapDelete("/{scenario}/{entity}/{id}", (HttpContext context, string scenario, string entity, string id, bool confirm = false) =>
{
    if (!Authorized(context.User, "DemoEditor"))
    {
        return Results.Forbid();
    }

    if (!confirm)
    {
        return Results.BadRequest(new { error = "Explicit confirmation is required." });
    }

    try
    {
        return store.Delete(scenario, entity, id) ? Results.NoContent() : Results.NotFound();
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
})
.WithName("DeleteRecord")
.WithSummary("Deletes a record only after explicit confirmation and relationship checks.");

api.MapPost("/demo/reset", (HttpContext context) =>
{
    if (!Authorized(context.User, "DemoAdmin"))
    {
        return Results.Forbid();
    }

    store.Reset();
    activity.Clear();
    return Results.NoContent();
})
.WithName("ResetDemo")
.WithSummary("Resets all deterministic business data and clears ephemeral MCP activity.");

api.MapGet("/mcp-activity", (HttpContext context, int page = 1, int pageSize = 50, string? scenario = null,
    string? capability = null, string? outcome = null, string? query = null) =>
{
    if (!Authorized(context.User, "DemoEditor"))
    {
        return Results.Forbid();
    }

    if (page < 1 || pageSize is < 1 or > 100)
    {
        return Results.BadRequest(new { error = "Page must be positive and pageSize must be between 1 and 100." });
    }

    var filtered = activity.List().Where(item =>
        (scenario is null || item.Scenario.Equals(scenario, StringComparison.OrdinalIgnoreCase)) &&
        (capability is null || item.Capability.Equals(capability, StringComparison.OrdinalIgnoreCase)) &&
        (outcome is null || item.Outcome.Equals(outcome, StringComparison.OrdinalIgnoreCase)) &&
        (query is null || JsonSerializer.Serialize(item).Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
    return Results.Ok(new { items = filtered.Skip((page - 1) * pageSize).Take(pageSize), page, pageSize, totalCount = filtered.Length, totalPages = (int)Math.Ceiling(filtered.Length / (double)pageSize), retained = activity.RetainedCount, capacity = activity.Capacity });
})
.WithName("ListMcpActivity")
.WithSummary("Returns a paginated, newest-first view of retained MCP activity.");

api.MapGet("/mcp-activity/{id}", (HttpContext context, string id) =>
{
    if (!Authorized(context.User, "DemoEditor"))
    {
        return Results.Forbid();
    }

    var entry = activity.List().FirstOrDefault(item => item.Id == id);
    return entry is null ? Results.NotFound() : Results.Ok(entry);
})
.WithName("GetMcpActivity")
.WithSummary("Returns one safe MCP activity event.");

api.MapGet("/mcp-activity/stream", async (HttpContext context) =>
{
    if (!Authorized(context.User, "DemoEditor"))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    var reader = activity.Subscribe(context.RequestAborted);
    try
    {
        await foreach (var item in reader.ReadAllAsync(context.RequestAborted))
        {
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(item)}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
    }
})
.WithName("StreamMcpActivity")
.WithSummary("Streams safe MCP activity events using Server-Sent Events.");

MapMcpEndpoint<ItsmReadOnlyTools>(app, "/mcp/readonly/itsm", store, activity);
MapMcpEndpoint<ItsmReadWriteTools>(app, "/mcp/readwrite/itsm", store, activity);
MapMcpEndpoint<CrmReadOnlyTools>(app, "/mcp/readonly/crm", store, activity);
MapMcpEndpoint<CrmReadWriteTools>(app, "/mcp/readwrite/crm", store, activity);
MapMcpEndpoint<ProjectReadOnlyTools>(app, "/mcp/readonly/projects", store, activity);
MapMcpEndpoint<ProjectReadWriteTools>(app, "/mcp/readwrite/projects", store, activity);

app.Run();

static void MapMcpEndpoint<TTools>(WebApplication app, string path, ScenarioStore store, McpActivityBuffer activity)
    where TTools : class
{
    var mcpBuilder = WebApplication.CreateBuilder();
    mcpBuilder.Services.AddSingleton(store);
    mcpBuilder.Services.AddSingleton(activity);
    var serverBuilder = mcpBuilder.Services.AddMcpServer()
        .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
        .WithTools<TTools>();
    if (path.StartsWith("/mcp/readonly/", StringComparison.Ordinal))
    {
        switch (path)
        {
            case "/mcp/readonly/itsm":
                serverBuilder.WithResources<ItsmResources>().WithPrompts<ItsmPrompts>();
                break;
            case "/mcp/readonly/crm":
                serverBuilder.WithResources<CrmResources>().WithPrompts<CrmPrompts>();
                break;
            case "/mcp/readonly/projects":
                serverBuilder.WithResources<ProjectResources>().WithPrompts<ProjectPrompts>();
                break;
        }
    }

    var mcpApp = mcpBuilder.Build();
    mcpApp.UseRouting();
#pragma warning disable ASP0014
    mcpApp.UseEndpoints(endpoints => endpoints.MapMcp());
#pragma warning restore ASP0014
    var pipeline = ((IApplicationBuilder)mcpApp).Build();

    app.Map(path, branch => branch.Run(async context =>
    {
        using var scope = mcpApp.Services.CreateScope();
        context.RequestServices = scope.ServiceProvider;
        await pipeline(context);
    }));
}

static bool Authorized(ClaimsPrincipal caller, string requiredRole) =>
    caller.IsInRole(requiredRole) || caller.IsInRole("DemoAdmin") ||
    requiredRole == "DemoViewer" && caller.IsInRole("DemoEditor");

public partial class Program;

public static class McpCatalog
{
    public static readonly string[] Tools =
    [
        "readonly_itsm_list_tickets", "readonly_itsm_get_ticket", "readonly_itsm_list_employees", "readonly_itsm_list_teams",
        "readonly_itsm_list_assets", "readonly_itsm_search_knowledge", "readonly_itsm_get_dashboard",
        "readwrite_itsm_create_ticket", "readwrite_itsm_update_ticket", "readwrite_itsm_assign_ticket", "readwrite_itsm_add_comment",
        "readwrite_itsm_resolve_ticket", "readwrite_itsm_close_ticket", "readwrite_itsm_delete_ticket",
        "readonly_crm_list_accounts", "readonly_crm_get_account", "readonly_crm_list_contacts", "readonly_crm_list_leads",
        "readonly_crm_list_opportunities", "readonly_crm_get_pipeline_summary", "readonly_crm_list_activities", "readonly_crm_search_records",
        "readwrite_crm_create_account", "readwrite_crm_update_account", "readwrite_crm_create_contact", "readwrite_crm_update_contact",
        "readwrite_crm_create_lead", "readwrite_crm_qualify_lead", "readwrite_crm_create_opportunity", "readwrite_crm_update_opportunity",
        "readwrite_crm_log_activity", "readwrite_crm_delete_record",
        "readonly_pm_list_portfolios", "readonly_pm_list_projects", "readonly_pm_get_project", "readonly_pm_list_tasks",
        "readonly_pm_list_milestones", "readonly_pm_list_risks", "readonly_pm_get_project_health", "readonly_pm_get_portfolio_summary",
        "readwrite_pm_create_project", "readwrite_pm_update_project", "readwrite_pm_create_task", "readwrite_pm_update_task",
        "readwrite_pm_assign_task", "readwrite_pm_complete_task", "readwrite_pm_create_milestone", "readwrite_pm_update_milestone",
        "readwrite_pm_create_risk", "readwrite_pm_update_risk", "readwrite_pm_add_status_update", "readwrite_pm_delete_record"
    ];
}
