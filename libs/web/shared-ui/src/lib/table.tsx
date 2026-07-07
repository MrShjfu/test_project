import type { ReactNode } from 'react';

export interface TableColumn<T> {
  header: string;
  render: (row: T) => ReactNode;
}

export interface TableProps<T> {
  rows: readonly T[];
  columns: readonly TableColumn<T>[];
  getRowKey: (row: T) => string;
  emptyMessage?: string;
}

/** Minimal shared table used across `libs/web/feature-*` (ADR-008: shared UI lives here). */
export function Table<T>({ rows, columns, getRowKey, emptyMessage = 'No data.' }: TableProps<T>) {
  if (rows.length === 0) {
    return <p>{emptyMessage}</p>;
  }

  return (
    <table>
      <thead>
        <tr>
          {columns.map((column) => (
            <th key={column.header}>{column.header}</th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={getRowKey(row)}>
            {columns.map((column) => (
              <td key={column.header}>{column.render(row)}</td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export default Table;
