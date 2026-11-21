import type { ReactNode } from "react";

export function Spinner() {
  return <svg className="loading-spinner" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" strokeWidth="3" opacity="0.25" />
    <path d="M12 3a9 9 0 0 1 9 9" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
  </svg>;
}

export default function LoadingStatus({ children, className = "" }: { children: ReactNode; className?: string }) {
  return <p role="status" className={"loading-status " + className}>
    <Spinner /><span>{children}</span>
  </p>;
}
