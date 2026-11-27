import { useLocation, useSearchParams } from "react-router";

import { readPage } from "../utils/readPage";

// Keep directory search text (including patient names) out of URLs and referrer headers.
export function useListSearch<T extends Record<string, string>>(key: string, userId: string, empty: T) {
  const [params, setParams] = useSearchParams();
  const { state } = useLocation();
  const saved = state?.listOwner === userId && state?.listKey === key ? state?.listFilters : null;
  const filters = Object.fromEntries(Object.keys(empty).map(name => [name,
    typeof saved?.[name] === "string" ? saved[name].slice(0, 200) : empty[name],
  ])) as T;
  const page = readPage(params.get("page"), 16);
  function update(nextFilters: T, nextPage: number) {
    const next = new URLSearchParams(params);
    if (nextPage === 1) next.delete("page"); else next.set("page", String(nextPage));
    setParams(next, { state: { ...state, listOwner: userId, listKey: key, listFilters: nextFilters } });
  }
  return { filters, page, setFilters: (filters: T) => update(filters, 1), goToPage: (page: number) => update(filters, page) };
}
