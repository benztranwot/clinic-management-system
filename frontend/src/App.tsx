import { useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { Link, NavLink, Navigate, Route, Routes } from "react-router";
import { useQueryClient } from "@tanstack/react-query";
import ClinicsPage from "./pages/ClinicsPage";
import ProvidersPage from "./pages/ProvidersPage";
import PatientsPage from "./pages/PatientsPage";
import PatientDetailsPage from "./pages/PatientDetailsPage";
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

  // Wait until Auth0 finishes checking the current session.
  if (isLoading) {
    return (
      <main className="app">
        <p role="status">Checking sign-in…</p>
      </main>
    );
  }

  // Handle errors that occur while processing authentication.
  if (error) {
    return (
      <main className="app">
        <h1 className="text-3xl font-semibold tracking-tight">
          Clinic Management
        </h1>
        <p role="alert">We couldn’t complete sign-in. Please try again.</p>
        <a href="/">Return to the homepage</a>
      </main>
    );
  }

  return (
    <main className="app">
      <h1 className="text-3xl font-semibold tracking-tight">
        Clinic Management
      </h1>

      {/* Show the appropriate controls for the current session. */}
      {isAuthenticated ? (
        <section>
          <p>Signed in as {user?.name ?? user?.email ?? "your account"}.</p>
          <button
            type="button"
            onClick={handleSignOut}
            disabled={isRedirecting}
          >
            {isRedirecting ? "Signing out…" : "Sign out"}
          </button>
        </section>
      ) : (
        <section>
          <p>Sign in to view application records.</p>
          <button type="button" onClick={handleSignIn} disabled={isRedirecting}>
            {isRedirecting ? "Opening sign-in…" : "Sign in"}
          </button>
        </section>
      )}

      {/* Show application pages after authentication finishes successfully. */}
      {isAuthenticated && !isRedirecting && (
        <>
          <nav aria-label="Main navigation" className="mt-6 flex gap-4">
            <NavLink
              to="/clinics"
              className={({ isActive }) =>
                isActive ? "font-semibold underline" : "text-blue-700"
              }
            >
              Clinics
            </NavLink>
            <NavLink
              to="/patients"
              className={({ isActive }) =>
                isActive ? "font-semibold underline" : "text-blue-700"
              }
            >
              Patients
            </NavLink>
          </nav>
          <Routes>
            <Route path="/" element={<Navigate to="/clinics" replace />} />
  
            <Route path="/clinics" element={<ClinicsPage />} />
            <Route path="/clinics/:clinicPublicId/providers" element={<ProvidersPage />} />
            <Route path="/patients" element={<PatientsPage />} />
            <Route path="/patients/:publicId" element={<PatientDetailsPage />} />
  
            <Route
              path="*"
              element={
                <section className="mt-8">
                  <h2 className="text-xl font-semibold">Page not found</h2>
                  <Link
                    to="/clinics"
                    className="mt-2 inline-block text-blue-700 underline"
                  >
                    Return to clinics
                  </Link>
                </section>
              }
            />
          </Routes>
        </>
      )}

      {actionError && <p role="alert">{actionError}</p>}
    </main>
  );
}

export default App;
