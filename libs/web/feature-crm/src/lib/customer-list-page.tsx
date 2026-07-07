import { useCallback, useEffect, useState } from 'react';
import { Button, Table } from 'shared-ui';
import { getJson, postJson, type components } from 'api-client';

type CustomerDto = components['schemas']['CustomerDto'];

const CUSTOMERS_PATH = '/api/v1/crm/customers';

export interface CustomerListPageProps {
  /** Dev-only bearer token, forwarded to every request. Defaults to the page's own token input. */
  token?: string;
}

/**
 * Internal Platform CRM customer list + create form (Task 17). Lists via GET and creates via
 * POST against the CRM module's API, typed entirely from the generated `api-client` schema —
 * no hand-written DTOs (ADR-009, engineering-rules.md §7).
 */
export function CustomerListPage({ token: tokenProp }: CustomerListPageProps = {}) {
  const [token, setToken] = useState(tokenProp ?? '');
  const [customers, setCustomers] = useState<CustomerDto[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const effectiveToken = tokenProp ?? token;

  const loadCustomers = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const page = await getJson(CUSTOMERS_PATH, {
        token: effectiveToken || undefined,
      });
      setCustomers(page.items);
      // openapi-typescript widens int32 fields to `number | string` (JSON Schema format hint,
      // not a runtime guarantee); the API always sends a JSON number, but we normalize
      // defensively rather than assert with `as number`.
      setTotalCount(Number(page.totalCount));
    } catch {
      setError('Failed to load customers.');
    } finally {
      setLoading(false);
    }
  }, [effectiveToken]);

  useEffect(() => {
    loadCustomers();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleCreate = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setError(null);
    try {
      await postJson(
        CUSTOMERS_PATH,
        { name, email },
        { token: effectiveToken || undefined }
      );
      setName('');
      setEmail('');
      await loadCustomers();
    } catch {
      setError('Failed to create customer.');
    }
  };

  return (
    <div>
      <h1>Customers</h1>

      {tokenProp === undefined && (
        <div>
          <label htmlFor="dev-token">
            Dev-only bearer token (pasted JWT, kept in memory only — never persisted)
          </label>
          <input
            id="dev-token"
            type="password"
            value={token}
            onChange={(e) => setToken(e.target.value)}
            autoComplete="off"
          />
        </div>
      )}

      {error && <p role="alert">{error}</p>}

      <form aria-label="Create customer" onSubmit={handleCreate}>
        <label htmlFor="customer-name">Name</label>
        <input
          id="customer-name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
        />

        <label htmlFor="customer-email">Email</label>
        <input
          id="customer-email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />

        <Button type="submit">Create</Button>
      </form>

      {loading ? (
        <p>Loading…</p>
      ) : (
        <>
          <Table
            rows={customers}
            getRowKey={(row) => row.id}
            emptyMessage="No customers yet."
            columns={[
              { header: 'Name', render: (row) => row.name },
              { header: 'Email', render: (row) => row.email },
            ]}
          />
          <p>{totalCount} total</p>
        </>
      )}
    </div>
  );
}

export default CustomerListPage;
