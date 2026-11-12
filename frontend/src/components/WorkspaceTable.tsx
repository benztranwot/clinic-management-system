import type { ReactNode } from "react";

export default function WorkspaceTable({
  label, columns, children,
}: {
  label: string;
  columns: readonly string[];
  children: ReactNode;
}) {
  return (
    <div className="queue-scroll" tabIndex={0} role="region" aria-label={label}>
      <table className="queue-table">
        <caption className="sr-only">{label}</caption>
        <thead>
          <tr>{columns.map((column) => <th key={column} scope="col">{column}</th>)}</tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}
