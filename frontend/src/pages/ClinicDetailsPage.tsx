import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "react-router";
import { ClinicRequestError, getClinic } from "../api/clinics";
import ClinicForm from "../components/clinics/ClinicForm";
import ClinicDirectories from "../components/clinics/ClinicDirectories";
import DeleteClinicButton from "../components/clinics/DeleteClinicButton";
import { ActionIcon } from "../components/ActionIcon";

export default function ClinicDetailsPage({ edit = false }: { edit?: boolean }) {
  const { user, getAccessTokenSilently } = useAuth0();
  const { clinicPublicId } = useParams();
  const navigate = useNavigate();
  const userId = user?.sub;
  const detailsPath = "/clinics/" + clinicPublicId;
  const query = useQuery({
    queryKey: ["clinic", userId, clinicPublicId],
    enabled: Boolean(userId && clinicPublicId),
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token || !clinicPublicId) throw new Error("Unable to verify this session.");
      const clinic = await getClinic(token.access_token, clinicPublicId, signal);
      return { clinic, canManage: token.scope?.split(/\s+/).includes("manage:clinics") ?? false };
    },
    retry: false,
  });

  const status = query.error instanceof ClinicRequestError ? query.error.status : undefined;

  return (
    <section className="workspace-panel mx-auto w-full max-w-4xl space-y-4" aria-labelledby="clinic-details-heading">
      <Link to={edit ? detailsPath : "/clinics"} className="workspace-button" data-variant="secondary">
        <ActionIcon name="back" />
        {edit ? "Back to clinic" : "Back to clinics"}
      </Link>
      <h1 id="clinic-details-heading" className="page-title">
        {edit ? "Edit clinic" : query.data?.clinic.name ?? "Clinic details"}
      </h1>
      {!userId || !clinicPublicId ? (
        <p role="alert">Unable to open this clinic record.</p>
      ) : query.isPending ? (
        <p role="status">Loading clinic…</p>
      ) : query.isError ? (
        <div className="space-y-2">
          <p role="alert">{status === 404 ? "Clinic not found." : status === 403
            ? "Your account doesn’t have permission to view clinics." : "Unable to load this clinic."}</p>
          {status !== 403 && status !== 404 && (
            <button type="button" data-variant="secondary" disabled={query.isFetching}
              onClick={() => void query.refetch()}><ActionIcon name="refresh" />Try again</button>
          )}
        </div>
      ) : edit ? (
        query.data.canManage ? (
          <ClinicForm key={clinicPublicId} userId={userId} clinic={query.data.clinic}
            onCancel={() => void navigate(detailsPath)}
            onSaved={() => void navigate(detailsPath, { replace: true })} />
        ) : <p role="alert">Your account doesn’t have permission to edit clinics.</p>
      ) : (
        <div className="space-y-4">
          <dl className="grid gap-3 sm:grid-cols-2">
            <div>
              <dt className="text-sm font-medium text-subtle">Address</dt>
              <dd>{query.data.clinic.address}</dd>
            </div>
            <div>
              <dt className="text-sm font-medium text-subtle">Time zone</dt>
              <dd>{query.data.clinic.timeZoneId}</dd>
            </div>
          </dl>
          <ClinicDirectories userId={userId} clinicId={clinicPublicId} />
          {query.data.canManage && (
            <div className="flex flex-wrap items-start gap-2 border-t border-panel-border pt-4">
              <Link className="workspace-button" to={detailsPath + "/edit"}><ActionIcon name="pencil" />Edit clinic</Link>
              <DeleteClinicButton clinic={query.data.clinic} userId={userId} disabled={false}
                onNotice={(message) => {
                  if (message === "Clinic deleted.") void navigate("/clinics", { replace: true, state: { clinicDeleted: true } });
                }} />
            </div>
          )}
        </div>
      )}
    </section>
  );
}
