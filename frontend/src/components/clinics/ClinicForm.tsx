import { useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ClinicRequestError,
  createClinic,
  updateClinic,
  type Clinic,
  type ClinicInput,
} from "../../api/clinics";
import { ActionIcon } from "../ActionIcon";

function ClinicForm({
  userId,
  clinic,
  onCancel,
  onSaved,
}: {
  userId: string;
  clinic: Clinic | null;
  onCancel: () => void;
  onSaved: (clinic: Clinic) => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const isManaging = useIsMutating({ mutationKey: ["manage-clinic", userId] }) > 0;
  const [name, setName] = useState(clinic?.name ?? "");
  const [address, setAddress] = useState(clinic?.address ?? "");
  const [timeZoneId, setTimeZoneId] = useState(clinic?.timeZoneId ?? "");
  const [fieldError, setFieldError] = useState("");
  const inputClass = "mt-1 w-full rounded-md border border-[var(--input)] px-3 py-2";

  const mutation = useMutation({
    mutationKey: ["manage-clinic", userId],
    mutationFn: async (input: ClinicInput) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) {
        throw new Error("Auth0 did not return an access token.");
      }

      return clinic
        ? updateClinic(accessToken, clinic.publicId, input)
        : createClinic(accessToken, input);
    },
    retry: false,
    onSuccess: async (savedClinic) => {
      queryClient.setQueryData<{ clinic: Clinic; canManage: boolean }>(
        ["clinic", userId, savedClinic.publicId],
        (current) => current && { ...current, clinic: savedClinic },
      );

      // Reload this user's list after the API saves the clinic.
      await queryClient.invalidateQueries({
        queryKey: ["clinics", userId],
        exact: true,
      });
      onSaved(savedClinic);
    },
  });

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isManaging) return;

    const input = { name: name.trim(), address: address.trim(), timeZoneId: timeZoneId.trim() };
    if (!input.name || !input.address || !input.timeZoneId) {
      setFieldError("Enter a clinic name, address, and time zone.");
      return;
    }

    setFieldError("");
    mutation.mutate(input);
  }

  const status =
    mutation.error instanceof ClinicRequestError
      ? mutation.error.status
      : undefined;

  let saveError =
    "Unable to confirm the save. Check the list before trying again.";
  if (status === 400) {
    saveError = "Check the clinic name and address, and enter an IANA time zone such as America/Chicago.";
  } else if (status === 401) {
    saveError = "Your sign-in couldn’t be verified. Sign out and sign back in.";
  } else if (status === 403) {
    saveError = "Your account doesn’t have permission to save clinics.";
  } else if (status === 404) {
    saveError = "This clinic is no longer available.";
  }

  return (
    <form
      className="mt-4 space-y-3 rounded-md border border-panel-border p-4"
      onSubmit={handleSubmit}
    >
      <fieldset disabled={isManaging} className="space-y-3">
        <label htmlFor="clinic-name" className="block text-sm font-medium">
          Clinic name
        </label>
        <input
          id="clinic-name"
          className={inputClass}
          value={name}
          onChange={(event) => {
            setName(event.target.value);
            setFieldError("");
            mutation.reset();
          }}
          maxLength={200}
          required
        />
        <label className="block text-sm font-medium">
          Address
          <input className={inputClass} value={address} maxLength={300} required
            onChange={(event) => {
              setAddress(event.target.value);
              setFieldError("");
              mutation.reset();
            }} />
        </label>
        <label className="block text-sm font-medium">
          Time zone
          <input className={inputClass} value={timeZoneId} maxLength={100} required
            placeholder="America/Chicago" aria-describedby="clinic-time-zone-help"
            onChange={(event) => {
              setTimeZoneId(event.target.value);
              setFieldError("");
              mutation.reset();
            }} />
        </label>
        <p id="clinic-time-zone-help" className="text-sm text-subtle">Use the clinic’s IANA time zone, such as America/Chicago.</p>
      </fieldset>
      {fieldError && <p role="alert">{fieldError}</p>}

      <div className="flex flex-wrap gap-2">
        <button type="submit" disabled={isManaging}>
          <ActionIcon name="save" />
          {mutation.isPending ? "Saving…" : "Save clinic"}
        </button>
        <button type="button" data-variant="secondary" onClick={onCancel} disabled={isManaging}><ActionIcon name="close" />Cancel</button>
      </div>

      {mutation.isError && <p role="alert">{saveError}</p>}
    </form>
  );
}

export default ClinicForm;
