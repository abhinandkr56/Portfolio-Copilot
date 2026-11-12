import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import Link from "next/link";
import { Suspense } from "react";
import { ConnectionPill, ConnectionPillSkeleton } from "@/components/ConnectionPill";
import { RefreshButton } from "@/components/RefreshButton";
import "./globals.css";

const geistSans = Geist({ variable: "--font-geist-sans", subsets: ["latin"] });
const geistMono = Geist_Mono({ variable: "--font-geist-mono", subsets: ["latin"] });

export const metadata: Metadata = {
  title: "Portfolio Copilot",
  description: "Read-only review of your Zerodha holdings against your own plan",
};

const nav = [
  { href: "/", label: "Dashboard" },
  { href: "/holdings", label: "Holdings" },
  { href: "/sips", label: "SIPs" },
  { href: "/plan", label: "Plan" },
  { href: "/ask", label: "Ask" },
] as const;

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className={`${geistSans.variable} ${geistMono.variable} h-full antialiased`}>
      <body className="flex min-h-full flex-col bg-zinc-50 text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
        <header className="border-b border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900">
          <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
            <Link href="/" className="font-semibold">Portfolio Copilot</Link>
            <nav aria-label="Main" className="order-3 flex w-full gap-4 overflow-x-auto text-sm sm:order-none sm:w-auto">
              {nav.map((n) => (
                <Link key={n.href} href={n.href} className="whitespace-nowrap text-zinc-600 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100">
                  {n.label}
                </Link>
              ))}
            </nav>
            <div className="ml-auto flex items-center gap-2">
              <Suspense fallback={<ConnectionPillSkeleton />}>
                <ConnectionPill />
              </Suspense>
              <RefreshButton />
            </div>
          </div>
        </header>
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6">{children}</main>
        <footer className="border-t border-zinc-200 px-4 py-4 text-center text-xs text-zinc-500 dark:border-zinc-800 dark:text-zinc-400">
          <p>AI: not set up yet (Phase 5)</p>
          <p className="mt-1 font-medium">Information only, not investment advice. You make the decisions.</p>
        </footer>
      </body>
    </html>
  );
}
