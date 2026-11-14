import type { QueryClient } from "@tanstack/react-query";

export function appointmentWriteKey(userId: string, publicId: string) {
  return ["appointment-write", userId, publicId];
}

export async function refreshAppointmentViews(queryClient: QueryClient, userId: string, publicId: string) {
  const views = [
    "appointment-attention", "upcoming-appointments", "provider-calendar",
    "provider-availability", "referral", "referrals",
  ];
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ["appointment", userId, publicId] }),
    ...views.map((view) => queryClient.invalidateQueries({ queryKey: [view, userId] })),
  ]);
}
