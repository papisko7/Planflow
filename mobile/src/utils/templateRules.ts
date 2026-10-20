import { EventTemplate, TaskType } from '../types/task';

// Mirrors the backend table PlanFlow.Domain.Services.EventTemplateRules: what each kind of item has.
// The backend enforces it (and drops what a template does not use); this copy lets the form hide those fields up front.
// If a row changes there, change it here as well - utils/__tests__/templateRules.test.ts pins the values.

export type AllDayMode = 'optional' | 'never' | 'always';
export type BusyMode = 'choice' | 'alwaysBusy' | 'none';
export type LocationMode = 'none' | 'text' | 'workingLocation';

export interface TemplateRules {
  label: string;
  defaultTitle: string | null; // pre-filled when the title is left empty
  titleLabel: string; // the caption above the title field
  allDay: AllDayMode;
  location: LocationMode;
  allowsColor: boolean;
  allowsDescription: boolean;
  allowsAlerts: boolean;
  allowsGuests: boolean;
  busy: BusyMode;
  informational: boolean;
}

const TASK: TemplateRules = {
  label: 'Task',
  defaultTitle: null,
  titleLabel: 'Title',
  allDay: 'optional',
  location: 'none',
  allowsColor: true,
  allowsDescription: true,
  allowsAlerts: true,
  allowsGuests: false,
  busy: 'none',
  informational: false,
};

const EVENT_RULES: Record<EventTemplate, TemplateRules> = {
  [EventTemplate.Default]: {
    label: 'Event',
    defaultTitle: null,
    titleLabel: 'Title',
    allDay: 'optional',
    location: 'text',
    allowsColor: true,
    allowsDescription: true,
    allowsAlerts: true,
    allowsGuests: true,
    busy: 'choice',
    informational: false,
  },
  [EventTemplate.OutOfOffice]: {
    label: 'Out of office',
    defaultTitle: 'Out of office',
    titleLabel: 'Title',
    allDay: 'optional',
    location: 'none',
    allowsColor: true,
    allowsDescription: false,
    allowsAlerts: false,
    allowsGuests: false,
    busy: 'none',
    informational: true,
  },
  [EventTemplate.FocusTime]: {
    label: 'Focus time',
    defaultTitle: 'Focus time',
    titleLabel: 'Title',
    allDay: 'never',
    location: 'text',
    allowsColor: true,
    allowsDescription: true,
    allowsAlerts: true,
    allowsGuests: false,
    busy: 'alwaysBusy',
    informational: true,
  },
  [EventTemplate.WorkingLocation]: {
    label: 'Working location',
    defaultTitle: 'Working location',
    titleLabel: 'Title',
    allDay: 'optional',
    location: 'workingLocation',
    allowsColor: false,
    allowsDescription: false,
    allowsAlerts: false,
    allowsGuests: false,
    busy: 'none',
    informational: true,
  },
  [EventTemplate.Birthday]: {
    label: 'Birthday',
    defaultTitle: null,
    titleLabel: 'Name',
    allDay: 'always',
    location: 'none',
    allowsColor: true,
    allowsDescription: false,
    allowsAlerts: true,
    allowsGuests: false,
    busy: 'none',
    informational: true,
  },
};

export function rulesFor(type: TaskType, template: EventTemplate): TemplateRules {
  return type === TaskType.Task ? TASK : EVENT_RULES[template];
}

export function isInformational(item: { taskType: TaskType; eventTemplate: EventTemplate }): boolean {
  return rulesFor(item.taskType, item.eventTemplate).informational;
}

// Whether an edit of an imported item is sent back to Google by the next sync. Google takes changes to its tasks and to plain events,
// Out of office and Focus time; a birthday or a working location it keeps for itself, so an edit of those stays in the app only.
export function sendsEditsToGoogle(item: { taskType: TaskType; eventTemplate: EventTemplate }): boolean {
  return (
    item.taskType === TaskType.Task ||
    item.eventTemplate === EventTemplate.Default ||
    item.eventTemplate === EventTemplate.OutOfOffice ||
    item.eventTemplate === EventTemplate.FocusTime
  );
}

// All-day once the template has had its say: Birthday always, Focus time never, a Task is never flagged (its all-day
// deadline is a different convention), the rest follow the switch.
export function resolveAllDay(type: TaskType, template: EventTemplate, requested: boolean): boolean {
  if (type !== TaskType.Event) return false;
  const mode = EVENT_RULES[template].allDay;
  return mode === 'always' ? true : mode === 'never' ? false : requested;
}

export function resolveBusy(type: TaskType, template: EventTemplate, requested: boolean): boolean {
  return rulesFor(type, template).busy !== 'choice' || requested;
}

export interface ItemKind {
  id: string;
  label: string;
  testID: string;
  taskType: TaskType;
  template: EventTemplate;
}

export const ITEM_KINDS: readonly ItemKind[] = [
  { id: 'task', label: 'Task', testID: 'schedule-type-task', taskType: TaskType.Task, template: EventTemplate.Default },
  { id: 'event', label: 'Event', testID: 'schedule-type-event', taskType: TaskType.Event, template: EventTemplate.Default },
  { id: 'out-of-office', label: 'Out of office', testID: 'schedule-template-out-of-office', taskType: TaskType.Event, template: EventTemplate.OutOfOffice },
  { id: 'focus-time', label: 'Focus time', testID: 'schedule-template-focus-time', taskType: TaskType.Event, template: EventTemplate.FocusTime },
  { id: 'working-location', label: 'Working location', testID: 'schedule-template-working-location', taskType: TaskType.Event, template: EventTemplate.WorkingLocation },
  { id: 'birthday', label: 'Birthday', testID: 'schedule-template-birthday', taskType: TaskType.Event, template: EventTemplate.Birthday },
];

export const WORKING_LOCATION_PRESETS = ['Home', 'Office'] as const;
