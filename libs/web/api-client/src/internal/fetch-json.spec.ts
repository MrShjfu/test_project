import { afterEach, describe, expect, it, vi } from 'vitest';
import { FetchJsonError, fetchJson } from './fetch-json';

describe('fetchJson', () => {
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

    const result = await fetchJson<{ items: unknown[]; totalCount: number }>(
      '/api/v1/crm/customers'
    );

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

  it('adds a bearer Authorization header when a token is provided', async () => {
    const fetchMock = vi.fn(
      async () => new Response(JSON.stringify({}), { status: 200 })
    );
    vi.stubGlobal('fetch', fetchMock);

    await fetchJson('/api/v1/crm/customers', { token: 'my-jwt' });

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/crm/customers',
      expect.objectContaining({
        headers: expect.objectContaining({ Authorization: 'Bearer my-jwt' }),
      })
    );
  });

  it('omits the Authorization header when no token is provided', async () => {
    const fetchMock = vi.fn(
      async () => new Response(JSON.stringify({}), { status: 200 })
    );
    vi.stubGlobal('fetch', fetchMock);

    await fetchJson('/api/v1/crm/customers');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
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

    await expect(fetchJson('/api/v1/crm/customers')).rejects.toMatchObject({
      status: 400,
      problemDetails,
    });
  });

  it('is an instance of FetchJsonError on failure', async () => {
    const fetchMock = vi.fn(
      async () => new Response('', { status: 500 })
    );
    vi.stubGlobal('fetch', fetchMock);

    await expect(fetchJson('/api/v1/crm/customers')).rejects.toBeInstanceOf(
      FetchJsonError
    );
  });
});
