import { beforeEach, describe, expect, it } from "vitest";
import { clearStoredApiKey, getStoredApiKey, setAuthError, setStoredApiKey, takeAuthError } from "./auth";

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
});

describe("stored API key", () => {
  it("is absent until set", () => {
    expect(getStoredApiKey()).toBeNull();
  });

  it("round-trips through set/get", () => {
    setStoredApiKey("abc123");
    expect(getStoredApiKey()).toBe("abc123");
  });

  it("is removed on clear", () => {
    setStoredApiKey("abc123");
    clearStoredApiKey();
    expect(getStoredApiKey()).toBeNull();
  });
});

describe("auth error", () => {
  it("is absent until set", () => {
    expect(takeAuthError()).toBeNull();
  });

  it("is returned once then cleared", () => {
    setAuthError("Incorrect API key.");
    expect(takeAuthError()).toBe("Incorrect API key.");
    expect(takeAuthError()).toBeNull();
  });
});
