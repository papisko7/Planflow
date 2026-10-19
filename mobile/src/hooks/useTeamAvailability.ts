import { useEffect, useState } from 'react';
import { getTeamAvailability } from '../services/availabilityService';
import type { MemberAvailabilityDto } from '../types/availability';

const NONE: Record<string, MemberAvailabilityDto> = {};

export function useTeamAvailability(teamId: string | null | undefined, enabled = true): Record<string, MemberAvailabilityDto> {
  const [byUser, setByUser] = useState<Record<string, MemberAvailabilityDto>>(NONE);

  useEffect(() => {
    if (!teamId || !enabled) {
      setByUser(NONE);
      return;
    }
    let cancelled = false;
    getTeamAvailability(teamId)
      .then((grid) => {
        if (!cancelled) setByUser(Object.fromEntries(grid.members.map((m) => [m.userId, m])));
      })
      .catch(() => {
        if (!cancelled) setByUser(NONE);
      });
    return () => {
      cancelled = true;
    };
  }, [teamId, enabled]);

  return byUser;
}
