/**
 * Factory Kiosk shell (Task 18): brand placeholder only, no routing yet — the
 * Factory Kiosk BFF and its feature libs land in a later task (ADR-008 — apps
 * compose feature libs, never own business logic). This app also carries the
 * offline PWA scaffold (see vite.config.mts) so it can be installed and load
 * from the precached app shell; offline data sync is explicitly out of scope.
 */
export function App() {
  return (
    <div>
      <h1>Helm Factory Kiosk</h1>
    </div>
  );
}

export default App;
