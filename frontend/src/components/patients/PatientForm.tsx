import { useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  PatientRequestError,
  registerPatient,
  updatePatient,
  type PatientDetails,
  type PatientInput,
} from "../../api/patients";
import { ActionIcon } from "../ActionIcon";

function PatientForm({
  userId,
  patient,
  onSaved,
  onCancel,
}: {
  userId: string;
  patient?: PatientDetails;
  onSaved?: () => void;
  onCancel?: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const [validationError, setValidationError] = useState("");

  const mutation = useMutation({
    mutationFn: async (input: PatientInput) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) {
        throw new Error("Auth0 did not return an access token.");
      }

      if (patient) {
        await updatePatient(accessToken, patient.publicId, input);
      } else {
        await registerPatient(accessToken, input);
      }
    },
    retry: false,
    onSuccess: async () => {
      // Keep the list and the open patient record in sync with the API.
      const refreshList = queryClient.invalidateQueries({
        queryKey: ["patients", userId],
        exact: true,
      });

      if (patient) {
        await Promise.all([
          refreshList,
          queryClient.invalidateQueries({
            queryKey: ["patient", userId, patient.publicId],
            exact: true,
          }),
        ]);
      } else {
        await refreshList;
      }
    },
  });

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (mutation.isPending) return;

    const form = event.currentTarget;
    const fields = new FormData(form);
    const firstName = String(fields.get("firstName") ?? "").trim();
    const lastName = String(fields.get("lastName") ?? "").trim();
    const dateOfBirth = String(fields.get("dateOfBirth") ?? "");

    if (!firstName || !lastName) {
      setValidationError("Enter a first name and a last name.");
      return;
    }

    setValidationError("");
    mutation.mutate(
      { firstName, lastName, dateOfBirth },
      {
        onSuccess: () => {
          if (onSaved) onSaved();
          else form.reset();
        },
      },
    );
  }

  const status =
    mutation.error instanceof PatientRequestError
      ? mutation.error.status
      : undefined;

  let saveError = "Unable to confirm the save. Check the patient record before trying again.";
  if (status === 400) {
    saveError =
      "Use 1 to 100 characters for each name and a date of birth on or before today.";
  } else if (status === 401) {
    saveError = "Your sign-in couldn’t be verified. Sign out and sign back in.";
  } else if (status === 403) {
    saveError = "Your account doesn’t have permission to save patients.";
  } else if (status === 404) {
    saveError = "This patient is no longer available.";
  }

  return (
    <form
      className="mt-4 space-y-3 rounded-md border border-panel-border p-4"
      onSubmit={handleSubmit}
      onChange={() => {
        if (validationError) setValidationError("");
        if (mutation.isError || mutation.isSuccess) mutation.reset();
      }}
    >
      <fieldset className="space-y-3" disabled={mutation.isPending}>
        <div>
          <label htmlFor="patient-first-name" className="block text-sm font-medium">
            First name
          </label>
          <input
            id="patient-first-name"
            name="firstName"
            defaultValue={patient?.firstName}
            className="mt-1 w-full rounded-md border border-[var(--input)] px-3 py-2"
            maxLength={100}
            required
          />
        </div>

        <div>
          <label htmlFor="patient-last-name" className="block text-sm font-medium">
            Last name
          </label>
          <input
            id="patient-last-name"
            name="lastName"
            defaultValue={patient?.lastName}
            className="mt-1 w-full rounded-md border border-[var(--input)] px-3 py-2"
            maxLength={100}
            required
          />
        </div>

        <div>
          <label htmlFor="patient-date-of-birth" className="block text-sm font-medium">
            Date of birth
          </label>
          <input
            id="patient-date-of-birth"
            name="dateOfBirth"
            defaultValue={patient?.dateOfBirth}
            type="date"
            className="mt-1 w-full rounded-md border border-[var(--input)] px-3 py-2"
            required
          />
        </div>

        <div className="flex flex-wrap gap-2">
          <button type="submit">
            <ActionIcon name={patient ? "save" : "plus"} />
            {mutation.isPending ? "Saving…" : patient ? "Save changes" : "Register patient"}
          </button>
          {onCancel && (
            <button type="button" data-variant="secondary" onClick={onCancel}><ActionIcon name="close" />Cancel</button>
          )}
        </div>
      </fieldset>

      {validationError && <p role="alert">{validationError}</p>}
      {mutation.isError && <p role="alert">{saveError}</p>}
      {!patient && mutation.isSuccess && (
        <div className="flex items-center gap-3">
          <p role="status">Patient registered.</p>
          <button
            type="button"
            aria-label="Dismiss patient registered message"
            onClick={() => mutation.reset()}
          >
            <ActionIcon name="close" />
            Dismiss
          </button>
        </div>
      )}
    </form>
  );
}

export default PatientForm;
