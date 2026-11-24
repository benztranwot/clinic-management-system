import { useAuth0 } from "@auth0/auth0-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import LoadingStatus, { Spinner } from "../components/LoadingStatus";
import { createVisitorWorkspace, getAccountOnboarding } from "./accountOnboarding";

export default function AccountOnboardingPanel({ userId, onCheckAccess, checkingAccess }: {
  userId: string;
  onCheckAccess: () => void;
  checkingAccess: boolean;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const queryClient = useQueryClient();
  const state = useQuery({
    queryKey: ["email-onboarding", userId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Your session could not be restored. Sign out and sign in again.");
      return getAccountOnboarding(token, signal);
    },
    retry: false,
  });

  const creation = useMutation({
    mutationFn: async () => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Your session expired. Sign out and sign in again.");
      return createVisitorWorkspace(token);
    },
    retry: false,
    onSuccess: (ready) => {
      queryClient.setQueryData(["email-onboarding", userId], ready);
      onCheckAccess();
    },
  });

  if (state.isPending) return <LoadingStatus>Checking your email status…</LoadingStatus>;

  return (
    <section className="workspace-panel mx-auto w-full max-w-lg space-y-4 text-center" aria-labelledby="account-onboarding-heading">
      {state.isError ? <>
        <h1 id="account-onboarding-heading" className="page-title">Unable to check your account</h1>
        <p role="alert">{state.error.message}</p>
        <button type="button" disabled={state.isFetching} onClick={() => void state.refetch()}>
          {state.isFetching ? <Spinner /> : null}Retry
        </button>
      </> : state.data.status === "email_confirmation_required" ? <>
        <h1 id="account-onboarding-heading" className="page-title">Verify your email</h1>
        <p>Your sign-in provider has not confirmed <strong className="break-all">{state.data.email}</strong> yet.</p>
        <p>Complete email verification through your sign-in provider. Check your Spam or Junk folder for its message.</p>
        <p>If verification is unavailable, contact the application owner. Verifying your email does not automatically grant workspace access.</p>
        <button type="button" disabled={state.isFetching} onClick={() => void state.refetch()}>
          {state.isFetching ? <Spinner /> : null}Check verification
        </button>
      </> : state.data.status === "workspace_pending" && state.data.canCreateWorkspace ? <>
        <h1 id="account-onboarding-heading" className="page-title">Create your private workspace</h1>
        <p>Your email is confirmed. Explore the app with a private copy of the sample clinic records.</p>
        <p>Your changes stay in your workspace and do not affect other visitors.</p>
        {creation.isError && <p role="alert">{creation.error.message}</p>}
        <button type="button" disabled={creation.isPending || checkingAccess}
          onClick={() => creation.mutate()}>
          {creation.isPending ? <Spinner /> : null}{creation.isPending ? "Creating workspace…" : "Create my workspace"}
        </button>
        {creation.isPending && <p role="status">Preparing your clinic records. This may take a moment.</p>}
      </> : <>
        <h1 id="account-onboarding-heading" className="page-title">
          {state.data.status === "ready" ? "Your account is ready" : "Email confirmed"}
        </h1>
        <p>{state.data.status === "ready" ? "You can open your workspace."
          : "Your email is confirmed. This account is waiting for workspace access."}</p>
        <button type="button" disabled={checkingAccess} onClick={onCheckAccess}>
          {checkingAccess ? <Spinner /> : null}{state.data.status === "ready" ? "Open workspace" : "Check access"}
        </button>
      </>}
    </section>
  );
}
