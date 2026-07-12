import type { SyncRequest, SyncResponse } from "./types";

export class SyncClient {
  private readonly baseUrl: string;
  private readonly apiKey: string;

  constructor(baseUrl: string, apiKey: string) {
    this.baseUrl = baseUrl;
    this.apiKey = apiKey;
  }

  async sync(request: SyncRequest): Promise<SyncResponse> {
    const response = await fetch(`${this.baseUrl}/sync`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-api-key": this.apiKey,
      },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      throw new Error(`POST /sync failed: ${response.status} ${response.statusText}`);
    }

    return (await response.json()) as SyncResponse;
  }
}
