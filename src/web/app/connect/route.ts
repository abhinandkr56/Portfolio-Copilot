import { connection } from "next/server";

// "Connect Zerodha" → the Api's /kite/login, so the login round trip (and its state cookie) stays on the Api's origin.
export async function GET() {
  await connection(); // read API_PUBLIC_URL at request time, never at build time
  const api = process.env.API_PUBLIC_URL ?? "http://127.0.0.1:5080";
  return Response.redirect(`${api}/kite/login`, 302);
}
