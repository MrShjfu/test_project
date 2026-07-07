import { afterEach, describe, expect, it, vi } from 'vitest';
import { FetchJsonError, getJson, postJson, withParams } from './fetch-json';

describe('getJson', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends JSON headers and returns the parsed body on success', async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(JSON.stringify({ items: [], totalCount: 0 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
    );
    vi.stubGlobal('fetch', fetchMock);

    // No manual type annotation: `result` is inferred as PagedResultOfCustomerDto from the path.
    const result = await getJson('/api/v1/crm/customers');

    expect(result).toEqual({ items: [], totalCount: 0 });
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/crm/customers',
      expect.objectContaining({
        headers: expect.objectContaining({
          'Content-Type': 'application/json',
          Accept: 'application/json',
        }),
      })
    );
  });

  it('accepts a query-string suffix on a valid path', async () => {
    const fetchMock = vi.fn(
      async () => new Response(JSON.stringify({ items: [], totalCount: 0 }), { status: 200 })
    );
    vi.stubGlobal('fetch', fetchMock);

    const result = await getJson('/api/v1/crm/customers?Page=1&PageSize=20');

    expect(result.items).toEqual([]);
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/crm/customers?Page=1&PageSize=20',
      expect.anything()
    );
  });

  it('resolves a path param via withParams and returns the by-id response', async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(JSON.stringify({ id: '1', name: 'Ada Lovelace', email: 'ada@example.com' }), {
          status: 200,
        })
    );
    vi.stubGlobal('fetch', fetchMock);

    const path = withParams('/api/v1/crm/customers/{id}', { id: '1' });
    const result = await getJson(path as '/api/v1/crm/customers/{id}');

    expect(result.name).toBe('Ada Lovelace');
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/crm/customers/1', expect.anything());
  });

  it('adds a bearer Authorization header when a token is provided', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({}), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);

    await getJson('/api/v1/crm/customers', { token: 'my-jwt' });

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/crm/customers',
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: 'Bearer my-jwt' }),
      })
    );
  });

  it('omits the Authorization header when no token is provided', async () => {
    const fetchMock = vi.fn<(input: string, init: RequestInit) => Promise<Response>>(
      async () => new Response(JSON.stringify({}), { status: 200 })
    );
    vi.stubGlobal('fetch', fetchMock);

    await getJson('/api/v1/crm/customers');

    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers).not.toHaveProperty('Authorization');
  });

  it('throws a FetchJsonError with status and problem-details body on !ok', async () => {
    const problemDetails = {
      type: 'about:blank',
      title: 'Bad Request',
      status: 400,
      detail: 'Name is required.',
    };
    const fetchMock = vi.fn(
      async () =>
        new Response(JSON.stringify(problemDetails), {
          status: 400,
          headers: { 'Content-Type': 'application/problem+json' },
        })
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(getJson('/api/v1/crm/customers')).rejects.toMatchObject({
      status: 400,
      problemDetails,
    });
  });

  it('is an instance of FetchJsonError on failure', async () => {
    const fetchMock = vi.fn(async () => new Response('', { status: 500 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(getJson('/api/v1/crm/customers')).rejects.toBeInstanceOf(FetchJsonError);
  });
});

describe('postJson', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('POSTs a JSON-serialized body and returns the parsed created resource', async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(JSON.stringify({ id: '2', name: 'Grace Hopper', email: 'grace@example.com' }), {
          status: 201,
        })
    );
    vi.stubGlobal('fetch', fetchMock);

    // No manual type annotation: `result` is inferred as CustomerDto from the path.
    const result = await postJson('/api/v1/crm/customers', {
      name: 'Grace Hopper',
      email: 'grace@example.com',
    });

    expect(result.id).toBe('2');
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/crm/customers',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ name: 'Grace Hopper', email: 'grace@example.com' }),
      })
    );
  });
});

// Compile-time-only regression checks for the path-typed `getJson`/`postJson` overloads. These
// are type-level assertions, not runtime tests: the calls below are never invoked (see the `if
// (false)` guard) — only type-checked by `tsc` (this project's `typecheck` target runs
// `tsc -p tsconfig.spec.json`, which includes this file). Vitest transpiles but does not
// type-check, so a real call here would hit the network/undici with a bogus URL at test time;
// the guard keeps these assertions type-only while still surfacing under `tsc --noEmit`.
describe('fetch-json compile-time typing (regression)', () => {
  it('has no runtime assertions — see the type-only block below this describe', () => {
    expect(true).toBe(true);
  });
});

// eslint-disable-next-line @typescript-eslint/no-unused-vars
function typeOnlyRegressionChecks() {
  if (false as boolean) {
    // @ts-expect-error — "/api/v1/crm/not-a-real-path" is not a key of `paths`.
    void getJson('/api/v1/crm/not-a-real-path');

    const result = getJsonResultForTypeCheckOnly();
    // @ts-expect-error — PagedResultOfCustomerDto has no `notARealField`; response type is
    // inferred from the path, not `any`, so this is caught at compile time.
    void result.notARealField;

    // @ts-expect-error — CreateCustomerRequest requires `name`/`email`, not `foo`.
    void postJson('/api/v1/crm/customers', { foo: 'bar' });

    // @ts-expect-error — "/api/v1/crm/customers/{id}" has no `post`, so it is not a PostPath.
    void postJson('/api/v1/crm/customers/{id}', { name: 'x', email: 'y' });
  }
}

/** Type-only helper: gives `typeOnlyRegressionChecks` a same-shape value without calling `fetch`. */
declare function getJsonResultForTypeCheckOnly(): Awaited<ReturnType<typeof getJson<'/api/v1/crm/customers'>>>;
