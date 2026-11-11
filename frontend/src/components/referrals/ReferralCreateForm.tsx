import { useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { getClinics } from "../../api/clinics";
import { getPatients } from "../../api/patients";
import {
  createReferral,
  getServiceEligibleProviders,
  referralErrorMessage,
  ReferralRequestError,
  type ReferralInput,
} from "../../api/referrals";
import { getServices } from "../../api/services";

function ReferralCreateForm({
  userId,
  onCancel,
}: {
  userId: string;
  onCancel: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [clinicId, setClinicId] = useState("");
  const [serviceId, setServiceId] = useState("");
  const [validationError, setValidationError] = useState("");

  const choices = useQuery({
    queryKey: ["referral-create-options", userId],
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken)
        throw new Error("Auth0 did not return an access token.");
      const [patients, clinics] = await Promise.all([
        getPatients(accessToken, signal),
        getClinics(accessToken, signal),
      ]);
      return { patients, clinics };
    },
    retry: false,
  });

  const services = useQuery({
    queryKey: ["referral-create-services", userId, clinicId],
    enabled: clinicId !== "",
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken)
        throw new Error("Auth0 did not return an access token.");
      return getServices(accessToken, clinicId, signal);
    },
    retry: false,
  });

  const providers = useQuery({
    queryKey: ["referral-create-providers", userId, serviceId],
    enabled: serviceId !== "",
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken)
        throw new Error("Auth0 did not return an access token.");
      return getServiceEligibleProviders(accessToken, serviceId, signal);
    },
    retry: false,
  });

  const creation = useMutation({
    mutationFn: async (input: ReferralInput) => {
      const accessToken = await getAccessTokenSilently();
      if (!accessToken)
        throw new Error("Auth0 did not return an access token.");
      return createReferral(accessToken, input);
    },
    retry: false,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["referrals", userId] });
    },
  });

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (
      creation.isPending ||
      !choices.isSuccess ||
      !services.isSuccess ||
      !providers.isSuccess
    )
      return;

    const data = new FormData(event.currentTarget);
    const input = {
      patientPublicId: String(data.get("patientPublicId") ?? ""),
      clinicalServicePublicId: serviceId,
      assignedProviderPublicId: String(
        data.get("assignedProviderPublicId") ?? "",
      ),
      referringProviderName: String(
        data.get("referringProviderName") ?? "",
      ).trim(),
      reason: String(data.get("reason") ?? "").trim(),
    };

    if (
      !input.patientPublicId ||
      !input.clinicalServicePublicId ||
      !input.referringProviderName ||
      !input.reason
    ) {
      setValidationError(
        "Select a patient and service, and enter the referring provider and reason.",
      );
      return;
    }

    if (
      !providers.data.some(
        (provider) => provider.publicId === input.assignedProviderPublicId,
      )
    ) {
      setValidationError(
        "Select a receiving provider who offers this service.",
      );
      return;
    }

    creation.mutate(input, {
      onSuccess: ({ publicId }) => void navigate("/referrals/" + publicId),
    });
  }

  const providerLookupForbidden =
    providers.error instanceof ReferralRequestError &&
    providers.error.status === 403;
  const fieldClass =
    "mt-1 block w-full rounded-md border border-field-border px-3 py-2";
  return (
    <form
      className="space-y-4"
      onSubmit={handleSubmit}
      onChange={() => {
        if (validationError) setValidationError("");
        if (creation.isError) creation.reset();
      }}
    >
      {choices.isPending && <p role="status">Loading patients and clinics…</p>}
      {choices.isError && (
        <div className="space-y-2">
          <p role="alert">
            {referralErrorMessage(
              choices.error,
              "Unable to load patients and clinics.",
            )}
          </p>
          <button
            type="button"
            data-variant="secondary"
            disabled={choices.isFetching}
            onClick={() => void choices.refetch()}
          >
            Try again
          </button>
        </div>
      )}
      {choices.isSuccess && (
        <fieldset className="space-y-3" disabled={creation.isPending}>
          <label className="block text-sm font-medium">
            Patient
            <select name="patientPublicId" className={fieldClass} required>
              <option value="">Choose a patient</option>
              {choices.data.patients.map((patient) => (
                <option key={patient.publicId} value={patient.publicId}>
                  {patient.lastName}, {patient.firstName}
                </option>
              ))}
            </select>
          </label>
          {choices.data.patients.length === 0 && (
            <p>Register a patient before creating a referral.</p>
          )}

          <label className="block text-sm font-medium">
            Clinic
            <select
              className={fieldClass}
              value={clinicId}
              onChange={(event) => {
                setClinicId(event.target.value);
                setServiceId("");
              }}
              required
            >
              <option value="">Choose a clinic</option>
              {choices.data.clinics.map((clinic) => (
                <option key={clinic.publicId} value={clinic.publicId}>
                  {clinic.name}
                </option>
              ))}
            </select>
          </label>

          <label className="block text-sm font-medium">
            Clinical service
            <select
              value={serviceId}
              onChange={(event) => setServiceId(event.target.value)}
              className={fieldClass}
              disabled={
                !clinicId || !services.isSuccess || services.data.length === 0
              }
              required
            >
              <option value="">Choose a service</option>
              {services.data?.map((service) => (
                <option key={service.publicId} value={service.publicId}>
                  {service.name}
                </option>
              ))}
            </select>
          </label>
          {clinicId && services.isPending && (
            <p role="status">Loading clinical services…</p>
          )}
          {services.isError && (
            <div className="space-y-2">
              <p role="alert">
                {referralErrorMessage(
                  services.error,
                  "Unable to load clinical services.",
                )}
              </p>
              <button
                type="button"
                data-variant="secondary"
                disabled={services.isFetching}
                onClick={() => void services.refetch()}
              >
                Retry clinical services
              </button>
            </div>
          )}
          {services.isSuccess && services.data.length === 0 && (
            <p>No services found at this clinic.</p>
          )}

          <label className="block text-sm font-medium">
            Receiving provider
            <select
              key={serviceId}
              name="assignedProviderPublicId"
              className={fieldClass}
              disabled={
                !serviceId ||
                !providers.isSuccess ||
                providers.data.length === 0
              }
              required
            >
              <option value="">Choose a receiving provider</option>
              {providers.data?.map((provider) => (
                <option key={provider.publicId} value={provider.publicId}>
                  {provider.lastName}, {provider.firstName}
                </option>
              ))}
            </select>
          </label>
          {serviceId && providers.isPending && (
            <p role="status">Loading eligible providers…</p>
          )}
          {providers.isError && (
            <div className="space-y-2">
              <p role="alert">
                {providerLookupForbidden
                  ? "Your account can’t view eligible providers. Ask an administrator for provider directory access."
                  : referralErrorMessage(
                      providers.error,
                      "Unable to load eligible providers.",
                    )}
              </p>
              {!providerLookupForbidden && (
                <button
                  type="button"
                  data-variant="secondary"
                  disabled={providers.isFetching}
                  onClick={() => void providers.refetch()}
                >
                  Retry providers
                </button>
              )}
            </div>
          )}
          {providers.isSuccess && providers.data.length === 0 && (
            <p>No providers at this clinic offer the selected service.</p>
          )}

          <label className="block text-sm font-medium">
            Referring provider name
            <input
              name="referringProviderName"
              className={fieldClass}
              maxLength={200}
              required
            />
          </label>
          <label className="block text-sm font-medium">
            Reason
            <textarea
              name="reason"
              className={fieldClass}
              maxLength={2000}
              rows={3}
              required
            />
          </label>
        </fieldset>
      )}
      <div className="flex items-center gap-2">
        <button
          className="flex-1"
          type="button"
          data-variant="secondary"
          disabled={creation.isPending}
          onClick={onCancel}
        >
          Cancel
        </button>
        {choices.isSuccess && (
          <button
            className="flex-1"
            type="submit"
            disabled={
              creation.isPending ||
              choices.data.patients.length === 0 ||
              !providers.isSuccess ||
              providers.data.length === 0
            }
          >
            {creation.isPending ? "Creating…" : "Create referral"}
          </button>
        )}
      </div>
      {validationError && <p role="alert">{validationError}</p>}
      {creation.isError && (
        <p role="alert">
          {referralErrorMessage(
            creation.error,
            "Unable to confirm creation. Check the referral list before trying again.",
          )}
        </p>
      )}
    </form>
  );
}

export default ReferralCreateForm;
