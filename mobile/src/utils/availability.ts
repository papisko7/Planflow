import {
  BlockKind,
  CalendarSharingLevel,
  DayAvailability,
  LoadLevel,
  MemberStatus,
  type MemberAvailabilityDto,
  type MemberCalendarEntryDto,
  type MemberTaskDto,
} from '../types/availability';
import { colors } from '../theme/theme';
import { addDays, startOfDay } from './dateUtils';
import { utcDay } from './schedule';

export const STATUS_LABEL: Record<MemberStatus, string> = {
  [MemberStatus.NoData]: 'No data',
  [MemberStatus.Free]: 'Free',
  [MemberStatus.Busy]: 'Busy',
  [MemberStatus.FocusTime]: 'Focus time',
  [MemberStatus.OutOfOffice]: 'Out of office',
};

export const STATUS_COLOR: Record<MemberStatus, string> = {
  [MemberStatus.NoData]: colors.textMuted,
  [MemberStatus.Free]: colors.success,
  [MemberStatus.Busy]: colors.urgencyHigh,
  [MemberStatus.FocusTime]: colors.primary,
  [MemberStatus.OutOfOffice]: colors.danger,
};

export const DAY_LABEL: Record<DayAvailability, string> = {
  [DayAvailability.NoData]: 'No data',
  [DayAvailability.Free]: 'Free',
  [DayAvailability.Partial]: 'Partly busy',
  [DayAvailability.Busy]: 'Busy',
  [DayAvailability.OutOfOffice]: 'Out of office',
};

export const DAY_COLOR: Record<DayAvailability, string> = {
  [DayAvailability.NoData]: colors.border,
  [DayAvailability.Free]: colors.success,
  [DayAvailability.Partial]: colors.urgencyMedium,
  [DayAvailability.Busy]: colors.urgencyHigh,
  [DayAvailability.OutOfOffice]: colors.danger,
};

export const LOAD_COLOR: Record<LoadLevel, string> = {
  [LoadLevel.Light]: colors.success,
  [LoadLevel.Balanced]: colors.urgencyHigh,
  [LoadLevel.Overloaded]: colors.danger,
};

export const BLOCK_LABEL: Record<BlockKind, string> = {
  [BlockKind.Busy]: 'Busy',
  [BlockKind.Free]: 'Free',
  [BlockKind.OutOfOffice]: 'Out of office',
  [BlockKind.FocusTime]: 'Focus time',
  [BlockKind.WorkingLocation]: 'Working location',
  [BlockKind.Birthday]: 'Birthday',
};

export const SHARING_LABEL: Record<CalendarSharingLevel, string> = {
  [CalendarSharingLevel.Off]: 'Not shared',
  [CalendarSharingLevel.Availability]: 'Availability only',
  [CalendarSharingLevel.Details]: 'Details',
};

export const SHARING_HELP: Record<CalendarSharingLevel, string> = {
  [CalendarSharingLevel.Off]: 'Teammates see only your team items. Your availability shows as "No data".',
  [CalendarSharingLevel.Availability]: 'Teammates see when you are busy, away or in focus time - never titles, places or descriptions.',
  [CalendarSharingLevel.Details]: 'Teammates also see titles and places. Events marked private in Google stay "Busy".',
};

// The server sends 999 for "work but no time at all" (a week off), which is not a real percentage.
export const NO_CAPACITY_PERCENT = 999;

export function formatLoad(percent: number | null): string {
  if (percent === null) return '-';
  if (percent >= NO_CAPACITY_PERCENT) return 'no time';
  return `${Math.round(percent)}%`;
}

// The first 10 characters of a calendar date from the API ("2026-10-19T00:00:00" -> local 19 Oct 2026).
export function parseApiDay(value: string): Date {
  const [y, m, d] = value.slice(0, 10).split('-').map(Number);
  return new Date(y, m - 1, d);
}

// Short note shown on a person's chip in the assignee / co-contractor pickers: their state right now and their load this week.
export function pickerHint(member: MemberAvailabilityDto | undefined): string | null {
  if (!member) return null;
  const parts: string[] = [];
  if (member.currentStatus === MemberStatus.OutOfOffice) parts.push('away');
  else if (member.currentStatus === MemberStatus.FocusTime) parts.push('focus');
  else if (member.currentStatus === MemberStatus.Busy) parts.push('busy');
  if (member.windowLoadPercent !== null) parts.push(formatLoad(member.windowLoadPercent));
  return parts.length > 0 ? parts.join(' · ') : null;
}

// A sentence of warning for the person picked in the form, or null when there is nothing to warn about.
export function assigneeWarning(member: MemberAvailabilityDto | undefined): string | null {
  if (!member) return null;
  if (member.currentStatus === MemberStatus.OutOfOffice) return `${member.displayName} is out of office right now.`;
  if (member.loadLevel === LoadLevel.Overloaded) {
    return `${member.displayName} is already overloaded this week (${formatLoad(member.windowLoadPercent)} of their time).`;
  }
  return null;
}

// Does a block of someone's calendar touch the given local day? An all-day block is read literally (20 Oct is 20 Oct everywhere),
// a timed one by the viewer's clock.
export function entryOnDay(entry: MemberCalendarEntryDto, day: Date): boolean {
  if (entry.isAllDay) {
    const first = utcDay(entry.startUtc);
    const last = addDays(utcDay(entry.endUtc), -1);
    return startOfDay(day) >= first && startOfDay(day) <= (last < first ? first : last);
  }
  const start = startOfDay(day).getTime();
  const end = addDays(startOfDay(day), 1).getTime();
  return new Date(entry.startUtc).getTime() < end && new Date(entry.endUtc).getTime() > start;
}

export function taskDueOnDay(task: MemberTaskDto, day: Date): boolean {
  const due = task.isAllDay ? utcDay(task.deadlineUtc) : startOfDay(new Date(task.deadlineUtc));
  return due.getTime() === startOfDay(day).getTime();
}

const pad = (n: number) => String(n).padStart(2, '0');
const clock = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;

// "all day" or "10:00-11:30" for one entry (a block that crosses midnight shows its clock times only).
export function entryTime(entry: MemberCalendarEntryDto): string {
  if (entry.isAllDay) return 'all day';
  return `${clock(new Date(entry.startUtc))}-${clock(new Date(entry.endUtc))}`;
}

export function entryTitle(entry: MemberCalendarEntryDto): string {
  return entry.title ?? BLOCK_LABEL[entry.kind];
}

export function hoursLabel(hours: number | null): string {
  if (hours === null) return 'no estimate';
  return hours >= 1 ? `${Math.round(hours * 10) / 10} h` : `${Math.round(hours * 60)} min`;
}
