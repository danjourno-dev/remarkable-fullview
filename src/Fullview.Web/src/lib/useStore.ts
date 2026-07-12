import { useSyncExternalStore } from "react";
import type { Entity } from "./types";
import {
  getLastSyncedAt,
  listEntities,
  outboxCount,
  subscribe,
} from "./store";

export function useEntities<T extends Entity>(entityType: T["entityType"]): T[] {
  return useSyncExternalStore(subscribe, () => listEntities<T>(entityType));
}

export function useOutboxCount(): number {
  return useSyncExternalStore(subscribe, outboxCount);
}

export function useLastSyncedAt(): string | null {
  return useSyncExternalStore(subscribe, getLastSyncedAt);
}
