import { generateMicroBreakdown } from '../microBreakdownEngine';
import { TaskStatus, type TaskDto } from '../../types/task';

function buildTask(overrides: Partial<TaskDto> = {}): TaskDto {
  return {
    id: 'task-1',
    teamId: 'team-1',
    assignedUserId: null,
    title: 'Untitled task',
    description: null,
    status: TaskStatus.Todo,
    deadlineUtc: null,
    impactScore: 1,
    blockedByTaskId: null,
    manualUrgencyOverride: null,
    manualUrgencyOverrideExpiresAtUtc: null,
    currentUrgencyScore: 0.5,
    createdAtUtc: '2026-01-01T00:00:00.000Z',
    updatedAtUtc: null,
    ...overrides,
  };
}

describe('generateMicroBreakdown', () => {
  it('matches the research template by title keyword', () => {
    const steps = generateMicroBreakdown(buildTask({ title: 'Research competitor pricing' }));

    expect(steps).toHaveLength(3);
    expect(steps[0].text).toBe('Open the primary document or URL');
  });

  it('matches the research template by description keyword when the title has no match', () => {
    const steps = generateMicroBreakdown(
      buildTask({ title: 'Chapter 3', description: 'Study the related literature' }),
    );

    expect(steps[0].text).toBe('Open the primary document or URL');
  });

  it('matches the coding template by title keyword', () => {
    const steps = generateMicroBreakdown(buildTask({ title: 'Fix the login bug' }));

    expect(steps).toHaveLength(3);
    expect(steps[0].text).toBe('Locate the target source file');
  });

  it('falls back to the high-impact template for unmatched high-impact tasks', () => {
    const steps = generateMicroBreakdown(buildTask({ title: 'Plan the wedding', impactScore: 9 }));

    expect(steps[0].text).toBe('Clear your desktop / workspace');
  });

  it('falls back to the universal 3-step template for unmatched low-impact tasks', () => {
    const steps = generateMicroBreakdown(buildTask({ title: 'Tidy up the garage', impactScore: 3 }));

    expect(steps).toEqual([
      { id: 'task-1-step-1', text: 'Set up your environment' },
      { id: 'task-1-step-2', text: 'Execute the core 5-minute action' },
      { id: 'task-1-step-3', text: 'Review your progress' },
    ]);
  });

  it('generates ids scoped to the task id', () => {
    const steps = generateMicroBreakdown(buildTask({ id: 'task-42', title: 'Write the tests' }));

    expect(steps.every((step) => step.id.startsWith('task-42-step-'))).toBe(true);
  });

  it('prefers a matched template over the high-impact fallback even for high-impact tasks', () => {
    const steps = generateMicroBreakdown(buildTask({ title: 'Implement the API', impactScore: 10 }));

    expect(steps[0].text).toBe('Locate the target source file');
  });
});
