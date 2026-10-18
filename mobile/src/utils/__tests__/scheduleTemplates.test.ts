import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { EventTemplate, TaskType, type ScheduleFields, type TaskDto } from '../../types/task';
import { allDayDays, emptySchedule, normalizeSchedule, scheduleForEditing, scheduleForRequest, switchKind } from '../schedule';
import { ITEM_KINDS } from '../templateRules';

const HOUR = 3_600_000;
const inHours = (h: number) => new Date(Date.now() + h * HOUR).toISOString();

const event = (over: Partial<ScheduleFields> = {}): ScheduleFields => ({ ...emptySchedule(TaskType.Event), ...over });
const task = (over: Partial<ScheduleFields> = {}): ScheduleFields => ({ ...emptySchedule(TaskType.Task), ...over });
const kind = (id: string) => ITEM_KINDS.find((k) => k.id === id)!;

describe('emptySchedule with a template', () => {
  it('a Birthday starts all-day, a Working location starts at Home, a plain event is neither', () => {
    expect(emptySchedule(TaskType.Event, EventTemplate.Birthday).isAllDay).toBe(true);
    expect(emptySchedule(TaskType.Event, EventTemplate.WorkingLocation).location).toBe('Home');
    const plain = emptySchedule(TaskType.Event);
    expect([plain.isAllDay, plain.location, plain.colorKey, plain.isBusy]).toEqual([false, null, null, true]);
  });
});

describe('normalizeSchedule with a template', () => {
  it('drops the place, colour and busy/free that the template does not have', () => {
    const ooo = normalizeSchedule(event({ eventTemplate: EventTemplate.OutOfOffice, location: 'Lisbon', colorKey: 'tomato', isBusy: false }));
    expect([ooo.location, ooo.colorKey, ooo.isBusy]).toEqual([null, 'tomato', true]);

    const where = normalizeSchedule(event({ eventTemplate: EventTemplate.WorkingLocation, location: ' Home ', colorKey: 'tomato' }));
    expect([where.location, where.colorKey]).toEqual(['Home', null]);
  });

  it('keeps a plain event as the user set it, and a Task never carries a template, place or all-day flag', () => {
    const plain = normalizeSchedule(event({ location: 'Room 4', colorKey: 'sage', isBusy: false, isAllDay: true }));
    expect([plain.location, plain.colorKey, plain.isBusy, plain.isAllDay]).toEqual(['Room 4', 'sage', false, true]);

    const asTask = normalizeSchedule(task({ eventTemplate: EventTemplate.FocusTime, location: 'Room 4', isAllDay: true, colorKey: 'grape' }));
    expect([asTask.eventTemplate, asTask.location, asTask.isAllDay, asTask.colorKey]).toEqual([EventTemplate.Default, null, false, 'grape']);
  });

  it('Focus time is never all-day and always busy; Birthday is always all-day', () => {
    const focus = normalizeSchedule(event({ eventTemplate: EventTemplate.FocusTime, isAllDay: true, isBusy: false }));
    expect([focus.isAllDay, focus.isBusy]).toEqual([false, true]);
    expect(normalizeSchedule(event({ eventTemplate: EventTemplate.Birthday, isAllDay: false })).isAllDay).toBe(true);
  });
});

describe('switchKind', () => {
  it('Event -> Out of office clears the place and busy/free the template does not have', () => {
    const next = switchKind(event({ location: 'Room 4', isBusy: false }), kind('out-of-office'));
    expect([next.taskType, next.eventTemplate, next.location, next.isBusy]).toEqual([TaskType.Event, EventTemplate.OutOfOffice, null, true]);
  });

  it('keeps the colour when the new kind has one and drops it for Working location', () => {
    const coloured = event({ colorKey: 'tomato' });
    expect(switchKind(coloured, kind('focus-time')).colorKey).toBe('tomato');
    expect(switchKind(coloured, kind('working-location')).colorKey).toBeNull();
  });

  it('Working location starts at Home, and leaving it does not carry the label into a free-text place', () => {
    const where = switchKind(event(), kind('working-location'));
    expect(where.location).toBe('Home');
    expect(switchKind(where, kind('focus-time')).location).toBeNull();
  });

  it('to a Birthday it snaps the dates to whole days; from it to Focus time it goes back to a timed slot', () => {
    const timed = event({ startUtc: new Date(2026, 9, 20, 14, 0).toISOString(), endUtc: new Date(2026, 9, 20, 15, 0).toISOString() });
    const birthday = switchKind(timed, kind('birthday'));
    expect(birthday.isAllDay).toBe(true);
    expect(allDayDays({ startUtc: birthday.startUtc, endUtc: birthday.endUtc, isAllDay: false })).not.toBeNull();

    const focus = switchKind(birthday, kind('focus-time'));
    expect(focus.isAllDay).toBe(false);
    expect(new Date(focus.startUtc!).getHours()).toBe(9);
  });

  it('Task -> Event does not invent dates', () => {
    const next = switchKind(task({ deadlineUtc: inHours(5) }), kind('event'));
    expect(next.taskType).toBe(TaskType.Event);
    expect(next.startUtc).toBeNull();
  });
});

