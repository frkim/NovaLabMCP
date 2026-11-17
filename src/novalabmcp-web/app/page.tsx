"use client";

import {
  Alert,
  AppBar,
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Drawer,
  FormControl,
  IconButton,
  InputLabel,
  MenuItem,
  Select,
  Snackbar,
  Stack,
  TextField,
  Toolbar,
  Tooltip,
  Typography,
} from "@mui/material";
import { useTheme } from "@mui/material/styles";
import {
  DataGrid,
  type GridColDef,
  type GridPaginationModel,
  type GridSortModel,
} from "@mui/x-data-grid";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useAuth } from "./providers";

type ScenarioId = "itsm" | "crm" | "projects";
type RecordData = Record<string, unknown> & { id: string };
type PageResult = {
  items: RecordData[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};
type ActivityEvent = {
  id: string;
  startedAt: string;
  completedAt: string;
  correlationId: string;
  capability: string;
  scenario: string;
  tool: string;
  caller: string;
  inputSummary: string;
  outcome: string;
  durationMs: number;
  resultSummary: string;
  errorCode?: string;
};
type McpCapability = "readonly" | "readwrite";
type McpTool = { name: string; description?: string };
type McpPreset = {
  id: string;
  label: string;
  tool: string;
  arguments: Record<string, unknown>;
};

const API =
  process.env.NEXT_PUBLIC_API_URL ??
  (process.env.NODE_ENV === "development" ? "http://localhost:5290" : "");
const SCENARIOS: Record<
  ScenarioId,
  { label: string; caption: string; entities: string[] }
> = {
  itsm: {
    label: "IT Service Desk",
    caption: "Contoso IT Service Desk",
    entities: [
      "tickets",
      "employees",
      "teams",
      "assets",
      "comments",
      "knowledge",
    ],
  },
  crm: {
    label: "CRM & Sales",
    caption: "Contoso CRM & Sales",
    entities: [
      "accounts",
      "contacts",
      "leads",
      "opportunities",
      "activities",
      "products",
      "sales-teams",
    ],
  },
  projects: {
    label: "Project Management",
    caption: "Contoso Project Management",
    entities: [
      "portfolios",
      "projects",
      "milestones",
      "tasks",
      "team-members",
      "risks",
      "status-updates",
    ],
  },
};
const MCP_PRESETS: Record<ScenarioId, Record<McpCapability, McpPreset[]>> = {
  itsm: {
    readonly: [
      {
        id: "tickets",
        label: "List recent tickets",
        tool: "readonly_itsm_list_tickets",
        arguments: { page: 1, pageSize: 10 },
      },
      {
        id: "dashboard",
        label: "Get service desk dashboard",
        tool: "readonly_itsm_get_dashboard",
        arguments: {},
      },
    ],
    readwrite: [
      {
        id: "create-ticket",
        label: "Create a sample ticket",
        tool: "readwrite_itsm_create_ticket",
        arguments: {
          ticket: {
            title: "MCP editor sample ticket",
            description: "Created from the MCP query editor.",
            status: "New",
            priority: "P3",
            requesterId: "itsm-employees-001",
          },
        },
      },
    ],
  },
  crm: {
    readonly: [
      {
        id: "accounts",
        label: "List accounts",
        tool: "readonly_crm_list_accounts",
        arguments: { page: 1, pageSize: 10 },
      },
      {
        id: "pipeline",
        label: "Get pipeline summary",
        tool: "readonly_crm_get_pipeline_summary",
        arguments: {},
      },
    ],
    readwrite: [
      {
        id: "create-lead",
        label: "Create a sample lead",
        tool: "readwrite_crm_create_lead",
        arguments: { lead: { name: "MCP editor sample lead" } },
      },
    ],
  },
  projects: {
    readonly: [
      {
        id: "projects",
        label: "List projects",
        tool: "readonly_pm_list_projects",
        arguments: { page: 1, pageSize: 10 },
      },
      {
        id: "portfolio",
        label: "Get portfolio summary",
        tool: "readonly_pm_get_portfolio_summary",
        arguments: {},
      },
    ],
    readwrite: [
      {
        id: "create-task",
        label: "Create a sample task",
        tool: "readwrite_pm_create_task",
        arguments: {
          task: {
            name: "MCP editor sample task",
            projectId: "projects-projects-001",
          },
        },
      },
    ],
  },
};

const entityLabels: Record<string, string> = {
  "sales-teams": "Sales teams",
  "team-members": "Team members",
  "status-updates": "Status updates",
  knowledge: "Knowledge articles",
  opportunities: "Opportunities",
};

function label(value: string) {
  return (
    entityLabels[value] ??
    value
      .replaceAll("-", " ")
      .replace(/\b\w/g, (letter) => letter.toUpperCase())
  );
}

function valueText(value: unknown): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "object") return JSON.stringify(value);
  return String(value);
}

function recordTitle(record: RecordData | null) {
  if (!record) return "Record";
  return valueText(
    record.title ?? record.name ?? record.ticketNumber ?? record.id,
  );
}

async function mcpJsonRpc(
  endpoint: string,
  payload: Record<string, unknown>,
  headers: Record<string, string>,
) {
  const response = await fetch(endpoint, {
    method: "POST",
    headers: {
      ...headers,
      Accept: "application/json, text/event-stream",
    },
    body: JSON.stringify(payload),
  });
  if (!response.ok) throw new Error(await responseMessage(response));

  const text = await response.text();
  let message: {
    result?: { tools?: McpTool[] };
    error?: { message?: string };
  };
  try {
    message = JSON.parse(text);
  } catch {
    const data = text
      .split("\n")
      .find((line) => line.startsWith("data: "))
      ?.slice(6);
    if (!data) throw new Error("The MCP server returned an invalid response.");
    message = JSON.parse(data);
  }
  if (message.error) {
    throw new Error(message.error.message ?? "The MCP request failed.");
  }
  return message;
}

