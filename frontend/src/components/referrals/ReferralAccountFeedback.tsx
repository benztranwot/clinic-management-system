import { referralErrorMessage, ReferralRequestError } from "../../api/referrals";
import type { useReferralAccount } from "./useReferralAccount";

export default function ReferralAccountFeedback({ query }: { query: ReturnType<typeof useReferralAccount> }) {
  if (query.isPending) return <p role="status">Checking referral access…</p>;
  if (!query.isError) return null;

  const accessDenied = query.error instanceof ReferralRequestError && query.error.status === 403;
  return (
    <div className="space-y-3">
      <p role="alert">{accessDenied
        ? "Your sign-in needs to be linked to an account in Clinic Management. Ask your administrator to check your account."
        : referralErrorMessage(query.error, "Unable to check referral access.")}</p>
      <button type="button" data-variant="secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>
        Try again
      </button>
    </div>
  );
}
