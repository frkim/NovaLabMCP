using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace NovaLabMCP.Api;

public sealed record McpActivityEvent(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string CorrelationId,
    string Capability,
    string Scenario,
    string Tool,
    string Caller,
    string InputSummary,
    string Outcome,
    long DurationMs,
    string ResultSummary,
    string? ErrorCode);

public sealed class McpActivityBuffer
{
    private static readonly Regex SensitiveFields = new(
        "(?<key>\"[^\"]*(?:token|password|secret|authorization|email|phone|body|query|description)[^\"]*\"\\s*:\\s*)\"(?:\\\\.|[^\"])*\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly object gate = new();
    private readonly Queue<McpActivityEvent> events = new();
    private readonly List<Channel<McpActivityEvent>> subscribers = [];
    private readonly int capacity;

    public McpActivityBuffer(int configuredCapacity = 2000)
    {
        capacity = Math.Clamp(configuredCapacity, 10, 10000);
    }

    public int Capacity => capacity;

    public IReadOnlyList<McpActivityEvent> List() 
    {
        lock (gate)
        {
            return events.Reverse().ToArray();
        }
    }

    public int RetainedCount
    {
        get
        {
            lock (gate)
            {
                return events.Count;
            }
        }
    }

    public T Track<T>(ClaimsPrincipal caller, string capability, string scenario, string tool, string inputSummary, Func<T> operation)
    {
        var started = DateTimeOffset.UtcNow;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var outcome = "Success";
        var resultSummary = "Operation failed";
        string? errorCode = null;
        try
        {
            var result = operation();
            resultSummary = "Completed";
            return result;
        }
        catch (Exception exception)
        {
            outcome = exception switch
            {
                ArgumentException or FormatException => "ValidationError",
                UnauthorizedAccessException => "Forbidden",
                TimeoutException => "Timeout",
                KeyNotFoundException => "ValidationError",
                _ => "Error"
            };
            errorCode = exception.GetType().Name;
            resultSummary = "Operation failed";
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var completed = DateTimeOffset.UtcNow;
            var entry = new McpActivityEvent(
                Guid.NewGuid().ToString("N"),
                started,
                completed,
                Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"),
                capability,
                scenario.ToUpperInvariant(),
                tool,
                Limit(caller.Identity?.Name ?? "demo-client", 80),
                Limit(SensitiveFields.Replace(inputSummary, "$1\"[REDACTED]\""), 240),
                outcome,
                stopwatch.ElapsedMilliseconds,
                Limit(resultSummary, 300),
                errorCode);

            lock (gate)
            {
                while (events.Count >= capacity)
                {
                    events.Dequeue();
                }

                events.Enqueue(entry);
                foreach (var subscriber in subscribers.ToArray())
                {
                    subscriber.Writer.TryWrite(entry);
                }
            }
        }
    }

    public ChannelReader<McpActivityEvent> Subscribe(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<McpActivityEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (gate)
        {
            subscribers.Add(channel);
        }

        cancellationToken.Register(() =>
        {
            lock (gate)
            {
                subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        });

        return channel.Reader;
    }

    public void Clear()
    {
        lock (gate)
        {
            events.Clear();
        }
    }

    public static string Summary(object value) => Limit(JsonSerializer.Serialize(value), 300);

    private static string Limit(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

public static class McpToolCalls
{
    public static void RequireViewer(ClaimsPrincipal caller) => RequireRole(caller, "DemoViewer");

    public static string List(
        ScenarioStore store,
        McpActivityBuffer activity,
        ClaimsPrincipal caller,
        string scenario,
        string entity,
        string tool,
        int page = 1,
        int pageSize = 10,
        string? query = null) =>
        activity.Track(caller, "READONLY", scenario, tool, McpActivityBuffer.Summary(new { page, pageSize, hasQuery = !string.IsNullOrWhiteSpace(query) }), () =>
        {
            RequireRole(caller, "DemoViewer");
            return store.List(scenario, entity, page, pageSize, query, null, null, null, null).ToJsonString();
        });

    public static string Get(ScenarioStore store, McpActivityBuffer activity, ClaimsPrincipal caller, string scenario, string entity, string id, string tool) =>
        activity.Track(caller, "READONLY", scenario, tool, McpActivityBuffer.Summary(new { id }), () =>
        {
            RequireRole(caller, "DemoViewer");
            return store.Get(scenario, entity, id)?.ToJsonString() ?? throw new KeyNotFoundException("Record not found.");
        });

    public static string Write(
        ScenarioStore store,
        McpActivityBuffer activity,
        ClaimsPrincipal caller,
        string scenario,
        string entity,
        string operation,
        string tool,
        string? id = null,
        System.Text.Json.Nodes.JsonObject? fields = null,
        bool confirm = false)
    {
        return activity.Track(caller, "READWRITE", scenario, tool, McpActivityBuffer.Summary(new { entity, id, fieldsPresent = fields is not null, confirm }), () =>
        {
            RequireRole(caller, "DemoEditor");
            if (operation is "delete" or "close" or "complete" && !confirm)
            {
                throw new ArgumentException("This operation requires explicit confirmation.");
            }

            if (operation == "create")
            {
                return store.Create(scenario, entity, fields ?? new()).ToJsonString();
            }

            if (operation == "update")
            {
                return store.Update(scenario, entity, id ?? string.Empty, fields ?? new())?.ToJsonString() ??
                    throw new KeyNotFoundException("Record not found.");
            }

            if (operation == "delete")
            {
                if (!store.Delete(scenario, entity, id ?? string.Empty))
                {
                    throw new KeyNotFoundException("Record not found.");
                }

                return McpActivityBuffer.Summary(new { operation, id, outcome = "deleted" });
            }

            var changes = fields?.DeepClone().AsObject() ?? new System.Text.Json.Nodes.JsonObject();
            if (operation == "assign")
            {
                changes["assigneeId"] ??= changes["employeeId"]?.DeepClone();
                changes["supportTeamId"] ??= changes["teamId"]?.DeepClone();
            }
            else if (operation == "resolve")
            {
                changes["status"] = "Resolved";
            }
            else if (operation == "close")
            {
                changes["status"] = "Closed";
            }
            else if (operation == "complete")
            {
                changes["status"] = "Completed";
                changes["completedAt"] = DateTimeOffset.UtcNow;
            }
            else if (operation == "qualify")
            {
                changes["qualificationStatus"] = "Qualified";
            }

            return store.Update(scenario, entity, id ?? string.Empty, changes)?.ToJsonString() ??
                throw new KeyNotFoundException("Record not found.");
        });
    }

    private static void RequireRole(ClaimsPrincipal caller, string role)
    {
        if (!caller.IsInRole(role) && !caller.IsInRole("DemoAdmin") &&
            !(role == "DemoViewer" && caller.IsInRole("DemoEditor")))
        {
            throw new UnauthorizedAccessException("The caller is not authorized for this MCP capability.");
        }
    }
}
