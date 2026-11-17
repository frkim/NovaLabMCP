"use client";

import { CssBaseline, createTheme, ThemeProvider } from "@mui/material";
import { useEffect, useMemo, useState } from "react";

export function Providers({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const [mode, setMode] = useState<"light" | "dark">("light");

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

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      {children}
    </ThemeProvider>
  );
}
