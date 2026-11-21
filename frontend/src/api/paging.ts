export type PageResult<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  hasMore: boolean;
};

export async function searchPage<T>(
  path: string, token: string, search: object, signal: AbortSignal,
  error: (status: number) => Error,
): Promise<PageResult<T>> {
  const response = await fetch(path, {
    method: "POST", headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
    body: JSON.stringify(search), signal, cache: "no-store",
  });
  if (!response.ok) throw error(response.status);
  return response.json();
}
