import LoadingStatus from "../components/LoadingStatus";
import WorkspacePagination from "../components/WorkspacePagination";
import { readPage } from "../utils/readPage";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useLocation, useSearchParams } from "react-router";
import {
  searchReferrals,
  referralErrorMessage,
  referralStatusLabels,
  type ReferralAccount,
} from "../api/referrals";
import ReferralAccountFeedback from "../features/referrals/components/ReferralAccountFeedback";
import WorkspaceFilterDialog from "../components/WorkspaceFilterDialog";
import WorkspaceTable from "../components/WorkspaceTable";
import ReferralStatusBadge from "../features/referrals/components/ReferralStatusBadge";
import { useReferralAccount } from "../features/referrals/hooks/useReferralAccount";
import { ActionIcon } from "../components/ActionIcon";

const statusFilters = ["PendingReview", "MissingInformation", "ReadyToSchedule", "Scheduled", "Closed"] as const;
const searchFields = [
  ["patient", "Patient"],
  ["service", "Service"],
  ["clinic", "Receiving clinic"],
] as const;
const emptySearch = { patient: "", service: "", clinic: "" };

function ReferralQueue({ userId, clinicId, canCreate }: { userId: string; clinicId?: string; canCreate: boolean }) {
  const { getAccessTokenSilently } = useAuth0();
  const [params, setParams] = useSearchParams();
  const page = readPage(params.get("page"));
  const filter = params.get("stage");
  const status = statusFilters.find((value) => value === filter) ?? "all";
  const { state } = useLocation();
  const search = state?.referralQueueOwner === userId ? (state.referralQueueSearch as typeof emptySearch | undefined) ?? emptySearch : emptySearch;
  const hasFilters = status !== "all" || Boolean(search.patient || search.service || search.clinic);
  const dialog = useRef<HTMLDialogElement>(null);

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const fields = new FormData(event.currentTarget);
    updateFilters(String(fields.get("status") ?? "all"), {
      patient: String(fields.get("patient") ?? "").trim(),
      service: String(fields.get("service") ?? "").trim(),
      clinic: String(fields.get("clinic") ?? "").trim(),
    });
  }

  function updateFilters(value: string, nextSearch = emptySearch) {
    const next = new URLSearchParams(params);
    next.delete("page");
    if (value === "all") next.delete("stage");
    else next.set("stage", value);
    setParams(next, {
      replace: true,
      state: { referralQueueOwner: userId, referralQueueSearch: nextSearch },
    });
    dialog.current?.close();
  }

  const query = useQuery({
    queryKey: ["referrals", userId, clinicId ?? "all", status, search, page],
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");

      return searchReferrals(accessToken, { ...search, stage: status, page }, signal, clinicId);
    },
    retry: false,
  });

  if (query.isPending) return <LoadingStatus>Loading referrals…</LoadingStatus>;
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

  const visible = query.data.items;
  const total = query.data.totalCount;

  return (
    <>
      <div className="page-heading">
        <div>
          <h1 id="referrals-heading" className="page-title">Referral queue</h1>
        </div>
        <div className="flex flex-wrap gap-2">
          {hasFilters && (
            <button type="button" data-variant="secondary" onClick={() => updateFilters("all")}>
              <ActionIcon name="reset" />Clear filters
            </button>
          )}
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
                <button type="button" data-variant="secondary" onClick={() => updateFilters("all")}>
                  <ActionIcon name="reset" />Clear filters
                </button>
                <button type="button" data-variant="secondary" onClick={() => dialog.current?.close()}><ActionIcon name="close" />Close</button>
              </div>
            </form>
          </WorkspaceFilterDialog>
          {canCreate && <Link to={{ pathname: "/referrals/new", search: params.toString() }}
            state={{ referralQueueOwner: userId, referralQueueSearch: search }} className="workspace-button">
            <ActionIcon name="plus" />New referral
          </Link>}
        </div>
      </div>
      {visible.length === 0 ? (
        <p className="queue-empty">{hasFilters ? "No referrals match these filters." : "No referrals on this page."}</p>
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
                  state={{
                    referralQueueOwner: userId,
                    referralQueueSearch: search,
                    referralQueueParams: params.toString(),
                  }}
                  className="workspace-row-action"
                  aria-label={"View referral for " + referral.patientFirstName + " " + referral.patientLastName + ": " + referral.serviceName}
                >
                  {referral.patientFirstName} {referral.patientLastName}
                </Link>
              </td>
              <td>{referral.serviceName}<span className="queue-meta">{referral.clinicName}</span></td>
              <td>
                <ReferralStatusBadge status={referral.status === "MissingInformation" ? referral.status : referral.stage} />
              </td>
              <td><time dateTime={referral.createdAtUtc}>{new Date(referral.createdAtUtc).toLocaleDateString()}</time></td>
            </tr>
          ))}
        </WorkspaceTable>
      )}
      <WorkspacePagination itemCount={visible.length} page={page} pageSize={query.data.pageSize} count={total} hasMore={query.data.hasMore} busy={query.isFetching}
        label="Referral pages" onPage={value => {
          const next = new URLSearchParams(params);
          if (value === 1) next.delete("page"); else next.set("page", String(value));
          setParams(next, { state: { referralQueueOwner: userId, referralQueueSearch: search } });
        }} />
    </>
  );
}

function StaffQueue({ userId, account }: { userId: string; account: ReferralAccount }) {
  const clinics = [...new Map(account.memberships.map((membership) => [
    membership.clinicPublicId, membership.clinicName,
  ])).entries()];
  const [params, setParams] = useSearchParams();
  const { state } = useLocation();
  const activeClinic = clinics.find(([id]) => id === params.get("clinic")) ?? clinics[0];

  function selectClinic(clinicId: string) {
    const next = new URLSearchParams(params);
    next.delete("page");
    next.set("clinic", clinicId);
    setParams(next, { state });
  }

  if (account.isAdministrator) return <ReferralQueue userId={userId} canCreate={account.canCreateReferrals} />;
  if (!activeClinic) return <p>Your account has no staff clinic membership.</p>;

  const [clinicId, clinicName] = activeClinic;
  return (
    <>
      {clinics.length === 1 ? (
        <p className="text-sm font-medium">{clinicName}</p>
      ) : (
        <label className="queue-filter">
          <span>Clinic</span>
          <select value={clinicId} onChange={(event) => selectClinic(event.target.value)}>
            {clinics.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
          </select>
        </label>
      )}
      <ReferralQueue userId={userId} clinicId={clinicId} canCreate={account.canCreateReferrals} />
    </>
  );
}

function ReferralsContent({ userId }: { userId: string }) {
  const query = useReferralAccount(userId);
  const { state } = useLocation();
  const [showCreated, setShowCreated] = useState(() => state?.referralCreatedBy === userId);
  useEffect(() => {
    if (!showCreated) return;
    const timer = window.setTimeout(() => setShowCreated(false), 5000);
    return () => window.clearTimeout(timer);
  }, [showCreated]);

  return (
    <section className="workspace-panel workspace-list-panel referral-queue-panel space-y-4" aria-labelledby="referrals-heading">
      {query.isSuccess
        ? <StaffQueue userId={userId} account={query.data.account} />
        : <ReferralAccountFeedback query={query} />}
      <div className="workspace-toast" role="status" aria-atomic="true">
        {showCreated && <p>Referral sent to the receiving clinic.</p>}
      </div>
    </section>
  );
}

export default function ReferralsPage() {
  const { user } = useAuth0();
  return user?.sub
    ? <ReferralsContent key={user.sub} userId={user.sub} />
    : <p role="alert">We couldn’t identify your session.</p>;
}
