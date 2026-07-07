import type { paths } from './schema';

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

/** Every path in the generated schema that declares a `get` operation. */
export type GetPath = {
  [P in keyof paths]: paths[P] extends { get: unknown } ? P : never;
}[keyof paths];

/** Every path in the generated schema that declares a `post` operation. */
export type PostPath = {
  [P in keyof paths]: paths[P] extends { post: unknown } ? P : never;
}[keyof paths];

/** Extracts the operation object for a given HTTP method at a given path. */
type OperationFor<P extends keyof paths, M extends 'get' | 'post'> = paths[P] extends {
  [K in M]: infer Op;
}
  ? Op
  : never;

/** Extracts the union of `application/json` success-response bodies (2xx) for an operation. */
type JsonSuccessBody<Op> = Op extends { responses: infer R }
  ? {
      [S in keyof R]: S extends 200 | 201
        ? R[S] extends { content: { 'application/json': infer B } }
          ? B
          : never
        : never;
    }[keyof R]
  : never;

/** Response body type for a GET on `P`, inferred from the generated schema — never `any`. */
export type GetResponse<P extends GetPath> = JsonSuccessBody<OperationFor<P, 'get'>>;

/** Response body type for a POST on `P`, inferred from the generated schema — never `any`. */
export type PostResponse<P extends PostPath> = JsonSuccessBody<OperationFor<P, 'post'>>;

/** Extracts the `application/json` request body type for a POST on `P`. */
export type PostBody<P extends PostPath> = OperationFor<P, 'post'> extends {
  requestBody: { content: { 'application/json': infer B } };
}
  ? B
  : never;

/**
 * Path-param map for `P`, or `never` if the path declares no path params. openapi-typescript
 * puts real path params under the operation's own `parameters.path` (e.g. `get.parameters.path`)
 * — the path-item-level `parameters.path` is always `never` — so this checks whichever of
 * `get`/`post` is present on `P`.
 */
type PathParamsFor<P extends keyof paths> = paths[P] extends {
  get: { parameters: { path: infer Params } };
}
  ? Params extends Record<string, never>
    ? never
    : Params
  : paths[P] extends { post: { parameters: { path: infer Params } } }
    ? Params extends Record<string, never>
      ? never
      : Params
    : never;

/**
 * Substitutes `{param}` placeholders in a schema path with concrete values, typed against the
 * path's own declared `parameters.path` shape (e.g. `/api/v1/crm/customers/{id}` + `{ id }`).
 * Returns a plain string — callers pass the result straight to {@link getJson}.
 */
export function withParams<P extends keyof paths>(
  path: P,
  params: PathParamsFor<P> extends never ? never : PathParamsFor<P>
): string {
  let result = path as string;
  for (const [key, value] of Object.entries(params as Record<string, unknown>)) {
    result = result.replace(`{${key}}`, String(value));
  }
  return result;
}

/**
 * Minimal typed wrapper around `fetch` for JSON APIs (engineering-rules.md §6/§7): sets JSON
 * headers, attaches a bearer token when provided, and throws a {@link FetchJsonError} carrying
 * the HTTP status and RFC 7807 Problem Details body when the response is not ok.
 *
 * Internal implementation detail — not exported. Use {@link getJson}/{@link postJson}, which bind
 * the path to its generated response type so call sites never need a manual type annotation.
 */
async function fetchJson<T>(path: string, init: FetchJsonInit = {}): Promise<T> {
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

/**
 * Typed GET bound to the generated `paths` — the response type is inferred from `P`, never
 * annotated by the caller (ADR-009: contracts, not hand-written DTOs). `P` may carry a
 * `?query` suffix for paths with query parameters (e.g. `/api/v1/crm/customers?Page=1`); for
 * path params (e.g. `/api/v1/crm/customers/{id}`), build the concrete path with
 * {@link withParams} first.
 */
export function getJson<P extends GetPath>(
  path: P | `${P & string}?${string}`,
  init?: FetchJsonInit
): Promise<GetResponse<P>> {
  return fetchJson<GetResponse<P>>(path, init);
}

/**
 * Typed POST bound to the generated `paths` — both the request body and response type are
 * inferred from `P`, never annotated by the caller (ADR-009).
 */
export function postJson<P extends PostPath>(
  path: P,
  body: PostBody<P>,
  init?: FetchJsonInit
): Promise<PostResponse<P>> {
  return fetchJson<PostResponse<P>>(path, {
    ...init,
    method: 'POST',
    body: JSON.stringify(body),
  });
}
