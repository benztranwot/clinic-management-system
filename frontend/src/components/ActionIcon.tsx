const paths = {
  back: "M19 12H5m7 7-7-7 7-7",
  forward: "M5 12h14m-7-7 7 7-7 7",
  calendar: "M4 5h16a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Zm3-2v4m10-4v4M3 10h18",
  calendarCheck: "M4 5h16a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Zm3-2v4m10-4v4M3 10h18m-12 6 2 2 4-4",
  calendarClock: "M12 21H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h16a1 1 0 0 1 1 1v5M7 3v4m10-4v4M3 10h18m1 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0Zm-4-2v2l1 1",
  calendarX: "M4 5h16a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1Zm3-2v4m10-4v4M3 10h18m-11 4 4 4m0-4-4 4",
  check: "m5 12 4 4L19 6",
  close: "m6 6 12 12M18 6 6 18",
  eye: "M2 12s3.5-6 10-6 10 6 10 6-3.5 6-10 6-10-6-10-6Zm13 0a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z",
  filter: "M3 4h18l-7 8v6l-4 2v-8L3 4Z",
  home: "m3 10 9-7 9 7M5 9v11h14V9m-9 11v-6h4v6",
  list: "M8 6h12M8 12h12M8 18h12M4 6h.01M4 12h.01M4 18h.01",
  login: "M14 4h5a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-5M3 12h12m-4-4 4 4-4 4",
  minus: "M5 12h14",
  pencil: "m4 16-1 5 5-1L20 8l-4-4L4 16Zm10-10 4 4",
  people: "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2m20 0v-2a4 4 0 0 0-3-4m-6-7a4 4 0 1 1-8 0 4 4 0 0 1 8 0Zm3-4a4 4 0 0 1 0 8",
  plus: "M12 5v14M5 12h14",
  refresh: "M20 7v5h-5M4 17v-5h5m-5 0a8 8 0 0 1 13-6l3 3m0 3a8 8 0 0 1-13 6l-3-3",
  reset: "M4 4v6h6m-6 0a8 8 0 1 1 1 8",
  save: "M4 3h13l4 4v13a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Zm3 0v6h9V3M7 21v-7h10v7",
  trash: "M3 6h18M9 6V3h6v3m4 0-1 14H6L5 6m5 4v6m4-6v6",
} as const;

export function ActionIcon({ name }: { name: keyof typeof paths }) {
  return (
    <svg
      aria-hidden="true"
      focusable="false"
      className="action-icon"
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d={paths[name]} />
    </svg>
  );
}
