import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { logout as authLogout, restoreSession } from '../services/authService';

interface SessionState {
  status: 'checking' | 'signedIn' | 'signedOut';
  signIn: () => void;
  signOut: () => Promise<void>;
}

const SessionContext = createContext<SessionState | null>(null);

// Single source of truth for "are we signed in" so Login/Register can flip the whole app's
// navigation tree (Auth stack <-> main Tabs) instead of each screen guessing independently.
export function SessionProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<SessionState['status']>('checking');

  useEffect(() => {
    restoreSession().then((hasSession) => setStatus(hasSession ? 'signedIn' : 'signedOut'));
  }, []);

  const signIn = useCallback(() => setStatus('signedIn'), []);
  const signOut = useCallback(async () => {
    await authLogout();
    setStatus('signedOut');
  }, []);

  const value = useMemo(() => ({ status, signIn, signOut }), [status, signIn, signOut]);

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSession(): SessionState {
  const ctx = useContext(SessionContext);
  if (!ctx) throw new Error('useSession must be used within a SessionProvider');
  return ctx;
}
