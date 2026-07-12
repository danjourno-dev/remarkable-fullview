import { NavLink } from "react-router-dom";
import type { ViewMode } from "../context/appContextDefinition";
import { useApp } from "../lib/useApp";
import { useLastSyncedAt, useOutboxCount } from "../lib/useStore";

const MODES: ViewMode[] = ["Personal", "Work", "All"];

const NAV_ITEMS = [
  { to: "/todos", label: "Todos" },
  { to: "/shopping", label: "Shopping" },
  { to: "/meals", label: "Meals" },
  { to: "/recipes", label: "Recipes" },
  { to: "/inbox", label: "Inbox" },
  { to: "/status", label: "Status" },
];

function formatSyncedAt(iso: string | null): string {
  if (!iso) return "never";
  return new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

export function Header() {
  const { mode, setMode, syncing, syncError, triggerSync } = useApp();
  const pending = useOutboxCount();
  const lastSyncedAt = useLastSyncedAt();

  return (
    <header className="header">
      <div className="header-top">
        <h1>Fullview</h1>
        <div className="mode-switcher">
          {MODES.map((m) => (
            <button
              key={m}
              className={m === mode ? "mode-button active" : "mode-button"}
              onClick={() => setMode(m)}
            >
              {m}
            </button>
          ))}
        </div>
      </div>
      <nav className="nav">
        {NAV_ITEMS.map((item) => (
          <NavLink key={item.to} to={item.to} className={({ isActive }) => (isActive ? "nav-link active" : "nav-link")}>
            {item.label}
          </NavLink>
        ))}
      </nav>
      <button className="sync-status" onClick={triggerSync} disabled={syncing}>
        {syncing ? "syncing…" : `synced ${formatSyncedAt(lastSyncedAt)} · ${pending} pending`}
      </button>
      {syncError && <div className="sync-error">Sync failed: {syncError}</div>}
    </header>
  );
}