async function responseMessage(response: Response) {
  try {
    const body = (await response.json()) as {
      detail?: string;
      title?: string;
      error?: string;
    };
    return (
      body.detail ??
      body.error ??
      body.title ??
      `Request failed (${response.status}).`
    );
  } catch {
    return `Request failed (${response.status}).`;
  }
}

export default function Home() {
  const theme = useTheme();
  const {
    enabled: entraEnabled,
    ready: authReady,
    account,
    roles: entraRoles,
    error: authError,
    getAccessToken,
    signIn,
    signOut,
  } = useAuth();
  const [scenario, setScenario] = useState<ScenarioId>("itsm");
  const [entity, setEntity] = useState("tickets");
  const [role, setRole] = useState("DemoEditor");
  const [rows, setRows] = useState<RecordData[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [pageModel, setPageModel] = useState<GridPaginationModel>({
    page: 0,
    pageSize: 10,
  });
  const [sortModel, setSortModel] = useState<GridSortModel>([]);
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [editorOpen, setEditorOpen] = useState(false);
  const [editing, setEditing] = useState<RecordData | null>(null);
  const [draft, setDraft] = useState('{\n  "name": ""\n}');
  const [deleteTarget, setDeleteTarget] = useState<RecordData | null>(null);
  const [selectedRecord, setSelectedRecord] = useState<RecordData | null>(null);
  const [activityRows, setActivityRows] = useState<ActivityEvent[]>([]);
  const [activityCapability, setActivityCapability] = useState("");
  const [activityOutcome, setActivityOutcome] = useState("");
  const [activityQuery, setActivityQuery] = useState("");
  const [mcpCapability, setMcpCapability] = useState<McpCapability>("readonly");
  const [mcpTools, setMcpTools] = useState<McpTool[]>([]);
  const [mcpTool, setMcpTool] = useState("");
  const [mcpPreset, setMcpPreset] = useState("");
  const [mcpArguments, setMcpArguments] = useState("{}");
  const [mcpResult, setMcpResult] = useState("");
  const [mcpLoading, setMcpLoading] = useState(false);
  const [mcpError, setMcpError] = useState("");
  const [hideSuccesses, setHideSuccesses] = useState(false);
  const [paused, setPaused] = useState(false);
  const [streamStatus, setStreamStatus] = useState("Connecting");
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const abortStream = useRef<AbortController | null>(null);
  const isActivity = entity === "mcp-activity";
  const isMcpEditor = entity === "mcp-editor";
  const mcpEndpoint = API
    ? `${API}/mcp/${mcpCapability}/${scenario}`
    : `/api/mcp/${mcpCapability}/${scenario}`;
  const mcpPresets = MCP_PRESETS[scenario][mcpCapability];
  const displayedMcpPreset =
    mcpPresets.find((preset) => preset.id === mcpPreset)?.id ??
    mcpPresets[0]?.id ??
    "";
  const canAdmin = entraEnabled
    ? entraRoles.includes("DemoAdmin")
    : role === "DemoAdmin";
  const canWrite = entraEnabled
    ? canAdmin || entraRoles.includes("DemoEditor")
    : role === "DemoEditor" || canAdmin;

  const requestHeaders = useMemo(
    () => ({
      "Content-Type": "application/json",
      ...(!entraEnabled ? { "X-Demo-Role": role } : {}),
    }),
    [entraEnabled, role],
  );
  const getRequestHeaders = useCallback(async () => {
    const token = await getAccessToken();
    return {
      ...requestHeaders,
      ...(token ? { Authorization: ["Bearer", token].join(" ") } : {}),
    };
  }, [getAccessToken, requestHeaders]);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedQuery(query), 300);
    return () => window.clearTimeout(timer);
  }, [query]);

  const loadRecords = useCallback(async () => {
    if (isActivity || isMcpEditor || (entraEnabled && (!authReady || !account)))
      return;
    setLoading(true);
    setError("");
    const parameters = new URLSearchParams({
      page: String(pageModel.page + 1),
      pageSize: String(pageModel.pageSize),
    });
    if (debouncedQuery) parameters.set("q", debouncedQuery);
    if (statusFilter) {
      parameters.set(
        "filterBy",
        entity === "opportunities" ? "stage" : "status",
      );
      parameters.set("filter", statusFilter);
    }
    if (sortModel[0]) {
      parameters.set("sort", sortModel[0].field);
      parameters.set("order", sortModel[0].sort ?? "asc");
    }
    try {
      const response = await fetch(
        `${API}/api/v1/${scenario}/${entity}?${parameters}`,
        { headers: await getRequestHeaders() },
      );
      if (!response.ok) throw new Error(await responseMessage(response));
      const data = (await response.json()) as PageResult;
      setRows(data.items);
      setTotalCount(data.totalCount);
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "The service could not be reached.",
      );
      setRows([]);
      setTotalCount(0);
    } finally {
      setLoading(false);
    }
  }, [
    debouncedQuery,
    entity,
    account,
    authReady,
    entraEnabled,
    getRequestHeaders,
    isActivity,
    isMcpEditor,
    pageModel,
    scenario,
    sortModel,
    statusFilter,
  ]);

  useEffect(() => {
    void loadRecords();
  }, [loadRecords]);

  useEffect(() => {
    const preset = MCP_PRESETS[scenario][mcpCapability][0];
    setMcpPreset(preset.id);
    setMcpTool(preset.tool);
    setMcpArguments(JSON.stringify(preset.arguments, null, 2));
    setMcpResult("");
    setMcpError("");
  }, [mcpCapability, scenario]);

  useEffect(() => {
    if (!isMcpEditor || (entraEnabled && (!authReady || !account))) return;
    let active = true;
    async function loadTools() {
      setMcpLoading(true);
      setMcpError("");
      try {
        const message = await mcpJsonRpc(
          mcpEndpoint,
          { jsonrpc: "2.0", id: 1, method: "tools/list", params: {} },
          await getRequestHeaders(),
        );
        if (active) {
          const tools = message.result?.tools ?? [];
          setMcpTools(tools);
          setMcpTool((current) =>
            tools.some((tool) => tool.name === current)
              ? current
              : (tools[0]?.name ?? ""),
          );
        }
      } catch (reason) {
        if (active) {
          setMcpTools([]);
          setMcpError(
            reason instanceof Error
              ? reason.message
              : "The MCP tool catalog could not be loaded.",
          );
        }
      } finally {
        if (active) setMcpLoading(false);
      }
    }
    void loadTools();
    return () => {
      active = false;
    };
  }, [
    account,
    authReady,
    entraEnabled,
    getRequestHeaders,
    isMcpEditor,
    mcpEndpoint,
  ]);

  useEffect(() => {
    setEntity((current) =>
      current === "mcp-editor" ? current : SCENARIOS[scenario].entities[0],
    );
    setPageModel({ page: 0, pageSize: 10 });
    setSortModel([]);
    setStatusFilter("");
    setSelectedRecord(null);
  }, [scenario]);

  useEffect(() => {
    if (!isActivity || (entraEnabled && (!authReady || !account))) {
      abortStream.current?.abort();
      return;
    }

    const controller = new AbortController();
    abortStream.current = controller;
    let active = true;

    async function connect() {
      while (active && !controller.signal.aborted) {
        try {
          const history = await fetch(
            `${API}/api/v1/mcp-activity?page=1&pageSize=50`,
            {
              headers: await getRequestHeaders(),
              signal: controller.signal,
            },
          );
          if (!history.ok) throw new Error(await responseMessage(history));
          const retained = (await history.json()) as { items: ActivityEvent[] };
          if (active) setActivityRows(retained.items);
          const response = await fetch(`${API}/api/v1/mcp-activity/stream`, {
            headers: await getRequestHeaders(),
            signal: controller.signal,
          });
          if (!response.ok || !response.body)
            throw new Error("The activity stream is unavailable.");
          if (active) setStreamStatus("Live");
          const reader = response.body.getReader();
          const decoder = new TextDecoder();
          let buffer = "";
          while (active && !controller.signal.aborted) {
            const { value, done } = await reader.read();
            if (done) break;
            buffer += decoder.decode(value, { stream: true });
            const messages = buffer.split("\n\n");
            buffer = messages.pop() ?? "";
            for (const message of messages) {
              const payload = message
                .split("\n")
                .find((line) => line.startsWith("data: "))
                ?.slice(6);
              if (!payload || paused) continue;
              const entry = JSON.parse(payload) as ActivityEvent;
              setActivityRows((current) =>
                [
                  entry,
                  ...current.filter((item) => item.id !== entry.id),
                ].slice(0, 100),
              );
            }
          }
        } catch (reason) {
          if (!controller.signal.aborted && active) {
            setStreamStatus("Reconnecting");
            setError(
              reason instanceof Error
                ? reason.message
                : "Activity stream disconnected.",
            );
            await new Promise((resolve) => window.setTimeout(resolve, 2000));
          }
        }
      }
    }

    void connect();
    return () => {
      active = false;
      controller.abort();
      setStreamStatus("Disconnected");
    };
  }, [account, authReady, entraEnabled, getRequestHeaders, isActivity, paused]);

  const columns = useMemo<GridColDef<RecordData>[]>(() => {
    const fields =
      rows.length > 0
        ? Object.keys(rows[0]).filter((field) => field !== "id")
        : [];
    return [
      { field: "id", headerName: "Record ID", minWidth: 160, flex: 0.8 },
      ...fields.map((field) => ({
        field,
        headerName: label(field),
        minWidth: field === "title" || field === "name" ? 280 : 140,
        flex: field === "title" || field === "name" ? 1.5 : 0.8,
        type: "string" as const,
        sortable: true,
        renderCell: (parameters: { value?: unknown }) => (
          <Typography
            variant="body2"
            noWrap
            title={valueText(parameters.value)}
          >
            {valueText(parameters.value)}
          </Typography>
        ),
      })),
    ];
  }, [rows]);

  const filteredActivity = useMemo(
    () =>
      activityRows.filter((item) => {
        if (activityCapability && item.capability !== activityCapability)
          return false;
        if (activityOutcome && item.outcome !== activityOutcome) return false;
        if (hideSuccesses && item.outcome === "Success") return false;
        return (
          !activityQuery ||
          JSON.stringify(item)
            .toLowerCase()
            .includes(activityQuery.toLowerCase())
        );
      }),
    [
      activityCapability,
      activityOutcome,
      activityQuery,
      activityRows,
      hideSuccesses,
    ],
  );

  function openCreate() {
    setEditing(null);
    setDraft(
      JSON.stringify(
        entity === "tickets"
          ? {
              title: "",
              description: "",
              status: "New",
              priority: "P3",
              requesterId: "itsm-employees-001",
            }
          : { name: "" },
        null,
        2,
      ),
    );
    setEditorOpen(true);
  }

  function openEdit(record: RecordData) {
    setEditing(record);
    setDraft(JSON.stringify(record, null, 2));
    setEditorOpen(true);
  }

  async function saveRecord() {
    try {
      const payload = JSON.parse(draft) as RecordData;
      const url = editing
        ? `${API}/api/v1/${scenario}/${entity}/${encodeURIComponent(editing.id)}`
        : `${API}/api/v1/${scenario}/${entity}`;
      const response = await fetch(url, {
        method: editing ? "PATCH" : "POST",
        headers: await getRequestHeaders(),
        body: JSON.stringify(payload),
      });
      if (!response.ok) throw new Error(await responseMessage(response));
      setEditorOpen(false);
      setSuccess(editing ? "Record updated." : "Record created.");
      await loadRecords();
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "Check the record data and try again.",
      );
    }
  }

  async function deleteRecord() {
    if (!deleteTarget) return;
    try {
      const response = await fetch(
        `${API}/api/v1/${scenario}/${entity}/${encodeURIComponent(deleteTarget.id)}?confirm=true`,
        {
          method: "DELETE",
          headers: await getRequestHeaders(),
        },
      );
      if (!response.ok) throw new Error(await responseMessage(response));
      setDeleteTarget(null);
      setSuccess("Record deleted.");
      await loadRecords();
    } catch (reason) {
      setDeleteTarget(null);
      setError(
        reason instanceof Error
          ? reason.message
          : "The record could not be deleted.",
      );
    }
  }

  async function resetDemo() {
    try {
      const response = await fetch(`${API}/api/v1/demo/reset`, {
        method: "POST",
        headers: await getRequestHeaders(),
      });
      if (!response.ok) throw new Error(await responseMessage(response));
      setSuccess("Seed data and MCP activity were reset.");
      await loadRecords();
    } catch (reason) {
      setError(
        reason instanceof Error
          ? reason.message
          : "The demo could not be reset.",
      );
    }
  }

  function setThemeMode() {
    const mode = theme.palette.mode === "dark" ? "light" : "dark";
    window.localStorage.setItem("novalabmcp-theme", mode);
    window.location.reload();
  }

  async function copyEvent(item: ActivityEvent) {
    await navigator.clipboard.writeText(JSON.stringify(item, null, 2));
    setSuccess("Event JSON copied.");
  }

  async function runMcpQuery() {
    setMcpLoading(true);
    setMcpError("");
    setMcpResult("");
    try {
      const args = JSON.parse(mcpArguments) as unknown;
      if (!args || typeof args !== "object" || Array.isArray(args)) {
        throw new Error("Tool arguments must be a JSON object.");
      }
      const message = await mcpJsonRpc(
        mcpEndpoint,
        {
          jsonrpc: "2.0",
          id: Date.now(),
          method: "tools/call",
          params: { name: mcpTool, arguments: args },
        },
        await getRequestHeaders(),
      );
      setMcpResult(JSON.stringify(message.result ?? message, null, 2));
    } catch (reason) {
      setMcpError(
        reason instanceof Error ? reason.message : "The MCP call failed.",
      );
    } finally {
      setMcpLoading(false);
    }
  }

  const drawer = (
    <Box
      sx={{
        width: 252,
        p: 2,
        height: "100%",
        display: "flex",
        flexDirection: "column",
      }}
    >
      <Stack
        direction="row"
        spacing={1.5}
        sx={{ alignItems: "center", px: 1, py: 1.5, mb: 2 }}
      >
        <Avatar
          sx={{
            bgcolor: "primary.main",
            width: 39,
            height: 39,
            fontWeight: 700,
          }}
        >
          N
        </Avatar>
        <Box>
          <Typography
            variant="subtitle1"
            sx={{ fontWeight: 800, lineHeight: 1.2 }}
          >
            NovaLabMCP
          </Typography>
          <Typography variant="caption" color="text.secondary">
            Business systems simulator
          </Typography>
        </Box>
      </Stack>
      <Typography
        variant="overline"
        color="text.secondary"
        sx={{ px: 1, mb: 1 }}
      >
        SCENARIOS
      </Typography>
      {Object.entries(SCENARIOS).map(([id, value]) => (
        <Button
          key={id}
          onClick={() => {
            setScenario(id as ScenarioId);
            setMobileNavOpen(false);
          }}
          variant={scenario === id && !isActivity ? "contained" : "text"}
          color={scenario === id ? "primary" : "inherit"}
          sx={{ justifyContent: "flex-start", mb: 0.5, px: 1.5 }}
        >
          {value.label}
        </Button>
      ))}
      <Divider sx={{ my: 2 }} />
      <Typography
        variant="overline"
        color="text.secondary"
        sx={{ px: 1, mb: 1 }}
      >
        DATA EXPLORER
      </Typography>
      <Box sx={{ flex: 1, overflowY: "auto" }}>
        {SCENARIOS[scenario].entities.map((item) => (
          <Button
            key={item}
            onClick={() => {
              setEntity(item);
              setPageModel({ page: 0, pageSize: 10 });
              setSelectedRecord(null);
              setMobileNavOpen(false);
            }}
            variant={entity === item ? "contained" : "text"}
            color={entity === item ? "primary" : "inherit"}
            sx={{
              justifyContent: "flex-start",
              mb: 0.5,
              px: 1.5,
              width: "100%",
            }}
          >
            {label(item)}
          </Button>
        ))}
      </Box>
      <Button
        onClick={() => {
          setEntity("mcp-activity");
          setActivityRows([]);
          setMobileNavOpen(false);
        }}
        variant={isActivity ? "contained" : "text"}
        color={isActivity ? "primary" : "inherit"}
        sx={{ justifyContent: "flex-start", px: 1.5 }}
      >
        MCP Command Center
      </Button>
      <Button
        onClick={() => {
          setEntity("mcp-editor");
          setMobileNavOpen(false);
        }}
        variant={isMcpEditor ? "contained" : "text"}
        color={isMcpEditor ? "primary" : "inherit"}
        sx={{ justifyContent: "flex-start", px: 1.5 }}
      >
        MCP Editor
      </Button>
      <Box sx={{ mt: 2, p: 1.5, bgcolor: "action.hover", borderRadius: 2 }}>
        <Typography variant="caption" sx={{ fontWeight: 700 }}>
          DEMO MODE
        </Typography>
        <Typography
          variant="caption"
          sx={{ display: "block" }}
          color="text.secondary"
        >
          In-memory data resets when service restarts.
        </Typography>
      </Box>
    </Box>
  );

  return (
    <Box sx={{ minHeight: "100vh", display: "flex" }}>
      <Drawer
        variant="permanent"
        sx={{
          display: { xs: "none", md: "block" },
          width: 252,
          flexShrink: 0,
          "& .MuiDrawer-paper": {
            width: 252,
            boxSizing: "border-box",
            borderRightColor: "divider",
          },
        }}
      >
        {drawer}
      </Drawer>
      <Drawer
        variant="temporary"
        open={mobileNavOpen}
        onClose={() => setMobileNavOpen(false)}
        sx={{
          display: { xs: "block", md: "none" },
          "& .MuiDrawer-paper": { width: 280 },
        }}
      >
        {drawer}
      </Drawer>
      <Box component="main" sx={{ flex: 1, minWidth: 0 }}>
        <AppBar
          position="sticky"
          color="inherit"
          elevation={0}
          sx={{ borderBottom: 1, borderColor: "divider" }}
        >
          <Toolbar sx={{ gap: 1.5, flexWrap: "wrap" }}>
            <Box sx={{ display: { xs: "block", md: "none" } }}>
              <Tooltip title="Open navigation">
                <IconButton
                  aria-label="Open navigation"
                  onClick={() => setMobileNavOpen(true)}
                >
                  <span>☰</span>
                </IconButton>
              </Tooltip>
            </Box>
            <Box sx={{ flex: 1, minWidth: 120 }}>
              <Typography variant="caption" color="text.secondary">
                CONTOSO / {SCENARIOS[scenario].caption.toUpperCase()}
              </Typography>
              <Typography variant="subtitle1" sx={{ fontWeight: 700 }}>
                Enterprise operations workspace
              </Typography>
            </Box>
            {entraEnabled ? (
              <Box sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                {account && (
                  <Typography variant="body2">
                    {account.name ?? account.username}
                  </Typography>
                )}
                <Button
                  onClick={() => void (account ? signOut() : signIn())}
                  disabled={!authReady}
                >
                  {account ? "Sign out" : "Sign in"}
                </Button>
              </Box>
            ) : (
              <FormControl size="small" sx={{ minWidth: 145 }}>
                <InputLabel id="role-label">Demo role</InputLabel>
                <Select
                  labelId="role-label"
                  label="Demo role"
                  value={role}
                  onChange={(event) => setRole(event.target.value)}
                >
                  <MenuItem value="DemoViewer">Viewer</MenuItem>
                  <MenuItem value="DemoEditor">Editor</MenuItem>
                  <MenuItem value="DemoAdmin">Admin</MenuItem>
                </Select>
              </FormControl>
            )}
            {canAdmin && (
              <Button color="warning" onClick={() => void resetDemo()}>
                Reset demo
              </Button>
            )}
            <Tooltip
              title={`Switch to ${theme.palette.mode === "dark" ? "light" : "dark"} mode`}
            >
              <IconButton
                aria-label="Toggle color theme"
                onClick={setThemeMode}
              >
                <span>{theme.palette.mode === "dark" ? "☀" : "◐"}</span>
              </IconButton>
            </Tooltip>
          </Toolbar>
        </AppBar>

        <Box sx={{ p: { xs: 2, md: 4 }, maxWidth: 1600, mx: "auto" }}>
          {entraEnabled && !account && (
            <Alert severity="info" sx={{ mb: 3 }}>
              {authReady
                ? "Sign in with your organization account to access NovaLabMCP."
                : "Connecting to Microsoft Entra ID…"}
            </Alert>
          )}
          {authError && (
            <Alert severity="error" sx={{ mb: 3 }}>
              {authError}
            </Alert>
          )}
          <Stack
            direction={{ xs: "column", sm: "row" }}
            spacing={2}
            sx={{
              justifyContent: "space-between",
              alignItems: { xs: "flex-start", sm: "center" },
              mb: 3,
            }}
          >
            <Box>
              <Typography variant="h4">
                {isActivity
                  ? "MCP Command Center"
                  : isMcpEditor
                    ? "MCP Editor"
                    : label(entity)}
              </Typography>
              <Typography color="text.secondary" sx={{ mt: 0.5 }}>
                {isActivity
                  ? "Inspect live, bounded MCP activity across all three domains."
                  : isMcpEditor
                    ? "Discover tools and run prebuilt or custom MCP queries across all three domains."
                    : `Browse and manage ${label(entity).toLowerCase()} in ${SCENARIOS[scenario].caption}.`}
              </Typography>
            </Box>
            {!isActivity && !isMcpEditor && canWrite && (
              <Button variant="contained" onClick={openCreate}>
                Create {label(entity).replace(/s$/, "")}
              </Button>
            )}
          </Stack>

          {!isActivity && !isMcpEditor && (
            <Alert severity="info" icon={false} sx={{ mb: 3 }}>
              <strong>In-memory demo data</strong> — resets when the service
              restarts. Each scenario is isolated; changes made in this browser
              do not create MCP command events.
            </Alert>
          )}

          {isMcpEditor ? (
            <Card variant="outlined">
              <CardContent>
                <Stack spacing={2}>
                  <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
                    <FormControl fullWidth size="small">
                      <InputLabel id="mcp-capability-label">
                        Capability
                      </InputLabel>
                      <Select
                        labelId="mcp-capability-label"
                        label="Capability"
                        value={mcpCapability}
                        onChange={(event) =>
                          setMcpCapability(event.target.value as McpCapability)
                        }
                      >
                        <MenuItem value="readonly">READONLY</MenuItem>
                        <MenuItem value="readwrite">READWRITE</MenuItem>
                      </Select>
                    </FormControl>
                    <FormControl fullWidth size="small">
                      <InputLabel id="mcp-scenario-label">Area</InputLabel>
                      <Select
                        labelId="mcp-scenario-label"
                        label="Area"
                        value={scenario}
                        onChange={(event) =>
                          setScenario(event.target.value as ScenarioId)
                        }
                      >
                        {Object.entries(SCENARIOS).map(([id, value]) => (
                          <MenuItem key={id} value={id}>
                            {value.label}
                          </MenuItem>
                        ))}
                      </Select>
                    </FormControl>
                    <FormControl fullWidth size="small">
                      <InputLabel id="mcp-preset-label">
                        Prebuilt query
                      </InputLabel>
                      <Select
                        labelId="mcp-preset-label"
                        label="Prebuilt query"
                        value={displayedMcpPreset}
                        onChange={(event) => {
                          const preset = mcpPresets.find(
                            (item) => item.id === event.target.value,
                          );
                          if (!preset) return;
                          setMcpPreset(preset.id);
                          setMcpTool(preset.tool);
                          setMcpArguments(
                            JSON.stringify(preset.arguments, null, 2),
                          );
                        }}
                      >
                        {mcpPresets.map((preset) => (
                          <MenuItem key={preset.id} value={preset.id}>
                            {preset.label}
                          </MenuItem>
                        ))}
                      </Select>
                    </FormControl>
                  </Stack>
                  <FormControl fullWidth size="small">
                    <InputLabel id="mcp-tool-label">MCP tool</InputLabel>
                    <Select
                      labelId="mcp-tool-label"
                      label="MCP tool"
                      value={mcpTool}
                      onChange={(event) => setMcpTool(event.target.value)}
                      disabled={mcpLoading || mcpTools.length === 0}
                    >
                      {mcpTool &&
                        !mcpTools.some((tool) => tool.name === mcpTool) && (
                          <MenuItem value={mcpTool}>{mcpTool}</MenuItem>
                        )}
                      {mcpTools.map((tool) => (
                        <MenuItem key={tool.name} value={tool.name}>
                          {tool.name}
                        </MenuItem>
                      ))}
                    </Select>
                  </FormControl>
                  <Typography variant="caption" color="text.secondary">
                    {mcpTools.find((tool) => tool.name === mcpTool)
                      ?.description ??
                      "Loading tools from the selected MCP endpoint."}
                  </Typography>
                  {mcpCapability === "readwrite" && (
                    <Alert severity="warning">
                      READWRITE calls can change the in-memory demo data. The
                      sample query creates a new record.
                    </Alert>
                  )}
                  {mcpError && <Alert severity="error">{mcpError}</Alert>}
                  <TextField
                    fullWidth
                    multiline
                    minRows={9}
                    label="Tool arguments (JSON)"
                    value={mcpArguments}
                    onChange={(event) => setMcpArguments(event.target.value)}
                    slotProps={{
                      htmlInput: {
                        spellCheck: false,
                        "aria-label": "MCP tool arguments JSON",
                      },
                    }}
                  />
                  <Stack
                    direction="row"
                    spacing={1}
                    sx={{ alignItems: "center" }}
                  >
                    <Button
                      variant="contained"
                      onClick={() => void runMcpQuery()}
                      disabled={mcpLoading || !mcpTool}
                    >
                      {mcpLoading ? "Running…" : "Run query"}
                    </Button>
                    {mcpLoading && <CircularProgress size={18} />}
                  </Stack>
                  {mcpResult && (
                    <Box>
                      <Typography variant="subtitle2" sx={{ mb: 1 }}>
                        MCP response
                      </Typography>
                      <Box
                        component="pre"
                        sx={{
                          whiteSpace: "pre-wrap",
                          overflowWrap: "anywhere",
                          p: 2,
                          m: 0,
                          bgcolor: "action.hover",
                          borderRadius: 2,
                          maxHeight: 480,
                          overflow: "auto",
                        }}
                      >
                        {mcpResult}
                      </Box>
                    </Box>
                  )}
                </Stack>
              </CardContent>
            </Card>
          ) : isActivity ? (
            <Card variant="outlined">
              <CardContent>
                <Stack
                  direction={{ xs: "column", lg: "row" }}
                  spacing={1.5}
                  sx={{
                    alignItems: { lg: "center" },
                    justifyContent: "space-between",
                    mb: 2,
                  }}
                >
                  <Stack
                    direction="row"
                    spacing={1}
                    sx={{ alignItems: "center" }}
                  >
                    <Chip
                      size="small"
                      color={streamStatus === "Live" ? "success" : "warning"}
                      label={`${streamStatus}${paused ? " · Paused" : ""}`}
                    />
                    <Typography variant="body2" color="text.secondary">
                      Showing newest retained events · buffer limit 2,000 ·
                      ephemeral
                    </Typography>
                  </Stack>
                  <Stack
                    direction={{ xs: "column", sm: "row" }}
                    spacing={1}
                    sx={{ flexWrap: "wrap" }}
                  >
                    <TextField
                      size="small"
                      label="Search events"
                      value={activityQuery}
                      onChange={(event) => setActivityQuery(event.target.value)}
                    />
                    <FormControl size="small" sx={{ minWidth: 135 }}>
                      <InputLabel id="capability-filter">Capability</InputLabel>
                      <Select
                        labelId="capability-filter"
                        label="Capability"
                        value={activityCapability}
                        onChange={(event) =>
                          setActivityCapability(event.target.value)
                        }
                      >
                        <MenuItem value="">All capabilities</MenuItem>
                        <MenuItem value="READONLY">READONLY</MenuItem>
                        <MenuItem value="READWRITE">READWRITE</MenuItem>
                      </Select>
                    </FormControl>
                    <FormControl size="small" sx={{ minWidth: 130 }}>
                      <InputLabel id="outcome-filter">Outcome</InputLabel>
                      <Select
                        labelId="outcome-filter"
                        label="Outcome"
                        value={activityOutcome}
                        onChange={(event) =>
                          setActivityOutcome(event.target.value)
                        }
                      >
                        <MenuItem value="">All outcomes</MenuItem>
                        <MenuItem value="Success">Success</MenuItem>
                        <MenuItem value="ValidationError">
                          Validation error
                        </MenuItem>
                        <MenuItem value="Forbidden">Forbidden</MenuItem>
                        <MenuItem value="Error">Error</MenuItem>
                      </Select>
                    </FormControl>
                    <Button
                      onClick={() => setHideSuccesses((current) => !current)}
                    >
                      {hideSuccesses ? "Show successes" : "Hide successes"}
                    </Button>
                    <Button onClick={() => setPaused((current) => !current)}>
                      {paused ? "Resume" : "Pause"}
                    </Button>
                    <Button
                      onClick={() => {
                        setActivityCapability("");
                        setActivityOutcome("");
                        setActivityQuery("");
                        setHideSuccesses(false);
                      }}
                    >
                      Clear filters
                    </Button>
                  </Stack>
                </Stack>
                <Divider sx={{ mb: 1 }} />
                {filteredActivity.length === 0 ? (
                  <Box sx={{ py: 8, textAlign: "center" }}>
                    <Typography variant="h6">No MCP activity yet</Typography>
                    <Typography color="text.secondary">
                      Invoke a tool through one of the READONLY or READWRITE
                      endpoints to see its event here.
                    </Typography>
                  </Box>
                ) : (
                  <Box sx={{ overflowX: "auto" }}>
                    <table className="activity-table">
                      <thead>
                        <tr>
                          <th>Time</th>
                          <th>Capability</th>
                          <th>Scenario</th>
                          <th>Tool</th>
                          <th>Caller</th>
                          <th>Duration</th>
                          <th>Outcome</th>
                          <th />
                        </tr>
                      </thead>
                      <tbody>
                        {filteredActivity.map((item) => (
                          <tr
                            key={item.id}
                            onClick={() =>
                              setSelectedRecord(item as unknown as RecordData)
                            }
                          >
                            <td>
                              {new Date(item.startedAt).toLocaleTimeString()}
                            </td>
                            <td>
                              <Chip
                                size="small"
                                label={item.capability}
                                color={
                                  item.capability === "READWRITE"
                                    ? "warning"
                                    : "primary"
                                }
                              />
                            </td>
                            <td>{item.scenario}</td>
                            <td>
                              <code>{item.tool}</code>
                            </td>
                            <td>{item.caller}</td>
                            <td>{item.durationMs} ms</td>
                            <td>
                              <Chip
                                size="small"
                                label={item.outcome}
                                color={
                                  item.outcome === "Success"
                                    ? "success"
                                    : "error"
                                }
                              />
                            </td>
                            <td>
                              <Button
                                size="small"
                                onClick={(event) => {
                                  event.stopPropagation();
                                  void copyEvent(item);
                                }}
                              >
                                Copy
                              </Button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </Box>
                )}
              </CardContent>
            </Card>
          ) : (
            <>
              <Stack
                direction={{ xs: "column", sm: "row" }}
                spacing={2}
                sx={{ mb: 2 }}
              >
                <Card variant="outlined" sx={{ flex: 1 }}>
                  <CardContent>
                    <Typography variant="overline" color="text.secondary">
                      RECORDS
                    </Typography>
                    <Typography variant="h5">
                      {totalCount.toLocaleString()}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      Matching {label(entity).toLowerCase()}
                    </Typography>
                  </CardContent>
                </Card>
                <Card variant="outlined" sx={{ flex: 1 }}>
                  <CardContent>
                    <Typography variant="overline" color="text.secondary">
                      SCENARIO
                    </Typography>
                    <Typography variant="h6">
                      {SCENARIOS[scenario].label}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      Isolated in-memory dataset
                    </Typography>
                  </CardContent>
                </Card>
                <Card variant="outlined" sx={{ flex: 1 }}>
                  <CardContent>
                    <Typography variant="overline" color="text.secondary">
                      MCP SURFACES
                    </Typography>
                    <Typography variant="h6">READONLY + READWRITE</Typography>
                    <Typography variant="caption" color="text.secondary">
                      Separate endpoints · Streamable HTTP
                    </Typography>
                  </CardContent>
                </Card>
              </Stack>

              <Card variant="outlined">
                <CardContent>
                  <Stack
                    direction={{ xs: "column", md: "row" }}
                    spacing={1.5}
                    sx={{ mb: 2 }}
                  >
                    <TextField
                      fullWidth
                      size="small"
                      label={`Search ${label(entity).toLowerCase()}`}
                      value={query}
                      onChange={(event) => {
                        setQuery(event.target.value);
                        setPageModel((current) => ({ ...current, page: 0 }));
                      }}
                    />
                    {rows.some((item) => "status" in item) && (
                      <FormControl size="small" sx={{ minWidth: 170 }}>
                        <InputLabel id="status-filter">
                          {entity === "opportunities" ? "Stage" : "Status"}
                        </InputLabel>
                        <Select
                          labelId="status-filter"
                          label={
                            entity === "opportunities" ? "Stage" : "Status"
                          }
                          value={statusFilter}
                          onChange={(event) => {
                            setStatusFilter(event.target.value);
                            setPageModel((current) => ({
                              ...current,
                              page: 0,
                            }));
                          }}
                        >
                          <MenuItem value="">All</MenuItem>
                          {[
                            ...new Set(
                              rows
                                .map((item) =>
                                  valueText(
                                    item[
                                      entity === "opportunities"
                                        ? "stage"
                                        : "status"
                                    ],
                                  ),
                                )
                                .filter((value) => value !== "—"),
                            ),
                          ].map((value) => (
                            <MenuItem key={value} value={value}>
                              {value}
                            </MenuItem>
                          ))}
                        </Select>
                      </FormControl>
                    )}
                    <Button
                      onClick={() => {
                        setQuery("");
                        setStatusFilter("");
                        setSortModel([]);
                        setPageModel({ page: 0, pageSize: 10 });
                      }}
                    >
                      Clear filters
                    </Button>
                  </Stack>
                  {error && (
                    <Alert
                      severity="error"
                      sx={{ mb: 2 }}
                      action={
                        <Button
                          color="inherit"
                          size="small"
                          onClick={() => void loadRecords()}
                        >
                          Retry
                        </Button>
                      }
                    >
                      {error}
                    </Alert>
                  )}
                  <Box sx={{ height: 560, width: "100%" }}>
                    <DataGrid
                      rows={rows}
                      columns={columns}
                      getRowId={(record) => record.id}
                      rowCount={totalCount}
                      loading={loading}
                      paginationMode="server"
                      sortingMode="server"
                      paginationModel={pageModel}
                      onPaginationModelChange={setPageModel}
                      onSortModelChange={setSortModel}
                      sortModel={sortModel}
                      pageSizeOptions={[10, 25, 50]}
                      disableRowSelectionOnClick
                      onRowClick={(parameters) =>
                        setSelectedRecord(parameters.row)
                      }
                      localeText={{
                        noRowsLabel: error
                          ? "Unable to load records"
                          : "No records match these filters.",
                      }}
                      sx={{
                        border: 0,
                        "& .MuiDataGrid-columnHeaders": {
                          bgcolor: "action.hover",
                        },
                        "& .MuiDataGrid-row:hover": { cursor: "pointer" },
                      }}
                    />
                  </Box>
                </CardContent>
              </Card>
            </>
          )}

          <Box sx={{ mt: 3, display: { xs: "block", md: "none" } }}>
            <FormControl fullWidth size="small">
              <InputLabel id="mobile-entity">Navigate entities</InputLabel>
              <Select
                labelId="mobile-entity"
                label="Navigate entities"
                value={
                  isActivity
                    ? "mcp-activity"
                    : isMcpEditor
                      ? "mcp-editor"
                      : entity
                }
                onChange={(event) => setEntity(event.target.value)}
              >
                {SCENARIOS[scenario].entities.map((item) => (
                  <MenuItem key={item} value={item}>
                    {label(item)}
                  </MenuItem>
                ))}
                <MenuItem value="mcp-activity">MCP Command Center</MenuItem>
                <MenuItem value="mcp-editor">MCP Editor</MenuItem>
              </Select>
            </FormControl>
          </Box>
        </Box>
      </Box>

      <Dialog
        open={editorOpen}
        onClose={() => setEditorOpen(false)}
        fullWidth
        maxWidth="md"
      >
        <DialogTitle>
          {editing
            ? `Edit ${recordTitle(editing)}`
            : `Create ${label(entity).replace(/s$/, "")}`}
        </DialogTitle>
        <DialogContent>
          <Typography color="text.secondary" variant="body2" sx={{ mb: 1.5 }}>
            Provide the record fields as JSON. The API validates relationships
            and business rules before saving.
          </Typography>
          <TextField
            autoFocus
            fullWidth
            multiline
            minRows={12}
            label="Record data"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            slotProps={{
              htmlInput: { spellCheck: false, "aria-label": "Record JSON" },
            }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setEditorOpen(false)}>Cancel</Button>
          <Button variant="contained" onClick={() => void saveRecord()}>
            Save record
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog
        open={deleteTarget !== null}
        onClose={() => setDeleteTarget(null)}
      >
        <DialogTitle>Delete this record?</DialogTitle>
        <DialogContent>
          <Typography>
            Delete <strong>{recordTitle(deleteTarget)}</strong>? This action
            cannot be undone. Related records may prevent deletion.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setDeleteTarget(null)}>Cancel</Button>
          <Button
            color="error"
            variant="contained"
            onClick={() => void deleteRecord()}
          >
            Confirm delete
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog
        open={selectedRecord !== null}
        onClose={() => setSelectedRecord(null)}
        fullWidth
        maxWidth="md"
      >
        <DialogTitle>
          {isActivity ? "MCP activity details" : recordTitle(selectedRecord)}
        </DialogTitle>
        <DialogContent>
          {selectedRecord && (
            <Box
              component="pre"
              sx={{
                whiteSpace: "pre-wrap",
                overflowWrap: "anywhere",
                p: 2,
                bgcolor: "action.hover",
                borderRadius: 2,
              }}
            >
              {JSON.stringify(selectedRecord, null, 2)}
            </Box>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setSelectedRecord(null)}>Close</Button>
          {!isActivity && canWrite && selectedRecord && (
            <Button
              onClick={() => {
                openEdit(selectedRecord);
                setSelectedRecord(null);
              }}
            >
              Edit
            </Button>
          )}
          {!isActivity && canWrite && selectedRecord && (
            <Button
              color="error"
              onClick={() => {
                setDeleteTarget(selectedRecord);
                setSelectedRecord(null);
              }}
            >
              Delete
            </Button>
          )}
          {isActivity && selectedRecord && (
            <Button
              onClick={() =>
                void copyEvent(selectedRecord as unknown as ActivityEvent)
              }
            >
              Copy event JSON
            </Button>
          )}
        </DialogActions>
      </Dialog>

      <Snackbar
        open={Boolean(success)}
        autoHideDuration={3500}
        onClose={() => setSuccess("")}
        message={success}
      />
      <Snackbar
        open={Boolean(error) && !loading}
        autoHideDuration={6500}
        onClose={() => setError("")}
        anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
      >
        <Alert
          severity="error"
          onClose={() => setError("")}
          sx={{ width: "100%" }}
        >
          {error}
        </Alert>
      </Snackbar>
      {loading && (
        <CircularProgress
          size={18}
          sx={{ position: "fixed", right: 20, bottom: 20 }}
        />
      )}
    </Box>
  );
}
