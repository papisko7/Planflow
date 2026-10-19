import { render, screen } from '@testing-library/react-native';
import { TaskCard } from '../TaskCard';
import { PLAIN_TASK_FIELDS } from '../../test-fixtures';
import { TaskStatus, type TaskDto } from '../../types/task';

jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const task: TaskDto = {
  id: 't1',
  teamId: 'team1',
  assignedUserId: 'ann',
  title: 'Write report',
  description: null,
  status: TaskStatus.Backlog,
  deadlineUtc: null,
  impactScore: 1,
  blockedByTaskId: null,
  manualUrgencyOverride: null,
  manualUrgencyOverrideExpiresAtUtc: null,
  currentUrgencyScore: 0.5,
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: null,
  ...PLAIN_TASK_FIELDS,
};

// A card shows how many people take part besides the assignee, or "You" when the viewer is one of them.
describe('TaskCard - people', () => {
  it('shows nothing when nobody takes part', async () => {
    await render(<TaskCard task={task} />);

    expect(screen.queryByTestId('item-people-t1')).toBeNull();
  });

  it('shows the head count', async () => {
    await render(<TaskCard task={{ ...task, participantUserIds: ['ben', 'cora'] }} />);

    expect(screen.getByTestId('item-people-t1').props.children.join('')).toContain('2');
  });

  it('says "You" when the viewer is the only one, and "You +N" with others', async () => {
    const { unmount } = await render(<TaskCard task={{ ...task, participantUserIds: ['me'] }} />);
    expect(screen.getByTestId('item-people-t1').props.children.join('')).toContain('You');
    expect(screen.getByTestId('item-people-t1').props.children.join('')).not.toContain('+');
    await unmount();

    await render(<TaskCard task={{ ...task, participantUserIds: ['me', 'ben', 'cora'] }} />);
    expect(screen.getByTestId('item-people-t1').props.children.join('')).toContain('You +2');
  });
});
