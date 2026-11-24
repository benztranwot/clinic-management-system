import { useAuth0 } from "@auth0/auth0-react";
import { useQuery } from "@tanstack/react-query";
import LoadingStatus, { Spinner } from "../components/LoadingStatus";
import { getAccountOnboarding } from "./accountOnboarding";

export default function AccountOnboardingPanel({ userId, onCheckAccess, checkingAccess }: {
  userId: string;
  onCheckAccess: () => void;
  checkingAccess: boolean;
}) {
  const { getAccessTokenSilently } = useAuth0();
  const state = useQuery({
    queryKey: ["email-onboarding", userId],
    queryFn: async ({ signal }) => {
      const token = await getAccessTokenSilently();
      if (!token) throw new Error("Your session could not be restored. Sign out and sign in again.");
      return getAccountOnboarding(token, signal);
    },
    retry: false,
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
