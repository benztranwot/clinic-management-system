import { useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useParams } from "react-router";
import { getProvider } from "../api/providers";
import {
  assignReferralProvider,
  getEligibleProviders,
  getReferral,
  referralErrorMessage,
  ReferralRequestError,
  referralStatusLabels,
  updateReferralStatus,
  type ReferralDetails,
  type ReferralStatus,
} from "../api/referrals";

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
  const canChange = canManage && referral.status !== "Closed";
  const detailKey = ["referral", userId, publicId];
  const eligibleKey = ["eligible-referral-providers", userId, publicId];

  const assignedProvider = useQuery({
    queryKey: ["provider", userId, referral.assignedProviderPublicId],
    enabled: referral.assignedProviderPublicId !== null,
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken || !referral.assignedProviderPublicId) {
        throw new Error("Unable to load the assigned provider.");
      }
      return getProvider(accessToken, referral.assignedProviderPublicId, signal);
    },
    retry: false,
  });

  const eligible = useQuery({
    queryKey: eligibleKey,
    enabled: canChange,
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
        queryClient.invalidateQueries({ queryKey: detailKey, exact: true }),
        queryClient.invalidateQueries({ queryKey: ["referrals", userId], exact: true }),
      ]);
    },
    onError: async (error) => {
      if (error instanceof ReferralRequestError && [400, 404, 409].includes(error.status)) {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: detailKey, exact: true }),
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
    if (change.isPending || !canChange) return;

    setNotice("");
    setValidationError("");
    change.mutate(request, {
      onSuccess: () => {
        if (request.action === "assign") {
          setProviderId("");
          setNotice("Provider assigned.");
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
    if (status === "ReadyToSchedule" && !referral.assignedProviderPublicId) return;
    if (status === "Closed" && !window.confirm("Close this referral? A closed referral cannot be reopened.")) return;
    submitChange({ action: "status", status, note });
  }

  const providerOptions = (eligible.data ?? []).filter(
    (provider) => provider.publicId !== referral.assignedProviderPublicId,
  );
  const selectedProvider = providerOptions.find((provider) => provider.publicId === providerId);
  const fieldClass = "mt-1 block w-full rounded-md border border-slate-400 px-3 py-2";

  let assignedName = "Unassigned";
  if (referral.assignedProviderPublicId) {
    assignedName = assignedProvider.isPending ? "Loading provider…" : "Assigned (name unavailable)";
    if (assignedProvider.data) {
      assignedName = assignedProvider.data.firstName + " " + assignedProvider.data.lastName;
    }
  }

  return (
    <div className="mt-4 space-y-4">
      <h3 className="font-semibold">{referral.patientFirstName} {referral.patientLastName}</h3>
      <dl className="space-y-3">
        <div><dt className="font-medium">Receiving clinic</dt><dd>{referral.clinicName}</dd></div>
        <div><dt className="font-medium">Clinical service</dt><dd>{referral.serviceName}</dd></div>
        <div><dt className="font-medium">Status</dt><dd>{referralStatusLabels[referral.status]}</dd></div>
        <div><dt className="font-medium">Assigned provider</dt><dd>{assignedName}</dd></div>
        <div><dt className="font-medium">Referring provider</dt><dd>{referral.referringProviderName}</dd></div>
        <div><dt className="font-medium">Reason</dt><dd className="whitespace-pre-wrap">{referral.reason}</dd></div>
        {referral.missingInformationNote && (
          <div>
            <dt className="font-medium">Missing information note</dt>
            <dd className="whitespace-pre-wrap">{referral.missingInformationNote}</dd>
          </div>
        )}
      </dl>

      {canChange && (
        <>
          <section aria-label="Provider assignment" className="space-y-2 border-t border-slate-200 pt-4">
            <h4 className="font-semibold">Provider assignment</h4>
            {eligible.isPending && <p role="status">Loading eligible providers…</p>}
            {eligible.isError && (
              <div className="space-y-2">
                <p role="alert">{referralErrorMessage(eligible.error, "Unable to load eligible providers.")}</p>
                <button type="button" disabled={eligible.isFetching} onClick={() => void eligible.refetch()}>
                  Try again
                </button>
              </div>
            )}
            {eligible.isSuccess && (
              providerOptions.length === 0 ? (
                referral.assignedProviderPublicId ? (
                  <p>No other eligible providers are available.</p>
                ) : (
                  <div className="space-y-2 rounded-md bg-slate-50 p-3">
                    <p>No providers at {referral.clinicName} offer {referral.serviceName} yet.</p>
                    <p>Open a provider and assign this service under Assigned services, then return here.</p>
                    <Link
                      to={"/clinics/" + referral.clinicPublicId + "/providers"}
                      className="inline-block font-medium text-blue-700 underline"
                    >
                      View providers at {referral.clinicName}
                    </Link>
                  </div>
                )
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
                      disabled={change.isPending}
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
                  <button type="submit" disabled={change.isPending || !selectedProvider}>
                    {change.isPending && change.variables.action === "assign" ? "Assigning…" : "Assign provider"}
                  </button>
                </form>
              )
            )}
          </section>

          <form className="space-y-2 border-t border-slate-200 pt-4" onSubmit={handleStatus}>
            <h4 className="font-semibold">Update status</h4>
            <fieldset className="space-y-2" disabled={change.isPending}>
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
                  <option value="ReadyToSchedule" disabled={!referral.assignedProviderPublicId}>Ready to schedule</option>
                  <option value="Closed">Closed</option>
                </select>
              </label>
              {!referral.assignedProviderPublicId && <p>Assign a provider before marking this referral ready to schedule.</p>}
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
                disabled={!status || (status === "ReadyToSchedule" && !referral.assignedProviderPublicId)}
              >
                {change.isPending && change.variables.action === "status" ? "Saving…" : "Save status"}
              </button>
            </fieldset>
          </form>
        </>
      )}

      {change.isError && (
        <p role="alert">{referralErrorMessage(change.error, "Unable to confirm the change. Check the referral before trying again.")}</p>
      )}
      {notice && (
        <div className="flex items-center gap-3">
          <p role="status">{notice}</p>
          <button type="button" aria-label="Dismiss referral message" onClick={() => setNotice("")}>Dismiss</button>
        </div>
      )}
    </div>
  );
}

function ReferralDetailsContent({ userId, publicId }: { userId: string; publicId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const query = useQuery({
    queryKey: ["referral", userId, publicId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token) throw new Error("Auth0 did not return an access token.");
      const referral = await getReferral(token.access_token, publicId, signal);

      return {
        referral,
        canManage: token.scope?.split(/\s+/).includes("manage:referrals") ?? false,
      };
    },
    retry: false,
  });

  if (query.isPending) {
    return <p className="mt-3" role="status">Loading referral…</p>;
  }
  if (query.isError) {
    if (query.error instanceof ReferralRequestError && query.error.status === 404) {
      return <p className="mt-3" role="alert">Referral not found.</p>;
    }
    return (
      <div className="mt-3 space-y-2">
        <p role="alert">{referralErrorMessage(query.error, "Unable to load this referral.")}</p>
        <button type="button" disabled={query.isFetching} onClick={() => void query.refetch()}>Try again</button>
      </div>
    );
  }

  return <ReferralRecord userId={userId} publicId={publicId} {...query.data} />;
}

function ReferralDetailsPage() {
  const { user } = useAuth0();
  const { publicId } = useParams<"publicId">();

  return (
    <section
      aria-labelledby="referral-details-heading"
      className="mt-8 rounded-lg border border-slate-200 bg-white p-6"
    >
      <Link to="/referrals" className="text-sm text-blue-700 underline">Back to referrals</Link>
      <h2 id="referral-details-heading" className="mt-3 text-xl font-semibold">Referral details</h2>
      {user?.sub && publicId ? (
        <ReferralDetailsContent key={user.sub + ":" + publicId} userId={user.sub} publicId={publicId} />
      ) : (
        <p className="mt-3" role="alert">Unable to open this referral.</p>
      )}
    </section>
  );
}

export default ReferralDetailsPage;
