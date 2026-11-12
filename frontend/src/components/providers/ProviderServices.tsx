import { useId, useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  assignProviderService,
  getProviderServices,
  ProviderServiceRequestError,
  removeProviderService,
} from "../../api/providerServices";
import { getServices, ServiceRequestError } from "../../api/services";
import { ActionIcon } from "../ActionIcon";

type AssignmentChange = {
  action: "assign" | "remove";
  serviceId: string;
};

function assignmentError(error: unknown, action?: AssignmentChange["action"]) {
  if (error instanceof ProviderServiceRequestError || error instanceof ServiceRequestError) {
    switch (error.status) {
      case 400:
        if (action === "assign") return "Select a service offered by this clinic.";
        break;
      case 401:
        return "Your sign-in couldn’t be verified. Sign out and sign back in.";
      case 403:
        return "Your account doesn’t have permission for this action.";
      case 404:
        return "The provider, service, or assignment is no longer available.";
      case 409:
        if (!action) break;
        return action === "remove"
          ? "This assignment is used by a referral and cannot be removed."
          : "This service is already assigned or is no longer available.";
    }
  }
  return action
    ? "Unable to confirm the change. Check the assigned services before trying again."
    : "Unable to load services.";
}

function ProviderServices({
  userId,
  clinicPublicId,
  providerPublicId,
  disabled,
}: {
  userId: string;
  clinicPublicId: string;
  providerPublicId: string;
  disabled: boolean;
}) {
  const headingId = useId();
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState("");
  const [notice, setNotice] = useState("");
  const assignmentsKey = ["provider-services", userId, providerPublicId];

  const assignments = useQuery({
    queryKey: assignmentsKey,
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token) throw new Error("Auth0 did not return an access token.");

      const services = await getProviderServices(token.access_token, providerPublicId, signal);
      return {
        services,
        canManage: token.scope?.split(/\s+/).includes("manage:provider-services") ?? false,
      };
    },
    retry: false,
  });

  const clinicServices = useQuery({
    queryKey: ["provider-service-options", userId, clinicPublicId],
    enabled: assignments.data?.canManage === true,
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");
      return getServices(accessToken, clinicPublicId, signal);
    },
    retry: false,
  });

  const change = useMutation({
    mutationKey: assignmentsKey,
    mutationFn: async ({ action, serviceId }: AssignmentChange) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");

      if (action === "assign") {
        await assignProviderService(accessToken, providerPublicId, serviceId);
      } else {
        await removeProviderService(accessToken, providerPublicId, serviceId);
      }
    },
    retry: false,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: assignmentsKey, exact: true });
    },
    onError: async (error) => {
      if (error instanceof ProviderServiceRequestError && (error.status === 404 || error.status === 409)) {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: assignmentsKey, exact: true }),
          queryClient.invalidateQueries({
            queryKey: ["provider-service-options", userId, clinicPublicId],
            exact: true,
          }),
        ]);
      }
    },
  });

  const isBusy = disabled || change.isPending;
  const assignedServices = assignments.data?.services ?? [];
  const assignedIds = new Set(assignedServices.map((service) => service.publicId));
  const options = (clinicServices.data ?? []).filter((service) => !assignedIds.has(service.publicId));
  const selectedService = options.find((service) => service.publicId === selectedId);

  function submitChange(request: AssignmentChange) {
    if (isBusy) return;

    setNotice("");
    change.mutate(request, {
      onSuccess: () => {
        setSelectedId("");
        setNotice(request.action === "assign" ? "Service assigned." : "Assignment removed.");
      },
    });
  }

  function handleAssign(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (selectedService && clinicServices.isSuccess) {
      submitChange({ action: "assign", serviceId: selectedService.publicId });
    }
  }

  return (
    <section aria-labelledby={headingId} className="space-y-3 border-t border-[var(--border)] pt-4">
      <h4 id={headingId} className="font-semibold">Assigned services</h4>

      {assignments.isPending && <p role="status">Loading assigned services…</p>}
      {assignments.isError && (
        <div className="space-y-2">
          <p role="alert">{assignmentError(assignments.error)}</p>
          <button type="button" disabled={assignments.isFetching} onClick={() => void assignments.refetch()}>
            <ActionIcon name="refresh" />
            Try again
          </button>
        </div>
      )}

      {assignments.isSuccess && (
        <>
          {assignedServices.length === 0 ? (
            <p>No services assigned.</p>
          ) : (
            <ul aria-label="Assigned services" className="divide-y divide-[var(--border)]">
              {assignedServices.map((service) => (
                <li key={service.publicId} className="flex items-center justify-between gap-3 py-2">
                  <span>{service.name}</span>
                  {assignments.data.canManage && (
                    <button
                      type="button"
                      data-variant="danger"
                      aria-label={"Remove " + service.name + " assignment"}
                      disabled={isBusy}
                      onClick={() => {
                        if (window.confirm("Remove the assignment for " + service.name + "?")) {
                          submitChange({ action: "remove", serviceId: service.publicId });
                        }
                      }}
                    >
                      <ActionIcon name="minus" />
                      {change.isPending && change.variables.action === "remove" &&
                        change.variables.serviceId === service.publicId ? "Removing…" : "Remove"}
                    </button>
                  )}
                </li>
              ))}
            </ul>
          )}

          {assignments.data.canManage && clinicServices.isPending && (
            <p role="status">Loading clinic services…</p>
          )}
          {assignments.data.canManage && clinicServices.isError && (
            <div className="space-y-2">
              <p role="alert">{assignmentError(clinicServices.error)}</p>
              <button type="button" disabled={clinicServices.isFetching} onClick={() => void clinicServices.refetch()}>
                <ActionIcon name="refresh" />
                Retry clinic services
              </button>
            </div>
          )}
          {assignments.data.canManage && clinicServices.isSuccess && (
            options.length === 0 ? (
              <p>No unassigned services at this clinic.</p>
            ) : (
              <form className="space-y-2" onSubmit={handleAssign}>
                <label className="block text-sm font-medium">
                  Service to assign
                  <select
                    className="mt-1 block w-full rounded-md border border-[var(--input)] px-3 py-2"
                    value={selectedService?.publicId ?? ""}
                    onChange={(event) => {
                      setSelectedId(event.target.value);
                      setNotice("");
                      change.reset();
                    }}
                    disabled={isBusy}
                    required
                  >
                    <option value="">Choose a service</option>
                    {options.map((service) => (
                      <option key={service.publicId} value={service.publicId}>{service.name}</option>
                    ))}
                  </select>
                </label>
                <button type="submit" disabled={isBusy || !selectedService}>
                  <ActionIcon name="plus" />
                  {change.isPending && change.variables.action === "assign" ? "Assigning…" : "Assign service"}
                </button>
              </form>
            )
          )}
        </>
      )}

      {change.isError && <p role="alert">{assignmentError(change.error, change.variables?.action)}</p>}
      {notice && (
        <div className="flex items-center gap-3">
          <p role="status">{notice}</p>
          <button type="button" aria-label="Dismiss assignment message" onClick={() => setNotice("")}>
            <ActionIcon name="close" />
            Dismiss
          </button>
        </div>
      )}
    </section>
  );
}

export default ProviderServices;
