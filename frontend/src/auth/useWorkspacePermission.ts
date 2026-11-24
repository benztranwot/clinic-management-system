import { useReferralAccount } from "../features/referrals/hooks/useReferralAccount";

// Server-reported workspace capabilities supplement existing Auth0 scopes in the UI.
// Every API operation still independently enforces its authorization policy.
export function useWorkspacePermission(userId: string | undefined, permission: string) {
  const account = useReferralAccount(userId);
  return account.data?.account.workspacePermissions?.includes(permission) === true;
}
