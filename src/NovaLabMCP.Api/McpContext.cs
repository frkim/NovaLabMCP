using System.ComponentModel;
using ModelContextProtocol.Server;

namespace NovaLabMCP.Api;

[McpServerResourceType]
public sealed class ItsmResources
{
    [McpServerResource(UriTemplate = "novalabmcp://itsm/glossary", Name = "IT Service Desk glossary", MimeType = "text/markdown")]
    [Description("IT Service Desk ticket statuses, priorities, and service-level guidance.")]
    public static string Glossary() => """
        # IT Service Desk glossary

        Ticket statuses: New, Open, In Progress, Pending, Resolved, and Closed.
        Priorities: P1 is critical, P2 is high, P3 is normal, and P4 is low.
        P1 incidents require an impact statement and an owning support team.
        Review assignment, affected assets, related comments, and SLA targets
        before recommending a resolution. Use READONLY tools for investigation.
        """;
}

[McpServerPromptType]
public sealed class ItsmPrompts
{
    [McpServerPrompt(Name = "daily_incident_review")]
    [Description("Reviews IT Service Desk incidents for the requested period.")]
    public static string DailyIncidentReview(
        [Description("The date or date range to review.")] string dateRange) => $"""
        Review IT Service Desk incidents for {dateRange}.
        First use READONLY tools to list tickets and retrieve relevant ticket,
        team, employee, asset, and knowledge details. Summarize priority,
        impact, SLA risk, ownership, and notable trends. Clearly separate
        observed facts from recommendations. Do not make changes; describe
        any proposed action and request user approval before using a READWRITE tool.
        """;

    [McpServerPrompt(Name = "analyze_major_incident")]
    [Description("Analyzes a major IT incident without making changes.")]
    public static string AnalyzeMajorIncident(
        [Description("The incident ticket ID.")] string ticketId) => $"""
        Analyze IT Service Desk incident {ticketId}.
        Use READONLY tools to retrieve the ticket and investigate related
        tickets, comments, assets, support teams, and relevant knowledge.
        Summarize impact, timeline, likely contributing factors, unresolved
        questions, and recommended next steps. Do not mutate records or claim
        causality that the available evidence does not support.
        """;

    [McpServerPrompt(Name = "team_workload_summary")]
    [Description("Summarizes workload and ticket risk for an IT support team.")]
    public static string TeamWorkloadSummary(
        [Description("The support team name or ID.")] string team) => $"""
        Summarize the current IT Service Desk workload for {team}.
        Use READONLY tools to find the team's tickets and relevant team
        information. Report ticket counts by status and priority, overdue or
        SLA-risk work, unassigned items, and the most urgent next actions.
        Base conclusions on retrieved data and do not make changes.
        """;
}

[McpServerResourceType]
public sealed class CrmResources
{
    [McpServerResource(UriTemplate = "novalabmcp://crm/glossary", Name = "CRM & Sales glossary", MimeType = "text/markdown")]
    [Description("CRM opportunity stages, pipeline guidance, and forecast definitions.")]
    public static string Glossary() => """
        # CRM & Sales glossary

        Opportunity stages progress from Qualification through Discovery,
        Proposal, Negotiation, and Closed Won or Closed Lost. Weighted pipeline
        value is the opportunity amount multiplied by its probability. Review
        expected close date, next step, account context, and recent activities
        before assessing risk. A lead must be qualified before conversion.
        """;
}

[McpServerPromptType]
public sealed class CrmPrompts
{
    [McpServerPrompt(Name = "weekly_pipeline_review")]
    [Description("Reviews CRM pipeline and forecast for a requested period.")]
    public static string WeeklyPipelineReview(
        [Description("The date or date range to review.")] string dateRange) => $"""
        Review the Contoso CRM pipeline for {dateRange}.
        First use READONLY tools to inspect opportunities, stage summaries,
        accounts, and recent activities. Summarize open and weighted value by
        stage, expected closes, stalled deals, overdue next steps, and material
        forecast risks. Distinguish retrieved facts from estimates. Do not
        change records; request approval before any READWRITE operation.
        """;

    [McpServerPrompt(Name = "opportunity_risk_review")]
    [Description("Assesses risks and next steps for a CRM opportunity.")]
    public static string OpportunityRiskReview(
        [Description("The opportunity ID.")] string opportunityId) => $"""
        Assess CRM opportunity {opportunityId}.
        Use READONLY tools to retrieve the opportunity, account, contacts, and
        recent activities. Evaluate stage, amount, probability, expected close
        date, engagement, and missing next steps. Provide evidence-based risks
        and recommended actions without changing the opportunity.
        """;

    [McpServerPrompt(Name = "prepare_account_briefing")]
    [Description("Prepares an account briefing from CRM records.")]
    public static string PrepareAccountBriefing(
        [Description("The account ID.")] string accountId) => $"""
        Prepare a concise briefing for CRM account {accountId}.
        Use READONLY tools to retrieve the account, its contacts, opportunities,
        and recent activities. Summarize account context, active pipeline,
        relationship coverage, recent engagement, risks, and suggested
        discussion points. Avoid inventing facts and do not modify records.
        """;
}

[McpServerResourceType]
public sealed class ProjectResources
{
    [McpServerResource(UriTemplate = "novalabmcp://projects/glossary", Name = "Project Management glossary", MimeType = "text/markdown")]
    [Description("Project health, milestone, task, and risk definitions.")]
    public static string Glossary() => """
        # Project Management glossary

        Project health is Green, Amber, or Red and reflects delivery confidence.
        Milestones track planned and actual dates. Tasks may depend on other
        tasks; blocked work should not be marked complete until dependencies
        are satisfied. Risks use probability and impact scores from 1 to 5.
        Review owner, mitigation, schedule, and budget context when assessing
        portfolio health.
        """;
}

[McpServerPromptType]
public sealed class ProjectPrompts
{
    [McpServerPrompt(Name = "portfolio_health_review")]
    [Description("Reviews project portfolio health and delivery risks.")]
    public static string PortfolioHealthReview(
        [Description("The portfolio name or ID.")] string portfolio) => $"""
        Review project portfolio {portfolio}.
        First use READONLY tools to inspect its projects, milestones, tasks,
        risks, and status updates. Summarize health counts, overdue milestones,
        blocked tasks, high risks, budget concerns, and projects needing
        attention. Support recommendations with retrieved evidence and do not
        make changes.
        """;

    [McpServerPrompt(Name = "project_risk_assessment")]
    [Description("Assesses delivery risks for a project.")]
    public static string ProjectRiskAssessment(
        [Description("The project ID.")] string projectId) => $"""
        Assess delivery risks for project {projectId}.
        Use READONLY tools to retrieve project health, milestones, tasks,
        dependencies, risks, and recent status updates. Evaluate schedule,
        scope, ownership, dependencies, and mitigation coverage. Separate
        existing risk records from newly inferred concerns. Do not create or
        update records.
        """;

    [McpServerPrompt(Name = "weekly_status_report")]
    [Description("Prepares a project status report for a reporting period.")]
    public static string WeeklyStatusReport(
        [Description("The project ID.")] string projectId,
        [Description("The week-ending date.")] string weekEnding) => $"""
        Prepare the weekly status report for project {projectId}, ending {weekEnding}.
        Use READONLY tools to inspect project health, milestones, tasks,
        dependencies, risks, and recent status updates. Report progress,
        completed and overdue work, upcoming milestones, blockers, risk
        changes, and decisions needed. Do not write a status update or modify
        any project record.
        """;
}
