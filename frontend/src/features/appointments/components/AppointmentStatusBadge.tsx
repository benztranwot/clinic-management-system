import type { AppointmentDetails } from "../../../api/appointments";
import { appointmentStatusLabels } from "../appointmentStatus";

type AppointmentStatus = AppointmentDetails["status"];

const badgeClass = "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ring-1 ring-inset";

const statusClasses: Record<AppointmentStatus, string> = {
  Confirmed: "bg-violet-50 text-violet-800 ring-violet-200",
  Cancelled: "bg-red-50 text-red-800 ring-red-200",
  Completed: "bg-emerald-50 text-emerald-800 ring-emerald-200",
};

export function AppointmentStatusBadge({ status }: { status: AppointmentStatus }) {
  return <span className={badgeClass + " " + statusClasses[status]}>{appointmentStatusLabels[status]}</span>;
}

export function TimeOffConflictBadge({ children = "Time-off conflict" }: { children?: string }) {
  return <span className={badgeClass + " bg-amber-50 text-amber-900 ring-amber-200"}>{children}</span>;
}
