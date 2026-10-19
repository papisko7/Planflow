import type { TaskStatus } from './task';
import type { TeamRole } from './team';

// Mirrors PlanFlow.Domain.Enums.CalendarSharingLevel (the API serialises enums as numbers).
export enum CalendarSharingLevel {
  Off = 0,
  Availability = 1,
  Details = 2,
}

// Mirrors PlanFlow.Domain.Services.MemberStatus
export enum MemberStatus {
  NoData = 0,
  Free = 1,
  Busy = 2,
  FocusTime = 3,
  OutOfOffice = 4,
}

// Mirrors PlanFlow.Domain.Services.DayAvailability
export enum DayAvailability {
  NoData = 0,
  Free = 1,
  Partial = 2,
  Busy = 3,
  OutOfOffice = 4,
}

// Mirrors PlanFlow.Domain.Services.LoadLevel
export enum LoadLevel {
  Light = 0,
  Balanced = 1,
  Overloaded = 2,
}

// Mirrors PlanFlow.Domain.Services.BlockKind
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

// Mirrors PlanFlow.Application.Availability.Common.MemberAvailabilityDto
export interface MemberAvailabilityDto {
  userId: string;
  displayName: string;
  role: TeamRole;
  sharingLevel: CalendarSharingLevel;
  currentStatus: MemberStatus;
  // Filled only when the person shares details ("Home", "Office"...).
  currentWorkingLocation: string | null;
  windowLoadPercent: number | null;
  loadLevel: LoadLevel;
  openTasks: number;
  // Open tasks the load cannot count because they have no effort estimate.
  tasksWithoutEffort: number;
  days: DayAvailabilityDto[];
}

// Mirrors PlanFlow.Application.Availability.Common.TeamAvailabilityDto
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

// Mirrors PlanFlow.Application.Availability.Common.MemberTaskDto
export interface MemberTaskDto {
  itemId: string;
  title: string;
  deadlineUtc: string;
  isAllDay: boolean;
  effortHours: number | null;
  status: TaskStatus;
  urgencyScore: number;
}

// Mirrors PlanFlow.Application.Availability.Common.MemberCalendarDto
export interface MemberCalendarDto {
  teamId: string;
  member: MemberAvailabilityDto;
  timeZoneId: string | null;
  entries: MemberCalendarEntryDto[];
  tasks: MemberTaskDto[];
}
