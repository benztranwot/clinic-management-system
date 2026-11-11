import { useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams, useSearchParams } from "react-router";
import { getProvider } from "../api/providers";
import {
  assignReferralProvider,
  getEligibleProviders,
  getReferral,
  referralErrorMessage,
  ReferralRequestError,
  updateReferralStatus,
  type ReferralDetails,
  type ReferralStatus,
} from "../api/referrals";
import ReferralAppointmentPanel from "../components/referrals/ReferralAppointmentPanel";
import ReferralStatusBadge from "../components/referrals/ReferralStatusBadge";

type ReferralChange =
  | { action: "assign"; providerPublicId: string }
  | { action: "status"; status: ReferralStatus; note: string | null };

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
  const [providerId, setProviderId] = useState("");
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
    enabled: canAssign,
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");
      return getEligibleProviders(accessToken, publicId, signal);
    },
    retry: false,
  });

  const change = useMutation({
    mutationFn: async (request: ReferralChange) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");

      if (request.action === "assign") {
        await assignReferralProvider(accessToken, publicId, request.providerPublicId);
      } else {
        await updateReferralStatus(accessToken, publicId, request.status, request.note);
      }
    },
    retry: false,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: detailKey }),
        queryClient.invalidateQueries({ queryKey: ["referrals", userId] }),
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

  function clearChangeError(action: ReferralChange["action"]) {
    if (change.isError && change.variables?.action === action) {
      change.reset();
    }
  }

  function submitChange(request: ReferralChange) {
    if (change.isPending || appointmentWritePending || (request.action === "assign" ? !canAssign : !canReview)) return;

    setNotice("");
    setValidationError("");
    change.mutate(request, {
      onSuccess: () => {
        if (request.action === "assign") {
          setProviderId("");
          setNotice("Receiving provider updated.");
        } else {
          setStatus("");
          setNotice(request.status === "Closed" ? "Referral closed." : "Status updated.");
        }
      },
    });
  }

  function handleStatus(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!status) return;

    const note = status === "MissingInformation"
      ? String(new FormData(event.currentTarget).get("note") ?? "").trim()
      : null;

    if (status === "MissingInformation" && !note) {
      setValidationError("Enter a note explaining what information is missing.");
      return;
    }
    if (status === "Closed" && !window.confirm("Close this referral? A closed referral cannot be reopened.")) return;
    submitChange({ action: "status", status, note });
  }

  const providerOptions = (eligible.data ?? []).filter(
    (provider) => provider.publicId !== referral.assignedProviderPublicId,
  );
  const selectedProvider = providerOptions.find((provider) => provider.publicId === providerId);
  const fieldClass = "mt-1 block w-full rounded-md border border-field-border px-3 py-2";

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

      {canAssign && (
        <section aria-label="Provider assignment" className="space-y-2 border-t border-panel-border pt-4">
          <h3 className="font-semibold">Reassign receiving provider</h3>
          {referral.stage === "ReadyToSchedule" && <p>The new provider will need to review this referral.</p>}
          {eligible.isPending && <p role="status">Loading eligible providers…</p>}
          {eligible.isError && (
            <div className="space-y-2">
              <p role="alert">{referralErrorMessage(eligible.error, "Unable to load eligible providers.")}</p>
              <button type="button" data-variant="secondary" disabled={eligible.isFetching} onClick={() => void eligible.refetch()}>
                Try again
              </button>
            </div>
          )}
          {eligible.isSuccess && (
            providerOptions.length === 0 ? (
              <p>No other eligible providers are available.</p>
            ) : (
              <form
                className="space-y-2"
                onSubmit={(event) => {
                  event.preventDefault();
                  if (selectedProvider) {
                    submitChange({ action: "assign", providerPublicId: selectedProvider.publicId });
                  }
                }}
              >
                <label className="block text-sm font-medium">
                  Eligible provider
                  <select
                    className={fieldClass}
                    value={selectedProvider?.publicId ?? ""}
                    onChange={(event) => {
                      setProviderId(event.target.value);
                      clearChangeError("assign");
                    }}
                    disabled={change.isPending || appointmentWritePending}
                    required
                  >
                    <option value="">Choose a provider</option>
                    {providerOptions.map((provider) => (
                      <option key={provider.publicId} value={provider.publicId}>
                        {provider.lastName}, {provider.firstName}
                      </option>
                    ))}
                  </select>
                </label>
                <button type="submit" disabled={change.isPending || appointmentWritePending || !selectedProvider}>
                  {change.isPending && change.variables.action === "assign" ? "Saving…" : "Reassign provider"}
                </button>
              </form>
            )
          )}
        </section>
      )}

      {canReview && (
        <form className="space-y-2 border-t border-panel-border pt-4" onSubmit={handleStatus}>
          <h3 className="font-semibold">Update status</h3>
          <fieldset className="space-y-2" disabled={change.isPending || appointmentWritePending}>
            <label className="block text-sm font-medium">
              New status
              <select
                className={fieldClass}
                value={status}
                onChange={(event) => {
                  setStatus(event.target.value as ReferralStatus | "");
                  setValidationError("");
                  clearChangeError("status");
                }}
                required
              >
                <option value="">Choose a status</option>
                <option value="PendingReview">Pending review</option>
                <option value="MissingInformation">Missing information</option>
                <option value="ReadyToSchedule">Ready to schedule</option>
                <option value="Closed">Closed</option>
              </select>
            </label>
            {status === "MissingInformation" && (
              <label className="block text-sm font-medium">
                Missing information note
                <textarea
                  name="note"
                  className={fieldClass}
                  defaultValue={referral.missingInformationNote ?? ""}
                  maxLength={1000}
                  onChange={() => {
                    setValidationError("");
                    clearChangeError("status");
                  }}
                  rows={3}
                  required
                />
              </label>
            )}
            {validationError && <p role="alert">{validationError}</p>}
            <button
              type="submit"
              data-variant={status === "Closed" ? "danger" : undefined}
              disabled={!status}
            >
              {change.isPending && change.variables.action === "status"
                ? "Saving…"
                : status === "Closed" ? "Close referral" : "Save status"}
            </button>
          </fieldset>
        </form>
      )}

      <ReferralAppointmentPanel
        key={referral.assignedProviderPublicId}
        referral={referral}
        userId={userId}
        referralWritePending={change.isPending}
        mutationKey={appointmentMutationKey}
      />

      {change.isError && (
        <p role="alert">{referralErrorMessage(change.error, "Unable to confirm the change. Check the referral before trying again.")}</p>
      )}
      {notice && (
        <div className="flex items-center gap-3">
          <p role="status">{notice}</p>
          <button type="button" data-variant="secondary" aria-label="Dismiss referral message" onClick={() => setNotice("")}>Dismiss</button>
        </div>
      )}
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
            <button type="button" data-variant="secondary" disabled={query.isFetching} onClick={() => void query.refetch()}>Try again</button>
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
          <span className="flex items-center gap-2">
            <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M19 12H5m7 7-7-7 7-7" />
            </svg>
            Back to referrals
          </span>
        </Link>
        {appointmentPublicId && (
          <Link
            className="workspace-button ml-auto"
            to={"/appointments/" + appointmentPublicId}
            state={{ fromReferral: true }}
          >
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
