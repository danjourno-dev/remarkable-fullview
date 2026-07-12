import { useState, type FormEvent, type ReactNode } from "react";
import { getStoredApiKey, setStoredApiKey, takeAuthError } from "../lib/auth";

export function AuthGate({ children }: { children: ReactNode }) {
  const [apiKey, setApiKey] = useState(() => getStoredApiKey());
  const [input, setInput] = useState("");
  const [error] = useState(() => takeAuthError());

  if (apiKey) return <>{children}</>;

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const trimmed = input.trim();
    if (!trimmed) return;
    setStoredApiKey(trimmed);
    setApiKey(trimmed);
  }

  return (
    <div className="auth-gate">
      <form className="auth-gate-form" onSubmit={handleSubmit}>
        <h1>Fullview</h1>
        <label htmlFor="api-key">API key</label>
        <input
          id="api-key"
          type="password"
          value={input}
          onChange={(event) => setInput(event.target.value)}
          autoFocus
        />
        {error && <div className="sync-error">{error}</div>}
        <button type="submit">Continue</button>
      </form>
    </div>
  );
}
