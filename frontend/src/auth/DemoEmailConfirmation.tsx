import { useState, type FormEvent } from "react";
import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router";

class DemoOnboardingDisabledError extends Error {}

export function DemoEmailConfirmation({ userId }: { userId: string }) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [code, setCode] = useState("");
  const validCode = /^[0-9]{8}$/.test(code);

  async function post(path: string, confirmationCode?: string) {
    const accessToken = await getAccessTokenSilently();
    const response = await fetch(path, {
      method: "POST",
      headers: {
        Authorization: "Bearer " + accessToken,
        ...(confirmationCode && { "Content-Type": "application/json" }),
      },
      body: confirmationCode ? JSON.stringify({ code: confirmationCode }) : undefined,
    });

    if (!response.ok) {
      if (response.status === 404) throw new DemoOnboardingDisabledError();
      const problem = await response.json().catch(() => null);
      throw new Error(typeof problem?.title === "string"
        ? problem.title
        : "We couldn’t complete email confirmation. Please try again.");
    }
    return response.status;
  }

  async function openWorkspace() {
    void navigate("/", { replace: true });
    await queryClient.invalidateQueries({ queryKey: ["referral-account", userId] });
  }

  const request = useQuery({
    queryKey: ["demo-email", userId],
    retry: false,
    refetchOnReconnect: false,
    refetchOnWindowFocus: false,
    retryOnMount: false,
    staleTime: Infinity,
    queryFn: async () => {
      const status = await post("/api/me/demo-email");
      if (status === 204) await openWorkspace();
      return status;
    },
  });

  const confirmation = useMutation({
    mutationFn: (value: string) => post("/api/me/demo-workspace", value),
    onSuccess: openWorkspace,
  });

  function submitCode(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (validCode && !request.isFetching && !confirmation.isPending) confirmation.mutate(code);
  }

  if (request.error instanceof DemoOnboardingDisabledError) {
    return (
      <section className="workspace-panel space-y-3">
        <h1 className="page-title">Unable to load your workspace</h1>
        <p role="alert">This account doesn’t have access to a workspace.</p>
      </section>
    );
  }

  return (
    <section className="demo-email-panel workspace-panel w-full max-w-lg space-y-4 text-center" aria-labelledby="demo-email-title">
      <h1 id="demo-email-title" className="page-title">Check your email</h1>
      {request.isFetching ? <p role="status">Sending your confirmation code…</p>
        : request.isError ? <p role="alert">{request.error.message}</p>
        : <p>We emailed you an 8-digit code. Enter it here within 20 minutes.</p>}
      <p className="text-sm text-subtle">If you don’t see the email in your inbox, check your Spam or Junk folder.</p>
      <form className="space-y-4" onSubmit={submitCode}>
        <div className="mx-auto w-full max-w-64">
          <label className="block text-sm font-semibold" htmlFor="demo-email-code">Confirmation code</label>
          <input
            id="demo-email-code"
            className="mt-2 block w-full rounded-md border border-field-border px-3 py-2.5 text-center text-lg tracking-widest tabular-nums"
            type="text"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={8}
            value={code}
            onChange={(event) => {
              setCode(event.target.value.replace(/[^0-9]/g, ""));
              if (confirmation.isError) confirmation.reset();
            }}
            disabled={confirmation.isPending}
          />
        </div>
        {confirmation.isError && <p role="alert">{confirmation.error.message}</p>}
        <div className="flex flex-wrap justify-center gap-2">
          <button type="submit" disabled={!validCode || request.isFetching || confirmation.isPending}>
            {confirmation.isPending ? "Confirming…" : "Confirm email"}
          </button>
          <button
            type="button"
            disabled={request.isFetching || confirmation.isPending}
            onClick={() => {
              confirmation.reset();
              void request.refetch();
            }}
          >
            Send another code
          </button>
        </div>
      </form>
    </section>
  );
}
