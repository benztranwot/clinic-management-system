import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { getMyReferrals, referralErrorMessage, ReferralRequestError } from "../api/referrals";
import ReferralStatusBadge from "../components/referrals/ReferralStatusBadge";

const createdDate = new Intl.DateTimeFormat(undefined, { dateStyle: "medium" });

function MyReferrals({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const query = useQuery({
    queryKey: ["my-referrals", userId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return getMyReferrals(token, signal);
    },
    retry: false,
  });

  const unlinked = query.error instanceof ReferralRequestError && query.error.status === 403;

  return (
    <>
      <div className="page-heading">
        <h1 id="my-referrals-heading" className="page-title">My referrals</h1>
        {!unlinked && (
          <button type="button" data-variant="secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>
            {query.isFetching ? "Refreshing…" : "Refresh"}
          </button>
        )}
      </div>
      {query.isPending ? (
        <p role="status">Loading your referrals…</p>
      ) : query.isError ? (
        <p role="alert">
          {unlinked
            ? "Your account is not linked to a patient record. Contact your clinic for help."
            : referralErrorMessage(query.error, "Unable to load your referrals. Try refreshing the list.")}
        </p>
      ) : query.data.length === 0 ? (
        <p className="text-sm text-subtle">You have no referrals yet.</p>
      ) : (
        <ul className="space-y-3" aria-labelledby="my-referrals-heading">
          {query.data.map((referral) => (
            <li key={referral.publicId} className="space-y-2 rounded-lg border border-panel-border p-4">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <h2 className="wrap-break-word font-semibold">{referral.serviceName}</h2>
                  <p className="text-sm text-subtle">{referral.clinicName}</p>
                </div>
                <ReferralStatusBadge status={referral.stage} />
              </div>
              {referral.status === "MissingInformation" && <p className="text-sm">More information is needed.</p>}
              <p className="text-sm text-subtle">
                Created <time dateTime={referral.createdAtUtc}>{createdDate.format(new Date(referral.createdAtUtc))}</time>
              </p>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

export default function MyReferralsPage() {
  const { user } = useAuth0();

  return (
    <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="my-referrals-heading">
      {user?.sub ? <MyReferrals key={user.sub} userId={user.sub} /> : (
        <>
          <h1 id="my-referrals-heading" className="page-title">My referrals</h1>
          <p role="alert">Unable to verify your session. Sign in again.</p>
        </>
      )}
    </section>
  );
}
