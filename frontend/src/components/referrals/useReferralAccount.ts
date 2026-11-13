import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { getReferralAccount } from "../../api/referrals";

export function useReferralAccount(userId?: string) {
  const { getAccessTokenSilently } = useAuth0();

  return useQuery({
    queryKey: ["referral-account", userId],
    enabled: Boolean(userId),
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Auth0 did not return an access token.");

      const account = await getReferralAccount(token, signal);
      return { account, canCreate: account.canCreateReferrals };
    },
    retry: false,
  });
}
