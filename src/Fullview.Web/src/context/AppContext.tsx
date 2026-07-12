import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { apiBaseUrl } from "../lib/config";
import { clearStoredApiKey, getStoredApiKey, setAuthError } from "../lib/auth";
import { SyncClient, UnauthorizedError } from "../lib/syncClient";
import { syncOnce } from "../lib/syncEngine";
import { SyncContext as EntityContext } from "../lib/types";
import { AppContext, type AppContextValue, type ViewMode } from "./appContextDefinition";

export function AppProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<ViewMode>("Personal");
  const [syncing, setSyncing] = useState(false);
  const [syncError, setSyncError] = useState<string | null>(null);
  const client = useMemo(() => new SyncClient(apiBaseUrl, getStoredApiKey() ?? ""), []);
  const inFlight = useRef(false);

  const triggerSync = useMemo(
    () => () => {
      if (inFlight.current) return;
      inFlight.current = true;
      setSyncing(true);
      syncOnce(client)
        .then(() => setSyncError(null))
        .catch((error: unknown) => {
          if (error instanceof UnauthorizedError) {
            // The stored key was rejected by the API — clear it and reload back to
            // the login screen (AuthGate reads localStorage fresh on mount).
            setAuthError("Incorrect API key.");
            clearStoredApiKey();
            window.location.reload();
            return;
          }
          setSyncError(error instanceof Error ? error.message : String(error));
        })
        .finally(() => {
          inFlight.current = false;
          setSyncing(false);
        });
    },
    [client],
  );

  // Sync fresh on open (mirrors the device: "the foreground app always syncs fresh on
  // open, so it catches up on anything changed elsewhere while it was closed" — Session 12).
  useEffect(() => {
    triggerSync();
  }, [triggerSync]);

  const value: AppContextValue = {
    mode,
    setMode,
    defaultEntityContext: mode === "Work" ? EntityContext.Work : EntityContext.Personal,
    syncing,
    syncError,
    triggerSync,
  };

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}
