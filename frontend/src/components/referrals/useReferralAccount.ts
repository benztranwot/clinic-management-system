import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { getReferralAccount } from "../../api/referrals";

export function useReferralAccount(userId: string) {
  const { getAccessTokenSilently } = useAuth0();

  return useQuery({
    queryKey: ["referral-account", userId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token) throw new Error("Auth0 did not return an access token.");

      const account = await getReferralAccount(token.access_token, signal);
      const canCreate = token.scope?.split(/\s+/).includes("manage:referrals") &&
        (account.isAdministrator || account.memberships.some(({ role }) => role === "Clinician"));

      return { account, canCreate: Boolean(canCreate) };
    },
    retry: false,
  });
}
