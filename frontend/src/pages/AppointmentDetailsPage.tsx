import { useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useQuery } from "@tanstack/react-query";
import { Link, useLocation, useParams } from "react-router";
import { getAppointment, type AppointmentDetails } from "../api/appointments";
import CancelAppointmentButton from "../components/appointments/CancelAppointmentButton";
import RescheduleAppointmentForm from "../components/appointments/RescheduleAppointmentForm";
import { appointmentLists } from "../components/appointments/appointmentLists";
import { appointmentWriteKey } from "../components/appointments/appointmentQueries";
import { ActionIcon } from "../components/ActionIcon";

function AppointmentRecord({ publicId, userId }: { publicId: string; userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const location = useLocation();
  const details = useQuery({
    queryKey: ["appointment", userId, publicId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify your session. Sign in again.");
      return getAppointment(publicId, token, signal);
    },
    retry: false,
  });

  const appointment = details.isSuccess ? details.data : undefined;
  const referralId = appointment?.referralPublicId;
  const referralLink = referralId && appointment
    ? { pathname: "/referrals/" + referralId, search: new URLSearchParams({ clinic: appointment.clinicPublicId }).toString() }
    : null;
  const backToReferral = location.state?.fromReferral === true && referralLink;
  const originList = location.state?.appointmentList;
  const fromConflicts = originList === "conflicts"
    || (originList !== "upcoming" && appointment?.overlapsProviderTimeOff);
  const backList = fromConflicts ? appointmentLists.conflicts : appointmentLists.upcoming;

  return (
    <>
      <div className="page-heading">
        <Link to={backToReferral || backList.path} className="workspace-button" data-variant="secondary">
          <ActionIcon name="back" />
          {backToReferral ? "Back to referral" : "Back to " + backList.title.toLowerCase()}
        </Link>
        {referralLink && (
          <Link className="workspace-button ml-auto" to={referralLink}>
            <ActionIcon name="eye" />
            View referral
          </Link>
        )}
      </div>
      <div className="page-heading">
        <h1 id="appointment-heading" className="page-title">Appointment details</h1>
        {appointment?.overlapsProviderTimeOff && (
          <span className="rounded-md bg-[var(--muted)] px-2 py-1 text-sm font-medium">Overlaps provider time off</span>
        )}
      </div>
      {details.isPending && <p role="status">Loading appointment…</p>}
      {details.isError && (
        <div className="space-y-2">
          <p role="alert">{details.error.message}</p>
          <button type="button" data-variant="secondary" disabled={details.isFetching} onClick={() => void details.refetch()}>
            <ActionIcon name="refresh" />
            {details.isFetching ? "Retrying…" : "Try again"}
          </button>
        </div>
      )}
      {appointment && <AppointmentInformation appointment={appointment} userId={userId} />}
    </>
  );
}

function AppointmentInformation({
  appointment, userId,
}: {
  appointment: AppointmentDetails;
  userId: string;
}) {
  const [rescheduling, setRescheduling] = useState(false);
  const [notice, setNotice] = useState("");
  const writing = useIsMutating({ mutationKey: appointmentWriteKey(userId, appointment.publicId) }) > 0;
  const timeZone = appointment.timeZoneId ?? "UTC";
  const date = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeZone });
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });
  const start = new Date(appointment.startUtc);

  return (
    <>
      <dl className="grid gap-3 sm:grid-cols-2">
        <div><dt className="text-sm text-subtle">Patient</dt><dd>{appointment.patientName}</dd></div>
        <div><dt className="text-sm text-subtle">Provider</dt><dd>{appointment.providerName}</dd></div>
        <div><dt className="text-sm text-subtle">Service</dt><dd>{appointment.serviceName}</dd></div>
        <div><dt className="text-sm text-subtle">Clinic</dt><dd>{appointment.clinicName}</dd></div>
        <div className="sm:col-span-2">
          <dt className="text-sm text-subtle">Scheduled time</dt>
          <dd><time dateTime={appointment.startUtc}>
            {date.format(start)} · {time.formatRange(start, new Date(appointment.endUtc))}
          </time> ({timeZone})</dd>
        </div>
        <div><dt className="text-sm text-subtle">Status</dt><dd>{appointment.status}</dd></div>
      </dl>
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
        {appointment.status === "Confirmed" && <button type="button" disabled><ActionIcon name="check" />Complete appointment</button>}
      </div>
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
    </>
  );
}

export default function AppointmentDetailsPage() {
  const { user } = useAuth0();
  const { publicId } = useParams<"publicId">();

  return (
    <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="appointment-heading">
      {publicId && user?.sub
        ? <AppointmentRecord key={user.sub + ":" + publicId} publicId={publicId} userId={user.sub} />
        : <>
            <h1 id="appointment-heading" className="page-title">Appointment details</h1>
            <p role="alert">Unable to open this appointment.</p>
          </>}
    </section>
  );
}
