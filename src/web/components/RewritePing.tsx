"use client";

import { useEffect, useState } from "react";
import { isPingOk } from "@/lib/ping";

export function RewritePing() {
  const [status, setStatus] = useState("checking…");

  useEffect(() => {
    fetch("/api/ping", { cache: "no-store" })
      .then(async (r) => setStatus(r.ok && isPingOk(await r.json()) ? "ok" : `error ${r.status}`))
      .catch(() => setStatus("unreachable"));
  }, []);

  return <p>Rewrite: {status}</p>;
}
