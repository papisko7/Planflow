import { EventTemplate, TaskType } from '../../types/task';
import { sendsEditsToGoogle } from '../templateRules';

// Mirror of backend GoogleMirror.IsPushable: which imported items get an edit sent back to Google by the next sync.
describe('sendsEditsToGoogle', () => {
  it('a task, a plain event, Out of office and Focus time are sent back', () => {
    expect(sendsEditsToGoogle({ taskType: TaskType.Task, eventTemplate: EventTemplate.Default })).toBe(true);
    expect(sendsEditsToGoogle({ taskType: TaskType.Event, eventTemplate: EventTemplate.Default })).toBe(true);
    expect(sendsEditsToGoogle({ taskType: TaskType.Event, eventTemplate: EventTemplate.OutOfOffice })).toBe(true);
    expect(sendsEditsToGoogle({ taskType: TaskType.Event, eventTemplate: EventTemplate.FocusTime })).toBe(true);
  });

  it('a birthday and a working location are kept by Google, so an edit stays in the app', () => {
    expect(sendsEditsToGoogle({ taskType: TaskType.Event, eventTemplate: EventTemplate.Birthday })).toBe(false);
    expect(sendsEditsToGoogle({ taskType: TaskType.Event, eventTemplate: EventTemplate.WorkingLocation })).toBe(false);
  });
});
