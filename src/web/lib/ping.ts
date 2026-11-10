// Phase 0 helper. Replaced by lib/api.ts + generated types in Phase 4.
export function isPingOk(body: unknown): boolean {
  return typeof body === "object" && body !== null && (body as { ok?: unknown }).ok === true;
}
