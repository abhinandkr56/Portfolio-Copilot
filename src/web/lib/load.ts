import { isKiteUnavailable, isSessionRequired } from "./api";

export type Loaded<T> = { kind: "ok"; data: T } | { kind: "session" } | { kind: "unavailable" };

/**
 * Runs a page's data loads. 401 kite-session-required → "session" (show ConnectBanner),
 * 503 kite-unavailable → "unavailable" (show ErrorPanel). Anything else is rethrown to error.tsx.
 */
export async function load<T>(fn: () => Promise<T>): Promise<Loaded<T>> {
  try {
    return { kind: "ok", data: await fn() };
  } catch (e) {
    if (isSessionRequired(e)) return { kind: "session" };
    if (isKiteUnavailable(e)) return { kind: "unavailable" };
    throw e;
  }
}
