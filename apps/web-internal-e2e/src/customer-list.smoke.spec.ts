import { test, expect } from '@playwright/test';

/**
 * Smoke test for the Internal Platform's CustomerListPage (Task 17/19). Mocks the CRM API at
 * the browser level via `page.route` — no backend runs in CI. Covers: the dev-only token input
 * is visible, and the customer table renders a row from the mocked paged envelope.
 */
test('loads customers page against a mocked CRM API', async ({ page }) => {
  await page.route('**/api/v1/crm/customers', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [{ id: '11111111-1111-1111-1111-111111111111', name: 'Aldo', email: 'a@x.com' }],
        totalCount: 1,
      }),
    });
  });

  await page.goto('/');

  await expect(
    page.getByLabel('Dev-only bearer token (pasted JWT, kept in memory only — never persisted)')
  ).toBeVisible();

  await expect(page.getByRole('cell', { name: 'Aldo' })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'a@x.com' })).toBeVisible();
});
