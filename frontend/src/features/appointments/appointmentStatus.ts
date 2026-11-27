import type { AppointmentDetails } from "../../api/appointments";

export const appointmentStatusLabels: Record<AppointmentDetails["status"], string> = {
  Confirmed: "Scheduled",
  Cancelled: "Cancelled",
  Completed: "Completed",
};
