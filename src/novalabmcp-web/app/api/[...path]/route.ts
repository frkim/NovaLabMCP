type RouteContext = {
  params: Promise<{ path: string[] }>;
};

async function forward(request: Request, context: RouteContext) {
  const target = process.env.API_PROXY_TARGET;
  if (!target) {
    return Response.json(
      { title: "The API proxy is not configured." },
      { status: 503 },
    );
  }

  const { path } = await context.params;
  const incoming = new URL(request.url);
  const destination = new URL(
    `${target.replace(/\/+$/, "")}/api/${path.map(encodeURIComponent).join("/")}`,
  );
  destination.search = incoming.search;

  const headers = new Headers(request.headers);
  for (const header of [
    "connection",
    "content-length",
    "host",
    "transfer-encoding",
  ]) {
    headers.delete(header);
  }

  const body =
    request.method === "GET" || request.method === "HEAD"
      ? undefined
      : await request.arrayBuffer();
  const response = await fetch(destination, {
    method: request.method,
    headers,
    body,
    cache: "no-store",
    redirect: "manual",
  });
  const responseHeaders = new Headers();
  for (const header of [
    "cache-control",
    "content-type",
    "location",
    "retry-after",
    "www-authenticate",
  ]) {
    const value = response.headers.get(header);
    if (value) responseHeaders.set(header, value);
  }

  return new Response(response.body, {
    status: response.status,
    headers: responseHeaders,
  });
}

export const GET = forward;
export const POST = forward;
export const PATCH = forward;
export const DELETE = forward;
