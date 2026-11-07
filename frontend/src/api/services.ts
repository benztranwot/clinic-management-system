export type ServiceInput = {
  name: string;
};

export type Service = ServiceInput & { publicId: string };

export class ServiceRequestError extends Error {
  readonly status: number;

  constructor(status: number) {
    super("Service request failed.");
    this.status = status;
  }
}

function clinicServicesPath(clinicPublicId: string) {
  return "/api/clinics/" + encodeURIComponent(clinicPublicId) + "/services";
}

function servicePath(publicId: string) {
  return "/api/services/" + encodeURIComponent(publicId);
}

async function readServiceData<T>(
  url: string,
  accessToken: string,
  signal: AbortSignal,
): Promise<T> {
  const response = await fetch(url, {
    headers: { Authorization: "Bearer " + accessToken },
    cache: "no-store",
    signal,
  });

  if (!response.ok) throw new ServiceRequestError(response.status);
  return response.json();
}

export function getServices(accessToken: string, clinicPublicId: string, signal: AbortSignal) {
  return readServiceData<Service[]>(clinicServicesPath(clinicPublicId), accessToken, signal);
}

export function getService(accessToken: string, publicId: string, signal: AbortSignal) {
  return readServiceData<Service>(servicePath(publicId), accessToken, signal);
}

async function writeService(
  url: string,
  accessToken: string,
  method: "POST" | "PUT" | "DELETE",
  input?: ServiceInput,
): Promise<void> {
  const headers = new Headers({ Authorization: "Bearer " + accessToken });
  if (input) headers.set("Content-Type", "application/json");

  const response = await fetch(url, {
    method,
    headers,
    body: input ? JSON.stringify(input) : undefined,
  });

  if (!response.ok) throw new ServiceRequestError(response.status);
}

export function createService(accessToken: string, clinicPublicId: string, input: ServiceInput) {
  return writeService(clinicServicesPath(clinicPublicId), accessToken, "POST", input);
}

export function updateService(accessToken: string, publicId: string, input: ServiceInput) {
  return writeService(servicePath(publicId), accessToken, "PUT", input);
}

export function deleteService(accessToken: string, publicId: string) {
  return writeService(servicePath(publicId), accessToken, "DELETE");
}
