export function readPage(value: string | null, pageSize = 10) {
  const page = Number(value);
  return Number.isInteger(page) && page >= 1 && page <= Math.floor(2147483647 / pageSize) ? page : 1;
}
