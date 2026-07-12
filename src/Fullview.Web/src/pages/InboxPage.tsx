import type { InboxPage as InboxPageEntity } from "../lib/types";
import { InboxPageState } from "../lib/types";
import { useEntities } from "../lib/useStore";

const STATE_LABEL: Record<InboxPageState, string> = {
  [InboxPageState.Queued]: "Queued",
  [InboxPageState.Processed]: "Processed",
  [InboxPageState.Filed]: "Filed",
};

/** Needs-review triage — empty for now (plan Stage 6): the capture flow that files pages
 * into InboxPage rows doesn't exist yet, so this just reads whatever the sync API returns
 * and lists it. No filing/processing actions until that flow lands. */
export function InboxPage() {
  const pages = useEntities<InboxPageEntity>("InboxPage");

  return (
    <div className="page">
      <ul className="list">
        {pages.map((page) => (
          <li key={page.id} className="list-item">
            <span className="badge">{STATE_LABEL[page.state]}</span>
            <span>{page.notes ?? page.s3Key ?? page.id}</span>
          </li>
        ))}
        {pages.length === 0 && <li className="empty">Nothing to review — the capture flow isn't built yet.</li>}
      </ul>
    </div>
  );
}
