"use client";

import {
  type AccountInfo,
  type IPublicClientApplication,
  PublicClientApplication,
} from "@azure/msal-browser";
import { CssBaseline, createTheme, ThemeProvider } from "@mui/material";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";

const clientId = process.env.NEXT_PUBLIC_ENTRA_CLIENT_ID ?? "";
const tenantId = process.env.NEXT_PUBLIC_ENTRA_TENANT_ID ?? "";
const apiScope = process.env.NEXT_PUBLIC_ENTRA_API_SCOPE ?? "";
const authEnabled = Boolean(clientId && tenantId && apiScope);

type AuthContextValue = {
  enabled: boolean;
  ready: boolean;
  account: AccountInfo | null;
  roles: string[];
  error: string;
  getAccessToken: () => Promise<string | null>;
  signIn: () => Promise<void>;
  signOut: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue>({
  enabled: false,
  ready: true,
  account: null,
  roles: [],
  error: "",
  getAccessToken: async () => null,
  signIn: async () => {},
  signOut: async () => {},
});

export function useAuth() {
  return useContext(AuthContext);
}

export function Providers({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const [mode, setMode] = useState<"light" | "dark">("light");
  const [instance, setInstance] = useState<IPublicClientApplication | null>(
    null,
  );
  const [account, setAccount] = useState<AccountInfo | null>(null);
  const [roles, setRoles] = useState<string[]>([]);
  const [authReady, setAuthReady] = useState(!authEnabled);
  const [authError, setAuthError] = useState("");

  useEffect(() => {
    if (!authEnabled) return;
    let active = true;
    const application = new PublicClientApplication({
      auth: {
        clientId,
        authority: `https://login.microsoftonline.com/${tenantId}`,
        redirectUri: window.location.origin,
      },
      cache: { cacheLocation: "sessionStorage" },
    });

    void application
      .initialize()
      .then(() => application.handleRedirectPromise())
      .then((result) => {
        const signedIn =
          result?.account ??
          application.getActiveAccount() ??
          application.getAllAccounts()[0] ??
          null;
        if (signedIn) application.setActiveAccount(signedIn);
        if (active) {
          setInstance(application);
          setAccount(signedIn);
          setAuthReady(true);
        }
      })
      .catch((reason: unknown) => {
        if (active) {
          setAuthError(
            reason instanceof Error
              ? reason.message
              : "Entra authentication could not be initialized.",
          );
          setAuthReady(true);
        }
      });

    return () => {
      active = false;
    };
  }, []);

  const signIn = useCallback(async () => {
    if (!instance) return;
    await instance.loginRedirect({ scopes: [apiScope] });
  }, [instance]);

  const signOut = useCallback(async () => {
    if (!instance || !account) return;
    await instance.logoutRedirect({ account });
  }, [account, instance]);

  const getAccessToken = useCallback(async () => {
    if (!authEnabled || !instance || !account) return null;
    try {
      const result = await instance.acquireTokenSilent({
        account,
        scopes: [apiScope],
      });
      const encodedClaims = result.accessToken
        .split(".")[1]
        .replaceAll("-", "+")
        .replaceAll("_", "/");
      const claims = JSON.parse(
        window.atob(
          encodedClaims.padEnd(Math.ceil(encodedClaims.length / 4) * 4, "="),
        ),
      ) as { roles?: unknown };
      setRoles(
        Array.isArray(claims.roles)
          ? claims.roles.filter(
              (claim): claim is string => typeof claim === "string",
            )
          : [],
      );
      return result.accessToken;
    } catch (reason) {
      setAuthError(
        reason instanceof Error
          ? reason.message
          : "A valid API access token is required. Sign in again.",
      );
      throw reason;
    }
  }, [account, instance]);

  useEffect(() => {
    const savedMode = window.localStorage.getItem("novalabmcp-theme");
    const prefersDark = window.matchMedia(
      "(prefers-color-scheme: dark)",
    ).matches;
    setMode(
      savedMode === "light" || savedMode === "dark"
        ? savedMode
        : prefersDark
          ? "dark"
          : "light",
    );
  }, []);

  const theme = useMemo(
    () =>
      createTheme({
        palette: {
          mode,
          primary: { main: mode === "dark" ? "#8ab4ff" : "#2558a8" },
          secondary: { main: "#087e8b" },
          background:
            mode === "dark"
              ? { default: "#111820", paper: "#18232e" }
              : { default: "#f4f7fb", paper: "#ffffff" },
        },
        shape: { borderRadius: 10 },
        typography: {
          fontFamily: 'Inter, "Segoe UI", Arial, sans-serif',
          h4: { fontWeight: 700, letterSpacing: "-0.03em" },
          h5: { fontWeight: 700, letterSpacing: "-0.02em" },
          button: { textTransform: "none", fontWeight: 600 },
        },
        components: {
          MuiButton: { styleOverrides: { root: { borderRadius: 8 } } },
          MuiPaper: { styleOverrides: { root: { backgroundImage: "none" } } },
        },
      }),
    [mode],
  );

  const authValue = useMemo(
    () => ({
      enabled: authEnabled,
      ready: authReady,
      account,
      roles,
      error: authError,
      getAccessToken,
      signIn,
      signOut,
    }),
    [account, authError, authReady, getAccessToken, roles, signIn, signOut],
  );

  return (
    <AuthContext.Provider value={authValue}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
      </ThemeProvider>
    </AuthContext.Provider>
  );
}
