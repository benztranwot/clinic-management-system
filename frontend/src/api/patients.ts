export type PatientSummary = {
  publicId: string;
  firstName: string;
  lastName: string;
};

export class PatientRequestError extends Error {
  readonly status: number;

  constructor(status: number) {
    super("Patient request failed.");
    this.status = status;
  }
}

export type PatientDetails = PatientSummary & {
  dateOfBirth: string;
};

export function getPatients(
  accessToken: string,
  signal: AbortSignal,
): Promise<PatientSummary[]> {
  return readPatientsApi("/api/patients", accessToken, signal);
}

export function getPatient(
  accessToken: string,
  publicId: string,
  signal: AbortSignal,
): Promise<PatientDetails> {
  return readPatientsApi(
    "/api/patients/" + encodeURIComponent(publicId),
    accessToken,
    signal,
  );
}

async function readPatientsApi<T>(
  url: string,
  accessToken: string,
  signal: AbortSignal,
): Promise<T> {
  const response = await fetch(url, {
    headers: { Authorization: "Bearer " + accessToken },
    cache: "no-store",
    signal,
  });

  if (!response.ok) {
    throw new PatientRequestError(response.status);
  }

  return response.json();
}

export type PatientInput = {
  firstName: string;
  lastName: string;
  dateOfBirth: string;
};

export function registerPatient(
  accessToken: string,
  input: PatientInput,
): Promise<void> {
  return savePatient("/api/patients", "POST", accessToken, input);
}

export function updatePatient(
  accessToken: string,
  publicId: string,
  input: PatientInput,
): Promise<void> {
  return savePatient(
    "/api/patients/" + encodeURIComponent(publicId),
    "PUT",
    accessToken,
    input,
  );
}

async function savePatient(
  url: string,
  method: "POST" | "PUT",
  accessToken: string,
  input: PatientInput,
): Promise<void> {
  const response = await fetch(url, {
    method,
    headers: {
      Authorization: "Bearer " + accessToken,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(input),
  });

  if (!response.ok) {
    throw new PatientRequestError(response.status);
  }
}

export async function deletePatient(
  accessToken: string,
  publicId: string,
): Promise<void> {
  const response = await fetch("/api/patients/" + encodeURIComponent(publicId), {
    method: "DELETE",
    headers: { Authorization: "Bearer " + accessToken },
  });

  if (!response.ok) {
    throw new PatientRequestError(response.status);
  }
}
