import { useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ClinicRequestError,
  createClinic,
  updateClinic,
  type Clinic,
} from "../../api/clinics";

function ClinicForm({
  userId,
  clinic,
  onCancel,
}: {
  userId: string;
  clinic: Clinic | null;
  onCancel: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const isManaging = useIsMutating({ mutationKey: ["manage-clinic", userId] }) > 0;
  const [name, setName] = useState(clinic?.name ?? "");
  const [nameError, setNameError] = useState("");

  const mutation = useMutation({
    mutationKey: ["manage-clinic", userId],
    mutationFn: async (clinicName: string) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) {
        throw new Error("Auth0 did not return an access token.");
      }

      return clinic
        ? updateClinic(accessToken, clinic.publicId, clinicName)
        : createClinic(accessToken, clinicName);
    },
    retry: false,
    onSuccess: async (savedClinic) => {
      setName(clinic ? savedClinic.name : "");

      // Reload this user's list after the API saves the clinic.
      await queryClient.invalidateQueries({
        queryKey: ["clinics", userId],
        exact: true,
      });
    },
  });

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isManaging) return;

    const trimmedName = name.trim();
    if (!trimmedName) {
      setNameError("Enter a clinic name.");
      return;
    }

    mutation.mutate(trimmedName);
  }

  const status =
    mutation.error instanceof ClinicRequestError
      ? mutation.error.status
      : undefined;

  let saveError =
    "Unable to confirm the save. Check the list before trying again.";
  if (status === 400) {
    saveError = "Enter a clinic name between 1 and 200 characters.";
  } else if (status === 401) {
    saveError = "Your sign-in couldn’t be verified. Sign out and sign back in.";
  } else if (status === 403) {
    saveError = "Your account doesn’t have permission to save clinics.";
  } else if (status === 404) {
    saveError = "This clinic is no longer available.";
  }

  return (
    <form
      className="mt-4 space-y-3 rounded-md border border-slate-200 p-4"
      onSubmit={handleSubmit}
    >
      <h3 className="font-semibold">{clinic ? "Edit clinic" : "Create clinic"}</h3>
      <div>
        <label htmlFor="clinic-name" className="block text-sm font-medium">
          Clinic name
        </label>
        <input
          id="clinic-name"
          className="mt-1 w-full rounded-md border border-slate-400 px-3 py-2"
          value={name}
          onChange={(event) => {
            setName(event.target.value);
            setNameError("");
            mutation.reset();
          }}
          maxLength={200}
          required
          disabled={isManaging}
          aria-invalid={Boolean(nameError)}
          aria-describedby={nameError ? "clinic-name-error" : undefined}
        />
        {nameError && (
          <p id="clinic-name-error" role="alert">
            {nameError}
          </p>
        )}
      </div>

      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={isManaging}>
          {mutation.isPending ? "Saving…" : "Save clinic"}
        </button>
        {clinic && (
          <button type="button" onClick={onCancel} disabled={isManaging}>
            {mutation.isSuccess ? "Done" : "Cancel"}
          </button>
        )}
      </div>

      {mutation.isError && <p role="alert">{saveError}</p>}
      {mutation.isSuccess && (
        <div className="flex items-center gap-3">
          <p role="status">Clinic saved.</p>
          <button
            type="button"
            aria-label="Dismiss clinic saved message"
            onClick={() => mutation.reset()}
          >
            Dismiss
          </button>
        </div>
      )}
    </form>
  );
}

export default ClinicForm;
