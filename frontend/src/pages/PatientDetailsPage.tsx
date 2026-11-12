import { useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useLocation, useNavigate, useParams } from "react-router";
import { deletePatient, getPatient, PatientRequestError, type PatientSummary } from "../api/patients";
import PatientForm from "../components/patients/PatientForm";
import { ActionIcon } from "../components/ActionIcon";

function PatientDetails({
  userId,
  publicId,
  edit,
}: {
  userId: string;
  publicId: string;
  edit: boolean;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const { state } = useLocation();
  const [showSaved, setShowSaved] = useState(state?.patientUpdated === true);
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const query = useQuery({
    queryKey: ["patient", userId, publicId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token) {
        throw new Error("Auth0 did not return an access token.");
      }

      const patient = await getPatient(token.access_token, publicId, signal);
      return {
        patient,
        canManage: token.scope?.split(/\s+/).includes("manage:patients") ?? false,
      };
    },
    retry: false,
  });

  const deletion = useMutation({
    mutationFn: async (patientId: string) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) {
        throw new Error("Auth0 did not return an access token.");
      }

      await deletePatient(accessToken, patientId);
    },
    retry: false,
    onSuccess: async (_, patientId) => {
      // Drop the deleted record from the cache only after the API confirms it.
      queryClient.removeQueries({
        queryKey: ["patient", userId, publicId],
        exact: true,
      });
      queryClient.setQueryData<{ patients: PatientSummary[]; canManage: boolean }>(
        ["patients", userId],
        (current) => current && {
          ...current,
          patients: current.patients.filter((patient) => patient.publicId !== patientId),
        },
      );
      await queryClient.invalidateQueries({
        queryKey: ["patients", userId],
        exact: true,
      });
    },
  });

  if (query.isPending) {
    return <p className="mt-3" role="status">Loading patient details…</p>;
  }

  if (query.isError) {
    const status = query.error instanceof PatientRequestError
      ? query.error.status
      : undefined;

    if (status === 404) {
      return <p className="mt-3" role="alert">Patient not found.</p>;
    }

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
        <p role="alert">Unable to load patient details.</p>
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

  const { patient, canManage } = query.data;
  const deleteStatus = deletion.error instanceof PatientRequestError
    ? deletion.error.status
    : undefined;

  let deleteError = "Unable to confirm deletion. Check the list before trying again.";
  if (deleteStatus === 409) {
    deleteError = "This patient still has referrals and cannot be deleted.";
  } else if (deleteStatus === 404) {
    deleteError = "This patient is no longer available. Return to the patient list.";
  } else if (deleteStatus === 403) {
    deleteError = "Your account doesn’t have permission to delete patients.";
  } else if (deleteStatus === 401) {
    deleteError = "Your sign-in couldn’t be verified. Sign out and sign back in.";
  }

  function handleDelete() {
    if (deletion.isPending) return;

    const name = patient.firstName + " " + patient.lastName;
    if (!window.confirm("Permanently delete " + name + "?")) return;

    setShowSaved(false);
    deletion.mutate(patient.publicId, {
      onSuccess: () => {
        void navigate("/patients", {
          replace: true,
          state: { patientDeleted: true },
        });
      },
    });
  }

  if (edit) {
    if (!canManage) return <p role="alert">Your account doesn’t have permission to edit patients.</p>;

    const detailsPath = "/patients/" + publicId;
    return (
      <PatientForm userId={userId} patient={patient}
        onCancel={() => void navigate(detailsPath)}
        onSaved={() => void navigate(detailsPath, { replace: true, state: { patientUpdated: true } })} />
    );
  }
  return (
    <div className="mt-4">
      <h3 className="font-semibold">{patient.firstName} {patient.lastName}</h3>
      <dl className="mt-3">
        <dt className="text-sm font-medium">Date of birth</dt>
        <dd><time dateTime={patient.dateOfBirth}>{patient.dateOfBirth}</time></dd>
      </dl>
      {canManage && (
        <div className="mt-4 flex flex-wrap gap-2">
          <Link className="workspace-button" to={"/patients/" + publicId + "/edit"}><ActionIcon name="pencil" />Edit patient</Link>
          <button
            type="button"
            disabled={deletion.isPending}
            data-variant="danger"
            onClick={handleDelete}
          >
            <ActionIcon name="trash" />
            {deletion.isPending ? "Deleting…" : "Delete patient"}
          </button>
        </div>
      )}
      {deletion.isError && <p className="mt-3" role="alert">{deleteError}</p>}
      {showSaved && (
        <div className="mt-3 flex items-center gap-3">
          <p role="status">Patient updated.</p>
          <button
            type="button"
            aria-label="Dismiss patient updated message"
            onClick={() => setShowSaved(false)}
          >
            <ActionIcon name="close" />
            Dismiss
          </button>
        </div>
      )}
    </div>
  );
}

function PatientDetailsPage({ edit = false }: { edit?: boolean }) {
  const { user } = useAuth0();
  const { publicId } = useParams<"publicId">();

  return (
    <section
      aria-labelledby="patient-details-heading"
      className="workspace-panel mx-auto w-full max-w-4xl space-y-4"
    >
      <Link to={edit && publicId ? "/patients/" + publicId : "/patients"} className="workspace-button" data-variant="secondary">
        <ActionIcon name="back" />
        {edit ? "Back to patient" : "Back to patients"}
      </Link>
      <h1 id="patient-details-heading" className="page-title">{edit ? "Edit patient" : "Patient details"}</h1>
      {user?.sub && publicId ? (
        <PatientDetails key={publicId + ":" + edit} userId={user.sub} publicId={publicId} edit={edit} />
      ) : (
        <p className="mt-3" role="alert">Unable to open this patient record.</p>
      )}
    </section>
  );
}

export default PatientDetailsPage;
