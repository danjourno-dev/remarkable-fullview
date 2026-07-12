import * as chrono from "chrono-node";
import { useState } from "react";
import { useApp } from "../lib/useApp";
import { newTodo } from "../lib/newEntity";
import { putLocal } from "../lib/store";

/** Parses a trailing/leading natural-language date out of the input (e.g. "buy milk
 * tomorrow", "next tuesday call the bank") and creates a Todo with the rest as the title,
 * defaulting to the currently-selected context (plan: "defaults new items to current
 * context"). */
export function QuickAdd() {
  const [text, setText] = useState("");
  const { defaultEntityContext, mode } = useApp();

  function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    const trimmed = text.trim();
    if (!trimmed) return;

    const results = chrono.parse(trimmed, new Date(), { forwardDate: true });
    let title = trimmed;
    let dueDate: string | null = null;

    if (results.length > 0) {
      const match = results[0];
      dueDate = match.start.date().toISOString().slice(0, 10);
      title = (trimmed.slice(0, match.index) + trimmed.slice(match.index + match.text.length))
        .replace(/\s+/g, " ")
        .trim();
      if (!title) title = trimmed;
    }

    putLocal(newTodo(defaultEntityContext, title, { dueDate }));
    setText("");
  }

  return (
    <form className="quick-add" onSubmit={handleSubmit}>
      <input
        type="text"
        placeholder={mode === "All" ? "Add a todo (defaults to Personal)…" : `Add a ${mode.toLowerCase()} todo, e.g. "call bank tomorrow"…`}
        value={text}
        onChange={(e) => setText(e.target.value)}
      />
      <button type="submit">Add</button>
    </form>
  );
}
