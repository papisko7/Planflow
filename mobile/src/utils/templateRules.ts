import { EventTemplate, TaskType } from '../types/task';

// Mirrors the backend table PlanFlow.Domain.Services.EventTemplateRules (plan point 4): what each kind of item has.
// The backend enforces it (and drops what a template does not use); this copy lets the form hide those fields up front.
// If a row changes there, change it here as well - utils/__tests__/templateRules.test.ts pins the values.

export type AllDayMode = 'optional' | 'never' | 'always';
export type BusyMode = 'choice' | 'alwaysBusy' | 'none';
export type LocationMode = 'none' | 'text' | 'workingLocation';

export interface TemplateRules {
  label: string;
  defaultTitle: string | null; // pre-filled when the title is left empty
  titlePlaceholder: string;
  allDay: AllDayMode;
  location: LocationMode;
  allowsColor: boolean;
  allowsDescription: boolean;
  busy: BusyMode;
  // Informational items (Out of office, Focus time, ...) have no Impact / Effort / score: they are shown in the calendar only.
  informational: boolean;
}

const TASK: TemplateRules = {
  label: 'Task',
  defaultTitle: null,
  titlePlaceholder: 'Task title',
  allDay: 'optional',
  location: 'none',
  allowsColor: true,
  allowsDescription: true,
  busy: 'none',
  informational: false,
};

const EVENT_RULES: Record<EventTemplate, TemplateRules> = {
  [EventTemplate.Default]: {
    label: 'Event',
    defaultTitle: null,
    titlePlaceholder: 'Event title',
    allDay: 'optional',
    location: 'text',
    allowsColor: true,
    allowsDescription: true,
    busy: 'choice',
    informational: false,
  },
  [EventTemplate.OutOfOffice]: {
    label: 'Out of office',
    defaultTitle: 'Out of office',
    titlePlaceholder: 'Out of office',
    allDay: 'optional',
    location: 'none',
    allowsColor: true,
    allowsDescription: false,
    busy: 'none',
    informational: true,
  },
  [EventTemplate.FocusTime]: {
    label: 'Focus time',
    defaultTitle: 'Focus time',
    titlePlaceholder: 'Focus time',
    allDay: 'never',
    location: 'text',
    allowsColor: true,
    allowsDescription: true,
    busy: 'alwaysBusy',
    informational: true,
  },
  [EventTemplate.WorkingLocation]: {
    label: 'Working location',
    defaultTitle: 'Working location',
    titlePlaceholder: 'Working location',
    allDay: 'optional',
    location: 'workingLocation',
    allowsColor: false,
    allowsDescription: false,
    busy: 'none',
    informational: true,
  },
  [EventTemplate.Birthday]: {
    label: 'Birthday',
    defaultTitle: null,
    titlePlaceholder: 'Name',
    allDay: 'always',
    location: 'none',
    allowsColor: true,
    allowsDescription: false,
    busy: 'none',
    informational: true,
  },
};

// A Task always uses the task rules; only an Event can use a special template.
export function rulesFor(type: TaskType, template: EventTemplate): TemplateRules {
  return type === TaskType.Task ? TASK : EVENT_RULES[template];
}

// Informational items (Out of office, Focus time, Working location, Birthday) are outside the ranking: the backend gives them no
// score, so the app shows no score for them and leaves them out of the "Do now" card, the urgency groups and the statistics.
export function isInformational(item: { taskType: TaskType; eventTemplate: EventTemplate }): boolean {
  return rulesFor(item.taskType, item.eventTemplate).informational;
}

// All-day once the template has had its say: Birthday always, Focus time never, a Task is never flagged (its all-day
// deadline is a different convention), the rest follow the switch.
export function resolveAllDay(type: TaskType, template: EventTemplate, requested: boolean): boolean {
  if (type !== TaskType.Event) return false;
  const mode = EVENT_RULES[template].allDay;
  return mode === 'always' ? true : mode === 'never' ? false : requested;
}

// Busy/Free is a real choice only for a plain event; everything else is stored as busy.
export function resolveBusy(type: TaskType, template: EventTemplate, requested: boolean): boolean {
  return rulesFor(type, template).busy !== 'choice' || requested;
}

// The chips of the form: a chip is a type plus a template (as in Google Calendar). The first two keep the ids the e2e flow uses.
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

// The three ways to say where you work from: two quick choices and a free-text label.
export const WORKING_LOCATION_PRESETS = ['Home', 'Office'] as const;
