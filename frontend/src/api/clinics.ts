export type ClinicInput = {
  name: string;
  address: string;
  timeZoneId: string;
};

export type ClinicUpdate = Pick<ClinicInput, "name"> | (ClinicInput & {
  expectedAddress: string;
  expectedTimeZoneId: string;
});

export type Clinic = ClinicInput & { publicId: string };

export class ClinicRequestError extends Error {
  readonly status: number;

  constructor(status: number) {
    super("Clinic request failed.");
    this.status = status;
  }
}

// Request clinic records using the signed-in user's access token.
export function getClinics(accessToken: string, signal: AbortSignal): Promise<Clinic[]> {
  return readClinicData("/api/clinics", accessToken, signal);
}

export function getClinic(
  accessToken: string,
  publicId: string,
  signal: AbortSignal,
): Promise<Clinic> {
  return readClinicData("/api/clinics/" + encodeURIComponent(publicId), accessToken, signal);
}

async function readClinicData<T>(
  url: string,
  accessToken: string,
  signal: AbortSignal,
): Promise<T> {
  const response = await fetch(url, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
    cache: "no-store",
    signal,
  });

  if (!response.ok) {
    throw new ClinicRequestError(response.status);
  }

  return response.json();
}

// Create a clinic or update the clinic identified by its public ID.
export function createClinic(
  accessToken: string,
  input: ClinicInput,
): Promise<Clinic> {
  return saveClinic("/api/clinics", "POST", accessToken, input);
}

export function updateClinic(
  accessToken: string,
  publicId: string,
  input: ClinicUpdate,
): Promise<Clinic> {
  return saveClinic(
    `/api/clinics/${encodeURIComponent(publicId)}`,
    "PUT",
    accessToken,
    input,
  );
}

// Both write endpoints return the saved clinic.
async function saveClinic(
  path: string,
  method: "POST" | "PUT",
  accessToken: string,
  input: ClinicInput | ClinicUpdate,
): Promise<Clinic> {
  const response = await fetch(path, {
    method,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(input),
  });

  if (!response.ok) {
    throw new ClinicRequestError(response.status);
  }

  return response.json();
}

// Delete an unreferenced clinic. A successful DELETE has no response body.
export async function deleteClinic(
  accessToken: string,
  publicId: string,
): Promise<void> {
  const response = await fetch(
    `/api/clinics/${encodeURIComponent(publicId)}`,
    {
      method: "DELETE",
      headers: {
        Authorization: `Bearer ${accessToken}`,
      },
    },
  );

  if (!response.ok) {
    throw new ClinicRequestError(response.status);
  }
}
