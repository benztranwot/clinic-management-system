import { useEffect, useState, type ReactNode } from "react";
import { NavLink } from "react-router";
import { appointmentLists } from "./appointments/appointmentLists";
import { useAppointmentAttention } from "./appointments/useAppointmentAttention";
import type { ReferralAccount } from "../api/referrals";

type Theme = "system" | "light" | "dark";
const themeKey = "clinic-management-theme";

function ThemeSelect() {
  const [theme, setTheme] = useState<Theme>(() => {
    try {
      const saved = window.localStorage.getItem(themeKey);
      return saved === "light" || saved === "dark" ? saved : "system";
    } catch {
      return "system";
    }
  });

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    try {
      window.localStorage.setItem(themeKey, theme);
    } catch {
      // Keep the current choice for this tab if the browser blocks storage.
    }
  }, [theme]);

  return (
    <label className="theme-control">
      <span>Theme</span>
      <select value={theme} onChange={(event) => setTheme(event.target.value as Theme)}>
        <option value="system">System</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
      </select>
    </label>
  );
}

function SchedulingLinks({ userId, account }: { userId: string; account?: ReferralAccount }) {
  const isClinician = account?.memberships.some(({ role }) => role === "Clinician");
  const canSchedule = account?.isAdministrator || account?.memberships.some(({ role }) => role === "FrontDesk");
  const attention = useAppointmentAttention(canSchedule ? userId : undefined);

  return (
    <>
      {account?.providerPublicId && isClinician && <NavLink to="/my-schedule">My schedule</NavLink>}
      {canSchedule && (
        <>
          <NavLink to={appointmentLists.upcoming.path}>{appointmentLists.upcoming.title}</NavLink>
          <NavLink to={appointmentLists.conflicts.path} className="workspace-nav-count">
            <span>{appointmentLists.conflicts.title}</span>
            {attention.isSuccess && (
              <>
                <span aria-hidden="true" className="rounded-full bg-[var(--muted)] px-2 text-xs tabular-nums">
                  {attention.data.length}
                </span>
                <span className="sr-only">{attention.data.length} appointments with availability conflicts</span>
              </>
            )}
          </NavLink>
        </>
      )}
    </>
  );
}

type AppShellProps = {
  userId?: string;
  accountName?: string;
  account?: ReferralAccount;
  showStaffNavigation: boolean;
  signingOut?: boolean;
  onSignOut: () => void;
  children: ReactNode;
};

export default function AppShell({
  accountName, account, showStaffNavigation, userId, signingOut, onSignOut, children,
}: AppShellProps) {
  return (
    <div className={accountName ? "app workspace" : "app guest"}>
      <a href="#main-content" className="skip-link">Skip to content</a>
      <header className="workspace-header">
        <div className="workspace-brand">
          <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <path d="M12 5v14M5 12h14" />
          </svg>
          Clinic Management
        </div>
        <div className="workspace-tools">
          <ThemeSelect />
          {accountName && (
            <>
              <span className="account-name">{accountName}</span>
              <button type="button" data-variant="secondary" onClick={onSignOut} disabled={signingOut}>
                {signingOut ? "Signing out…" : "Sign out"}
              </button>
            </>
          )}
        </div>
      </header>
      {accountName && (
        <nav aria-label="Main navigation" className="workspace-nav">
          {showStaffNavigation && (
            <>
              <NavLink to="/referrals">Referrals</NavLink>
              <NavLink to="/clinics">Clinics</NavLink>
              <NavLink to="/patients">Patients</NavLink>
              {userId && <SchedulingLinks userId={userId} account={account} />}
            </>
          )}
          {account?.patientPublicId && (
            <>
              <NavLink to="/my-referrals">My referrals</NavLink>
              <NavLink to="/my-appointments">My appointments</NavLink>
            </>
          )}
        </nav>
      )}
      <main id="main-content" tabIndex={-1} className="workspace-main">{children}</main>
    </div>
  );
}
