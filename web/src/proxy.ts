import { NextResponse, type NextRequest } from "next/server";

const apiUrl = (process.env.API_URL ?? "http://localhost:5214").replace(/\/+$/, "");

// Browser calls to /api/* are forwarded to the API, so the browser only ever talks to this site
// and the refresh cookie stays first-party (vercel.app and onrender.com are different "sites").
export function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const target = new URL(`${apiUrl}${pathname.replace(/^\/api/, "") || "/"}${search}`);

  const headers = new Headers(request.headers);
  headers.delete("host"); // the API's host, not ours, must be used upstream

  // Never pass on trust headers a client made up itself.
  for (const name of [...headers.keys()]) {
    if (name.startsWith("x-radar-")) {
      headers.delete(name);
    }
  }

  // Prove to the API that this request really comes from our proxy, and tell it who the client is.
  const secret = process.env.WEB_PROXY_SECRET;
  if (secret) {
    headers.set("x-radar-proxy-secret", secret);
    headers.set("x-radar-client-ip", clientIp(request));
    headers.set("x-radar-host", request.nextUrl.host);
    headers.set("x-radar-proto", request.nextUrl.protocol.replace(":", ""));
    headers.set("x-radar-path-base", "/api");
  }

  return NextResponse.rewrite(target, { request: { headers } });
}

// On Vercel, x-real-ip / x-forwarded-for are set by the platform (a client can't fake them there).
function clientIp(request: NextRequest): string {
  return (
    request.headers.get("x-real-ip") ??
    request.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ??
    "127.0.0.1"
  );
}

export const config = {
  matcher: "/api/:path*",
};
