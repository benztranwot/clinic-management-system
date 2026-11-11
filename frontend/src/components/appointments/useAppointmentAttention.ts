import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { getAppointmentConflicts } from "../../api/appointments";

export function useAppointmentAttention(userId?: string) {
  const { getAccessTokenSilently } = useAuth0();

  return useQuery({
    queryKey: ["appointment-attention", userId],
    enabled: Boolean(userId),
    staleTime: 15_000,
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return getAppointmentConflicts(token, signal);
    },
    retry: false,
  });
}
