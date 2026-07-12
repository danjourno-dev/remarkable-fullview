import type { Entity } from "./types";

export class UnauthorizedError extends Error {
  constructor() {
    super("Unauthorized");
    this.name = "UnauthorizedError";
  }
}

export class SyncClient {
  private readonly baseUrl: string;
  private readonly apiKey: string;

  constructor(baseUrl: string, apiKey: string) {
    this.baseUrl = baseUrl;
    this.apiKey = apiKey;
  }

  async getAll(): Promise<Entity[]> {
    const response = await fetch(`${this.baseUrl}/entities`, {
      method: "GET",
      headers: {
        "x-api-key": this.apiKey,
      },
    });

    if (response.status === 401 || response.status === 403) {
      throw new UnauthorizedError();
    }

    if (!response.ok) {
      throw new Error(`GET /entities failed: ${response.status} ${response.statusText}`);
    }

    return (await response.json()) as Entity[];
  }

  async push(entity: Entity): Promise<void> {
    const response = await fetch(`${this.baseUrl}/entities/${entity.id}`, {
      method: "PUT",
      headers: {
        "Content-Type": "application/json",
        "x-api-key": this.apiKey,
      },
      body: JSON.stringify(entity),
    });

    if (response.status === 401 || response.status === 403) {
      throw new UnauthorizedError();
    }

    if (!response.ok) {
      throw new Error(`PUT /entities/${entity.id} failed: ${response.status} ${response.statusText}`);
    }
  }
}
