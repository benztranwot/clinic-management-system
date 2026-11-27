import { useWorkspacePermission } from "../auth/useWorkspacePermission";
import LoadingStatus, { Spinner } from "../components/LoadingStatus";
import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useLocation, useParams, useSearchParams } from "react-router";
import {
  getEligibleProviders,
  getReferral,
  referralErrorMessage,
  ReferralRequestError,
  saveReferralUpdate,
  type ReferralUpdate,
  type ReferralDetails,
  type ReferralStatus,
} from "../api/referrals";
import ReferralAppointmentPanel from "../features/referrals/components/ReferralAppointmentPanel";
import ReferralStatusBadge from "../features/referrals/components/ReferralStatusBadge";
import { useReferralAccount } from "../features/referrals/hooks/useReferralAccount";
import { ActionIcon } from "../components/ActionIcon";

type ReferralSnapshot = Pick<ReferralUpdate,
  "expectedProviderPublicId" | "expectedStatus" | "expectedMissingInformationNote">;

function ReferralRecord({
  userId,
  referral,
  publicId,
  canManage,
}: {
  userId: string;
  referral: ReferralDetails;
  publicId: string;
  canManage: boolean;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const { data: accountData } = useReferralAccount(userId);
  const [snapshot, setSnapshot] = useState<ReferralSnapshot | null>(null);
  const [providerId, setProviderId] = useState("");
  const [note, setNote] = useState("");
  const [status, setStatus] = useState<ReferralStatus | "">("");
  const [validationError, setValidationError] = useState("");
  const [notice, setNotice] = useState("");
  const editor = useRef<HTMLDialogElement>(null);
  const patientName = useRef<HTMLElement>(null);
  const editorTitle = useId();
  const referralStatusTitle = useId();
  const isOpenForReview = referral.stage !== "Closed" && referral.stage !== "Scheduled";
  const canReview = canManage && isOpenForReview && referral.canReview;
  const canAssign = canManage && isOpenForReview && referral.canReassign;
  const detailKey = ["referral", userId, publicId];
  const appointmentMutationKey = ["referral-appointment", userId, publicId];
  const appointmentWritePending = useIsMutating({ mutationKey: appointmentMutationKey, exact: true }) > 0;
  const eligibleKey = ["eligible-referral-providers", userId, publicId];

  const eligible = useQuery({
    queryKey: eligibleKey,
    enabled: canAssign && snapshot !== null,
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");
      return getEligibleProviders(accessToken, publicId, signal);
    },
    retry: false,
  });

  const change = useMutation({
    mutationFn: async (request: ReferralUpdate) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");
      await saveReferralUpdate(accessToken, publicId, request);
    },
    retry: false,
    onSuccess: async (_, request) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: detailKey }),
        queryClient.invalidateQueries({ queryKey: ["referrals", userId] }),
        queryClient.invalidateQueries({ queryKey: ["my-referrals", userId] }),
      ]);
      setSnapshot(null);
      setNotice(request.status === "Closed" ? "Referral closed." : "Referral updated.");
    },
    onError: async (error) => {
      if (error instanceof ReferralRequestError && [400, 404, 409].includes(error.status)) {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: detailKey }),
          queryClient.invalidateQueries({ queryKey: eligibleKey, exact: true }),
        ]);
      }
    },
  });

  useEffect(() => {
    const dialog = editor.current;
    if (!dialog) return;
    if (snapshot && !dialog.open) dialog.showModal();
    if (!snapshot && dialog.open) {
      dialog.close();
      if (document.activeElement === document.body) patientName.current?.focus();
    }
  }, [snapshot]);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(""), 5000);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const providerOptions = (eligible.data ?? []).filter(
    (provider) => provider.publicId !== referral.assignedProviderPublicId,
  );
  const selectedProvider = providerOptions.find((provider) => provider.publicId === providerId);
  const fieldClass = "mt-1 block w-full rounded-md border border-field-border px-3 py-2";
  const stale = snapshot !== null && (
    snapshot.expectedProviderPublicId !== referral.assignedProviderPublicId ||
    snapshot.expectedStatus !== referral.status ||
    snapshot.expectedMissingInformationNote !== referral.missingInformationNote
  );
  const providerChanged = canAssign && Boolean(selectedProvider);
  const account = accountData?.account;
  const retainsReview = account?.isAdministrator || (
    account?.providerPublicId === selectedProvider?.publicId &&
    account?.memberships.some(({ clinicPublicId, role }) =>
      clinicPublicId === referral.clinicPublicId && role === "Clinician")
  );
  const canSaveStatus = canReview && (!providerChanged || Boolean(retainsReview));
  const statusChanged = canSaveStatus && status !== "" && (
    providerChanged || status !== referral.status ||
    (status === "MissingInformation" && note.trim() !== referral.missingInformationNote)
  );
  const disabled = change.isPending || appointmentWritePending;

  function beginEdit() {
    setSnapshot({
      expectedProviderPublicId: referral.assignedProviderPublicId,
      expectedStatus: referral.status,
      expectedMissingInformationNote: referral.missingInformationNote,
    });
    setProviderId("");
    setStatus("");
    setNote(referral.missingInformationNote ?? "");
    setValidationError("");
    change.reset();
  }

  function cancelEdit() {
    if (change.isPending) return;
    setSnapshot(null);
    change.reset();
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!snapshot || stale || disabled || (!providerChanged && !statusChanged)) return;
    if (statusChanged && status === "MissingInformation" && !note.trim()) {
      setValidationError("Enter a note explaining what information is missing.");
      return;
    }
    if (statusChanged && status === "Closed" && !window.confirm("Close this referral? A closed referral cannot be reopened.")) return;

    setNotice("");
    setValidationError("");
    change.mutate({
      ...snapshot,
      providerPublicId: providerChanged ? selectedProvider?.publicId : undefined,
      status: statusChanged && status ? status : undefined,
      missingInformationNote: statusChanged && status === "MissingInformation" ? note.trim() : undefined,
    });
  }

  return (
    <div className="mt-4 space-y-4">
      <dl className="grid gap-4 rounded-lg border border-panel-border p-4 sm:grid-cols-2">
        <div>
          <dt className="font-medium">Patient</dt>
          <dd ref={patientName} tabIndex={-1}>{referral.patientFirstName} {referral.patientLastName}</dd>
        </div>
        <div>
          <dt className="font-medium">Status</dt>
          <dd>
            <ReferralStatusBadge
              status={referral.status === "MissingInformation" ? referral.status : referral.stage}
              showStatusLabel={false}
            />
          </dd>
        </div>
        <div><dt className="font-medium">Receiving clinic</dt><dd>{referral.clinicName}</dd></div>
        <div><dt className="font-medium">Clinical service</dt><dd>{referral.serviceName}</dd></div>
        <div><dt className="font-medium">Receiving provider</dt><dd>{referral.assignedProviderName}</dd></div>
        <div><dt className="font-medium">Referred by</dt><dd>{referral.referringProviderName}</dd></div>
        <div className="sm:col-span-2 border-t border-panel-border pt-3">
          <dt className="font-medium">Reason for referral</dt>
          <dd className="whitespace-pre-wrap wrap-break-word">{referral.reason}</dd>
        </div>
        {referral.missingInformationNote && (
          <div className="sm:col-span-2">
            <dt className="font-medium">Missing information note</dt>
            <dd className="whitespace-pre-wrap wrap-break-word">{referral.missingInformationNote}</dd>
          </div>
        )}
      </dl>

      {(canAssign || canReview) && (
        <section aria-labelledby={referralStatusTitle} className="rounded-lg border border-panel-border p-4">
          <div className="page-heading">
            <h2 id={referralStatusTitle} className="font-semibold">Referral status</h2>
            <button type="button" data-variant="secondary" aria-haspopup="dialog" onClick={beginEdit} disabled={disabled}>
              <ActionIcon name="pencil" />Edit referral
            </button>
          </div>
        </section>
      )}

      <dialog ref={editor} className="workspace-dialog workspace-panel" aria-labelledby={editorTitle}
        onCancel={(event) => { event.preventDefault(); cancelEdit(); }}>
        {snapshot && (
          <form className="space-y-4" onSubmit={submit}
            onChange={() => {
              if (validationError) setValidationError("");
              if (change.isError) change.reset();
            }}>
            <h2 id={editorTitle} className="text-lg font-semibold">Edit referral</h2>
            <fieldset className="grid gap-4 sm:grid-cols-2" disabled={disabled || stale}>
              {canAssign && (
                <div className="space-y-2">
                  <label className="block text-sm font-medium">
                    Receiving provider
                    <select className={fieldClass} value={selectedProvider?.publicId ?? ""}
                      onChange={(event) => setProviderId(event.target.value)}
                      disabled={!eligible.isSuccess || providerOptions.length === 0}>
                      <option value="">Keep current provider</option>
                      {providerOptions.map((provider) => (
                        <option key={provider.publicId} value={provider.publicId}>{provider.lastName}, {provider.firstName}</option>
                      ))}
                    </select>
                  </label>
                  {eligible.isPending && <LoadingStatus>Loading eligible providers…</LoadingStatus>}
                  {eligible.isError && (
                    <div className="space-y-2">
                      <p role="alert">{referralErrorMessage(eligible.error, "Unable to load eligible providers.")}</p>
                      <button type="button" data-variant="secondary" disabled={eligible.isFetching}
                        onClick={() => void eligible.refetch()}><ActionIcon name="refresh" />Try again</button>
                    </div>
                  )}
                  {eligible.isSuccess && providerOptions.length === 0 && <p>No other eligible providers are available.</p>}
                  {providerChanged && canReview && !canSaveStatus && (
                    <p className="text-sm text-subtle">The receiving provider can review this referral after reassignment.</p>
                  )}
                  {providerChanged && referral.stage === "ReadyToSchedule" && !statusChanged && (
                    <p className="text-sm text-subtle">Changing the provider returns the referral to pending review.</p>
                  )}
                </div>
              )}
              {canSaveStatus && (
                <label className="block text-sm font-medium">
                  Status
                  <select className={fieldClass} value={status}
                    onChange={(event) => setStatus(event.target.value as ReferralStatus | "")}>
                    <option value="">{providerChanged ? "Apply reassignment rules" : "Keep current status"}</option>
                    <option value="PendingReview">Pending review</option>
                    <option value="MissingInformation">Missing information</option>
                    <option value="ReadyToSchedule">Ready to schedule</option>
                    <option value="Closed">Closed</option>
                  </select>
                </label>
              )}
              {canSaveStatus && status === "MissingInformation" && (
                <label className="block text-sm font-medium sm:col-span-2">
                  Missing information note
                  <textarea className={fieldClass} value={note} onChange={(event) => setNote(event.target.value)}
                    maxLength={1000} rows={3} required />
                </label>
              )}
            </fieldset>
            {stale && <p role="alert">This referral changed. Cancel and reopen the editor.</p>}
            {validationError && <p role="alert">{validationError}</p>}
            {change.isError && !stale && (
              <p role="alert">{referralErrorMessage(change.error, "Unable to confirm the change. Check the referral before trying again.")}</p>
            )}
            <div className="flex flex-wrap justify-end gap-2">
              <button type="button" data-variant="secondary" disabled={change.isPending}
                onClick={cancelEdit}>Cancel</button>
              <button type="submit" disabled={disabled || stale || (!providerChanged && !statusChanged)}>
                {change.isPending ? <Spinner /> : <ActionIcon name="save" />}
                {change.isPending ? "Saving…" : "Save changes"}
              </button>
            </div>
          </form>
        )}
      </dialog>

      <ReferralAppointmentPanel
        key={referral.assignedProviderPublicId}
        referral={referral}
        userId={userId}
        referralWritePending={change.isPending}
        mutationKey={appointmentMutationKey}
      />

      <div className="workspace-toast" role="status" aria-atomic="true">
        {notice && <p>{notice}</p>}
      </div>
    </div>
  );
}

