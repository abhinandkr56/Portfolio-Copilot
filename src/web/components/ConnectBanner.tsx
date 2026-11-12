export function ConnectBanner() {
  return (
    <section className="rounded-lg border border-blue-200 bg-blue-50 p-5 dark:border-blue-900 dark:bg-blue-950/50">
      <p className="text-base font-medium">Connect your Zerodha account to start.</p>
      <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
        You log in on Zerodha&apos;s own page. This app only reads your holdings; it can&apos;t place orders.
      </p>
      <a
        href="/connect"
        className="mt-4 inline-flex rounded-md bg-blue-700 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-700"
      >
        Connect Zerodha
      </a>
    </section>
  );
}
