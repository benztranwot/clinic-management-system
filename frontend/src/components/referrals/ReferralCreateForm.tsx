import { useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";
import { getPatients } from "../../api/patients";
import {
  createReferral,
  getReferralReceivingOptions,
  referralErrorMessage,
  type ReferralAccount,
  type ReferralDestination,
  type ReferralInput,
} from "../../api/referrals";
import { ActionIcon } from "../ActionIcon";

const fieldClass = "mt-1 block w-full rounded-md border border-field-border px-3 py-2";
const serviceKey = (name: string) => name.trim().toLowerCase();
const destinationKey = (destination: ReferralDestination) =>
  destination.clinicalServicePublicId + ":" + destination.providerPublicId;

function ReferralCreateForm({ userId, account, onCancel }: {
  userId: string;
  account: ReferralAccount;
  onCancel: () => void;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [service, setService] = useState("");
  const [destinationId, setDestinationId] = useState("");
  const [validationError, setValidationError] = useState("");

  async function getToken() {
    const token = await getAccessTokenSilently();
    if (!token) throw new Error("Auth0 did not return an access token.");
    return token;
  }

  const choices = useQuery({
    queryKey: ["referral-create-options", userId],
    queryFn: async ({ signal }) => {
      const token = await getToken();
      const [patients, options] = await Promise.all([
        getPatients(token, signal),
        getReferralReceivingOptions(token, signal),
      ]);
      return { patients, ...options };
    },
    retry: false,
  });

  const creation = useMutation({
    mutationFn: async (input: ReferralInput) => createReferral(await getToken(), input),
    retry: false,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["referrals", userId] }),
  });

  const destinations = choices.data?.destinations ?? [];
  const serviceNames = new Map(destinations.map(({ serviceName }) => [serviceKey(serviceName), serviceName]));
  const selectedService = serviceNames.has(service) ? service : "";
  const matchingDestinations = destinations.filter((item) => serviceKey(item.serviceName) === selectedService);
  const destination = matchingDestinations.find((item) => destinationKey(item) === destinationId);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (creation.isPending || !choices.isSuccess || !destination) return;

    const data = new FormData(event.currentTarget);
    const patientPublicId = String(data.get("patientPublicId") ?? "");
    const reason = String(data.get("reason") ?? "").trim();

    if (!choices.data.patients.some((patient) => patient.publicId === patientPublicId)) {
      setValidationError("Choose a patient.");
      return;
    }
    if (!reason) {
      setValidationError("Enter a reason for the referral.");
      return;
    }

    creation.mutate({
      patientPublicId,
      clinicalServicePublicId: destination.clinicalServicePublicId,
      assignedProviderPublicId: destination.providerPublicId,
      reason,
    }, {
      onSuccess: ({ publicId }) => {
        const clinicId = destination.clinicPublicId;
        const hasMembership = account.memberships.some((membership) => membership.clinicPublicId === clinicId);
        if (account.isAdministrator || hasMembership) {
          void navigate({
            pathname: "/referrals/" + publicId,
            search: account.isAdministrator ? "" : new URLSearchParams({ clinic: clinicId }).toString(),
          });
        } else {
          void navigate("/referrals", { state: { referralCreatedBy: userId } });
        }
      },
    });
  }

  return (
    <form className="space-y-4" onSubmit={handleSubmit} onChange={() => {
      if (validationError) setValidationError("");
      if (creation.isError) creation.reset();
    }}>
      {choices.isPending && <p role="status">Loading patients and receiving providers…</p>}
      {choices.isError && (
        <div className="space-y-2">
          <p role="alert">{referralErrorMessage(choices.error, "Unable to load referral options.")}</p>
          <button type="button" data-variant="secondary" disabled={choices.isFetching}
            onClick={() => void choices.refetch()}><ActionIcon name="refresh" />Try again</button>
        </div>
      )}
      {choices.isSuccess && (
        <fieldset className="space-y-4" disabled={creation.isPending}>
          <label className="block text-sm font-medium">
            Clinical service
            <select className={fieldClass} value={selectedService} required
              onChange={(event) => {
                setService(event.target.value);
                setDestinationId("");
              }}>
              <option value="">Choose a service</option>
              {[...serviceNames].map(([key, label]) => <option key={key} value={key}>{label}</option>)}
            </select>
          </label>
          {serviceNames.size === 0 && <p>No services have an eligible provider yet.</p>}
          {selectedService && (
            <label className="block text-sm font-medium">
              Receiving clinic and provider
              <select className={fieldClass} value={destination ? destinationId : ""} required
                onChange={(event) => setDestinationId(event.target.value)}>
                <option value="">Choose a clinic and provider</option>
                {matchingDestinations.map((item) => (
                  <option key={destinationKey(item)} value={destinationKey(item)}>
                    {item.clinicName} — {item.providerName}
                  </option>
                ))}
              </select>
            </label>
          )}

          {destination && (
            <div className="space-y-4 border-t border-panel-border pt-4">
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
              {choices.data.patients.length === 0 && <p>Register a patient before creating a referral.</p>}
              <p className="text-sm">
                <span className="font-medium">Referred by</span>{" "}
                {choices.data.referringProviderName}
              </p>
              <label className="block text-sm font-medium">
                Reason
                <textarea name="reason" className={fieldClass} maxLength={2000} rows={3}
                  placeholder="Explain why the patient needs this service." required />
              </label>
            </div>
          )}
        </fieldset>
      )}
      {validationError && <p role="alert">{validationError}</p>}
      {creation.isError && (
        <p role="alert">{referralErrorMessage(creation.error, "Unable to confirm creation. Check the referral list before trying again.")}</p>
      )}
      <div className="flex items-center gap-2">
        <button className="flex-1" type="button" data-variant="secondary" disabled={creation.isPending} onClick={onCancel}>
          <ActionIcon name="close" />Cancel
        </button>
        {choices.isSuccess && (
          <button className="flex-1" type="submit" disabled={creation.isPending || !destination || choices.data.patients.length === 0}>
            <ActionIcon name="plus" />{creation.isPending ? "Creating…" : "Create referral"}
          </button>
        )}
      </div>
    </form>
  );
}

export default ReferralCreateForm;