describe('scheduleForRequest (the stored convention for all-day)', () => {
  it('turns local all-day days into midnight UTC of the first day and the midnight after the last', () => {
    const request = scheduleForRequest(
      event({ isAllDay: true, startUtc: new Date(2026, 9, 20, 0, 0).toISOString(), endUtc: new Date(2026, 9, 21, 23, 59).toISOString() }),
    );
    expect(request.isAllDay).toBe(true);
    expect(request.startUtc).toBe('2026-10-20T00:00:00.000Z');
    expect(request.endUtc).toBe('2026-10-22T00:00:00.000Z');
  });

  it('an all-day event with no end lasts that one day', () => {
    const request = scheduleForRequest(event({ isAllDay: true, startUtc: new Date(2026, 9, 20, 0, 0).toISOString() }));
    expect(request.endUtc).toBe('2026-10-21T00:00:00.000Z');
  });

  it('leaves a timed event and a Task alone', () => {
    const start = inHours(48);
    expect(scheduleForRequest(event({ startUtc: start, endUtc: inHours(49) })).startUtc).toBe(start);
    expect(scheduleForRequest(task({ deadlineUtc: inHours(5) })).deadlineUtc).not.toBeNull();
  });

  it('is the inverse of what the edit form shows: the round trip keeps the same days', () => {
    const stored = {
      ...PLAIN_TASK_FIELDS, taskType: TaskType.Event, isAllDay: true, startUtc: '2026-10-20T00:00:00.000Z', endUtc: '2026-10-22T00:00:00.000Z',
    } as TaskDto;
    const form = scheduleForEditing(stored);
    expect(form.isAllDay).toBe(true);
    const days = allDayDays({ startUtc: form.startUtc, endUtc: form.endUtc, isAllDay: false })!;
    expect([days.first.getDate(), days.last.getDate()]).toEqual([20, 21]);
    const back = scheduleForRequest(form);
    expect([back.startUtc, back.endUtc]).toEqual(['2026-10-20T00:00:00.000Z', '2026-10-22T00:00:00.000Z']);
  });
});

describe('scheduleForEditing with all-day and templates', () => {
  it('opens an older all-day event (shape only, no flag) as all-day and moves it to the flag convention on save', () => {
    const legacy = {
      ...PLAIN_TASK_FIELDS, taskType: TaskType.Event, isAllDay: false,
      startUtc: new Date(2026, 9, 20, 0, 0).toISOString(), endUtc: new Date(2026, 9, 20, 23, 59).toISOString(),
    } as TaskDto;
    const form = scheduleForEditing(legacy);
    expect(form.isAllDay).toBe(true);
    expect(scheduleForRequest(form).startUtc).toBe('2026-10-20T00:00:00.000Z');
  });

  it('carries the template, place, colour and busy/free of the stored item', () => {
    const stored = {
      ...PLAIN_TASK_FIELDS, taskType: TaskType.Event, eventTemplate: EventTemplate.FocusTime, location: 'Library', colorKey: 'sage', isBusy: true,
      startUtc: '2026-10-19T09:00:00.000Z', endUtc: '2026-10-19T11:00:00.000Z',
    } as TaskDto;
    const form = scheduleForEditing(stored);
    expect([form.eventTemplate, form.location, form.colorKey]).toEqual([EventTemplate.FocusTime, 'Library', 'sage']);
  });
});

describe('switchKind - a task\'s planned start', () => {
  it('Event -> Task starts the dates from scratch: the meeting times are not a planned start', () => {
    const meeting = event({ startUtc: inHours(5), endUtc: inHours(6) });
    const asTask = switchKind(meeting, kind('task'));

    expect([asTask.taskType, asTask.startUtc, asTask.endUtc]).toEqual([TaskType.Task, null, null]);
  });

  it('Task -> Event keeps a planned start as the event start, and the request carries the start of a Task', () => {
    const planned = task({ startUtc: inHours(5), deadlineUtc: inHours(9) });

    expect(switchKind(planned, kind('event')).startUtc).toBe(planned.startUtc);
    expect(scheduleForRequest(planned).startUtc).toBe(planned.startUtc);
    expect(scheduleForRequest(planned).endUtc).toBeNull();
  });
});
