import { CustomerListPage } from 'feature-crm';

/**
 * Internal Platform shell (Task 17): a single route, `/` → CustomerListPage, imports the feature
 * only via the `feature-crm` lib alias (ADR-008 — apps compose feature libs, never own business
 * logic). Additional routes/router wiring land alongside the next feature lib.
 */
export function App() {
  return <CustomerListPage />;
}

export default App;
