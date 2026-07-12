import { SyncClient } from "./syncClient";
import {
  applyRemoteDelta,
  clearOutboxThrough,
  getCursor,
  getDeviceId,
  getOutbox,
  setCursor,
  setLastSyncedAt,
} from "./store";

/** Mirrors the device's SyncEngine.SyncOnceAsync (Fullview.Device): drain the outbox
 * snapshot, POST /sync, apply the returned delta with the same LWW rule, advance the
 * cursor and lastSyncedAt. A failed call leaves the outbox and cursor untouched so the
 * next trigger retries — same contract as the device side. */
export async function syncOnce(client: SyncClient): Promise<void> {
  const outboxSnapshot = getOutbox();

  const response = await client.sync({
    deviceId: getDeviceId(),
    cursor: getCursor(),
    outbox: outboxSnapshot,
  });

  applyRemoteDelta(response.delta);
  clearOutboxThrough(new Set(outboxSnapshot.map((e) => e.id)));
  setCursor(response.cursor);
  setLastSyncedAt(new Date().toISOString());
}
