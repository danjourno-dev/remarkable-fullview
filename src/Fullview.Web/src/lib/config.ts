function requireEnv(key: string): string {
  const value = import.meta.env[key];
  if (!value) {
    throw new Error(
      `${key} is not set — copy .env.example to .env.local and fill it in.`,
    );
  }
  return value;
}

export const apiBaseUrl = requireEnv("VITE_API_BASE_URL");
export const apiKey = requireEnv("VITE_API_KEY");
