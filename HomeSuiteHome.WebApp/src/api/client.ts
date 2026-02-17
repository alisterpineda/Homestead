type RequestConfig<TData = unknown> = {
  url: string;
  method: "GET" | "POST" | "PUT" | "DELETE" | "PATCH";
  params?: Record<string, string | number | boolean | undefined>;
  headers?: HeadersInit;
  data?: TData;
  signal?: AbortSignal;
};

export async function customFetch<TResult>(config: RequestConfig): Promise<TResult> {
  const url = new URL(config.url, window.location.origin);

  if (config.params) {
    for (const [key, value] of Object.entries(config.params)) {
      if (value !== undefined) {
        url.searchParams.set(key, String(value));
      }
    }
  }

  const headers = new Headers(config.headers);

  let body: BodyInit | undefined;
  if (config.data instanceof FormData) {
    body = config.data;
  } else if (config.data !== undefined) {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(config.data);
  }

  const response = await fetch(url.toString(), {
    method: config.method,
    headers,
    body,
    signal: config.signal,
  });

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}: ${response.statusText}`);
  }

  const contentType = response.headers.get("content-type");
  if (contentType?.includes("application/json")) {
    return (await response.json()) as TResult;
  }

  return undefined as TResult;
}

export default customFetch;
