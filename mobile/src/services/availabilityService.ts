import { http } from './httpClient';
import { dayKey } from '../utils/dateUtils';
import type { MemberCalendarDto, TeamAvailabilityDto } from '../types/availability';

// Who in the team is free, busy or away over `days` days from today, and how loaded they are.
// The server trims what each person shares; any member of the team may ask (no team switch needed).
export async function getTeamAvailability(teamId: string, days = 7, from: Date = new Date()): Promise<TeamAvailabilityDto> {
  const { data } = await http.get<TeamAvailabilityDto>(`/api/teams/${teamId}/availability`, {
    params: { from: dayKey(from), days },
  });
  return data;
}

export async function getMemberCalendar(teamId: string, userId: string, days = 7, from: Date = new Date()): Promise<MemberCalendarDto> {
  const { data } = await http.get<MemberCalendarDto>(`/api/teams/${teamId}/members/${userId}/calendar`, {
    params: { from: dayKey(from), days },
  });
  return data;
}
