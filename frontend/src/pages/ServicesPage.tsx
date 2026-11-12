import { useState, type FormEvent } from "react";
import { ClinicServicesTable } from "../components/clinics/ClinicDirectoryTables";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useLocation, useNavigate, useParams } from "react-router";
import { ClinicRequestError, getClinic } from "../api/clinics";
import {
  createService,
  deleteService,
  getService,
  getServices,
  ServiceRequestError,
  updateService,
  type Service,
  type ServiceInput,
} from "../api/services";
import { ActionIcon } from "../components/ActionIcon";

type ServiceChange =
  | { action: "create"; input: ServiceInput }
  | { action: "update"; publicId: string; input: ServiceInput }
  | { action: "delete"; publicId: string };

function serviceError(
  error: unknown,
  fallback: string,
  action?: ServiceChange["action"],
) {
  if (error instanceof ClinicRequestError && error.status === 404) {
    return "Clinic not found.";
  }

  if (error instanceof ServiceRequestError || error instanceof ClinicRequestError) {
    switch (error.status) {
      case 400: return "Enter a service name between 1 and 200 characters.";
      case 401: return "Your sign-in couldn’t be verified. Sign out and sign back in.";
      case 403: return "Your account doesn’t have permission for this action.";
      case 404: return "This clinic or service is no longer available.";
      case 409:
        return action === "delete"
          ? "This service is linked to providers or referrals and cannot be deleted."
          : "This clinic already has a service with that name.";
    }
  }
  return fallback;
}

function ServiceForm({
  service,
  isSaving,
  onSave,
  onCancel,
}: {
  service?: Service;
  isSaving: boolean;
  onSave: (input: ServiceInput) => void;
  onCancel: () => void;
}) {
  const [nameError, setNameError] = useState("");

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isSaving) return;

    const data = new FormData(event.currentTarget);
    const name = String(data.get("name") ?? "").trim();
    if (!name) {
      setNameError("Enter a service name.");
      return;
    }

    setNameError("");
    onSave({ name });
  }

  const inputClass = "mt-1 w-full rounded-md border border-[var(--input)] px-3 py-2";
  return (
    <form
      className="mt-4 rounded-md border border-[var(--border)] p-4"
      onSubmit={handleSubmit}
      onChange={() => setNameError("")}
    >
      <fieldset className="space-y-3" disabled={isSaving}>
        <legend className="font-semibold">{service ? "Edit service" : "Add service"}</legend>
        <label className="block text-sm font-medium">
          Service name
          <input
            name="name"
            className={inputClass}
            defaultValue={service?.name}
            maxLength={200}
            required
          />
        </label>
        {nameError && <p role="alert">{nameError}</p>}
        <div className="flex flex-wrap gap-2">
          <button type="submit"><ActionIcon name="save" />{isSaving ? "Saving…" : "Save service"}</button>
          <button type="button" onClick={onCancel}><ActionIcon name="close" />Cancel</button>
        </div>
      </fieldset>
    </form>
  );
}

