export const appointmentPageSize = 14;

export const appointmentLists = {
  upcoming: {
    path: "/appointments/upcoming",
    title: "Upcoming appointments",
    empty: "No upcoming appointments.",
  },
  conflicts: {
    path: "/appointments/availability-conflicts",
    title: "Availability conflicts",
    empty: "No scheduled appointments overlap provider time off.",
  },
} as const;

export type AppointmentListView = keyof typeof appointmentLists;
