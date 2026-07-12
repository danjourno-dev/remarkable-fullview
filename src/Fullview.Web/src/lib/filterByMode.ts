import type { ViewMode } from "../context/appContextDefinition";
import { SyncContext } from "./types";

export function matchesMode(context: SyncContext, mode: ViewMode): boolean {
  if (mode === "All") return true;
  return mode === "Work" ? context === SyncContext.Work : context === SyncContext.Personal;
}
