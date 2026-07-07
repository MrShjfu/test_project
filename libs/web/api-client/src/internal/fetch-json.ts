/** RFC 7807 Problem Details shape, per engineering-rules.md §6 (errors always carry `traceId`). */
export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  traceId?: string;
  [key: string]: unknown;
}

/** Thrown by {@link fetchJson} when the response status is not ok (2xx). */
export class FetchJsonError extends Error {
  readonly status: number;
  readonly problemDetails: ProblemDetails | undefined;

  constructor(status: number, problemDetails: ProblemDetails | undefined) {
    super(problemDetails?.detail ?? problemDetails?.title ?? `Request failed with status ${status}`);
    this.name = 'FetchJsonError';
    this.status = status;
    this.problemDetails = problemDetails;
  }
}

export interface FetchJsonInit extends RequestInit {
  /** Dev-only bearer token; see apps/web-internal's auth stub. */
  token?: string;
}

/**
 * Minimal typed wrapper around `fetch` for JSON APIs (engineering-rules.md §6/§7): sets JSON
 * headers, attaches a bearer token when provided, and throws a {@link FetchJsonError} carrying
 * the HTTP status and RFC 7807 Problem Details body when the response is not ok.
 */
export async function fetchJson<T>(path: string, init: FetchJsonInit = {}): Promise<T> {
  const { token, headers, ...rest } = init;

  const requestHeaders: Record<string, string> = {
    'Content-Type': 'application/json',
    Accept: 'application/json',
    ...(headers as Record<string, string> | undefined),
  };
  if (token) {
    requestHeaders['Authorization'] = `Bearer ${token}`;
  }

  const response = await fetch(path, { ...rest, headers: requestHeaders });

  if (!response.ok) {
    const problemDetails = await safeParseJson<ProblemDetails>(response);
    throw new FetchJsonError(response.status, problemDetails);
  }

  return safeParseJson<T>(response) as Promise<T>;
}

async function safeParseJson<T>(response: Response): Promise<T | undefined> {
  const text = await response.text();
  if (!text) {
    return undefined;
  }
  return JSON.parse(text) as T;
}
