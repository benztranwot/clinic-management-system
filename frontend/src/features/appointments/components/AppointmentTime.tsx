import type { PatientAppointment } from "../../../api/appointments";

export default function AppointmentTime({ appointment }: {
  appointment: Pick<PatientAppointment, "startUtc" | "endUtc" | "timeZoneId">;
}) {
  const timeZone = appointment.timeZoneId ?? "UTC";
  const start = new Date(appointment.startUtc);
  const date = new Intl.DateTimeFormat(undefined, { dateStyle: "full", timeZone });
  const time = new Intl.DateTimeFormat(undefined, { timeStyle: "short", timeZone });

  return <>
    <time dateTime={appointment.startUtc}>
      {date.format(start)} · {time.formatRange(start, new Date(appointment.endUtc))}
    </time>
    {" "}({timeZone})
  </>;
}
