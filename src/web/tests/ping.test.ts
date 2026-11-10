import { describe, expect, it } from "vitest";
import { isPingOk } from "@/lib/ping";

describe("isPingOk", () => {
  it("accepts { ok: true }", () => expect(isPingOk({ ok: true })).toBe(true));
  it("rejects anything else", () => {
    expect(isPingOk({ ok: false })).toBe(false);
    expect(isPingOk(null)).toBe(false);
    expect(isPingOk("ok")).toBe(false);
  });
});
