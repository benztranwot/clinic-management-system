import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router";
import { getProviders, ProviderRequestError } from "../../api/providers";
import { getServices, ServiceRequestError } from "../../api/services";
import { ClinicProvidersTable, ClinicServicesTable } from "./ClinicDirectoryTables";
import { ActionIcon } from "../ActionIcon";

export default function ClinicDirectories({ userId, clinicId }: { userId: string; clinicId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const path = "/clinics/" + clinicId;
  const providers = useQuery({
    queryKey: ["clinic-provider-list", userId, clinicId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify this session.");
      return getProviders(token, clinicId, signal);
    },
    retry: false,
  });
  const services = useQuery({
    queryKey: ["clinic-service-list", userId, clinicId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Unable to verify this session.");
      return getServices(token, clinicId, signal);
    },
    retry: false,
  });

  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <section className="space-y-3" aria-labelledby="clinic-providers-heading">
        <div className="page-heading">
          <h2 id="clinic-providers-heading" className="text-lg font-semibold">Providers</h2>
          {providers.isSuccess && <Link className="workspace-button" data-variant="secondary"
            to={path + "/providers"}><ActionIcon name="people" />View providers</Link>}
        </div>
        {providers.isPending && <p role="status">Loading providers…</p>}
        {providers.isError && (providers.error instanceof ProviderRequestError && providers.error.status === 403
          ? <p>You don’t have permission to view providers.</p>
          : <button type="button" data-variant="secondary" disabled={providers.isFetching}
              onClick={() => void providers.refetch()}><ActionIcon name="refresh" />Retry loading providers</button>)}
        {providers.isSuccess && <ClinicProvidersTable clinicId={clinicId} providers={providers.data} />}
      </section>
      <section className="space-y-3" aria-labelledby="clinic-services-heading">
        <div className="page-heading">
          <h2 id="clinic-services-heading" className="text-lg font-semibold">Services</h2>
          {services.isSuccess && <Link className="workspace-button" data-variant="secondary"
            to={path + "/services"}><ActionIcon name="list" />View services</Link>}
        </div>
        {services.isPending && <p role="status">Loading services…</p>}
        {services.isError && (services.error instanceof ServiceRequestError && services.error.status === 403
          ? <p>You don’t have permission to view services.</p>
          : <button type="button" data-variant="secondary" disabled={services.isFetching}
              onClick={() => void services.refetch()}><ActionIcon name="refresh" />Retry loading services</button>)}
        {services.isSuccess && <ClinicServicesTable clinicId={clinicId} services={services.data} />}
      </section>
    </div>
  );
}
