import { EventTemplate, TaskType } from '../../types/task';
import { PALETTE, colorHex, colorLabel, readableTextColor } from '../palette';
import { ITEM_KINDS, resolveAllDay, resolveBusy, rulesFor } from '../templateRules';

// The same keys, in the same order, as backend PlanFlow.Domain.Services.ItemColors.Keys.
const BACKEND_COLOR_KEYS = [
  'beetroot', 'cherry-blossom', 'flamingo', 'tomato', 'tangerine', 'pumpkin',
  'mango', 'banana', 'citron', 'avocado', 'pistachio', 'basil',
  'sage', 'eucalyptus', 'peacock', 'cobalt', 'lavender', 'blueberry',
  'wisteria', 'amethyst', 'grape', 'cocoa', 'graphite', 'birch',
];

describe('rulesFor (mirror of the backend EventTemplateRules table)', () => {
  it('a Task uses the task rules whatever template it carries', () => {
    const rules = rulesFor(TaskType.Task, EventTemplate.OutOfOffice);
    expect(rules.label).toBe('Task');
    expect(rules.informational).toBe(false);
    expect(rules.allowsDescription).toBe(true);
    expect(rules.location).toBe('none');
  });

  it('a plain event has a place, a busy/free choice and is ranked', () => {
    const rules = rulesFor(TaskType.Event, EventTemplate.Default);
    expect(rules).toMatchObject({ location: 'text', busy: 'choice', allDay: 'optional', allowsColor: true, allowsDescription: true, informational: false });
  });

  it.each([
    [EventTemplate.OutOfOffice, { allDay: 'optional', location: 'none', allowsColor: true, allowsDescription: false, busy: 'none', defaultTitle: 'Out of office' }],
    [EventTemplate.FocusTime, { allDay: 'never', location: 'text', allowsColor: true, allowsDescription: true, busy: 'alwaysBusy', defaultTitle: 'Focus time' }],
    [EventTemplate.WorkingLocation, { allDay: 'optional', location: 'workingLocation', allowsColor: false, allowsDescription: false, busy: 'none', defaultTitle: 'Working location' }],
    [EventTemplate.Birthday, { allDay: 'always', location: 'none', allowsColor: true, allowsDescription: false, busy: 'none', defaultTitle: null }],
  ])('template %i follows the plan table and is informational', (template, expected) => {
    const rules = rulesFor(TaskType.Event, template);
    expect(rules).toMatchObject(expected);
    expect(rules.informational).toBe(true);
  });
});

describe('resolveAllDay / resolveBusy', () => {
  it('Birthday is always all-day, Focus time never, a Task is never flagged, the rest follow the request', () => {
    expect(resolveAllDay(TaskType.Event, EventTemplate.Birthday, false)).toBe(true);
    expect(resolveAllDay(TaskType.Event, EventTemplate.FocusTime, true)).toBe(false);
    expect(resolveAllDay(TaskType.Task, EventTemplate.Default, true)).toBe(false);
    expect(resolveAllDay(TaskType.Event, EventTemplate.Default, true)).toBe(true);
    expect(resolveAllDay(TaskType.Event, EventTemplate.OutOfOffice, false)).toBe(false);
  });

  it('busy/free is a choice only for a plain event; everything else is busy', () => {
    expect(resolveBusy(TaskType.Event, EventTemplate.Default, false)).toBe(false);
    expect(resolveBusy(TaskType.Event, EventTemplate.Default, true)).toBe(true);
    expect(resolveBusy(TaskType.Event, EventTemplate.FocusTime, false)).toBe(true);
    expect(resolveBusy(TaskType.Event, EventTemplate.OutOfOffice, false)).toBe(true);
    expect(resolveBusy(TaskType.Task, EventTemplate.Default, false)).toBe(true);
  });
});

describe('ITEM_KINDS', () => {
  it('has one chip per template plus Task, and keeps the ids the e2e flow uses', () => {
    expect(ITEM_KINDS.map((k) => k.label)).toEqual(['Task', 'Event', 'Out of office', 'Focus time', 'Working location', 'Birthday']);
    expect(ITEM_KINDS[0].testID).toBe('schedule-type-task');
    expect(ITEM_KINDS[1].testID).toBe('schedule-type-event');
    expect(new Set(ITEM_KINDS.map((k) => k.testID)).size).toBe(ITEM_KINDS.length);
  });
});

describe('palette', () => {
  it('has the same 24 colour keys, in the same order, as the backend', () => {
    expect(PALETTE.map((c) => c.key)).toEqual(BACKEND_COLOR_KEYS);
  });

  it('gives every colour a well-formed hex and a label', () => {
    for (const color of PALETTE) {
      expect(color.hex).toMatch(/^#[0-9a-f]{6}$/);
      expect(color.label.length).toBeGreaterThan(0);
    }
  });

  it('colorHex / colorLabel: known key, default (null) and a key from a newer server', () => {
    expect(colorHex('tomato')).toBe('#d50004');
    expect(colorLabel('cherry-blossom')).toBe('Cherry blossom');
    expect(colorHex(null)).toBeNull();
    expect(colorLabel(null)).toBe('Default colour');
    expect(colorHex('neon')).toBeNull();
  });

  it('picks dark text on light colours and white text on dark ones', () => {
    for (const light of ['banana', 'citron', 'wisteria', 'birch', 'avocado']) {
      expect(readableTextColor(colorHex(light)!)).toBe('#111827');
    }
    for (const dark of ['tomato', 'graphite', 'blueberry', 'basil', 'grape', 'cocoa']) {
      expect(readableTextColor(colorHex(dark)!)).toBe('#FFFFFF');
    }
  });
});
