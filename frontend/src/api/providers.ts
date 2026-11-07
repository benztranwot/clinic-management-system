export type ProviderInput = {
  firstName: string;
  lastName: string;
};

export type Provider = ProviderInput & { publicId: string };

export class ProviderRequestError extends Error {
  readonly status: number;

  constructor(status: number) {
    super("Provider request failed.");
    this.status = status;
  }
}

function clinicProvidersPath(clinicPublicId: string) {
  return "/api/clinics/" + encodeURIComponent(clinicPublicId) + "/providers";
}

function providerPath(publicId: string) {
  return "/api/providers/" + encodeURIComponent(publicId);
}

async function readProviderData<T>(
  url: string,
  accessToken: string,
  signal: AbortSignal,
): Promise<T> {
  const response = await fetch(url, {
    headers: { Authorization: "Bearer " + accessToken },
    cache: "no-store",
    signal,
  });

  if (!response.ok) throw new ProviderRequestError(response.status);
  return response.json();
}

export function getProviders(accessToken: string, clinicPublicId: string, signal: AbortSignal) {
  return readProviderData<Provider[]>(clinicProvidersPath(clinicPublicId), accessToken, signal);
}

export function getProvider(accessToken: string, publicId: string, signal: AbortSignal) {
  return readProviderData<Provider>(providerPath(publicId), accessToken, signal);
}

async function writeProvider(
  url: string,
  accessToken: string,
  method: "POST" | "PUT" | "DELETE",
  input?: ProviderInput,
): Promise<void> {
  const headers = new Headers({ Authorization: "Bearer " + accessToken });
  if (input) headers.set("Content-Type", "application/json");

  const response = await fetch(url, {
    method,
    headers,
    body: input ? JSON.stringify(input) : undefined,
  });

  if (!response.ok) throw new ProviderRequestError(response.status);
}

export function createProvider(accessToken: string, clinicPublicId: string, input: ProviderInput) {
  return writeProvider(clinicProvidersPath(clinicPublicId), accessToken, "POST", input);
}

export function updateProvider(accessToken: string, publicId: string, input: ProviderInput) {
  return writeProvider(providerPath(publicId), accessToken, "PUT", input);
}

export function deleteProvider(accessToken: string, publicId: string) {
  return writeProvider(providerPath(publicId), accessToken, "DELETE");
}