function ReferralDetailsContent({
  userId,
  publicId,
  clinicPublicId,
}: {
  userId: string;
  publicId: string;
  clinicPublicId: string | null;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const query = useQuery({
    queryKey: ["referral", userId, publicId, clinicPublicId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token) throw new Error("Auth0 did not return an access token.");
      const referral = await getReferral(token.access_token, publicId, signal, clinicPublicId);

      return {
        referral,
        canManage: token.scope?.split(/\s+/).includes("manage:referrals") ?? false,
      };
    },
    retry: false,
  });

  const workspaceCanManage = useWorkspacePermission(userId, "manage:referrals");
  const result = query.isSuccess ? { ...query.data, canManage: workspaceCanManage || query.data.canManage } : undefined;
  const notFound = query.error instanceof ReferralRequestError && query.error.status === 404;

  return (
    <>
      <h1 id="referral-details-heading" className="page-title">Referral details</h1>
      {query.isPending && <LoadingStatus className="mt-3">Loading referral…</LoadingStatus>}
      {query.isError && (
        notFound ? (
          <p className="mt-3" role="alert">Referral not found.</p>
        ) : (
          <div className="mt-3 space-y-2">
            <p role="alert">{referralErrorMessage(query.error, "Unable to load this referral.")}</p>
            <button type="button" data-variant="secondary" disabled={query.isFetching} onClick={() => void query.refetch()}><ActionIcon name="refresh" />Try again</button>
          </div>
        )
      )}
      {result && <ReferralRecord userId={userId} publicId={publicId} {...result} />}
    </>
  );
}

