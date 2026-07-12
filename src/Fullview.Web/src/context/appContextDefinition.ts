import { createContext } from "react";
import type { SyncContext as EntityContext } from "../lib/types";

export type ViewMode = "Personal" | "Work" | "All";

export interface AppContextValue {
  mode: ViewMode;
  setMode: (mode: ViewMode) => void;
  /** The entity context a newly-created item should default to. "All" has no single
   * answer, so quick-add defaults to Personal per the plan ("defaults new items to
   * current context"). */
  defaultEntityContext: EntityContext;
  syncing: boolean;
  syncError: string | null;
  triggerSync: () => void;
}

export const AppContext = createContext<AppContextValue | null>(null);
