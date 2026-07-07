import { render, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { fetchJson } from 'api-client';
import App from './app';

vi.mock('api-client', () => ({
  fetchJson: vi.fn(),
}));

const fetchJsonMock = vi.mocked(fetchJson);

describe('App', () => {
  beforeEach(() => {
    fetchJsonMock.mockReset();
    fetchJsonMock.mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('renders the CustomerListPage at the root route', async () => {
    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Customers' })).toBeInTheDocument();
  });
});
