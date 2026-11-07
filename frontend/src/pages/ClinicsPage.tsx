import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import { ClinicRequestError, getClinics } from "../api/clinics";

function ClinicList({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();

  const query = useQuery({
    // Keep cached clinic data separate for each signed-in user.
    queryKey: ["clinics", userId],
    queryFn: async ({ signal }) => {
      const accessToken = await getAccessTokenSilently();

      if (!accessToken) {
        throw new Error("Auth0 did not return an access token.");
      }

      return getClinics(accessToken, signal);
    },
    staleTime: 30_000,
    retry: false,
  });

  if (query.isPending) {
    return (
      <p className="mt-3" role="status">
        Loading clinics…
      </p>
    );
  }

  if (query.isError) {
    const status =
      query.error instanceof ClinicRequestError
        ? query.error.status
        : undefined;

    if (status === 401) {
      return (
        <p className="mt-3" role="alert">
          Your sign-in couldn’t be verified. Sign out and sign back in.
        </p>
      );
    }

    if (status === 403) {
      return (
        <p className="mt-3" role="alert">
          Your account doesn’t have permission to view clinics.
        </p>
      );
    }

    return (
      <div className="mt-3">
        <p role="alert">Unable to load clinics.</p>
        <button
          type="button"
          className="mt-3"
          disabled={query.isFetching}
          onClick={() => void query.refetch()}
        >
          Try again
        </button>
      </div>
    );
  }

  if (query.data.length === 0) {
    return <p className="mt-3 text-slate-600">No clinics found.</p>;
  }

  return (
    <ul className="mt-3 divide-y divide-slate-200" aria-label="Clinics">
      {query.data.map((clinic) => (
        <li key={clinic.publicId} className="py-3 font-medium">
          {clinic.name}
        </li>
      ))}
    </ul>
  );
}

function ClinicsPage() {
  const { user } = useAuth0();

  return (
    <section
      aria-labelledby="clinics-heading"
      className="mt-8 rounded-lg border border-slate-200 bg-white p-6"
    >
      <h2 id="clinics-heading" className="text-xl font-semibold text-slate-900">
        Clinics
      </h2>

      {user?.sub ? (
        <ClinicList userId={user.sub} />
      ) : (
        <p className="mt-3" role="alert">
          We couldn’t identify your session. Sign out and sign back in.
        </p>
      )}
    </section>
  );
}

export default ClinicsPage;
