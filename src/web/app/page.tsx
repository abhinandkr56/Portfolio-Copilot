import { Suspense } from "react";
import { connection } from "next/server";
import { RewritePing } from "@/components/RewritePing";
import { isPingOk } from "@/lib/ping";

async function ServerPing() {
  await connection(); // render at request time, never at build time
  const apiUrl = process.env.API_URL ?? "http://127.0.0.1:5080";
  let status: string;
  try {
    const res = await fetch(`${apiUrl}/api/ping`, { cache: "no-store" });
    status = res.ok && isPingOk(await res.json()) ? "ok" : `error ${res.status}`;
  } catch {
    status = "unreachable";
  }
  return <p>Api: {status}</p>;
}

export default function Home() {
  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-2 p-6">
      <h1 className="text-2xl font-semibold">Portfolio Copilot</h1>
      <Suspense fallback={<p>Api: checking…</p>}>
        <ServerPing />
      </Suspense>
      <RewritePing />
      <footer className="mt-8 text-sm opacity-70">
        Information only, not investment advice. You make the decisions.
      </footer>
    </main>
  );
}