function ServiceDirectory({
  userId,
  clinicPublicId,
  selectedId,
}: {
  userId: string;
  clinicPublicId: string;
  selectedId: string | null;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { state } = useLocation();
  const [formMode, setFormMode] = useState<"create" | "edit" | null>(null);
  const [notice, setNotice] = useState(state?.deleted === true ? "Service deleted." : "");

  const directoryKey = ["services", userId, clinicPublicId];
  const directory = useQuery({
    queryKey: directoryKey,
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token) throw new Error("Auth0 did not return an access token.");

      const [clinic, services] = await Promise.all([
        getClinic(token.access_token, clinicPublicId, signal),
        getServices(token.access_token, clinicPublicId, signal),
      ]);

      return {
        clinic,
        services,
        canManage: token.scope?.split(/\s+/).includes("manage:services") ?? false,
      };
    },
    retry: false,
  });

  const belongsToClinic = directory.data?.services.some((item) => item.publicId === selectedId?.toLowerCase());
  const detail = useQuery({
    queryKey: ["service", userId, selectedId],
    enabled: Boolean(belongsToClinic),
    queryFn: async ({ signal }) => {
      if (!selectedId) throw new Error("Select a service first.");
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");
      return getService(accessToken, selectedId, signal);
    },
    retry: false,
  });

  const change = useMutation({
    mutationFn: async (request: ServiceChange) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");

      switch (request.action) {
        case "create":
          await createService(accessToken, clinicPublicId, request.input);
          break;
        case "update":
          await updateService(accessToken, request.publicId, request.input);
          break;
        case "delete":
          await deleteService(accessToken, request.publicId);
          break;
      }
    },
    retry: false,
    onSuccess: async (_, request) => {
      if (request.action === "update") {
        await queryClient.invalidateQueries({
          queryKey: ["service", userId, request.publicId],
          exact: true,
        });
      } else if (request.action === "delete") {
        queryClient.removeQueries({
          queryKey: ["service", userId, request.publicId],
          exact: true,
        });
      }

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: directoryKey, exact: true }),
        queryClient.invalidateQueries({ queryKey: ["clinic-service-list", userId, clinicPublicId], exact: true }),
      ]);
    },
  });

  function closeServiceForm() {
    change.reset();
    setNotice("");
    setFormMode(null);
  }

  function submitChange(request: ServiceChange) {
    if (change.isPending) return;
    setNotice("");
    change.mutate(request, {
      onSuccess: () => {
        setFormMode(null);
        if (request.action === "delete") {
          void navigate("/clinics/" + clinicPublicId + "/services", { replace: true, state: { deleted: true } });
          return;
        }
        const messages = {
          create: "Service added.",
          update: "Service updated.",
        };
        setNotice(messages[request.action]);
      },
    });
  }

  if (directory.isPending) {
    return <p className="mt-3" role="status">Loading services…</p>;
  }
  if (directory.isError) {
    return (
      <div className="mt-3">
        <p role="alert">{serviceError(directory.error, "Unable to load services.")}</p>
        <button type="button" className="mt-3" disabled={directory.isFetching} onClick={() => void directory.refetch()}>
          <ActionIcon name="refresh" />
          Try again
        </button>
      </div>
    );
  }

  const { clinic, services, canManage } = directory.data;
  const service = detail.data;
  return (
    <>
      <div className="page-heading">
        <div>
          <h1 id="services-heading" className="page-title">{selectedId ? "Service details" : "Services"}</h1>
          <p className="queue-meta">{clinic.name}</p>
        </div>
        {selectedId === null && canManage && formMode !== "create" && (
          <button type="button" onClick={() => {
            closeServiceForm();
            setFormMode("create");
          }}><ActionIcon name="plus" />Add service</button>
        )}
      </div>

      {selectedId === null ? (
        <>
          {canManage && formMode === "create" && (
            <ServiceForm
              isSaving={change.isPending}
              onSave={(input) => submitChange({ action: "create", input })}
              onCancel={closeServiceForm}
            />
          )}
          <ClinicServicesTable clinicId={clinicPublicId} services={services} />
        </>
      ) : !belongsToClinic ? (
        <p role="alert">This service was not found at this clinic.</p>
      ) : (
        <div className="mt-4 space-y-3">
          {detail.isPending && <p role="status">Loading service details…</p>}
          {detail.isError && (
            <div>
              <p role="alert">{serviceError(detail.error, "Unable to load service details.")}</p>
              <button type="button" className="mt-3" disabled={detail.isFetching} onClick={() => void detail.refetch()}>
                <ActionIcon name="refresh" />
                Try again
              </button>
            </div>
          )}
          {service && detail.isSuccess && (
            <>
              <dl>
                <dt className="text-sm font-medium">Service name</dt>
                <dd>{service.name}</dd>
              </dl>
              {canManage && formMode === "edit" && (
                <ServiceForm
                  key={service.publicId}
                  service={service}
                  isSaving={change.isPending}
                  onSave={(input) => submitChange({ action: "update", publicId: service.publicId, input })}
                  onCancel={closeServiceForm}
                />
              )}
              {canManage && formMode !== "edit" && (
                <div className="flex flex-wrap gap-2">
                  <button
                    type="button"
                    disabled={change.isPending}
                    onClick={() => {
                      change.reset();
                      setNotice("");
                      setFormMode("edit");
                    }}
                  >
                    <ActionIcon name="pencil" />
                    Edit service
                  </button>
                  <button
                    type="button"
                    disabled={change.isPending}
                    data-variant="danger"
                    onClick={() => {
                      if (window.confirm("Permanently delete " + service.name + "?")) {
                        submitChange({ action: "delete", publicId: service.publicId });
                      }
                    }}
                  >
                    <ActionIcon name="trash" />
                    {change.isPending ? "Deleting…" : "Delete service"}
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      )}

      {change.isError && (
        <p className="mt-3" role="alert">
          {serviceError(
            change.error,
            "Unable to confirm the change. Check the service list before trying again.",
            change.variables?.action,
          )}
        </p>
      )}
      {notice && (
        <div className="mt-3 flex items-center gap-3">
          <p role="status">{notice}</p>
          <button type="button" aria-label="Dismiss service message" onClick={() => setNotice("")}>
            <ActionIcon name="close" />
            Dismiss
          </button>
        </div>
      )}
    </>
  );
}

function ServicesPage() {
  const { user } = useAuth0();
  const { clinicPublicId, publicId } = useParams<"clinicPublicId" | "publicId">();

  return (
    <section
      aria-label="Services"
      className="workspace-panel mx-auto w-full max-w-4xl space-y-4"
    >
      <Link to={clinicPublicId ? "/clinics/" + clinicPublicId : "/clinics"}
        className="workspace-button" data-variant="secondary">
        <ActionIcon name="back" />
        {clinicPublicId ? "Back to clinic" : "Back to clinics"}
      </Link>
      {user?.sub && clinicPublicId ? (
        <ServiceDirectory
          key={user.sub + ":" + clinicPublicId + ":" + (publicId ?? "")}
          userId={user.sub}
          clinicPublicId={clinicPublicId}
          selectedId={publicId ?? null}
        />
      ) : (
        <p className="mt-3" role="alert">Unable to open this clinic’s services.</p>
      )}
    </section>
  );
}

export default ServicesPage;
