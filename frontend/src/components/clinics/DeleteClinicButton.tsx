import { useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ClinicRequestError, deleteClinic, type Clinic } from "../../api/clinics";

function DeleteClinicButton({
  clinic,
  userId,
  disabled,
  onNotice,
}: {
  clinic: Clinic;
  userId: string;
  disabled: boolean;
  onNotice: (message: string) => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const [isRefreshing, setIsRefreshing] = useState(false);

  const mutation = useMutation({
    mutationKey: ["manage-clinic", userId],
    mutationFn: async () => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) {
        throw new Error("Auth0 did not return an access token.");
      }

      await deleteClinic(accessToken, clinic.publicId);
    },
    retry: false,
    onSuccess: async () => {
      onNotice("Clinic deleted.");

      // Refresh the list after SQL Server confirms the deletion.
      await queryClient.invalidateQueries({
        queryKey: ["clinics", userId],
        exact: true,
      });
    },
  });

  function handleDelete() {
    if (disabled || mutation.isPending || isRefreshing) return;

    // Name the clinic so the user can confirm the intended record.
    if (window.confirm(`Delete "${clinic.name}"? This action is permanent.`)) {
      mutation.mutate();
    }
  }

  async function handleRefresh() {
    if (disabled || mutation.isPending || isRefreshing) return;
    setIsRefreshing(true);

    try {
      // Report success only after the clinic list finishes reloading.
      await queryClient.invalidateQueries(
        { queryKey: ["clinics", userId], exact: true },
        { throwOnError: true },
      );
      mutation.reset();
      onNotice("Clinic list refreshed.");
    } catch {
      // The clinic list displays its query error and retry control.
    } finally {
      setIsRefreshing(false);
    }
  }

  const status =
    mutation.error instanceof ClinicRequestError
      ? mutation.error.status
      : undefined;

  let deleteError =
    "Unable to confirm deletion. Refresh the list before trying again.";
  if (status === 401) {
    deleteError = "Your sign-in couldn’t be verified. Sign out and sign back in.";
  } else if (status === 403) {
    deleteError = "Your account doesn’t have permission to delete clinics.";
  } else if (status === 404) {
    deleteError = "This clinic no longer exists. Refresh the list.";
  } else if (status === 409) {
    deleteError = "This clinic still has providers or services and cannot be deleted.";
  }

  return (
    <div>
      <button
        type="button"
        disabled={disabled || mutation.isPending || isRefreshing}
        aria-label={`Delete ${clinic.name}`}
        onClick={handleDelete}
      >
        {mutation.isPending ? "Deleting…" : "Delete"}
      </button>

      {mutation.isError && (
        <div className="mt-2 max-w-xs text-sm">
          <p role="alert">{deleteError}</p>
          <button
            type="button"
            className="mt-2"
            disabled={disabled || mutation.isPending || isRefreshing}
            onClick={() => void handleRefresh()}
          >
            {isRefreshing ? "Refreshing…" : "Refresh list"}
          </button>
        </div>
      )}
    </div>
  );
}

export default DeleteClinicButton;
