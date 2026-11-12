export function ErrorPanel({ message }: { message: string }) {
  return (
    <section role="alert" className="rounded-lg border border-amber-300 bg-amber-50 p-5 text-amber-950 dark:border-amber-800 dark:bg-amber-950/50 dark:text-amber-100">
      <p className="font-medium">{message}</p>
    </section>
  );
}

export const KITE_DOWN_MESSAGE = "Zerodha is not responding, try again in a minute";
