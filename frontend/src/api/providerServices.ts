import type { Service } from "./services";

export class ProviderServiceRequestError extends Error {
  readonly status: number;

  constructor(status: number) {
    super("Provider service request failed.");
    this.status = status;
  }
}

function providerServicesPath(providerPublicId: string) {
  return "/api/providers/" + encodeURIComponent(providerPublicId) + "/services";
}

export async function getProviderServices(
  accessToken: string,
  providerPublicId: string,
  signal: AbortSignal,
): Promise<Service[]> {
  const response = await fetch(providerServicesPath(providerPublicId), {
    headers: { Authorization: "Bearer " + accessToken },
    cache: "no-store",
    signal,
  });

  if (!response.ok) throw new ProviderServiceRequestError(response.status);
  return response.json();
}

export async function assignProviderService(
  accessToken: string,
  providerPublicId: string,
  clinicalServicePublicId: string,
): Promise<void> {
  const response = await fetch(providerServicesPath(providerPublicId), {
    method: "POST",
    headers: {
      Authorization: "Bearer " + accessToken,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ clinicalServicePublicId }),
  });

  if (!response.ok) throw new ProviderServiceRequestError(response.status);
}

export async function removeProviderService(
  accessToken: string,
  providerPublicId: string,
  clinicalServicePublicId: string,
): Promise<void> {
  const url = providerServicesPath(providerPublicId) + "/" + encodeURIComponent(clinicalServicePublicId);
  const response = await fetch(url, {
    method: "DELETE",
    headers: { Authorization: "Bearer " + accessToken },
  });

  if (!response.ok) throw new ProviderServiceRequestError(response.status);
}
