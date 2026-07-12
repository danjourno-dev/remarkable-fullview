import { ulid } from "ulid";
import type { Entity } from "./types";

const ENTITIES_KEY = "fullview.entities";
const OUTBOX_KEY = "fullview.outbox";
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
  snapshotCache.clear();
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

/** Caches derived snapshots (e.g. per entityType) so useSyncExternalStore's getSnapshot
 * returns a referentially stable value when nothing has changed — without this, a fresh
 * array/object on every call makes React think the store changed on every render, which
 * triggers an infinite render loop (React error #185). Cleared whenever the store notifies. */
const snapshotCache = new Map<string, unknown>();

function cached<T>(cacheKey: string, compute: () => T): T {
  if (!snapshotCache.has(cacheKey)) {
    snapshotCache.set(cacheKey, compute());
  }
  return snapshotCache.get(cacheKey) as T;
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

export function getLastSyncedAt(): string | null {
  return localStorage.getItem(LAST_SYNCED_KEY);
}

export function setLastSyncedAt(iso: string): void {
  localStorage.setItem(LAST_SYNCED_KEY, iso);
  notify();
}

export function getEntities(): Record<string, Entity> {
  return cached(ENTITIES_KEY, () => readJson<Record<string, Entity>>(ENTITIES_KEY, {}));
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

/** Applies a full snapshot pulled from `GET /entities`: last-write-wins by updatedAt,
 * skipping entities that aren't strictly newer than what's stored locally (matches the
 * device's DeviceStore.ApplyRemoteSnapshot) so a just-pushed local write doesn't get
 * regressed by its own echo coming back in the same snapshot. */
export function applyRemoteSnapshot(snapshot: Entity[]): void {
  const entities = getEntities();
  for (const remote of snapshot) {
    const existing = entities[remote.id];
    if (!existing || remote.updatedAt > existing.updatedAt) {
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
  return cached(`${ENTITIES_KEY}:${entityType}`, () =>
    Object.values(getEntities()).filter(
      (e): e is T => e.entityType === entityType && !e.deleted,
    ),
  );
}
