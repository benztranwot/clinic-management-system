import { useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useSearchParams } from "react-router";
import {
  getClinicReferrals,
  getReferrals,
  referralErrorMessage,
  referralStatusLabels,
  type ReferralAccount,
} from "../api/referrals";
import ReferralAccountFeedback from "../components/referrals/ReferralAccountFeedback";
import ReferralStatusBadge from "../components/referrals/ReferralStatusBadge";
import { useReferralAccount } from "../components/referrals/useReferralAccount";

const stageFilters = ["PendingReview", "ReadyToSchedule", "Scheduled", "Closed"] as const;

function ReferralQueue({ userId, clinicId }: { userId: string; clinicId?: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const [params, setParams] = useSearchParams();
  const filter = params.get("stage");
  const stage = stageFilters.find((value) => value === filter) ?? "all";

  const query = useQuery({
    queryKey: ["referrals", userId, clinicId ?? "all"],
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");

      return clinicId
        ? getClinicReferrals(accessToken, clinicId, signal)
        : getReferrals(accessToken, signal);
    },
    retry: false,
  });

  if (query.isPending) return <p role="status">Loading referrals…</p>;
  if (query.isError) {
    return (
      <div className="space-y-3">
        <p role="alert">{referralErrorMessage(query.error, "Unable to load referrals.")}</p>
        <button type="button" data-variant="secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>
          Try again
        </button>
      </div>
    );
  }

  const referrals = query.data;
  const visible = stage === "all" ? referrals : referrals.filter((referral) => referral.stage === stage);

  return (
    <>
      <label className="queue-filter">
        <span>Status</span>
        <select value={stage} onChange={(event) => {
          const next = new URLSearchParams(params);
          next.set("stage", event.target.value);
          setParams(next, { replace: true });
        }}>
          <option value="all">All statuses ({referrals.length})</option>
          {stageFilters.map((value) => (
            <option key={value} value={value}>
              {referralStatusLabels[value]} ({referrals.filter((referral) => referral.stage === value).length})
            </option>
          ))}
        </select>
      </label>
      {visible.length === 0 ? (
        <p className="queue-empty">{referrals.length ? "No referrals match this status." : "No referrals in this queue yet."}</p>
      ) : (
        <div className="queue-scroll" tabIndex={0} role="region" aria-label="Referral queue">
          <table className="queue-table">
            <caption className="sr-only">Referrals in the selected queue</caption>
            <thead>
              <tr>
                <th scope="col">Patient</th>
                <th scope="col">Service and clinic</th>
                <th scope="col">Status</th>
                <th scope="col">Received</th>
              </tr>
            </thead>
            <tbody>
              {visible.map((referral) => (
                <tr key={referral.publicId}>
                  <td>
                    <Link
                      to={{
                        pathname: "/referrals/" + referral.publicId,
                        search: clinicId ? new URLSearchParams({ clinic: clinicId }).toString() : "",
                      }}
                      className="workspace-link font-semibold"
                    >
                      {referral.patientFirstName} {referral.patientLastName}
                    </Link>
                  </td>
                  <td>{referral.serviceName}<span className="queue-meta">{referral.clinicName}</span></td>
                  <td>
                    <ReferralStatusBadge status={referral.stage} />
                    {referral.status === "MissingInformation" && <span className="queue-meta">Missing information</span>}
                  </td>
                  <td><time dateTime={referral.createdAtUtc}>{new Date(referral.createdAtUtc).toLocaleDateString()}</time></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

function StaffQueue({ userId, account }: { userId: string; account: ReferralAccount }) {
  const clinics = [...new Map(account.memberships.map((membership) => [
    membership.clinicPublicId, membership.clinicName,
  ])).entries()];
  const [selectedClinic, setSelectedClinic] = useState("");
  const activeClinic = clinics.find(([id]) => id === selectedClinic) ?? clinics[0];

  if (account.isAdministrator) return <ReferralQueue userId={userId} />;
  if (!activeClinic) return <p>Your account has no staff clinic membership.</p>;

  const [clinicId, clinicName] = activeClinic;
  return (
    <>
      {clinics.length === 1 ? (
        <p className="text-sm font-medium">{clinicName}</p>
      ) : (
        <label className="queue-filter">
          <span>Clinic</span>
          <select value={clinicId} onChange={(event) => setSelectedClinic(event.target.value)}>
            {clinics.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
          </select>
        </label>
      )}
      <ReferralQueue userId={userId} clinicId={clinicId} />
    </>
  );
}

function ReferralsContent({ userId }: { userId: string }) {
  const query = useReferralAccount(userId);

  return (
    <section className="workspace-panel space-y-4" aria-labelledby="referrals-heading">
      <div className="page-heading">
        <h1 id="referrals-heading" className="page-title">Referral queue</h1>
        {query.data?.canCreate && <Link to="/referrals/new" className="workspace-button">New referral</Link>}
      </div>
      {query.isSuccess
        ? <StaffQueue userId={userId} account={query.data.account} />
        : <ReferralAccountFeedback query={query} />}
    </section>
  );
}

export default function ReferralsPage() {
  const { user } = useAuth0();
  return user?.sub
    ? <ReferralsContent key={user.sub} userId={user.sub} />
    : <p role="alert">We couldn’t identify your session.</p>;
}
