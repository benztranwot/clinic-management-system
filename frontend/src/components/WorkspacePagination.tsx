import { ActionIcon } from "./ActionIcon";

export default function WorkspacePagination({ page, pageSize, itemCount, count, hasMore, busy, onPage, label }: {
  page: number; pageSize: number; itemCount: number; count?: number; hasMore: boolean; busy: boolean;
  onPage: (page: number) => void; label: string;
}) {
  const pages = count === undefined ? undefined : Math.max(1, Math.ceil(count / pageSize));
  return (
    <nav className="workspace-pagination" aria-label={label} data-full-page={itemCount >= pageSize}>
      <span role="status" aria-live="polite">
        {pages !== undefined && page > pages ? `No results on page ${page}` : `Page ${page}${pages !== undefined ? ` of ${pages}` : ""}`} {count !== undefined && ` · ${count} results`}
      </span>
      <div className="flex flex-wrap gap-2">
        {page > 2 && <button type="button" data-variant="secondary" disabled={busy} onClick={() => onPage(1)}>First page</button>}
        <button type="button" data-variant="secondary" disabled={page === 1 || busy} onClick={() => onPage(page - 1)}>
          <ActionIcon name="back" />Previous
        </button>
        <button type="button" data-variant="secondary" disabled={!hasMore || busy} onClick={() => onPage(page + 1)}>
          Next<ActionIcon name="forward" />
        </button>
      </div>
    </nav>
  );
}
