import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiGet, apiSend, isKiteUnavailable, isSessionRequired } from "@/lib/api";

function mockFetch(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/problem+json" } }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

afterEach(() => vi.unstubAllGlobals());

describe("api", () => {
  it("turns a 401 ProblemDetails into an ApiError that needs a session", async () => {
    mockFetch(401, { type: "kite-session-required", title: "Connect your Zerodha account", status: 401 });

    const error = await apiGet("/api/holdings").catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(401);
    expect(isSessionRequired(error)).toBe(true);
    expect(isKiteUnavailable(error)).toBe(false);
  });

  it("recognises 503 kite-unavailable", async () => {
    mockFetch(503, { type: "kite-unavailable", status: 503 });

    expect(isKiteUnavailable(await apiGet("/api/holdings").catch((e: unknown) => e))).toBe(true);
  });

  it("keeps validation errors from a 400", async () => {
    mockFetch(400, { status: 400, errors: { driftBandPoints: ["Must be between 1 and 20."] } });

    const error = (await apiSend("PUT", "/api/plan", {}).catch((e: unknown) => e)) as ApiError;

    expect(error.errors.driftBandPoints).toEqual(["Must be between 1 and 20."]);
  });

  it("calls the same-origin /api path in the browser, without caching", async () => {
    const fetchMock = mockFetch(200, { ok: true });

    await apiGet("/api/ping");

    expect(fetchMock).toHaveBeenCalledWith("/api/ping", expect.objectContaining({ method: "GET", cache: "no-store" }));
  });
});
