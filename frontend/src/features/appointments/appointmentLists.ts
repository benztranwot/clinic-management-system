export const appointmentLists = {
  upcoming: {
    path: "/appointments/upcoming",
    title: "Upcoming appointments",
    badge: "Scheduled",
    empty: "No upcoming appointments.",
  },
  conflicts: {
    path: "/appointments/availability-conflicts",
    title: "Availability conflicts",
    badge: "Overlaps provider time off",
    empty: "No confirmed appointments overlap provider time off.",
  },
} as const;

export type AppointmentListView = keyof typeof appointmentLists;
