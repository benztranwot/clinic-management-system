import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useLocation, useNavigate } from "react-router";
import { getPatients, PatientRequestError } from "../api/patients";
import PatientForm from "../components/patients/PatientForm";

function PatientList({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const query = useQuery({
    // Isolate each user's records and cancel requests when this page unmounts.
    queryKey: ["patients", userId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token) {
        throw new Error("Auth0 did not return an access token.");
      }

      const patients = await getPatients(token.access_token, signal);

      return {
        patients,
        canManage: token.scope?.split(/\s+/).includes("manage:patients") ?? false,
      };
    },
    staleTime: 30_000,
    retry: false,
  });

  if (query.isPending) {
    return <p className="mt-3" role="status">Loading patients…</p>;
  }

  if (query.isError) {
    const status = query.error instanceof PatientRequestError
      ? query.error.status
      : undefined;

    if (status === 401) {
      return (
        <p className="mt-3" role="alert">
          Your sign-in couldn’t be verified. Sign out and sign back in.
        </p>
      );
    }

    if (status === 403) {
      return (
        <p className="mt-3" role="alert">
          Your account doesn’t have permission to view patients.
        </p>
      );
    }

    return (
      <div className="mt-3">
        <p role="alert">Unable to load patients.</p>
        <button
          type="button"
          className="mt-3"
          disabled={query.isFetching}
          onClick={() => void query.refetch()}
        >
          {query.isFetching ? "Trying again…" : "Try again"}
        </button>
      </div>
    );
  }

  const { patients, canManage } = query.data;

  return (
    <>
      {canManage && <PatientForm userId={userId} />}
      {patients.length === 0 ? (
        <p className="mt-3 text-slate-600">No patients found.</p>
      ) : (
        <ul className="mt-3 divide-y divide-slate-200" aria-label="Patients">
          {patients.map((patient) => (
            <li key={patient.publicId} className="py-3 font-medium break-words">
              <Link
                to={`/patients/${patient.publicId}`}
                className="text-blue-700 underline"
              >
                {patient.lastName}, {patient.firstName}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function PatientsPage() {
  const { user } = useAuth0();
  const { state } = useLocation();
  const navigate = useNavigate();

  return (
    <section
      aria-labelledby="patients-heading"
      className="mt-8 rounded-lg border border-slate-200 bg-white p-6"
    >
      <h2 id="patients-heading" className="text-xl font-semibold text-slate-900">
        Patients
      </h2>
      {state?.patientDeleted === true && (
        <div className="mt-3 flex items-center gap-3">
          <p role="status">Patient deleted.</p>
          <button
            type="button"
            aria-label="Dismiss patient deleted message"
            onClick={() => void navigate("/patients", { replace: true, state: null })}
          >
            Dismiss
          </button>
        </div>
      )}
      {user?.sub ? (
        <PatientList userId={user.sub} />
      ) : (
        <p className="mt-3" role="alert">
          We couldn’t identify your session. Sign out and sign back in.
        </p>
      )}
    </section>
  );
}

export default PatientsPage;
