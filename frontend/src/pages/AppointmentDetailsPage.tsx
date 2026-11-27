import LoadingStatus, { Spinner } from "../components/LoadingStatus";
import { useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useLocation, useParams } from "react-router";
import { AppointmentRequestError, completeAppointment, getAppointment, type AppointmentDetails } from "../api/appointments";
import CancelAppointmentButton from "../features/appointments/components/CancelAppointmentButton";
import { AppointmentStatusBadge, TimeOffConflictBadge } from "../features/appointments/components/AppointmentStatusBadge";
import RescheduleAppointmentForm from "../features/appointments/components/RescheduleAppointmentForm";
import { appointmentLists } from "../features/appointments/appointmentLists";
import { appointmentWriteKey, refreshAppointmentViews } from "../features/appointments/appointmentQueries";
import { dateInTimeZone } from "../features/appointments/clinicDates";
import { useReferralAccount } from "../features/referrals/hooks/useReferralAccount";
import { ActionIcon } from "../components/ActionIcon";

function AppointmentRecord({ publicId, userId }: { publicId: string; userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const location = useLocation();
  const account = useReferralAccount(userId).data?.account;
  const writing = useIsMutating({ mutationKey: appointmentWriteKey(userId, publicId) }) > 0;
  const details = useQuery({
    queryKey: ["appointment", userId, publicId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return getAppointment(publicId, token, signal);
    },
    retry: false,
    refetchInterval: query => {
      const appointment = query.state.data;
      const canCompleteHere = account?.isAdministrator || (account?.providerPublicId === appointment?.providerPublicId
        && account?.memberships.some(membership => membership.role === "Clinician"));
      if (writing || !canCompleteHere || !appointment || appointment.status !== "Confirmed" || appointment.canComplete) return false;
      // The clock schedules a server check; it never grants completion eligibility.
      const untilStart = Date.parse(appointment.startUtc) - Date.now();
      return untilStart > 0 ? Math.min(untilStart + 250, 2_147_483_647) : 15_000;
    },
  });

  const appointment = details.isSuccess ? details.data : undefined;
  const referralId = appointment?.referralPublicId;
  const referralLink = referralId && appointment
    ? { pathname: "/referrals/" + referralId, search: new URLSearchParams({ clinic: appointment.clinicPublicId }).toString() }
    : null;
  const backToReferral = location.state?.fromReferral === true && referralLink;
  const fromSchedule = location.state?.fromProviderSchedule === true && location.state.scheduleOwner === userId;
  const originList = location.state?.appointmentList;
  const fromList = originList === "upcoming" || originList === "conflicts";
  const clinicianOnly = account?.providerPublicId && !account.isAdministrator
    && account.memberships.some(({ role }) => role === "Clinician")
    && !account.memberships.some(({ role }) => role === "FrontDesk");
  const returnToSchedule = fromSchedule || (!fromList && clinicianOnly);
  const scheduleDate = fromSchedule && typeof location.state.scheduleDate === "string"
    ? location.state.scheduleDate
    : appointment && dateInTimeZone(new Date(appointment.startUtc), appointment.timeZoneId ?? "UTC");
  const scheduleSearch = new URLSearchParams();
  if (scheduleDate) scheduleSearch.set("date", scheduleDate);
  if (fromSchedule && (location.state.schedulePeriod === "morning" || location.state.schedulePeriod === "afternoon")) {
    scheduleSearch.set("period", location.state.schedulePeriod);
  }
  const fromConflicts = originList === "conflicts"
    || (originList !== "upcoming" && !location.search && appointment?.overlapsProviderTimeOff);
  const backList = fromConflicts ? appointmentLists.conflicts : appointmentLists.upcoming;
  const backState = fromList && location.state?.filterOwner === userId
    ? { patientFilter: location.state.patientFilter, filterOwner: userId }
    : undefined;
  const backLocation = returnToSchedule
    ? { pathname: "/my-schedule", search: scheduleSearch.toString() }
    : { pathname: backList.path, search: location.search };

  return (
    <div className="mx-auto w-full max-w-4xl space-y-3">
      <Link to={backToReferral || backLocation}
        state={backState} className="workspace-button" data-variant="secondary">
        <ActionIcon name="back" />
        {backToReferral ? "Back to referral" : returnToSchedule ? "Back to my schedule" : "Back to " + backList.title.toLowerCase()}
      </Link>
      <section className="workspace-panel space-y-4" aria-labelledby="appointment-heading">
        <h1 id="appointment-heading" className="page-title">Appointment details</h1>
        {details.isPending && <LoadingStatus>Loading appointment…</LoadingStatus>}
        {details.isError && (
          <div className="space-y-2">
            <p role="alert">{details.error.message}</p>
            <button type="button" data-variant="secondary" disabled={details.isFetching} onClick={() => void details.refetch()}>
              <ActionIcon name="refresh" />
              {details.isFetching ? "Retrying…" : "Try again"}
            </button>
          </div>
        )}
        {appointment && <AppointmentInformation appointment={appointment} userId={userId}
          showSchedulingHandoff={Boolean(clinicianOnly)} />}
        {appointment && referralLink && (
          <section aria-labelledby="appointment-referral-heading" className="rounded-lg border border-panel-border p-4">
            <div className="page-heading">
              <h2 id="appointment-referral-heading" className="font-semibold">Referral</h2>
              <Link className="workspace-button" to={referralLink}>
                <ActionIcon name="eye" />
                View referral
              </Link>
            </div>
          </section>
        )}
      </section>
    </div>
  );
}

function AppointmentInformation({
  appointment, userId, showSchedulingHandoff,
}: {
  appointment: AppointmentDetails;
  userId: string;
  showSchedulingHandoff: boolean;
}) {
  const [rescheduling, setRescheduling] = useState(false);
  const [notice, setNotice] = useState("");
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const mutationKey = appointmentWriteKey(userId, appointment.publicId);
  const writing = useIsMutating({ mutationKey }) > 0;
  const completion = useMutation({
    mutationKey,
    mutationFn: async () => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      await completeAppointment(appointment.publicId, token);
    },
    retry: false,
    onSuccess: () => refreshAppointmentViews(queryClient, userId, appointment.publicId),
    onError: async (error) => {
      if (error instanceof AppointmentRequestError && error.status === 409) {
        await refreshAppointmentViews(queryClient, userId, appointment.publicId);
      }
    },
  });
  const timeZone = appointment.timeZoneId ?? "UTC";
  const date = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeZone });
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });
  const start = new Date(appointment.startUtc);

  return (
    <>
      <dl className="grid gap-4 rounded-lg border border-panel-border p-4 sm:grid-cols-2">
        <div><dt className="font-medium">Patient</dt><dd>{appointment.patientName}</dd></div>
        <div>
          <dt className="font-medium">Status</dt>
          <dd><AppointmentStatusBadge status={appointment.status} /></dd>
        </div>
        <div><dt className="font-medium">Provider</dt><dd>{appointment.providerName}</dd></div>
        <div><dt className="font-medium">Service</dt><dd>{appointment.serviceName}</dd></div>
        <div><dt className="font-medium">Clinic</dt><dd>{appointment.clinicName}</dd></div>
        <div>
          <dt className="font-medium">Scheduled time</dt>
          <dd><time dateTime={appointment.startUtc}>
            {date.format(start)} · {time.formatRange(start, new Date(appointment.endUtc))}
          </time> ({timeZone})</dd>
        </div>
        {appointment.overlapsProviderTimeOff && (
          <div className="sm:col-span-2 border-t border-panel-border pt-3">
            <dt className="font-medium">Availability</dt>
            <dd className="space-y-1">
              <TimeOffConflictBadge>Overlaps provider time off</TimeOffConflictBadge>
              {showSchedulingHandoff && (
                <p className="text-sm text-subtle">Contact front desk to coordinate rescheduling with the patient.</p>
              )}
            </dd>
          </div>
        )}
      </dl>
      <section aria-labelledby="appointment-actions-heading"
        className="space-y-3 rounded-lg border border-panel-border p-4 not-has-[button,form,[role=status],[role=alert]]:hidden">
        <div className="page-heading">
          <h2 id="appointment-actions-heading" className="font-semibold">Appointment actions</h2>
          <div className="flex flex-wrap items-center gap-2">
            {appointment.canReschedule && !rescheduling && (
              <button type="button" data-variant="secondary" disabled={writing} onClick={() => {
                setNotice("");
                setRescheduling(true);
              }}>
                <ActionIcon name="calendarClock" />
                Reschedule
              </button>
            )}
            <CancelAppointmentButton publicId={appointment.publicId} userId={userId} canCancel={appointment.canCancel} />
            {appointment.canComplete && !completion.isSuccess && (
              <button type="button" disabled={writing} onClick={() => {
                const message = appointment.referralPublicId
                  ? "Complete this appointment and close its referral?"
                  : "Complete this appointment?";
                if (window.confirm(message)) {
                  setNotice("");
                  completion.mutate();
                }
              }}>
                {completion.isPending ? <Spinner /> : <ActionIcon name="check" />}
                {completion.isPending ? "Completing…" : "Complete appointment"}
              </button>
            )}
          </div>
        </div>
        {completion.isSuccess && <p role="status">Appointment completed.</p>}
        {completion.isError && <p role="alert">{completion.error.message}</p>}
        {rescheduling && appointment.canReschedule && (
          <RescheduleAppointmentForm
            appointment={appointment}
            userId={userId}
            timeZone={timeZone}
            onClose={() => setRescheduling(false)}
            onSaved={() => {
              setRescheduling(false);
              setNotice("Appointment rescheduled.");
            }}
          />
        )}
        {notice && <p role="status">{notice}</p>}
      </section>
    </>
  );
}

export default function AppointmentDetailsPage() {
  const { user } = useAuth0();
  const { publicId } = useParams<"publicId">();

  return publicId && user?.sub
    ? <AppointmentRecord key={user.sub + ":" + publicId} publicId={publicId} userId={user.sub} />
    : (
      <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="appointment-heading">
        <h1 id="appointment-heading" className="page-title">Appointment details</h1>
        <p role="alert">Unable to open this appointment.</p>
      </section>
    );
}
