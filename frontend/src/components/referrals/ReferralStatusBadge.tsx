import { referralStatusLabels, type ReferralStage } from "../../api/referrals";

const statusStyles = {
  PendingReview: "bg-blue-50 text-blue-800 ring-blue-200",
  ReadyToSchedule: "bg-emerald-50 text-emerald-800 ring-emerald-200",
  Scheduled: "bg-violet-50 text-violet-800 ring-violet-200",
  Closed: "bg-slate-100 text-slate-700 ring-slate-200",
} satisfies Record<ReferralStage, string>;

function ReferralStatusBadge({ status }: { status: ReferralStage }) {
  return (
    <span className={"inline-flex rounded-full px-2.5 py-1 text-xs font-semibold ring-1 ring-inset " + statusStyles[status]}>
      <span className="sr-only">Status: </span>
      {referralStatusLabels[status]}
    </span>
  );
}

export default ReferralStatusBadge;