function ReferralDetailsPage() {
  const { user } = useAuth0();
  const { publicId } = useParams<"publicId">();
  const { state } = useLocation();
  const [searchParams] = useSearchParams();
  const clinicPublicId = searchParams.get("clinic") || null;
  const queueState = user?.sub && state?.referralQueueOwner === user.sub ? state : undefined;
  const backSearch = queueState?.referralQueueParams ?? (clinicPublicId
    ? new URLSearchParams({ clinic: clinicPublicId }).toString() : "");

  return (
    <div className="mx-auto w-full max-w-4xl space-y-3">
      <Link to={{ pathname: "/referrals", search: backSearch }} state={queueState}
        className="workspace-button" data-variant="secondary">
        <ActionIcon name="back" />
        Back to referrals
      </Link>
      <section className="workspace-panel" aria-labelledby="referral-details-heading">
        {user?.sub && publicId ? (
          <ReferralDetailsContent
            key={[user.sub, publicId, clinicPublicId].join(":")}
            userId={user.sub}
            publicId={publicId}
            clinicPublicId={clinicPublicId}
          />
        ) : (
          <>
            <h1 id="referral-details-heading" className="page-title">Referral details</h1>
            <p className="mt-3" role="alert">Unable to open this referral.</p>
          </>
        )}
      </section>
    </div>
  );
}

export default ReferralDetailsPage;
