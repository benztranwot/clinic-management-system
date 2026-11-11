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
import NewReferralPage from "./pages/NewReferralPage";
import AppointmentListPage from "./pages/AppointmentAttentionPage";
import AppointmentDetailsPage from "./pages/AppointmentDetailsPage";
import ProviderSchedulePage from "./pages/ProviderSchedulePage";
import { appointmentLists } from "./components/appointments/appointmentLists";
import "./App.css";

function App() {
  const { isLoading, isAuthenticated, error, user, loginWithRedirect, logout } =
    useAuth0();

  const queryClient = useQueryClient();

  const [actionError, setActionError] = useState<string | null>(null);
  const [isRedirecting, setIsRedirecting] = useState(false);

  // Open Auth0's hosted sign-in page.
  async function handleSignIn() {
    setActionError(null);
    setIsRedirecting(true);

    try {
      await loginWithRedirect();
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
  const accountName = signedIn ? user?.name?.trim() || user?.email?.trim() || "Signed in" : undefined;

  return (
    <AppShell
      accountName={accountName}
      userId={signedIn && !isRedirecting ? user?.sub : undefined}
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
          <form className="workspace-panel space-y-3" onSubmit={(event) => {
            event.preventDefault();
            if (!isRedirecting) void handleSignIn();
          }}>
            <button type="submit" className="w-full" disabled={isRedirecting}>
              {isRedirecting ? "Opening sign-in…" : "Sign in"}
            </button>
            {actionError && <p role="alert">{actionError}</p>}
          </form>
        </section>
      ) : !isRedirecting && (
        <Routes>
          <Route path="/" element={<Navigate to="/referrals" replace />} />
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
              <Link to="/referrals" className="workspace-link">Return to referrals</Link>
            </section>
          } />
        </Routes>
      )}
      {signedIn && actionError && <p role="alert" className="mt-3">{actionError}</p>}
    </AppShell>
  );
}

export default App;
