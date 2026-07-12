import { SyncClient } from "./syncClient";
import { applyRemoteSnapshot, clearOutboxThrough, getOutbox, setLastSyncedAt } from "./store";

/** Mirrors the device's SyncEngine.SyncOnceAsync (Fullview.Device): push the outbox one
 * entity at a time (PUT per item, clearing each outbox entry only after its PUT succeeds),
 * then GET /entities and apply the full snapshot. There's no cursor, so this always
 * converges regardless of clock skew between this browser and whatever else writes to the
 * store (see PROGRESS.md's Decisions for why the old cursor-based delta was dropped). A
 * failed call leaves the remaining outbox untouched so the next trigger retries. */
export async function syncOnce(client: SyncClient): Promise<void> {
  const outbox = getOutbox();

  for (const entity of outbox) {
    await client.push(entity);
    clearOutboxThrough(new Set([entity.id]));
  }

  const entities = await client.getAll();
  applyRemoteSnapshot(entities);
  setLastSyncedAt(new Date().toISOString());
}
