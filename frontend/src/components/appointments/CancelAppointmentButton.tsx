import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQueryClient } from "@tanstack/react-query";
import { cancelAppointment } from "../../api/appointments";
import { appointmentWriteKey, refreshAppointmentViews } from "./appointmentQueries";

export default function CancelAppointmentButton({
  publicId,
  userId,
  canCancel,
}: {
  publicId: string;
  userId: string;
  canCancel: boolean;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const mutationKey = appointmentWriteKey(userId, publicId);
  const writing = useIsMutating({ mutationKey }) > 0;
  const cancel = useMutation({
    mutationKey,
    mutationFn: async () => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      await cancelAppointment(publicId, token);
    },
    retry: false,
    onSuccess: () => refreshAppointmentViews(queryClient, userId, publicId),
  });

  return (
    <>
      {canCancel && !cancel.isSuccess && (
        <button
          type="button"
          data-variant="danger"
          disabled={writing}
          onClick={() => {
            if (window.confirm("Cancel this appointment and release its time slot?")) cancel.mutate();
          }}
        >
          {cancel.isPending ? "Canceling…" : "Cancel appointment"}
        </button>
      )}
      {cancel.isSuccess && <p role="status">Appointment canceled.</p>}
      {cancel.isError && <p role="alert" className="basis-full">{cancel.error.message}</p>}
    </>
  );
}
