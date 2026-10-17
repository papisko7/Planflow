import { render, screen } from '@testing-library/react-native';
import { TaskCard } from '../TaskCard';
import { UrgencyBreakdown } from '../UrgencyBreakdown';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, TaskType, type TaskDto } from '../../types/task';

const base: TaskDto = {
  id: 't1',
  teamId: 'team1',
  assignedUserId: null,
  title: 'Standup',
  description: null,
  status: TaskStatus.Todo,
  deadlineUtc: null,
  impactScore: null,
  blockedByTaskId: null,
  manualUrgencyOverride: null,
  manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.7,
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: null,
  ...PLAIN_TASK_FIELDS,
};

describe('TaskCard - CalendarItem fields', () => {
  it('shows the priority label and explanation from the backend', async () => {
    await render(<TaskCard task={{ ...base, reasonText: '[High] due in 2d, impact 8/10' }} />);

    expect(screen.getByText('[High]')).toBeTruthy();
    expect(screen.getByText(/due in 2d, impact 8\/10/)).toBeTruthy();
  });

  it('falls back to the urgency tier when there is no explanation yet', async () => {
    await render(<TaskCard task={base} />);

    expect(screen.getByText(/High/)).toBeTruthy();
  });

  it('marks an Event and shows its time range, but a Task shows effort and repetition', async () => {
    const event: TaskDto = {
      ...base,
      taskType: TaskType.Event,
      startUtc: new Date(2026, 9, 20, 14, 0).toISOString(),
      endUtc: new Date(2026, 9, 20, 15, 30).toISOString(),
    };
    const { unmount } = await render(<TaskCard task={event} />);
    expect(screen.getByText('Event')).toBeTruthy();
    expect(screen.getByText(/14:00-15:30/)).toBeTruthy();
    await unmount();

    await render(<TaskCard task={{ ...base, estimatedEffortHours: 48, isRecurring: true, recurrenceIntervalHours: 168 }} />);
    expect(screen.queryByText('Event')).toBeNull();
    expect(screen.getByText(/2 d/)).toBeTruthy();
    expect(screen.getByText(/every 1 wk/)).toBeTruthy();
  });
});

describe('UrgencyBreakdown - reason text', () => {
  const breakdown = {
    deadlineComponent: 0.5,
    blockingComponent: 0,
    impactComponent: null,
    userOverrideComponent: 0,
    finalScore: 0.5,
    isBlocked: false,
    createdAtUtc: '2026-10-18T10:00:00Z',
  };

  it('renders the explanation when present and nothing when absent', async () => {
    const { rerender } = await render(<UrgencyBreakdown breakdown={{ ...breakdown, reasonText: '[Medium] due in 3d, impact not rated' }} />);
    expect(screen.getByTestId('urgency-reason')).toBeTruthy();
    expect(screen.getByText('[Medium]')).toBeTruthy();

    await rerender(<UrgencyBreakdown breakdown={breakdown} />);
    expect(screen.queryByTestId('urgency-reason')).toBeNull();
  });
});
