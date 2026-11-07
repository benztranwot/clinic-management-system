export type Clinic = {
  publicId: string;
  name: string;
};

export class ClinicRequestError extends Error {
  readonly status: number;

  constructor(status: number) {
    super("Unable to load clinics.");
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
