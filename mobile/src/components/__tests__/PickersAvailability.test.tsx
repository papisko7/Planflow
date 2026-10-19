import { render, screen } from '@testing-library/react-native';
import { AssigneePicker } from '../AssigneePicker';
import { ParticipantPicker } from '../ParticipantPicker';
import { CalendarSharingLevel, LoadLevel, MemberStatus, type MemberAvailabilityDto } from '../../types/availability';
import { TeamRole, type TeamMemberDto } from '../../types/team';

jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const member = (userId: string, name: string): TeamMemberDto => ({ id: `m-${userId}`, userId, userDisplayName: name, role: TeamRole.Member, joinedAtUtc: '2026-01-01T00:00:00Z' });
const members = [member('me', 'Mia'), member('ann', 'Ann'), member('ben', 'Ben')];

const info = (userId: string, over: Partial<MemberAvailabilityDto>): MemberAvailabilityDto => ({
  userId, displayName: userId, role: TeamRole.Member, sharingLevel: CalendarSharingLevel.Availability, currentStatus: MemberStatus.Free,
  currentWorkingLocation: null, windowLoadPercent: 30, loadLevel: LoadLevel.Light, openTasks: 0, tasksWithoutEffort: 0, days: [], ...over,
});

const availability = {
  ann: info('ann', { displayName: 'Ann', currentStatus: MemberStatus.OutOfOffice, windowLoadPercent: 10 }),
  ben: info('ben', { displayName: 'Ben', currentStatus: MemberStatus.Busy, windowLoadPercent: 140, loadLevel: LoadLevel.Overloaded }),
};

describe('AssigneePicker availability hints', () => {
  it('adds how free each person is to their chip', async () => {
    await render(<AssigneePicker members={members} value={null} onChange={jest.fn()} availability={availability} />);

    expect(screen.getByText('Ann · away · 10%')).toBeTruthy();
    expect(screen.getByText('Ben · busy · 140%')).toBeTruthy();
    expect(screen.getByText('Mia (you)')).toBeTruthy(); // nothing known about her: the plain name
  });

  it('warns about the chosen person only', async () => {
    await render(<AssigneePicker members={members} value="ann" onChange={jest.fn()} availability={availability} />);

    expect(screen.getByTestId('assignee-warning')).toHaveTextContent('Ann is out of office right now.');
  });

  it('warns about an overloaded person and stays quiet for someone without a problem', async () => {
    const { rerender } = await render(<AssigneePicker members={members} value="ben" onChange={jest.fn()} availability={availability} />);
    expect(screen.getByTestId('assignee-warning')).toHaveTextContent(/overloaded/);

    await rerender(<AssigneePicker members={members} value="me" onChange={jest.fn()} availability={availability} />);
    expect(screen.queryByTestId('assignee-warning')).toBeNull();
  });

  it('works exactly as before without availability data', async () => {
    await render(<AssigneePicker members={members} value="ann" onChange={jest.fn()} />);

    expect(screen.getByText('Ann')).toBeTruthy();
    expect(screen.queryByTestId('assignee-warning')).toBeNull();
  });
});

describe('ParticipantPicker availability hints', () => {
  it('adds hints to the chips and warns about each chosen person who is away or overloaded', async () => {
    await render(
      <ParticipantPicker members={members} assigneeId={null} value={['ann', 'ben']} onChange={jest.fn()} label="Co-contractors" availability={availability} />,
    );

    expect(screen.getByText('Ann · away · 10%')).toBeTruthy();
    expect(screen.getByTestId('participant-warning-ann')).toBeTruthy();
    expect(screen.getByTestId('participant-warning-ben')).toBeTruthy();
  });

  it('does not warn about people who were not picked', async () => {
    await render(
      <ParticipantPicker members={members} assigneeId={null} value={[]} onChange={jest.fn()} label="Guests" availability={availability} />,
    );

    expect(screen.queryByTestId('participant-warning-ann')).toBeNull();
  });
});
