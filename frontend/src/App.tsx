import { useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { Link, Navigate, Route, Routes } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import ClinicsPage from "./pages/ClinicsPage";
import ProvidersPage from "./pages/ProvidersPage";
import ServicesPage from "./pages/ServicesPage";
import PatientsPage from "./pages/PatientsPage";
import PatientDetailsPage from "./pages/PatientDetailsPage";
import ReferralsPage from "./pages/ReferralsPage";
import ReferralDetailsPage from "./pages/ReferralDetailsPage";
import AppShell from "./components/AppShell";
import MyReferralsPage from "./pages/MyReferralsPage";
import MyAppointmentsPage from "./pages/MyAppointmentsPage";
import { useReferralAccount } from "./components/referrals/useReferralAccount";
import NewReferralPage from "./pages/NewReferralPage";
import AppointmentListPage from "./pages/AppointmentAttentionPage";
import AppointmentDetailsPage from "./pages/AppointmentDetailsPage";
import ProviderSchedulePage from "./pages/ProviderSchedulePage";
import { appointmentLists } from "./components/appointments/appointmentLists";
import "./App.css";

const signInMethods = [
  { connection: "Clinic-Username", label: "Sign in with username and password", primary: true },
  { connection: "google-oauth2", label: "Continue with Google", primary: false },
  { connection: "windowslive", label: "Continue with Microsoft", primary: false },
] as const;

type SignInConnection = (typeof signInMethods)[number]["connection"];

function App() {
  const { isLoading, isAuthenticated, error, user, loginWithRedirect, logout } =
    useAuth0();

  const queryClient = useQueryClient();

  const [actionError, setActionError] = useState<string | null>(null);
  const [isRedirecting, setIsRedirecting] = useState(false);

  // Start the hosted Auth0 flow for the selected connection.
  async function handleSignIn(connection: SignInConnection) {
    if (isRedirecting) return;
    setActionError(null);
    setIsRedirecting(true);

    try {
      await loginWithRedirect({ authorizationParams: { connection } });
    } catch {
      setActionError("Unable to start sign-in. Please try again.");
      setIsRedirecting(false);
    }
  }

  // End the application session and return to the homepage.
  async function handleSignOut() {
    setActionError(null);
    setIsRedirecting(true);

    try {
      // Cancel pending requests and remove cached records before signing out.
      await queryClient.cancelQueries();
      queryClient.clear();

      await logout({
        logoutParams: {
          returnTo: window.location.origin,
        },
      });
    } catch {
      setActionError("Unable to sign out. Please try again.");
      setIsRedirecting(false);
    }
  }

  const signedIn = isAuthenticated && !isLoading && !error;
  const accountName = signedIn ? user?.name?.trim() || user?.nickname?.trim() || user?.email?.trim() || "Signed in" : undefined;

  const userId = signedIn && !isRedirecting ? user?.sub : undefined;
  const accountQuery = useReferralAccount(userId);
  const account = accountQuery.isSuccess ? accountQuery.data.account : undefined;
  const loadingWorkspace = accountQuery.isPending;
  const patientOnly = Boolean(account?.patientPublicId && !account.isAdministrator && account.memberships.length === 0);
  const homePath = accountQuery.isSuccess ? (patientOnly ? "/my-referrals" : "/referrals") : "/";

  return (
    <AppShell
      accountName={accountName}
      userId={userId}
      account={account}
      showStaffNavigation={accountQuery.isSuccess && !patientOnly}
      signingOut={isRedirecting}
      onSignOut={() => void handleSignOut()}
    >
      {isLoading ? (
        <p role="status">Checking sign-in…</p>
      ) : error ? (
        <section className="workspace-panel space-y-3">
          <h1 className="page-title">Sign-in error</h1>
          <p role="alert">We couldn’t complete sign-in. Please try again.</p>
          <a className="workspace-link" href="/">Return to the homepage</a>
        </section>
      ) : !signedIn ? (
        <section className="w-full max-w-md space-y-4 text-center" aria-labelledby="welcome-heading">
          <h1 id="welcome-heading" className="page-title">Welcome to Clinic Management System</h1>
          <div className="workspace-panel space-y-3">
            {signInMethods.map(({ connection, label, primary }) => (
              <button
                key={connection}
                type="button"
                className="w-full"
                data-variant={primary ? undefined : "secondary"}
                disabled={isRedirecting}
                onClick={() => void handleSignIn(connection)}
              >
                {label}
              </button>
            ))}
            {isRedirecting && <p role="status">Opening sign-in…</p>}
            {actionError && <p role="alert">{actionError}</p>}
          </div>
        </section>
      ) : !isRedirecting && (
        <Routes>
          <Route path="/" element={loadingWorkspace
            ? <p role="status">Loading your workspace…</p>
            : accountQuery.isError
              ? <section className="workspace-panel space-y-3">
                  <h1 className="page-title">Unable to load your workspace</h1>
                  <p role="alert">We couldn’t load your account. Please try again.</p>
                  <button
                    type="button"
                    disabled={accountQuery.isFetching}
                    onClick={() => void accountQuery.refetch()}
                  >
                    {accountQuery.isFetching ? "Retrying…" : "Retry"}
                  </button>
                </section>
              : <Navigate to={homePath} replace />} />
          <Route path="/my-referrals" element={<MyReferralsPage />} />
          <Route path="/my-appointments" element={<MyAppointmentsPage userId={userId} />} />
          <Route path="/clinics" element={<div className="legacy-directory"><ClinicsPage /></div>} />
          <Route path="/clinics/:clinicPublicId/providers" element={<div className="legacy-directory"><ProvidersPage /></div>} />
          <Route path="/clinics/:clinicPublicId/services" element={<div className="legacy-directory"><ServicesPage /></div>} />
          <Route path="/patients" element={<div className="legacy-directory"><PatientsPage /></div>} />
          <Route path="/patients/:publicId" element={<div className="legacy-directory"><PatientDetailsPage /></div>} />
          <Route path="/referrals" element={<ReferralsPage />} />
          <Route path="/my-schedule" element={<ProviderSchedulePage />} />
          <Route path={appointmentLists.upcoming.path} element={<AppointmentListPage view="upcoming" />} />
          <Route path={appointmentLists.conflicts.path} element={<AppointmentListPage view="conflicts" />} />
          <Route path="/appointments/needs-attention" element={<Navigate to={appointmentLists.conflicts.path} replace />} />
          <Route path="/appointments/:publicId" element={<AppointmentDetailsPage />} />
          <Route path="/referrals/new" element={<NewReferralPage />} />
          <Route path="/referrals/:publicId" element={<ReferralDetailsPage />} />
          <Route path="*" element={
            <section className="workspace-panel space-y-3">
              <h1 className="page-title">Page not found</h1>
              <Link to={homePath} className="workspace-link">Return to your workspace</Link>
            </section>
          } />
        </Routes>
      )}
      {signedIn && actionError && <p role="alert" className="mt-3">{actionError}</p>}
    </AppShell>
  );
}

export default App;
