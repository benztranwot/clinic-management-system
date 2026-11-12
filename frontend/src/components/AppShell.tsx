import { useEffect, useRef, useState, type ReactNode } from "react";
import { Link, NavLink } from "react-router";
import { appointmentLists } from "./appointments/appointmentLists";
import { useAppointmentAttention } from "./appointments/useAppointmentAttention";
import type { ReferralAccount } from "../api/referrals";

type Theme = "system" | "light" | "dark";
const themeKey = "clinic-management-theme";

function HeaderDisclosure({ label, trigger, children, closeOnSelect = false }: {
  label: string;
  trigger: ReactNode;
  children: ReactNode;
  closeOnSelect?: boolean;
}) {
  const disclosure = useRef<HTMLDetailsElement>(null);
  const summary = useRef<HTMLElement>(null);

  function close() {
    if (disclosure.current) disclosure.current.open = false;
    summary.current?.focus();
  }

  useEffect(() => {
    function dismissOutside(event: PointerEvent) {
      const menu = disclosure.current;
      if (menu?.open && event.target instanceof Node && !menu.contains(event.target)) {
        menu.open = false;
      }
    }

    document.addEventListener("pointerdown", dismissOutside);
    return () => document.removeEventListener("pointerdown", dismissOutside);
  }, []);

  return (
    <details className="header-disclosure" ref={disclosure} onKeyDown={(event) => {
      if (event.key === "Escape" && disclosure.current?.open) {
        event.preventDefault();
        close();
      }
    }}>
      <summary className="header-trigger" aria-label={label} ref={summary}>{trigger}</summary>
      <div className="header-dropdown" onClick={(event) => {
        if (closeOnSelect && event.target instanceof Element && event.target.closest("button")) close();
      }}>
        {children}
      </div>
    </details>
  );
}

const themes = ["system", "light", "dark"] as const;
const themeLabels = { system: "System", light: "Light", dark: "Dark" };

function ThemeIcon({ theme }: { theme: Theme }) {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" width="18" height="18" fill="none"
      stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      {theme === "system" && <path d="M4 5h16v11H4zM8 20h8m-4-4v4" />}
      {theme === "light" && <>
        <circle cx="12" cy="12" r="4" />
        <path d="M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1.5 1.5m11 11L19 19M5 19l1.5-1.5m11-11L19 5" />
      </>}
      {theme === "dark" && <path d="M20.9 13.4A9 9 0 0 1 10.6 3.1 9 9 0 1 0 20.9 13.4Z" />}
    </svg>
  );
}

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
    <HeaderDisclosure label={"Theme: " + themeLabels[theme]} trigger={<ThemeIcon theme={theme} />} closeOnSelect>
      {themes.map((option) => (
        <button type="button" key={option} aria-pressed={theme === option} onClick={() => setTheme(option)}>
          <ThemeIcon theme={option} />
          {themeLabels[option]}
          {theme === option && <span className="theme-selected" aria-hidden="true">✓</span>}
        </button>
      ))}
    </HeaderDisclosure>
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
  homePath: string;
  accountEmail?: string;
  account?: ReferralAccount;
  showStaffNavigation: boolean;
  signingOut?: boolean;
  onSignOut: () => void;
  children: ReactNode;
};

export default function AppShell({
  accountName, accountEmail, homePath, account, showStaffNavigation, userId, signingOut, onSignOut, children,
}: AppShellProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const hasNavigation = Boolean(accountName && (showStaffNavigation || account?.patientPublicId));

  return (
    <div className={accountName ? "app workspace" : "app guest"}>
      <a href="#main-content" className="skip-link">Skip to content</a>
      <header className="workspace-header">
        <Link to={homePath} className="workspace-brand" onClick={() => setMenuOpen(false)}>
          <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
            <path d="M12 5v14M5 12h14" />
          </svg>
          Clinic Management
        </Link>
        {hasNavigation && (
          <button type="button" className="workspace-menu-toggle" data-variant="secondary"
            aria-controls="workspace-navigation" aria-expanded={menuOpen}
            onClick={() => setMenuOpen((open) => !open)}>Menu</button>
        )}
        {hasNavigation && (
          <nav id="workspace-navigation" aria-label="Main navigation" className="workspace-nav"
            data-expanded={menuOpen} onClick={(event) => {
              if (event.target instanceof Element && event.target.closest("a")) setMenuOpen(false);
            }}>
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
        <div className="workspace-tools">
          <ThemeSelect />
          {accountName && (
            <HeaderDisclosure label={"Account: " + accountName} trigger={
              <span aria-hidden="true" className="account-avatar">
                {accountName.split(/\s+/).slice(0, 2).map((word) => Array.from(word)[0]).join("").toUpperCase()}
              </span>
            }>
              <div className="account-info">
                <p className="font-semibold">{accountName}</p>
                {accountEmail && accountEmail !== accountName && <p className="queue-meta">{accountEmail}</p>}
              </div>
              <button type="button" onClick={onSignOut} disabled={signingOut}>
                {signingOut ? "Signing out…" : "Sign out"}
              </button>
            </HeaderDisclosure>
          )}
        </div>
      </header>
      <main id="main-content" tabIndex={-1} className="workspace-main">{children}</main>
    </div>
  );
}
