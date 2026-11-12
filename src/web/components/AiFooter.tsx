import { apiGet } from "@/lib/api";
import type { SessionResponse } from "@/lib/types";

/** "AI: {provider} · {model}" from /api/session (doc 07 §Footer). */
export async function AiFooter() {
  let label = "unknown";
  try {
    const s = await apiGet<SessionResponse>("/api/session");
    label = `${s.aiProvider ?? "–"} · ${s.aiModel ?? "–"}`;
  } catch {
    // keep "unknown"
  }
  return <p>AI: {label}</p>;
}
