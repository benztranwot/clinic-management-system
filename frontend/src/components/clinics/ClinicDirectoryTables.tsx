import { Link } from "react-router";
import type { Provider } from "../../api/providers";
import type { Service } from "../../api/services";
import WorkspaceTable from "../WorkspaceTable";

export function ClinicProvidersTable({ clinicId, providers }: { clinicId: string; providers: Provider[] }) {
  if (!providers.length) return <p className="queue-empty">No providers found at this clinic.</p>;

  return (
    <WorkspaceTable label="Providers" columns={["Last name", "First name"]}>
      {providers.map((provider) => (
        <tr key={provider.publicId} className="workspace-action-row">
          <td>
            <Link className="workspace-row-action"
              to={"/clinics/" + clinicId + "/providers/" + provider.publicId}
              aria-label={"View provider: " + provider.firstName + " " + provider.lastName}>
              {provider.lastName}
            </Link>
          </td>
          <td>{provider.firstName}</td>
        </tr>
      ))}
    </WorkspaceTable>
  );
}

export function ClinicServicesTable({ clinicId, services }: { clinicId: string; services: Service[] }) {
  if (!services.length) return <p className="queue-empty">No services found at this clinic.</p>;

  return (
    <WorkspaceTable label="Services" columns={["Service name"]}>
      {services.map((service) => (
        <tr key={service.publicId} className="workspace-action-row">
          <td>
            <Link className="workspace-row-action"
              to={"/clinics/" + clinicId + "/services/" + service.publicId}>
              {service.name}
            </Link>
          </td>
        </tr>
      ))}
    </WorkspaceTable>
  );
}
