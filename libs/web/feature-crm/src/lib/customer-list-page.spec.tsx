import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fetchJson } from 'api-client';
import { CustomerListPage } from './customer-list-page';

vi.mock('api-client', () => ({
  fetchJson: vi.fn(),
}));

const fetchJsonMock = vi.mocked(fetchJson);

describe('CustomerListPage', () => {
  beforeEach(() => {
    fetchJsonMock.mockReset();
  });

  it('renders rows from the {items, totalCount} envelope', async () => {
    fetchJsonMock.mockResolvedValueOnce({
      items: [{ id: '1', name: 'Ada Lovelace', email: 'ada@example.com' }],
      totalCount: 1,
    });

    render(<CustomerListPage />);

    expect(await screen.findByText('Ada Lovelace')).toBeInTheDocument();
    expect(screen.getByText('ada@example.com')).toBeInTheDocument();
    expect(screen.getByText(/1 total/i)).toBeInTheDocument();
    expect(fetchJsonMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/crm/customers'),
      expect.anything()
    );
  });

  it('creates a customer via POST and refreshes the list', async () => {
    fetchJsonMock
      // initial list load
      .mockResolvedValueOnce({ items: [], totalCount: 0 })
      // POST create
      .mockResolvedValueOnce({ id: '2', name: 'Grace Hopper', email: 'grace@example.com' })
      // refresh list after create
      .mockResolvedValueOnce({
        items: [{ id: '2', name: 'Grace Hopper', email: 'grace@example.com' }],
        totalCount: 1,
      });

    render(<CustomerListPage />);
    await waitFor(() => expect(fetchJsonMock).toHaveBeenCalledTimes(1));

    const form = screen.getByRole('form', { name: /create customer/i });
    await userEvent.type(within(form).getByLabelText(/name/i), 'Grace Hopper');
    await userEvent.type(within(form).getByLabelText(/email/i), 'grace@example.com');
    await userEvent.click(within(form).getByRole('button', { name: /create/i }));

    await waitFor(() =>
      expect(fetchJsonMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/crm/customers'),
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ name: 'Grace Hopper', email: 'grace@example.com' }),
        })
      )
    );

    expect(await screen.findByText('Grace Hopper')).toBeInTheDocument();
  });
});
