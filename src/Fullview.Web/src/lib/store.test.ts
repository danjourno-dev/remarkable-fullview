import { beforeEach, describe, expect, it } from "vitest";
import {
  applyRemoteDelta,
  clearOutboxThrough,
  getEntities,
  getOutbox,
  listEntities,
  outboxCount,
  putLocal,
} from "./store";
import { SyncContext, type Todo } from "./types";

function todo(overrides: Partial<Todo> = {}): Todo {
  return {
    id: "01ABC",
    entityType: "Todo",
    context: SyncContext.Personal,
    updatedAt: "2026-07-12T10:00:00.000Z",
    updatedBy: "web",
    deleted: false,
    title: "buy milk",
    priority: 1,
    dueDate: null,
    energy: null,
    completed: false,
    ...overrides,
  };
}

beforeEach(() => {
  localStorage.clear();
});

describe("putLocal", () => {
  it("stores the entity and queues it in the outbox", () => {
    const t = todo();
    putLocal(t);

    expect(getEntities()[t.id]).toEqual(t);
    expect(getOutbox()).toEqual([t]);
    expect(outboxCount()).toBe(1);
  });

  it("replaces a stale outbox entry for the same id rather than duplicating it", () => {
    putLocal(todo({ title: "buy milk" }));
    putLocal(todo({ title: "buy oat milk" }));

    expect(getOutbox()).toHaveLength(1);
    expect((getOutbox()[0] as Todo).title).toBe("buy oat milk");
  });
});

describe("applyRemoteDelta", () => {
  it("applies a remote entity when there is no local copy", () => {
    applyRemoteDelta([todo()]);
    expect(listEntities<Todo>("Todo")).toHaveLength(1);
  });

  it("does not overwrite a newer local write with an older remote one (LWW)", () => {
    putLocal(todo({ title: "local newer", updatedAt: "2026-07-12T12:00:00.000Z" }));
    applyRemoteDelta([todo({ title: "remote older", updatedAt: "2026-07-12T10:00:00.000Z" })]);

    expect((getEntities()["01ABC"] as Todo).title).toBe("local newer");
  });

  it("remote wins on an exact timestamp tie, matching DynamoSyncStore's server rule", () => {
    putLocal(todo({ title: "local", updatedAt: "2026-07-12T12:00:00.000Z" }));
    applyRemoteDelta([todo({ title: "remote", updatedAt: "2026-07-12T12:00:00.000Z" })]);

    expect((getEntities()["01ABC"] as Todo).title).toBe("remote");
  });

  it("applies a tombstone so deletions converge", () => {
    putLocal(todo());
    applyRemoteDelta([todo({ deleted: true, updatedAt: "2026-07-12T12:00:00.000Z" })]);

    expect(getEntities()["01ABC"].deleted).toBe(true);
    expect(listEntities<Todo>("Todo")).toHaveLength(0);
  });
});

describe("clearOutboxThrough", () => {
  it("removes only the given ids, leaving later writes queued", () => {
    putLocal(todo({ id: "a" }));
    putLocal(todo({ id: "b" }));

    clearOutboxThrough(new Set(["a"]));

    expect(getOutbox().map((e) => e.id)).toEqual(["b"]);
  });
});
