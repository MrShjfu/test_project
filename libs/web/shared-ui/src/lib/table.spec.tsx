import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Table } from './table';

interface Row {
  id: string;
  name: string;
  email: string;
}

const rows: Row[] = [
  { id: '1', name: 'Ada Lovelace', email: 'ada@example.com' },
  { id: '2', name: 'Grace Hopper', email: 'grace@example.com' },
];

describe('Table', () => {
  it('renders a header cell per column', () => {
    render(
      <Table
        rows={rows}
        getRowKey={(row) => row.id}
        columns={[
          { header: 'Name', render: (row) => row.name },
          { header: 'Email', render: (row) => row.email },
        ]}
      />
    );

    expect(screen.getByRole('columnheader', { name: 'Name' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Email' })).toBeInTheDocument();
  });

  it('renders one row per item with cells from each column', () => {
    render(
      <Table
        rows={rows}
        getRowKey={(row) => row.id}
        columns={[
          { header: 'Name', render: (row) => row.name },
          { header: 'Email', render: (row) => row.email },
        ]}
      />
    );

    expect(screen.getByText('Ada Lovelace')).toBeInTheDocument();
    expect(screen.getByText('grace@example.com')).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(rows.length + 1); // + header row
  });

  it('renders an empty state message when there are no rows', () => {
    render(
      <Table
        rows={[]}
        getRowKey={(row: Row) => row.id}
        columns={[{ header: 'Name', render: (row: Row) => row.name }]}
        emptyMessage="No customers yet"
      />
    );

    expect(screen.getByText('No customers yet')).toBeInTheDocument();
  });
});
