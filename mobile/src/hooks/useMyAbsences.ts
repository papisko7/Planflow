import { useEffect, useState } from 'react';
import { getMyAbsences } from '../services/userService';
import type { MyAbsence } from '../types/user';

const NONE: MyAbsence[] = [];
const MAX_AGE_MS = 5 * 60_000;

// One shared copy for every card on screen: the first caller fetches, the others wait for the same answer.
let cached: { at: number; data: MyAbsence[] } | null = null;
let inflight: Promise<MyAbsence[]> | null = null;

export function resetMyAbsencesCache(): void {
  cached = null;
  inflight = null;
}

// The signed-in user's own Out of office time ahead. Best effort: when it cannot be loaded there is simply no warning.
export function useMyAbsences(): MyAbsence[] {
  const [absences, setAbsences] = useState<MyAbsence[]>(cached?.data ?? NONE);

  useEffect(() => {
    let cancelled = false;
    if (cached && Date.now() - cached.at < MAX_AGE_MS) {
      setAbsences(cached.data);
      return;
    }
    inflight ??= getMyAbsences()
      .then((data) => {
        const list = Array.isArray(data) ? data : NONE;
        cached = { at: Date.now(), data: list };
        return list;
      })
      .finally(() => {
        inflight = null;
      });
    inflight
      .then((data) => {
        if (!cancelled) setAbsences(data);
      })
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, []);

  return absences;
}
