import { useRef, useState, type FormEvent } from "react";
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
import WorkspaceFilterDialog from "../components/WorkspaceFilterDialog";
import WorkspaceTable from "../components/WorkspaceTable";
import ReferralStatusBadge from "../components/referrals/ReferralStatusBadge";
import { useReferralAccount } from "../components/referrals/useReferralAccount";
import { ActionIcon } from "../components/ActionIcon";

const statusFilters = ["PendingReview", "MissingInformation", "ReadyToSchedule", "Scheduled", "Closed"] as const;
const searchFields = [
  ["patient", "Patient"],
  ["service", "Service"],
  ["clinic", "Receiving clinic"],
] as const;
const emptySearch = { patient: "", service: "", clinic: "" };

function includesFilter(value: string, filter: string) {
  return value.toLowerCase().includes(filter.toLowerCase());
}

function ReferralQueue({ userId, clinicId }: { userId: string; clinicId?: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const [params, setParams] = useSearchParams();
  const filter = params.get("stage");
  const status = statusFilters.find((value) => value === filter) ?? "all";
  const [search, setSearch] = useState(emptySearch);
  const dialog = useRef<HTMLDialogElement>(null);

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const fields = new FormData(event.currentTarget);
    setSearch({
      patient: String(fields.get("patient") ?? "").trim(),
      service: String(fields.get("service") ?? "").trim(),
      clinic: String(fields.get("clinic") ?? "").trim(),
    });
    setStatus(String(fields.get("status") ?? "all"));
  }

  function setStatus(value: string) {
    const next = new URLSearchParams(params);
    if (value === "all") next.delete("stage");
    else next.set("stage", value);
    setParams(next, { replace: true });
    dialog.current?.close();
  }

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
          <ActionIcon name="refresh" />
          Try again
        </button>
      </div>
    );
  }

  const referrals = query.data;
  const visible = referrals.filter((referral) =>
    (status === "all" || (status === "MissingInformation" ? referral.status : referral.stage) === status) &&
    includesFilter(referral.patientFirstName + " " + referral.patientLastName, search.patient) &&
    includesFilter(referral.serviceName, search.service) &&
    includesFilter(referral.clinicName, search.clinic),
  );

  return (
    <>
      <div className="page-heading">
        <p className="text-sm text-subtle" role="status">
          {visible.length} of {referrals.length} referrals · {status === "all" ? "All statuses" : referralStatusLabels[status]}
        </p>
        <WorkspaceFilterDialog dialogRef={dialog} title="Filter referrals"
          onOpen={() => dialog.current?.querySelector("form")?.reset()}>
          <form className="space-y-4" onSubmit={applyFilters}>
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="grid gap-1 text-sm font-medium">
                Status
                <select key={status} name="status" defaultValue={status}
                  className="min-w-0 rounded-md border border-panel-border px-3 py-2">
                  <option value="all">All statuses</option>
                  {statusFilters.map((value) => (
                    <option key={value} value={value}>{referralStatusLabels[value]}</option>
                  ))}
                </select>
              </label>
              {searchFields.map(([name, label]) => (
                <label key={name} className="grid gap-1 text-sm font-medium">
                  {label}
                  <input name={name} type="search" maxLength={200} defaultValue={search[name]}
                    className="min-w-0 rounded-md border border-panel-border px-3 py-2" />
                </label>
              ))}
            </div>
            <div className="flex flex-wrap gap-2">
              <button type="submit"><ActionIcon name="check" />Apply filters</button>
              <button type="button" data-variant="secondary" onClick={() => {
                setSearch(emptySearch);
                setStatus("all");
              }}><ActionIcon name="reset" />Clear filters</button>
              <button type="button" data-variant="secondary" onClick={() => dialog.current?.close()}><ActionIcon name="close" />Close</button>
            </div>
          </form>
        </WorkspaceFilterDialog>
      </div>
      {visible.length === 0 ? (
        <p className="queue-empty">{referrals.length ? "No referrals match these filters." : "No referrals in this queue yet."}</p>
      ) : (
        <WorkspaceTable label="Referral queue" columns={["Patient", "Service and clinic", "Status", "Received"]}>
          {visible.map((referral) => (
            <tr key={referral.publicId} className="workspace-action-row">
              <td>
                <Link
                  to={{
                    pathname: "/referrals/" + referral.publicId,
                    search: clinicId ? new URLSearchParams({ clinic: clinicId }).toString() : "",
                  }}
                  className="workspace-row-action"
                  aria-label={"View referral for " + referral.patientFirstName + " " + referral.patientLastName + ": " + referral.serviceName}
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
        </WorkspaceTable>
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
        {query.data?.canCreate && <Link to="/referrals/new" className="workspace-button"><ActionIcon name="plus" />New referral</Link>}
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
