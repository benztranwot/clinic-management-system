import { useWorkspacePermission } from "../auth/useWorkspacePermission";
import LoadingStatus from "../components/LoadingStatus";
import DirectoryFilters from "../components/DirectoryFilters";
import WorkspacePagination from "../components/WorkspacePagination";
import { useListSearch } from "../components/useListSearch";
import { useAuth0 } from "@auth0/auth0-react";
import { Link, useLocation, useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ClinicRequestError, searchClinics } from "../api/clinics";
import WorkspaceTable from "../components/WorkspaceTable";
import ClinicForm from "../features/clinics/components/ClinicForm";
import { ActionIcon } from "../components/ActionIcon";

function ClinicList({ userId, create }: { userId: string; create: boolean }) {
  const location = useLocation();
  const list = useListSearch("clinics", userId, { name: "", address: "", timeZone: "" });
  const navigate = useNavigate();
  const workspaceCanManage = useWorkspacePermission(userId, "manage:clinics");
  const { getAccessTokenSilently } = useAuth0();
  const query = useQuery({
    // Keep cached clinic data separate for each signed-in user.
    queryKey: ["clinics", userId, list.filters, list.page],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });

      if (!token?.access_token) {
        throw new Error("Auth0 did not return an access token.");
      }

      const clinics = await searchClinics(token.access_token, { ...list.filters, page: list.page }, signal);

      return {
        clinics,
        canManage:
          token.scope?.split(/\s+/).includes("manage:clinics") ?? false,
      };
    },
    staleTime: 30_000,
    retry: false,
  });

  if (query.isPending) {
    return (
      <LoadingStatus className="mt-3">Loading clinics…</LoadingStatus>
    );
  }

  if (query.isError) {
    const status =
      query.error instanceof ClinicRequestError
        ? query.error.status
        : undefined;

    if (status === 401) {
      return (
        <p className="mt-3" role="alert">
          Your sign-in couldn’t be verified. Sign out and sign back in.
        </p>
      );
    }

    if (status === 403) {
      return (
        <p className="mt-3" role="alert">
          Your account doesn’t have permission to view clinics.
        </p>
      );
    }

    return (
      <div className="mt-3">
        <p role="alert">Unable to load clinics.</p>
        <button
          type="button"
          className="mt-3"
          disabled={query.isFetching}
          onClick={() => void query.refetch()}
        >
          <ActionIcon name="refresh" />
          Try again
        </button>
      </div>
    );
  }

  const { clinics: results } = query.data;
  const canManage = workspaceCanManage || query.data.canManage;
  const clinics = results.items;

  if (create) {
    return canManage ? (
      <ClinicForm userId={userId} clinic={null}
          onCancel={() => void navigate("/clinics")}
          onSaved={(saved) => void navigate("/clinics/" + saved.publicId, { replace: true })} />
    ) : <p role="alert">Your account doesn’t have permission to create clinics.</p>;
  }

  return (
    <>
      <div className="page-heading">
        <h1 id="clinics-heading" className="page-title">Clinics</h1>
        <div className="flex flex-wrap gap-2">
          <DirectoryFilters title="Filter clinics" fields={[["name", "Clinic name"], ["address", "Address"], ["timeZone", "Time zone"]]} filters={list.filters} onApply={list.setFilters} />
          {canManage && <Link className="workspace-button" to="/clinics/new"><ActionIcon name="plus" />New clinic</Link>}
        </div>
      </div>
      {clinics.length === 0 ? (
        <p className="queue-empty">No clinics found.</p>
      ) : (
        <WorkspaceTable label="Clinics" columns={["Clinic name", "Address", "Time zone"]}>
          {clinics.map((clinic) => (
            <tr key={clinic.publicId} className="workspace-action-row">
              <td><Link className="workspace-row-action" state={{ listReturn: { search: location.search, state: location.state } }} to={"/clinics/" + clinic.publicId}>{clinic.name}</Link></td>
              <td>{clinic.address}</td>
              <td>{clinic.timeZoneId}</td>
            </tr>
          ))}
        </WorkspaceTable>
      )}
      <WorkspacePagination itemCount={results.items.length} page={list.page} pageSize={results.pageSize} count={results.totalCount} hasMore={results.hasMore}
        busy={query.isFetching} onPage={list.goToPage} label="Clinics pages" />
    </>
  );
}

function ClinicsPage({ create = false }: { create?: boolean }) {
  const { user } = useAuth0();
  const { state } = useLocation();

  return (
    <section aria-label="Clinics" className="workspace-panel workspace-list-panel mx-auto w-full max-w-4xl space-y-4">
      {create && <h1 className="page-title">New clinic</h1>}
      {!create && state?.clinicDeleted === true && <p role="status">Clinic deleted.</p>}
      {user?.sub ? (
        <ClinicList userId={user.sub} create={create} />
      ) : (
        <p className="mt-3" role="alert">
          We couldn’t identify your session. Sign out and sign back in.
        </p>
      )}
    </section>
  );
}

export default ClinicsPage;
