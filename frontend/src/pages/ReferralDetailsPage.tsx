import { useEffect, useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams, useSearchParams } from "react-router";
import { getProvider } from "../api/providers";
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
import ReferralAppointmentPanel from "../components/referrals/ReferralAppointmentPanel";
import ReferralStatusBadge from "../components/referrals/ReferralStatusBadge";
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
  const [snapshot, setSnapshot] = useState<ReferralSnapshot | null>(null);
  const [providerId, setProviderId] = useState("");
  const [note, setNote] = useState("");
  const [status, setStatus] = useState<ReferralStatus | "">("");
  const [validationError, setValidationError] = useState("");
  const [notice, setNotice] = useState("");
  const isOpenForReview = referral.stage !== "Closed" && referral.stage !== "Scheduled";
  const canReview = canManage && isOpenForReview && referral.canReview;
  const canAssign = canManage && isOpenForReview && referral.canReassign;
  const detailKey = ["referral", userId, publicId];
  const appointmentMutationKey = ["referral-appointment", userId, publicId];
  const appointmentWritePending = useIsMutating({ mutationKey: appointmentMutationKey, exact: true }) > 0;
  const eligibleKey = ["eligible-referral-providers", userId, publicId];

  const assignedProvider = useQuery({
    queryKey: ["provider", userId, referral.assignedProviderPublicId],
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) {
        throw new Error("Unable to load the assigned provider.");
      }
      return getProvider(accessToken, referral.assignedProviderPublicId, signal);
    },
    retry: false,
  });

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
      setSnapshot(null);
      setNotice(request.status === "Closed" ? "Referral closed." : "Referral updated.");
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: detailKey }),
        queryClient.invalidateQueries({ queryKey: ["referrals", userId] }),
        queryClient.invalidateQueries({ queryKey: ["my-referrals", userId] }),
      ]);
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
  const statusChanged = canReview && status !== "" && (
    status !== referral.status || (status === "MissingInformation" && note.trim() !== referral.missingInformationNote)
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

  const assignedName = assignedProvider.data
    ? assignedProvider.data.firstName + " " + assignedProvider.data.lastName
    : assignedProvider.isPending ? "Loading provider…" : "Assigned (name unavailable)";

  return (
    <div className="mt-4 space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-lg font-semibold">{referral.patientFirstName} {referral.patientLastName}</h2>
        <div className="flex flex-wrap items-center gap-2">
          <ReferralStatusBadge status={referral.stage} />
          {referral.status === "MissingInformation" && (
            <span className="text-sm font-medium text-subtle">Missing information</span>
          )}
        </div>
      </div>
      <dl className="grid gap-3 sm:grid-cols-2">
        <div><dt className="font-medium">Receiving clinic</dt><dd>{referral.clinicName}</dd></div>
        <div><dt className="font-medium">Clinical service</dt><dd>{referral.serviceName}</dd></div>
        <div><dt className="font-medium">Assigned provider</dt><dd>{assignedName}</dd></div>
        <div><dt className="font-medium">Referring provider</dt><dd>{referral.referringProviderName}</dd></div>
        <div className="sm:col-span-2"><dt className="font-medium">Reason</dt><dd className="whitespace-pre-wrap">{referral.reason}</dd></div>
        {referral.missingInformationNote && (
          <div className="sm:col-span-2">
            <dt className="font-medium">Missing information note</dt>
            <dd className="whitespace-pre-wrap">{referral.missingInformationNote}</dd>
          </div>
        )}
      </dl>

      {(canAssign || canReview || snapshot) && (
        <div className="border-t border-panel-border pt-4">
          {snapshot ? (
            <form className="space-y-4" onSubmit={submit}
              onChange={() => {
                if (validationError) setValidationError("");
                if (change.isError) change.reset();
              }}>
              <h3 className="font-semibold">Edit referral</h3>
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
                    {eligible.isPending && <p role="status">Loading eligible providers…</p>}
                    {eligible.isError && (
                      <div className="space-y-2">
                        <p role="alert">{referralErrorMessage(eligible.error, "Unable to load eligible providers.")}</p>
                        <button type="button" data-variant="secondary" disabled={eligible.isFetching}
                          onClick={() => void eligible.refetch()}><ActionIcon name="refresh" />Try again</button>
                      </div>
                    )}
                    {eligible.isSuccess && providerOptions.length === 0 && <p>No other eligible providers are available.</p>}
                    {providerChanged && referral.stage === "ReadyToSchedule" && !statusChanged && (
                      <p className="text-sm text-subtle">Changing the provider returns the referral to pending review.</p>
                    )}
                  </div>
                )}
                {canReview && (
                  <label className="block text-sm font-medium">
                    Status
                    <select className={fieldClass} value={status}
                      onChange={(event) => setStatus(event.target.value as ReferralStatus | "")}>
                      <option value="">Keep current status</option>
                      <option value="PendingReview">Pending review</option>
                      <option value="MissingInformation">Missing information</option>
                      <option value="ReadyToSchedule">Ready to schedule</option>
                      <option value="Closed">Closed</option>
                    </select>
                  </label>
                )}
                {canReview && status === "MissingInformation" && (
                  <label className="block text-sm font-medium sm:col-span-2">
                    Missing information note
                    <textarea className={fieldClass} value={note} onChange={(event) => setNote(event.target.value)}
                      maxLength={1000} rows={3} required />
                  </label>
                )}
              </fieldset>
              {stale && <p role="alert">This referral changed. Cancel changes and reopen the editor.</p>}
              {validationError && <p role="alert">{validationError}</p>}
              {change.isError && !stale && (
                <p role="alert">{referralErrorMessage(change.error, "Unable to confirm the change. Check the referral before trying again.")}</p>
              )}
              <div className="flex flex-wrap gap-2">
                <button type="button" data-variant="secondary" disabled={change.isPending}
                  onClick={() => { setSnapshot(null); change.reset(); }}><ActionIcon name="close" />Cancel changes</button>
                <button type="submit" disabled={disabled || stale || (!providerChanged && !statusChanged)}>
                  <ActionIcon name="pencil" />
                  {change.isPending ? "Saving…" : "Update referral"}
                </button>
              </div>
            </form>
          ) : (
            <button type="button" onClick={beginEdit} disabled={disabled}><ActionIcon name="pencil" />Edit referral</button>
          )}
        </div>
      )}

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

  const result = query.isSuccess ? query.data : undefined;
  const notFound = query.error instanceof ReferralRequestError && query.error.status === 404;

  return (
    <>
      <ReferralDetailsHeader appointmentPublicId={result?.referral.confirmedAppointment?.publicId} />
      {query.isPending && <p className="mt-3" role="status">Loading referral…</p>}
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

function ReferralDetailsHeader({ appointmentPublicId }: { appointmentPublicId?: string }) {
  return (
    <>
      <div className="page-heading">
        <Link to="/referrals" className="workspace-button" data-variant="secondary">
          <ActionIcon name="back" />
          Back to referrals
        </Link>
        {appointmentPublicId && (
          <Link
            className="workspace-button ml-auto"
            to={"/appointments/" + appointmentPublicId}
            state={{ fromReferral: true }}
          >
            <ActionIcon name="eye" />
            View appointment
          </Link>
        )}
      </div>
      <h1 id="referral-details-heading" className="page-title mt-3">Referral details</h1>
    </>
  );
}

function ReferralDetailsPage() {
  const { user } = useAuth0();
  const { publicId } = useParams<"publicId">();
  const [searchParams] = useSearchParams();
  const clinicPublicId = searchParams.get("clinic") || null;

  return (
    <section
      aria-labelledby="referral-details-heading"
      className="workspace-panel mx-auto w-full max-w-5xl"
    >
      {user?.sub && publicId ? (
        <ReferralDetailsContent
          key={[user.sub, publicId, clinicPublicId].join(":")}
          userId={user.sub}
          publicId={publicId}
          clinicPublicId={clinicPublicId}
        />
      ) : (
        <>
          <ReferralDetailsHeader />
          <p className="mt-3" role="alert">Unable to open this referral.</p>
        </>
      )}
    </section>
  );
}

export default ReferralDetailsPage;
