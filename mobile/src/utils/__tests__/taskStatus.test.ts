import { TaskStatus, TaskType } from '../../types/task';
import { defaultStatusFor, isFinishedStatus, normalizeStatus, STATUS_LABEL, statusesFor } from '../taskStatus';

describe('task status rules (mirror of the backend)', () => {
  it('offers Backlog / In progress / Completed / Cancelled for a task', () => {
    expect(statusesFor(TaskType.Task)).toEqual([
      TaskStatus.Backlog,
      TaskStatus.InProgress,
      TaskStatus.Completed,
      TaskStatus.Cancelled,
    ]);
  });

  it('offers only Confirmed / Tentative / Cancelled for an event - an event is never "done"', () => {
    expect(statusesFor(TaskType.Event)).toEqual([TaskStatus.Confirmed, TaskStatus.Tentative, TaskStatus.Cancelled]);
  });

  it('starts a task in the backlog and an event confirmed', () => {
    expect(defaultStatusFor(TaskType.Task)).toBe(TaskStatus.Backlog);
    expect(defaultStatusFor(TaskType.Event)).toBe(TaskStatus.Confirmed);
  });

  it('keeps a legal status and replaces one that belongs to the other type', () => {
    expect(normalizeStatus(TaskType.Task, TaskStatus.InProgress)).toBe(TaskStatus.InProgress);
    expect(normalizeStatus(TaskType.Event, TaskStatus.Tentative)).toBe(TaskStatus.Tentative);
    expect(normalizeStatus(TaskType.Event, TaskStatus.Completed)).toBe(TaskStatus.Confirmed);
    expect(normalizeStatus(TaskType.Task, TaskStatus.Confirmed)).toBe(TaskStatus.Backlog);
    expect(normalizeStatus(TaskType.Event, TaskStatus.Cancelled)).toBe(TaskStatus.Cancelled);
  });

  it('counts only Completed and Cancelled as finished', () => {
    expect(isFinishedStatus(TaskStatus.Completed)).toBe(true);
    expect(isFinishedStatus(TaskStatus.Cancelled)).toBe(true);
    expect(isFinishedStatus(TaskStatus.Backlog)).toBe(false);
    expect(isFinishedStatus(TaskStatus.InProgress)).toBe(false);
    expect(isFinishedStatus(TaskStatus.Confirmed)).toBe(false);
    expect(isFinishedStatus(TaskStatus.Tentative)).toBe(false);
  });

  it('has a label for every status the server can send', () => {
    for (const status of [0, 1, 3, 4, 5, 6]) {
      expect(STATUS_LABEL[status as TaskStatus]).toBeTruthy();
    }
  });

  it('keeps the numbers the backend stores (2 = the retired Blocked is gone)', () => {
    expect(TaskStatus.Backlog).toBe(0);
    expect(TaskStatus.InProgress).toBe(1);
    expect(TaskStatus.Completed).toBe(3);
    expect(TaskStatus.Cancelled).toBe(4);
    expect(TaskStatus.Confirmed).toBe(5);
    expect(TaskStatus.Tentative).toBe(6);
    expect((TaskStatus as unknown as Record<number, string>)[2]).toBeUndefined();
  });
});
