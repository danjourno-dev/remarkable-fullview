import { useApp } from "../lib/useApp";
import { getDeviceId } from "../lib/store";
import { useLastSyncedAt, useOutboxCount } from "../lib/useStore";

export function StatusPage() {
  const { syncing, syncError, triggerSync } = useApp();
  const pending = useOutboxCount();
  const lastSyncedAt = useLastSyncedAt();

  return (
    <div className="page status-page">
      <dl>
        <dt>This browser's device id</dt>
        <dd>{getDeviceId()}</dd>
        <dt>Last synced</dt>
        <dd>{lastSyncedAt ? new Date(lastSyncedAt).toLocaleString() : "never"}</dd>
        <dt>Pending outbox writes</dt>
        <dd>{pending}</dd>
        <dt>Sync status</dt>
        <dd>{syncing ? "syncing…" : syncError ? `error: ${syncError}` : "idle"}</dd>
      </dl>
      <button onClick={triggerSync} disabled={syncing}>
        Sync now
      </button>
    </div>
  );
}
