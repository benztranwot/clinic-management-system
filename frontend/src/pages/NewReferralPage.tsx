import { useAuth0 } from "@auth0/auth0-react";
import { useLocation, useNavigate } from "react-router";
import ReferralAccountFeedback from "../components/referrals/ReferralAccountFeedback";
import ReferralCreateForm from "../components/referrals/ReferralCreateForm";
import { useReferralAccount } from "../components/referrals/useReferralAccount";

function NewReferralContent({ userId }: { userId: string }) {
  const navigate = useNavigate();
  const { search, state } = useLocation();
  const queueState = state?.referralQueueOwner === userId ? state : undefined;
  const account = useReferralAccount(userId);
  if (!account.isSuccess) return <ReferralAccountFeedback query={account} />;
  if (!account.data.canCreate) return <p role="alert">Your account doesn’t have access to create referrals.</p>;

  return (
    <ReferralCreateForm
      userId={userId}
      account={account.data.account}
      onCancel={() => void navigate({ pathname: "/referrals", search }, { state: queueState, replace: true })}
    />
  );
}

export default function NewReferralPage() {
  const { user } = useAuth0();

  return (
    <section className="workspace-panel mx-auto w-full max-w-2xl space-y-3" aria-labelledby="new-referral-heading">
      <h1 id="new-referral-heading" className="page-title">New referral</h1>
      {user?.sub
        ? <NewReferralContent key={user.sub} userId={user.sub} />
        : <p role="alert">We couldn’t identify your session.</p>}
    </section>
  );
}
