import { QuickAdd } from "../components/QuickAdd";
import { useApp } from "../lib/useApp";
import { matchesMode } from "../lib/filterByMode";
import { getDeviceId, putLocal } from "../lib/store";
import { TodoPriority, type Todo } from "../lib/types";
import { useEntities } from "../lib/useStore";

const PRIORITY_LABEL: Record<TodoPriority, string> = {
  [TodoPriority.Focus]: "Focus",
  [TodoPriority.Normal]: "Normal",
  [TodoPriority.Someday]: "Someday",
};

function touch(todo: Todo, changes: Partial<Todo>): Todo {
  return { ...todo, ...changes, updatedAt: new Date().toISOString(), updatedBy: getDeviceId() };
}

export function TodosPage() {
  const { mode } = useApp();
  const todos = useEntities<Todo>("Todo").filter((t) => matchesMode(t.context, mode));
  const open = todos.filter((t) => !t.completed).sort((a, b) => a.priority - b.priority);
  const done = todos.filter((t) => t.completed);

  return (
    <div className="page">
      <QuickAdd />
      <ul className="list">
        {open.map((todo) => (
          <li key={todo.id} className="list-item">
            <label>
              <input
                type="checkbox"
                checked={todo.completed}
                onChange={() => putLocal(touch(todo, { completed: true }))}
              />
              <span className="todo-title">{todo.title}</span>
            </label>
            <span className={`badge priority-${todo.priority}`}>{PRIORITY_LABEL[todo.priority]}</span>
            {todo.dueDate && <span className="badge due">{todo.dueDate}</span>}
            <select
              value={todo.priority}
              onChange={(e) => putLocal(touch(todo, { priority: Number(e.target.value) as TodoPriority }))}
            >
              {Object.entries(PRIORITY_LABEL).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
            <button onClick={() => putLocal(touch(todo, { deleted: true }))} aria-label="Delete">
              ×
            </button>
          </li>
        ))}
        {open.length === 0 && <li className="empty">Nothing open.</li>}
      </ul>
      {done.length > 0 && (
        <details className="done-section">
          <summary>{done.length} done</summary>
          <ul className="list">
            {done.map((todo) => (
              <li key={todo.id} className="list-item done">
                <label>
                  <input
                    type="checkbox"
                    checked={todo.completed}
                    onChange={() => putLocal(touch(todo, { completed: false }))}
                  />
                  <span className="todo-title">{todo.title}</span>
                </label>
              </li>
            ))}
          </ul>
        </details>
      )}
    </div>
  );
}
