import { useState, type FormEvent } from "react";
import { ClinicProvidersTable } from "../components/clinics/ClinicDirectoryTables";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useLocation, useNavigate, useParams } from "react-router";
import { ClinicRequestError, getClinic } from "../api/clinics";
import ProviderServices from "../components/providers/ProviderServices";
import {
  createProvider,
  deleteProvider,
  getProvider,
  getProviders,
  ProviderRequestError,
  updateProvider,
  type Provider,
  type ProviderInput,
} from "../api/providers";
import { ActionIcon } from "../components/ActionIcon";

type ProviderChange =
  | { action: "create"; input: ProviderInput }
  | { action: "update"; publicId: string; input: ProviderInput }
  | { action: "delete"; publicId: string };

function providerError(error: unknown, fallback: string) {
  if (error instanceof ClinicRequestError && error.status === 404) {
    return "Clinic not found.";
  }

  if (error instanceof ProviderRequestError || error instanceof ClinicRequestError) {
    switch (error.status) {
      case 400: return "Enter 1 to 100 characters for each name.";
      case 401: return "Your sign-in couldn’t be verified. Sign out and sign back in.";
      case 403: return "Your account doesn’t have permission for this action.";
      case 404: return "This clinic or provider is no longer available.";
      case 409: return "This provider is linked to services and cannot be deleted.";
    }
  }
  return fallback;
}

function ProviderForm({
  provider,
  isSaving,
  onSave,
  onCancel,
}: {
  provider?: Provider;
  isSaving: boolean;
  onSave: (input: ProviderInput) => void;
  onCancel: () => void;
}) {
  const [nameError, setNameError] = useState("");

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (isSaving) return;

    const data = new FormData(event.currentTarget);
    const firstName = String(data.get("firstName") ?? "").trim();
    const lastName = String(data.get("lastName") ?? "").trim();
    if (!firstName || !lastName) {
      setNameError("Enter a first name and a last name.");
      return;
    }

    setNameError("");
    onSave({ firstName, lastName });
  }

  const inputClass = "mt-1 w-full rounded-md border border-[var(--input)] px-3 py-2";
  return (
    <form
      className="mt-4 rounded-md border border-[var(--border)] p-4"
      onSubmit={handleSubmit}
      onChange={() => setNameError("")}
    >
      <fieldset className="space-y-3" disabled={isSaving}>
        <legend className="font-semibold">{provider ? "Edit provider" : "Add provider"}</legend>
        <label className="block text-sm font-medium">
          First name
          <input
            name="firstName"
            className={inputClass}
            defaultValue={provider?.firstName}
            maxLength={100}
            required
          />
        </label>
        <label className="block text-sm font-medium">
          Last name
          <input
            name="lastName"
            className={inputClass}
            defaultValue={provider?.lastName}
            maxLength={100}
            required
          />
        </label>
        {nameError && <p role="alert">{nameError}</p>}
        <div className="flex flex-wrap gap-2">
          <button type="submit"><ActionIcon name="save" />{isSaving ? "Saving…" : "Save provider"}</button>
          <button type="button" onClick={onCancel}><ActionIcon name="close" />Cancel</button>
        </div>
      </fieldset>
    </form>
  );
}

