import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getJson, postJson } from 'api-client';
import { CustomerListPage } from './customer-list-page';

vi.mock('api-client', () => ({
  getJson: vi.fn(),
  postJson: vi.fn(),
}));

const getJsonMock = vi.mocked(getJson);
const postJsonMock = vi.mocked(postJson);

describe('CustomerListPage', () => {
  beforeEach(() => {
    getJsonMock.mockReset();
    postJsonMock.mockReset();
  });

  it('renders rows from the {items, totalCount} envelope', async () => {
    getJsonMock.mockResolvedValueOnce({
      items: [{ id: '1', name: 'Ada Lovelace', email: 'ada@example.com' }],
      totalCount: 1,
    });

    render(<CustomerListPage />);

    expect(await screen.findByText('Ada Lovelace')).toBeInTheDocument();
    expect(screen.getByText('ada@example.com')).toBeInTheDocument();
    expect(screen.getByText(/1 total/i)).toBeInTheDocument();
    expect(getJsonMock).toHaveBeenCalledWith(
      expect.stringContaining('/api/v1/crm/customers'),
      expect.anything()
    );
  });

  it('creates a customer via POST and refreshes the list', async () => {
    getJsonMock
      // initial list load
      .mockResolvedValueOnce({ items: [], totalCount: 0 })
      // refresh list after create
      .mockResolvedValueOnce({
        items: [{ id: '2', name: 'Grace Hopper', email: 'grace@example.com' }],
        totalCount: 1,
      });
    postJsonMock.mockResolvedValueOnce({
      id: '2',
      name: 'Grace Hopper',
      email: 'grace@example.com',
    });

    render(<CustomerListPage />);
    await waitFor(() => expect(getJsonMock).toHaveBeenCalledTimes(1));

    const form = screen.getByRole('form', { name: /create customer/i });
    await userEvent.type(within(form).getByLabelText(/name/i), 'Grace Hopper');
    await userEvent.type(within(form).getByLabelText(/email/i), 'grace@example.com');
    await userEvent.click(within(form).getByRole('button', { name: /create/i }));

    await waitFor(() =>
      expect(postJsonMock).toHaveBeenCalledWith(
        expect.stringContaining('/api/v1/crm/customers'),
        { name: 'Grace Hopper', email: 'grace@example.com' },
        expect.anything()
      )
    );

    expect(await screen.findByText('Grace Hopper')).toBeInTheDocument();
  });
});
