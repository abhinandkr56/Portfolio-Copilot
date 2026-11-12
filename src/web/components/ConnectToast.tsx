"use client";

import { useEffect, useState } from "react";

const reasons: Record<string, string> = {
  "login-cancelled": "the Zerodha login was cancelled",
  "state-mismatch": "the login link expired or didn't match; please try again",
  "no-request-token": "Zerodha didn't return a login token",
  "exchange-failed": "Zerodha didn't accept the login",
  "kite-unavailable": "Zerodha is not responding",
  "kite-api-key-missing": "the Kite API key isn't set (KITE_API_KEY)",
};

/** Shows the result of the login round trip (?connected=1 / ?connected=0&reason=…), then tidies the URL. */
export function ConnectToast({ connected, reason }: { connected?: string; reason?: string }) {
  const [visible, setVisible] = useState(connected === "1" || connected === "0");

  useEffect(() => {
    if (!visible) return;
    const url = new URL(window.location.href);
    url.searchParams.delete("connected");
    url.searchParams.delete("reason");
    window.history.replaceState(null, "", url);
    const t = setTimeout(() => setVisible(false), 6000);
    return () => clearTimeout(t);
  }, [visible]);

  if (!visible) return null;
  const ok = connected === "1";
  return (
    <div
      role="status"
      className={`rounded-md px-4 py-2 text-sm font-medium ${ok ? "bg-green-100 text-green-900 dark:bg-green-950 dark:text-green-100" : "bg-red-100 text-red-900 dark:bg-red-950 dark:text-red-100"}`}
    >
      {ok ? "Connected" : `Couldn't connect: ${reasons[reason ?? ""] ?? reason ?? "unknown reason"}`}
    </div>
  );
}
