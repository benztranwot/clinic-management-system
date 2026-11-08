import { useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { getReferrals, referralErrorMessage, referralStatusLabels } from "../api/referrals";
import ReferralCreateForm from "../components/referrals/ReferralCreateForm";

function ReferralList({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const [showCreateForm, setShowCreateForm] = useState(false);
  const query = useQuery({
    queryKey: ["referrals", userId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token) throw new Error("Auth0 did not return an access token.");

      const referrals = await getReferrals(token.access_token, signal);
      return {
        referrals,
        canManage: token.scope?.split(/\s+/).includes("manage:referrals") ?? false,
      };
    },
    retry: false,
  });

  if (query.isPending) {
    return <p className="mt-3" role="status">Loading referrals…</p>;
  }

  if (query.isError) {
    return (
      <div className="mt-3 space-y-2">
        <p role="alert">{referralErrorMessage(query.error, "Unable to load referrals.")}</p>
        <button type="button" disabled={query.isFetching} onClick={() => void query.refetch()}>
          Try again
        </button>
      </div>
    );
  }

  const { referrals, canManage } = query.data;
  return (
    <>
      {canManage && (
        showCreateForm ? (
          <ReferralCreateForm userId={userId} onCancel={() => setShowCreateForm(false)} />
        ) : (
          <button type="button" className="mt-3" onClick={() => setShowCreateForm(true)}>
            New referral
          </button>
        )
      )}

      {referrals.length === 0 ? (
        <p className="mt-3">No referrals found.</p>
      ) : (
        <ul aria-label="Referrals" className="mt-3 divide-y divide-slate-200">
          {referrals.map((referral) => (
            <li key={referral.publicId} className="space-y-1 py-3">
              <Link to={"/referrals/" + referral.publicId} className="font-medium text-blue-700 underline">
                {referral.patientFirstName} {referral.patientLastName} · {referral.serviceName}
              </Link>
              <p className="text-sm text-slate-600">{referral.clinicName}</p>
              <p className="text-sm">
                {referralStatusLabels[referral.status]} ·{" "}
                <time dateTime={referral.createdAtUtc}>{new Date(referral.createdAtUtc).toLocaleString()}</time>
              </p>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function ReferralsPage() {
  const { user } = useAuth0();

  return (
    <section
      aria-labelledby="referrals-heading"
      className="mt-8 rounded-lg border border-slate-200 bg-white p-6"
    >
      <h2 id="referrals-heading" className="text-xl font-semibold">Referrals</h2>
      {user?.sub ? (
        <ReferralList key={user.sub} userId={user.sub} />
      ) : (
        <p className="mt-3" role="alert">We couldn’t identify your session.</p>
      )}
    </section>
  );
}

export default ReferralsPage;
