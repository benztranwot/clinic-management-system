import { useState, type ReactNode } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { Navigate, Outlet, Route, Routes, useParams } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import ClinicsPage from "./pages/ClinicsPage";
import ClinicDetailsPage from "./pages/ClinicDetailsPage";
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
import { GoogleLogo, MicrosoftLogo } from "./components/SignInLogos";
import NotFoundPage from "./pages/NotFoundPage";
import "./App.css";
import { ActionIcon } from "./components/ActionIcon";

const signInMethods = [
  { connection: "Clinic-Username", label: "Sign in with username and password", primary: true, Logo: null },
  { connection: "google-oauth2", label: "Continue with Google", primary: false, Logo: GoogleLogo },
  { connection: "windowslive", label: "Continue with Microsoft", primary: false, Logo: MicrosoftLogo },
] as const;

type SignInConnection = (typeof signInMethods)[number]["connection"];

const publicIdPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

function WorkspaceAccess({ signedIn, signIn, homePath }: {
  signedIn: boolean;
  signIn: ReactNode;
  homePath: string;
}) {
  const { publicId, clinicPublicId } = useParams();
  // Reject malformed IDs before deciding whether the matched route needs sign-in.
  if ([publicId, clinicPublicId].some((id) => id !== undefined && !publicIdPattern.test(id))) {
    return <NotFoundPage homePath={homePath} />;
  }

  return signedIn ? <Outlet /> : signIn;
}

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
      await loginWithRedirect({
        authorizationParams: connection === "google-oauth2"
          ? { connection, prompt: "login" }
          : { connection },
      });
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
      homePath={homePath}
      accountEmail={signedIn ? user?.email : undefined}
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
      ) : (!signedIn || !isRedirecting) && (
        <Routes>
          <Route element={<WorkspaceAccess signedIn={signedIn} homePath={homePath} signIn={(
              <section className="w-full max-w-md space-y-4 text-center" aria-labelledby="welcome-heading">
                <h1 id="welcome-heading" className="page-title">Welcome to Clinic Management System</h1>
                <div className="workspace-panel space-y-3">
                  {signInMethods.map(({ connection, label, primary, Logo }) => (
                    <button
                      key={connection}
                      type="button"
                      className="sign-in-button w-full"
                      data-variant={primary ? undefined : "secondary"}
                      disabled={isRedirecting}
                      onClick={() => void handleSignIn(connection)}
                    >
                      {Logo ? <Logo /> : <ActionIcon name="login" />}
                      {label}
                    </button>
                  ))}
                  {isRedirecting && <p role="status">Opening sign-in…</p>}
                  {actionError && <p role="alert">{actionError}</p>}
                </div>
              </section>
          )} />}>
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
                      <ActionIcon name="refresh" />
                      {accountQuery.isFetching ? "Retrying…" : "Retry"}
                    </button>
                  </section>
                : <Navigate to={homePath} replace />} />
            <Route path="/my-referrals" element={<MyReferralsPage />} />
            <Route path="/my-appointments" element={<MyAppointmentsPage userId={userId} />} />
            <Route path="/clinics" element={<ClinicsPage />} />
            <Route path="/clinics/new" element={<ClinicsPage create />} />
            <Route path="/clinics/:clinicPublicId" element={<ClinicDetailsPage />} />
            <Route path="/clinics/:clinicPublicId/edit" element={<ClinicDetailsPage edit />} />
            <Route path="/clinics/:clinicPublicId/providers" element={<ProvidersPage />} />
            <Route path="/clinics/:clinicPublicId/providers/:publicId" element={<ProvidersPage />} />
            <Route path="/clinics/:clinicPublicId/services" element={<ServicesPage />} />
            <Route path="/clinics/:clinicPublicId/services/:publicId" element={<ServicesPage />} />
            <Route path="/patients" element={<PatientsPage />} />
            <Route path="/patients/new" element={<PatientsPage create />} />
            <Route path="/patients/:publicId" element={<PatientDetailsPage />} />
            <Route path="/patients/:publicId/edit" element={<PatientDetailsPage edit />} />
            <Route path="/referrals" element={<ReferralsPage />} />
            <Route path="/my-schedule" element={<ProviderSchedulePage />} />
            <Route path={appointmentLists.upcoming.path} element={<AppointmentListPage view="upcoming" />} />
            <Route path={appointmentLists.conflicts.path} element={<AppointmentListPage view="conflicts" />} />
            <Route path="/appointments/needs-attention" element={<Navigate to={appointmentLists.conflicts.path} replace />} />
            <Route path="/appointments/:publicId" element={<AppointmentDetailsPage />} />
            <Route path="/referrals/new" element={<NewReferralPage />} />
            <Route path="/referrals/:publicId" element={<ReferralDetailsPage />} />
          </Route>
          <Route path="*" element={<NotFoundPage homePath={homePath} />} />
        </Routes>
      )}
      {signedIn && actionError && <p role="alert" className="mt-3">{actionError}</p>}
    </AppShell>
  );
}

export default App;
