import { useState } from "react";
import { useApp } from "../lib/useApp";
import { matchesMode } from "../lib/filterByMode";
import { newShoppingItem } from "../lib/newEntity";
import { getDeviceId, putLocal } from "../lib/store";
import type { ShoppingItem } from "../lib/types";
import { useEntities } from "../lib/useStore";

function touch(item: ShoppingItem, changes: Partial<ShoppingItem>): ShoppingItem {
  return { ...item, ...changes, updatedAt: new Date().toISOString(), updatedBy: getDeviceId() };
}

export function ShoppingPage() {
  const { mode, defaultEntityContext } = useApp();
  const [text, setText] = useState("");
  const items = useEntities<ShoppingItem>("ShoppingItem").filter((i) => matchesMode(i.context, mode));
  const unchecked = items.filter((i) => !i.checked);
  const checked = items.filter((i) => i.checked);

  function handleAdd(event: React.FormEvent) {
    event.preventDefault();
    const trimmed = text.trim();
    if (!trimmed) return;
    putLocal(newShoppingItem(defaultEntityContext, trimmed));
    setText("");
  }

  return (
    <div className="page">
      <form className="quick-add" onSubmit={handleAdd}>
        <input
          type="text"
          placeholder="Add a shopping item…"
          value={text}
          onChange={(e) => setText(e.target.value)}
        />
        <button type="submit">Add</button>
      </form>
      <ul className="list">
        {unchecked.map((item) => (
          <li key={item.id} className="list-item">
            <label>
              <input type="checkbox" checked={item.checked} onChange={() => putLocal(touch(item, { checked: true }))} />
              <span>{item.name}</span>
            </label>
            {item.category && <span className="badge">{item.category}</span>}
            <button onClick={() => putLocal(touch(item, { deleted: true }))} aria-label="Delete">
              ×
            </button>
          </li>
        ))}
        {unchecked.length === 0 && <li className="empty">Shopping list is empty.</li>}
      </ul>
      {checked.length > 0 && (
        <details className="done-section">
          <summary>{checked.length} checked off</summary>
          <ul className="list">
            {checked.map((item) => (
              <li key={item.id} className="list-item done">
                <label>
                  <input type="checkbox" checked={item.checked} onChange={() => putLocal(touch(item, { checked: false }))} />
                  <span>{item.name}</span>
                </label>
              </li>
            ))}
          </ul>
        </details>
      )}
    </div>
  );
}
