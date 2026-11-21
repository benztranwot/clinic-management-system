import LoadingStatus from "../../../components/LoadingStatus";
import { referralErrorMessage, ReferralRequestError } from "../../../api/referrals";
import type { useReferralAccount } from "../hooks/useReferralAccount";
import { ActionIcon } from "../../../components/ActionIcon";

export default function ReferralAccountFeedback({ query }: { query: ReturnType<typeof useReferralAccount> }) {
  if (query.isPending) return <LoadingStatus>Checking referral access…</LoadingStatus>;
  if (!query.isError) return null;

  const accessDenied = query.error instanceof ReferralRequestError && query.error.status === 403;
  return (
    <div className="space-y-3">
      <p role="alert">{accessDenied
        ? "Your sign-in needs to be linked to an account in Clinic Management. Ask your administrator to check your account."
        : referralErrorMessage(query.error, "Unable to check referral access.")}</p>
      <button type="button" data-variant="secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>
        <ActionIcon name="refresh" />
        Try again
      </button>
    </div>
  );
}
