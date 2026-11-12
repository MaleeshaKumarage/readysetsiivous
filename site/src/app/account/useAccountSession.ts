'use client';
import { useEffect, useState } from 'react';
import { initAuth, isAuthenticated, isAdmin, isManager, isPartner, refreshToken, selectAuthTenant } from '@/lib/auth';
import { API_URL } from '@/lib/api';

export interface Account { realm: string; person?: { id: string; firstName: string; lastName: string; role: string; isActive: boolean }; company?: { id: string; name: string; isActive: boolean; isPartnerEnabled: boolean }; employee?: { id: string; firstName: string; lastName: string; role: string; isActive: boolean; identitySyncPending?: boolean } | null; }

/** Display name for the signed-in account. */
export function accountName(account: Account | null): string {
  if (!account) return 'Tenant administrator';
  if (account.person) return `${account.person.firstName} ${account.person.lastName}`;
  if (account.employee) return `${account.employee.firstName} ${account.employee.lastName}`;
  return 'Tenant administrator';
}

/** Role labels shown on the account pages. Uses the signed-in login's roles. */
export function accountRoles(account: Account | null): string[] {
  if (!account) return [];
  return [isAdmin() && 'Administrator', isManager() && 'Manager', isPartner() ? 'Partner' : account.employee && 'Cleaner'].filter(Boolean) as string[];
}

/** Loads and verifies the signed-in account for a tenant. Shared by the account page and the profile page. */
export function useAccountSession(tenant: string) {
  const [loading, setLoading] = useState(true);
  const [authed, setAuthed] = useState(false);
  const [account, setAccount] = useState<Account | null>(null);
  const [error, setError] = useState('');
  useEffect(() => {
    let cancelled = false;
    setLoading(true); setAuthed(false); setAccount(null); setError('');
    async function load() {
      try {
        selectAuthTenant(tenant);
        await initAuth();
        if (cancelled) return;
        if (!isAuthenticated()) { setAuthed(false); return; }
        setAuthed(true);
        const jwt = await refreshToken();
        if (!jwt) throw new Error('Please sign in again.');
        const response = await fetch(`${API_URL}/api/v1/${isPartner() ? 'partner/me' : 'me'}`, { headers: { Authorization: `Bearer ${jwt}` }, cache: 'no-store' });
        if (!response.ok) {
          const problem = await response.json().catch(() => ({})) as { detail?: string; title?: string };
          throw new Error(problem.detail || problem.title || 'Could not verify your account. Please try again.');
        }
        const identity = await response.json() as Account;
        if (identity.realm !== tenant) throw new Error('This login belongs to a different tenant. Sign out and open your invitation again.');
        if (isPartner() && (!identity.person?.isActive || identity.person.role !== 'partner' || !identity.company?.isActive || !identity.company.isPartnerEnabled)) throw new Error('Partner access is unavailable. Contact your tenant administrator.');
        if (!isPartner() && !identity.employee && !isAdmin()) throw new Error('Your login is not linked to an employee. Contact your tenant administrator.');
        if (!cancelled) setAccount(identity);
      } catch (error) { if (!cancelled) setError(error instanceof Error ? error.message : 'Account setup could not be confirmed.'); }
      finally { if (!cancelled) setLoading(false); }
    }
    load();
    return () => { cancelled = true; };
  }, [tenant]);
  return { loading, authed, account, error, setError };
}
