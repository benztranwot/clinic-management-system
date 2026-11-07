export type Clinic = {
  publicId: string;
  name: string;
};

export class ClinicRequestError extends Error {
  readonly status: number;

  constructor(status: number) {
    super("Clinic request failed.");
    this.status = status;
  }
}

// Request the clinic list using the signed-in user's access token.
export async function getClinics(
  accessToken: string,
  signal: AbortSignal,
): Promise<Clinic[]> {
  const response = await fetch("/api/clinics", {
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
  name: string,
): Promise<Clinic> {
  return saveClinic("/api/clinics", "POST", accessToken, name);
}

export function updateClinic(
  accessToken: string,
  publicId: string,
  name: string,
): Promise<Clinic> {
  return saveClinic(
    `/api/clinics/${encodeURIComponent(publicId)}`,
    "PUT",
    accessToken,
    name,
  );
}

// Both write endpoints accept a clinic name and return the saved record.
async function saveClinic(
  path: string,
  method: "POST" | "PUT",
  accessToken: string,
  name: string,
): Promise<Clinic> {
  const response = await fetch(path, {
    method,
    headers: {
      Authorization: `Bearer ${accessToken}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ name }),
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