function ProviderDirectory({
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
  const [notice, setNotice] = useState(state?.deleted === true ? "Provider deleted." : "");
  const isChangingAssignment = useIsMutating({
    mutationKey: ["provider-services", userId, selectedId],
    exact: true,
  }) > 0;

  const directoryKey = ["providers", userId, clinicPublicId];
  const directory = useQuery({
    queryKey: directoryKey,
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });
      if (!token?.access_token) throw new Error("Auth0 did not return an access token.");

      const [clinic, providers] = await Promise.all([
        getClinic(token.access_token, clinicPublicId, signal),
        getProviders(token.access_token, clinicPublicId, signal),
      ]);

      return {
        clinic,
        providers,
        canManage: token.scope?.split(/\s+/).includes("manage:providers") ?? false,
      };
    },
    retry: false,
  });

  const belongsToClinic = directory.data?.providers.some((item) => item.publicId === selectedId);
  const detail = useQuery({
    queryKey: ["provider", userId, selectedId],
    enabled: Boolean(belongsToClinic),
    queryFn: async ({ signal }) => {
      if (!selectedId) throw new Error("Select a provider first.");
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");
      return getProvider(accessToken, selectedId, signal);
    },
    retry: false,
  });

  const change = useMutation({
    mutationFn: async (request: ProviderChange) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken) throw new Error("Auth0 did not return an access token.");

      switch (request.action) {
        case "create":
          await createProvider(accessToken, clinicPublicId, request.input);
          break;
        case "update":
          await updateProvider(accessToken, request.publicId, request.input);
          break;
        case "delete":
          await deleteProvider(accessToken, request.publicId);
          break;
      }
    },
    retry: false,
    onSuccess: async (_, request) => {
      if (request.action === "update") {
        await queryClient.invalidateQueries({
          queryKey: ["provider", userId, request.publicId],
          exact: true,
        });
      } else if (request.action === "delete") {
        queryClient.removeQueries({
          queryKey: ["provider", userId, request.publicId],
          exact: true,
        });
      }

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: directoryKey, exact: true }),
        queryClient.invalidateQueries({ queryKey: ["clinic-provider-list", userId, clinicPublicId], exact: true }),
      ]);
    },
  });

  function closeProviderForm() {
    change.reset();
    setNotice("");
    setFormMode(null);
  }

  function submitChange(request: ProviderChange) {
    if (change.isPending) return;
    setNotice("");
    change.mutate(request, {
      onSuccess: () => {
        setFormMode(null);
        if (request.action === "delete") {
          void navigate("/clinics/" + clinicPublicId + "/providers", { replace: true, state: { deleted: true } });
          return;
        }
        const messages = {
          create: "Provider added.",
          update: "Provider updated.",
        };
        setNotice(messages[request.action]);
      },
    });
  }

  if (directory.isPending) {
    return <p className="mt-3" role="status">Loading providers…</p>;
  }
  if (directory.isError) {
    return (
      <div className="mt-3">
        <p role="alert">{providerError(directory.error, "Unable to load providers.")}</p>
        <button type="button" className="mt-3" disabled={directory.isFetching} onClick={() => void directory.refetch()}>
          <ActionIcon name="refresh" />
          Try again
        </button>
      </div>
    );
  }

  const { clinic, providers, canManage } = directory.data;
  const provider = detail.data;
  return (
    <>
      <div className="page-heading">
        <div>
          <h1 id="providers-heading" className="page-title">{selectedId ? "Provider details" : "Providers"}</h1>
          <p className="queue-meta">{clinic.name}</p>
        </div>
        {selectedId === null && canManage && formMode !== "create" && (
          <button type="button" onClick={() => {
            closeProviderForm();
            setFormMode("create");
          }}><ActionIcon name="plus" />Add provider</button>
        )}
      </div>

      {selectedId === null ? (
        <>
          {canManage && formMode === "create" && (
            <ProviderForm
              isSaving={change.isPending}
              onSave={(input) => submitChange({ action: "create", input })}
              onCancel={closeProviderForm}
            />
          )}
          <ClinicProvidersTable clinicId={clinicPublicId} providers={providers} />
        </>
      ) : !belongsToClinic ? (
        <p role="alert">This provider was not found at this clinic.</p>
      ) : (
        <div className="mt-4 space-y-3">
          {detail.isPending && <p role="status">Loading provider details…</p>}
          {detail.isError && (
            <div>
              <p role="alert">{providerError(detail.error, "Unable to load provider details.")}</p>
              <button type="button" className="mt-3" disabled={detail.isFetching} onClick={() => void detail.refetch()}>
                <ActionIcon name="refresh" />
                Try again
              </button>
            </div>
          )}
          {provider && detail.isSuccess && (
            <>
              <dl>
                <dt className="text-sm font-medium">Provider name</dt>
                <dd>{provider.firstName} {provider.lastName}</dd>
              </dl>
              {formMode !== "edit" && (
                <ProviderServices
                  key={provider.publicId}
                  userId={userId}
                  clinicPublicId={clinicPublicId}
                  providerPublicId={provider.publicId}
                  disabled={change.isPending}
                />
              )}
              {canManage && formMode === "edit" && (
                <ProviderForm
                  key={provider.publicId}
                  provider={provider}
                  isSaving={change.isPending}
                  onSave={(input) => submitChange({ action: "update", publicId: provider.publicId, input })}
                  onCancel={closeProviderForm}
                />
              )}
              {canManage && formMode !== "edit" && (
                <div className="flex flex-wrap gap-2">
                  <button
                    type="button"
                    disabled={change.isPending || isChangingAssignment}
                    onClick={() => {
                      change.reset();
                      setNotice("");
                      setFormMode("edit");
                    }}
                  >
                    <ActionIcon name="pencil" />
                    Edit provider
                  </button>
                  <button
                    type="button"
                    disabled={change.isPending || isChangingAssignment}
                    data-variant="danger"
                    onClick={() => {
                      const name = provider.firstName + " " + provider.lastName;
                      if (window.confirm("Permanently delete " + name + "?")) {
                        submitChange({ action: "delete", publicId: provider.publicId });
                      }
                    }}
                  >
                    <ActionIcon name="trash" />
                    {change.isPending ? "Deleting…" : "Delete provider"}
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      )}

      {change.isError && (
        <p className="mt-3" role="alert">
          {providerError(change.error, "Unable to confirm the change. Check the provider list before trying again.")}
        </p>
      )}
      {notice && (
        <div className="mt-3 flex items-center gap-3">
          <p role="status">{notice}</p>
          <button type="button" aria-label="Dismiss provider message" onClick={() => setNotice("")}>
            <ActionIcon name="close" />
            Dismiss
          </button>
        </div>
      )}
    </>
  );
}

function ProvidersPage() {
  const { user } = useAuth0();
  const { clinicPublicId, publicId } = useParams<"clinicPublicId" | "publicId">();
  const selectedId = publicId?.toLowerCase() ?? null;

  return (
    <section
      aria-label="Providers"
      className="workspace-panel mx-auto w-full max-w-4xl space-y-4"
    >
      <Link to={clinicPublicId ? "/clinics/" + clinicPublicId : "/clinics"}
        className="workspace-button" data-variant="secondary">
        <ActionIcon name="back" />
        {clinicPublicId ? "Back to clinic" : "Back to clinics"}
      </Link>
      {user?.sub && clinicPublicId ? (
        <ProviderDirectory
          key={user.sub + ":" + clinicPublicId + ":" + (selectedId ?? "")}
          userId={user.sub}
          clinicPublicId={clinicPublicId}
          selectedId={selectedId}
        />
      ) : (
        <p className="mt-3" role="alert">Unable to open this clinic’s providers.</p>
      )}
    </section>
  );
}

export default ProvidersPage;
