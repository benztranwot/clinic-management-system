import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useLocation, useNavigate } from "react-router";
import { getPatients, PatientRequestError } from "../api/patients";
import WorkspaceTable from "../components/WorkspaceTable";
import PatientForm from "../components/patients/PatientForm";
import { ActionIcon } from "../components/ActionIcon";

const birthDateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeZone: "UTC" });

function PatientList({ userId, create }: { userId: string; create: boolean }) {
  const navigate = useNavigate();
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
          <ActionIcon name="refresh" />
          {query.isFetching ? "Trying again…" : "Try again"}
        </button>
      </div>
    );
  }

  const { patients, canManage } = query.data;

  if (create) {
    return canManage ? (
      <PatientForm userId={userId}
          onCancel={() => void navigate("/patients")}
          onSaved={() => void navigate("/patients", { replace: true, state: { patientCreated: true } })} />
    ) : <p role="alert">Your account doesn’t have permission to create patients.</p>;
  }

  return (
    <>
      <div className="page-heading">
        <h1 id="patients-heading" className="page-title">Patients</h1>
        {canManage && <Link className="workspace-button" to="/patients/new"><ActionIcon name="plus" />Register patient</Link>}
      </div>
      {patients.length === 0 ? (
        <p className="queue-empty">No patients found.</p>
      ) : (
        <WorkspaceTable label="Patients" columns={["Last name", "First name", "Date of birth"]}>
          {patients.map((patient) => (
            <tr key={patient.publicId} className="workspace-action-row">
              <td>
                <Link className="workspace-row-action"
                  aria-label={"View patient: " + patient.firstName + " " + patient.lastName}
                  to={"/patients/" + patient.publicId}>
                  {patient.lastName}
                </Link>
              </td>
              <td>{patient.firstName}</td>
              <td>
                <time dateTime={patient.dateOfBirth}>
                  {birthDateFormat.format(new Date(patient.dateOfBirth + "T00:00:00Z"))}
                </time>
              </td>
            </tr>
          ))}
        </WorkspaceTable>
      )}
    </>
  );
}

function PatientsPage({ create = false }: { create?: boolean }) {
  const { user } = useAuth0();
  const { state } = useLocation();
  const navigate = useNavigate();

  return (
    <section aria-label="Patients" className="workspace-panel mx-auto w-full max-w-4xl space-y-4">
      {create && <h1 className="page-title">Register patient</h1>}
      {!create && state?.patientCreated === true && <p role="status">Patient registered.</p>}
      {state?.patientDeleted === true && (
        <div className="mt-3 flex items-center gap-3">
          <p role="status">Patient deleted.</p>
          <button
            type="button"
            aria-label="Dismiss patient deleted message"
            onClick={() => void navigate("/patients", { replace: true, state: null })}
          >
            <ActionIcon name="close" />
            Dismiss
          </button>
        </div>
      )}
      {user?.sub ? (
        <PatientList userId={user.sub} create={create} />
      ) : (
        <p className="mt-3" role="alert">
          We couldn’t identify your session. Sign out and sign back in.
        </p>
      )}
    </section>
  );
}

export default PatientsPage;
