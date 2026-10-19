import type { TaskStatus } from './task';
import type { TeamRole } from './team';

export enum CalendarSharingLevel {
  Off = 0,
  Availability = 1,
  Details = 2,
}

export enum MemberStatus {
  NoData = 0,
  Free = 1,
  Busy = 2,
  FocusTime = 3,
  OutOfOffice = 4,
}

export enum DayAvailability {
  NoData = 0,
  Free = 1,
  Partial = 2,
  Busy = 3,
  OutOfOffice = 4,
}

export enum LoadLevel {
  Light = 0,
  Balanced = 1,
  Overloaded = 2,
}

export enum BlockKind {
  Busy = 0,
  Free = 1,
  OutOfOffice = 2,
  FocusTime = 3,
  WorkingLocation = 4,
  Birthday = 5,
}

// Mirrors PlanFlow.Application.Availability.Common.DayAvailabilityDto. `date` is a calendar date (read its first 10 characters).
export interface DayAvailabilityDto {
  date: string;
  availability: DayAvailability;
  capacityHours: number;
  loadHours: number;
  loadPercent: number | null;
  loadLevel: LoadLevel;
}

export interface MemberAvailabilityDto {
  userId: string;
  displayName: string;
  role: TeamRole;
  sharingLevel: CalendarSharingLevel;
  currentStatus: MemberStatus;
  currentWorkingLocation: string | null;
  windowLoadPercent: number | null;
  loadLevel: LoadLevel;
  openTasks: number;
  tasksWithoutEffort: number;
  days: DayAvailabilityDto[];
}

export interface TeamAvailabilityDto {
  teamId: string;
  from: string;
  days: number;
  nowUtc: string;
  members: MemberAvailabilityDto[];
}

// Mirrors PlanFlow.Application.Availability.Common.MemberCalendarEntryDto.
// `source` is "Team" (a team event, shown in full) or "Personal" (imported; title/location are null unless the person shares details).
export interface MemberCalendarEntryDto {
  itemId: string;
  source: 'Team' | 'Personal';
  kind: BlockKind;
  startUtc: string;
  endUtc: string;
  isAllDay: boolean;
  title: string | null;
  location: string | null;
}

export interface MemberTaskDto {
  itemId: string;
  title: string;
  deadlineUtc: string;
  isAllDay: boolean;
  effortHours: number | null;
  status: TaskStatus;
  urgencyScore: number;
}

export interface MemberCalendarDto {
  teamId: string;
  member: MemberAvailabilityDto;
  timeZoneId: string | null;
  entries: MemberCalendarEntryDto[];
  tasks: MemberTaskDto[];
  // Completed tasks of this team whose deadline is in the requested window (an item keeps no completion time).
  finishedTasks: MemberTaskDto[];
}
