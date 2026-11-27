import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router";
import { getMyReferral, referralErrorMessage, ReferralRequestError, referralStatusLabels } from "../api/referrals";
import { ActionIcon } from "../components/ActionIcon";
import LoadingStatus from "../components/LoadingStatus";
import PatientDetailsPanel from "../components/PatientDetailsPanel";
import ReferralStatusBadge from "../features/referrals/components/ReferralStatusBadge";

const createdDate = new Intl.DateTimeFormat(undefined, { dateStyle: "long" });

const nextSteps = {
  PendingReview: "The receiving clinic is reviewing this referral.",
  MissingInformation: "Contact the receiving clinic to ask what information is needed.",
  ReadyToSchedule: "Contact the receiving clinic to arrange an appointment.",
  Scheduled: "Contact the clinic if you need to change the appointment.",
  Closed: "Contact the clinic if you have questions about this referral.",
} satisfies Record<keyof typeof referralStatusLabels, string>;

export default function MyReferralDetailsPage({ userId }: { userId?: string }) {
  const { publicId = "" } = useParams();
  const { getAccessTokenSilently } = useAuth0();
  const details = useQuery({
    queryKey: ["my-referrals", userId, publicId],
    enabled: Boolean(userId && publicId),
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return getMyReferral(token, publicId, signal);
    },
    retry: false,
  });
  const referral = details.isSuccess ? details.data : undefined;
  const status = referral?.status === "MissingInformation" ? referral.status : referral?.stage;

  return (
    <PatientDetailsPanel title="Referral details" backTo="/my-referrals" backLabel="Back to my referrals">
      {!userId ? (
        <p role="alert">Unable to verify your session. Sign in again.</p>
      ) : details.isPending ? (
        <LoadingStatus>Loading your referral…</LoadingStatus>
      ) : details.isError ? (
        <div className="space-y-2">
          <p role="alert">
            {details.error instanceof ReferralRequestError && details.error.status === 404
              ? "Referral not found."
              : referralErrorMessage(details.error, "Unable to load your referral.")}
          </p>
          <button type="button" data-variant="secondary" disabled={details.isFetching} onClick={() => void details.refetch()}>
            <ActionIcon name="refresh" />
            {details.isFetching ? "Retrying…" : "Try again"}
          </button>
        </div>
      ) : referral && status && (
        <>
          <dl className="grid gap-4 rounded-lg border border-panel-border p-4 sm:grid-cols-2">
            <div><dt className="font-medium">Service</dt><dd>{referral.serviceName}</dd></div>
            <div><dt className="font-medium">Status</dt><dd><ReferralStatusBadge status={status} showStatusLabel={false} /></dd></div>
            <div><dt className="font-medium">Receiving clinic</dt><dd>{referral.clinicName}<br />{referral.clinicAddress}</dd></div>
            <div><dt className="font-medium">Assigned provider</dt><dd>{referral.providerName}</dd></div>
            <div>
              <dt className="font-medium">Created</dt>
              <dd><time dateTime={referral.createdAtUtc}>{createdDate.format(new Date(referral.createdAtUtc))}</time></dd>
            </div>
          </dl>
          <p className="text-sm text-subtle">{nextSteps[status]}</p>
          {referral.appointmentPublicId && (
            <Link className="workspace-button" data-variant="secondary" to={"/my-appointments/" + referral.appointmentPublicId}>
              <ActionIcon name="eye" />
              View appointment
            </Link>
          )}
        </>
      )}
    </PatientDetailsPanel>
  );
}
