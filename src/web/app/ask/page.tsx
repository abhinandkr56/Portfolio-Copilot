import { ChatPanel } from "@/components/ChatPanel";

export default function AskPage() {
  return (
    <div className="space-y-3">
      <h1 className="text-2xl font-semibold">Ask</h1>
      <p className="text-sm text-zinc-600 dark:text-zinc-400">
        Questions are answered from your own holdings and plan. Every number is checked against your data; the assistant
        doesn&apos;t give buy or sell advice.
      </p>
      <ChatPanel />
    </div>
  );
}
