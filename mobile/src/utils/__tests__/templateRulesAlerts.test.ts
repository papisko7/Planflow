import { EventTemplate, TaskType } from '../../types/task';
import { rulesFor } from '../templateRules';

// The form hides the alerts and guests a kind of item does not have. This is the mirror of the backend's
// EventTemplateRules (AllowsAlerts / AllowsGuests); if a row changes there, change it here as well.
describe('templateRules - alerts and guests', () => {
  it.each([
    ['Task', TaskType.Task, EventTemplate.Default, true, false], // a task has co-contractors instead of guests
    ['Event', TaskType.Event, EventTemplate.Default, true, true],
    ['Out of office', TaskType.Event, EventTemplate.OutOfOffice, false, false],
    ['Focus time', TaskType.Event, EventTemplate.FocusTime, true, false],
    ['Working location', TaskType.Event, EventTemplate.WorkingLocation, false, false],
    ['Birthday', TaskType.Event, EventTemplate.Birthday, true, false],
  ] as const)('%s: alerts %s, guests %s', (_label, type, template, alerts, guests) => {
    const rules = rulesFor(type, template);

    expect(rules.allowsAlerts).toBe(alerts);
    expect(rules.allowsGuests).toBe(guests);
  });
});
