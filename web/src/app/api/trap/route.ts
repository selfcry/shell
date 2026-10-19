import { NextResponse, type NextRequest } from "next/server"

/**
 * Honeypot: only invisible, non-focusable links point here (see Footer.tsx).
 * A real visitor or compliant crawler never reaches this route — anything
 * that does is a bot ignoring robots.txt and the hidden markup. The log line
 * is picked up by a fail2ban jail on the VPS to ban the IP.
 */
export async function GET(request: NextRequest) {
  const ip = request.headers.get("x-forwarded-for") ?? request.headers.get("x-real-ip") ?? "unknown"
  const ua = request.headers.get("user-agent") ?? "unknown"
  console.error(`[HONEYPOT] ip=${ip} ua=${ua}`)
  return NextResponse.json({ error: "Not found" }, { status: 404 })
}
