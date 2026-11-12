// Small typed wrapper over fetch (doc 07). Server: calls API_URL directly. Browser: same-origin /api/* (rewritten).

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly type: string | undefined,
    public readonly title: string | undefined,
    public readonly detail: string | undefined,
    /** Field errors from a 400 ValidationProblem, keyed by the plan's camelCase field names. */
    public readonly errors: Record<string, string[]> = {},
  ) {
    super(detail ?? title ?? `Request failed with ${status}`);
    this.name = "ApiError";
  }
}

export const isSessionRequired = (e: unknown): e is ApiError =>
  e instanceof ApiError && e.type === "kite-session-required";

export const isKiteUnavailable = (e: unknown): e is ApiError =>
  e instanceof ApiError && e.type === "kite-unavailable";

/** Server components call the Api directly (server-only API_URL); the browser uses the same-origin rewrite. */
export function apiBaseUrl(): string {
  return typeof window === "undefined" ? (process.env.API_URL ?? "http://127.0.0.1:5080") : "";
}

type ProblemBody = { type?: string; title?: string; detail?: string; errors?: Record<string, string[]> };

async function toApiError(res: Response): Promise<ApiError> {
  let body: ProblemBody = {};
  try {
    body = (await res.json()) as ProblemBody;
  } catch {
    // not JSON: keep the status only
  }
  return new ApiError(res.status, body.type, body.title, body.detail, body.errors ?? {});
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${apiBaseUrl()}${path}`, {
    method,
    cache: "no-store", // the Api does the caching
    headers: body === undefined ? { Accept: "application/json" } : { Accept: "application/json", "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) throw await toApiError(res);
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export const apiGet = <T>(path: string) => request<T>("GET", path);

export const apiSend = <T>(method: "POST" | "PUT" | "DELETE", path: string, body?: unknown) => request<T>(method, path, body);
