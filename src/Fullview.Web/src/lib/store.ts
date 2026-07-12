import { ulid } from "ulid";
import type { Entity } from "./types";

const ENTITIES_KEY = "fullview.entities";
const OUTBOX_KEY = "fullview.outbox";
const CURSOR_KEY = "fullview.cursor";
const DEVICE_ID_KEY = "fullview.deviceId";
const LAST_SYNCED_KEY = "fullview.lastSyncedAt";

/** Plain pub-sub so React components (via useSyncExternalStore in lib/useStore.ts) can
 * re-render when localStorage-backed state changes — localStorage itself only fires a
 * `storage` event for changes made in *other* tabs, not the current one. */
const listeners = new Set<() => void>();

export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function notify(): void {
  for (const listener of listeners) listener();
}

function readJson<T>(key: string, fallback: T): T {
  const raw = localStorage.getItem(key);
  if (!raw) return fallback;
  try {
    return JSON.parse(raw) as T;
  } catch {
    return fallback;
  }
}

function writeJson(key: string, value: unknown): void {
  localStorage.setItem(key, JSON.stringify(value));
}

/** "web" + a short random suffix so multiple browsers/tabs each get a stable device id
 * across reloads (persisted in localStorage), matching the device's own DeviceId concept. */
export function getDeviceId(): string {
  let id = localStorage.getItem(DEVICE_ID_KEY);
  if (!id) {
    id = `web-${ulid().slice(-10)}`;
    localStorage.setItem(DEVICE_ID_KEY, id);
  }
  return id;
}

export function getCursor(): string | null {
  return localStorage.getItem(CURSOR_KEY);
}

export function setCursor(cursor: string): void {
  localStorage.setItem(CURSOR_KEY, cursor);
  notify();
}

export function getLastSyncedAt(): string | null {
  return localStorage.getItem(LAST_SYNCED_KEY);
}

export function setLastSyncedAt(iso: string): void {
  localStorage.setItem(LAST_SYNCED_KEY, iso);
  notify();
}

export function getEntities(): Record<string, Entity> {
  return readJson<Record<string, Entity>>(ENTITIES_KEY, {});
}

function setEntities(entities: Record<string, Entity>): void {
  writeJson(ENTITIES_KEY, entities);
}

export function getOutbox(): Entity[] {
  return readJson<Entity[]>(OUTBOX_KEY, []);
}

function setOutbox(outbox: Entity[]): void {
  writeJson(OUTBOX_KEY, outbox);
}

export function outboxCount(): number {
  return getOutbox().length;
}

/** Applies a local edit: last-write-wins locally (this IS the write), stores it, and
 * queues it in the outbox for the next sync. Idempotent to replay by id. */
export function putLocal(entity: Entity): void {
  const entities = getEntities();
  entities[entity.id] = entity;
  setEntities(entities);

  const outbox = getOutbox();
  const withoutStale = outbox.filter((e) => e.id !== entity.id);
  withoutStale.push(entity);
  setOutbox(withoutStale);
  notify();
}

/** Applies a delta pulled from `/sync`: last-write-wins by updatedAt, remote wins ties
 * (matches DynamoSyncStore's server-side rule) so a device's own echoed write doesn't
 * regress local state. */
export function applyRemoteDelta(delta: Entity[]): void {
  const entities = getEntities();
  for (const remote of delta) {
    const existing = entities[remote.id];
    if (!existing || remote.updatedAt >= existing.updatedAt) {
      entities[remote.id] = remote;
    }
  }
  setEntities(entities);
  notify();
}

export function clearOutboxThrough(ids: Set<string>): void {
  const remaining = getOutbox().filter((e) => !ids.has(e.id));
  setOutbox(remaining);
  notify();
}

export function listEntities<T extends Entity>(
  entityType: T["entityType"],
): T[] {
  return Object.values(getEntities()).filter(
    (e): e is T => e.entityType === entityType && !e.deleted,
  );
}
