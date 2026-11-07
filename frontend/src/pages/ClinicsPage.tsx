import { useState } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useIsMutating, useQuery } from "@tanstack/react-query";
import { ClinicRequestError, getClinics, type Clinic } from "../api/clinics";
import ClinicForm from "../components/clinics/ClinicForm";
import DeleteClinicButton from "../components/clinics/DeleteClinicButton";

function ClinicList({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const [editingClinic, setEditingClinic] = useState<Clinic | null>(null);
  const [notice, setNotice] = useState("");
  const isManaging = useIsMutating({ mutationKey: ["manage-clinic", userId] }) > 0;

  const query = useQuery({
    // Keep cached clinic data separate for each signed-in user.
    queryKey: ["clinics", userId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently({ detailedResponse: true });

      if (!token?.access_token) {
        throw new Error("Auth0 did not return an access token.");
      }

      const clinics = await getClinics(token.access_token, signal);

      return {
        clinics,
        canManage:
          token.scope?.split(/\s+/).includes("manage:clinics") ?? false,
      };
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

  const { clinics, canManage } = query.data;

  return (
    <>
      {canManage && (
        <ClinicForm
          key={editingClinic?.publicId ?? "create"}
          userId={userId}
          clinic={editingClinic}
          onCancel={() => setEditingClinic(null)}
        />
      )}

      {notice && (
        <div className="mt-3 flex items-center gap-3">
          <p role="status">{notice}</p>
          <button
            type="button"
            aria-label="Dismiss clinic notification"
            onClick={() => setNotice("")}
          >
            Dismiss
          </button>
        </div>
      )}

      {clinics.length === 0 ? (
        <p className="mt-3 text-slate-600">No clinics found.</p>
      ) : (
        <ul className="mt-3 divide-y divide-slate-200" aria-label="Clinics">
          {clinics.map((clinic) => (
            <li
              key={clinic.publicId}
              className="flex items-center justify-between gap-3 py-3 font-medium"
            >
              <span className="min-w-0 break-words">{clinic.name}</span>
              {canManage && (
                <div className="flex shrink-0 flex-wrap items-start gap-2">
                  <button
                    type="button"
                    aria-label={`Edit ${clinic.name}`}
                    disabled={isManaging || editingClinic?.publicId === clinic.publicId}
                    onClick={() => setEditingClinic(clinic)}
                  >
                    Edit
                  </button>
                  <DeleteClinicButton
                    clinic={clinic}
                    userId={userId}
                    disabled={isManaging || editingClinic?.publicId === clinic.publicId}
                    onNotice={setNotice}
                  />
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </>
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
