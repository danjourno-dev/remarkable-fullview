const API_KEY_STORAGE_KEY = "fullview.apiKey";
const AUTH_ERROR_STORAGE_KEY = "fullview.authError";

export function getStoredApiKey(): string | null {
  return localStorage.getItem(API_KEY_STORAGE_KEY);
}

export function setStoredApiKey(key: string): void {
  localStorage.setItem(API_KEY_STORAGE_KEY, key);
}

export function clearStoredApiKey(): void {
  localStorage.removeItem(API_KEY_STORAGE_KEY);
}

export function setAuthError(message: string): void {
  sessionStorage.setItem(AUTH_ERROR_STORAGE_KEY, message);
}

/** Reads and clears the pending auth error, so it's shown once after a reload. */
export function takeAuthError(): string | null {
  const message = sessionStorage.getItem(AUTH_ERROR_STORAGE_KEY);
  if (message) sessionStorage.removeItem(AUTH_ERROR_STORAGE_KEY);
  return message;
}
