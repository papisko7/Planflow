import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react-native';
import { ParticipantPicker } from '../ParticipantPicker';
import { TeamRole, type TeamMemberDto } from '../../types/team';

jest.mock('../../services/authService', () => ({ getCurrentUserId: () => 'me' }));

const member = (userId: string, name: string): TeamMemberDto => ({ id: `m-${userId}`, userId, userDisplayName: name, role: TeamRole.Member, joinedAtUtc: '2026-01-01T00:00:00Z' });
const members = [member('me', 'Mia'), member('ann', 'Ann'), member('ben', 'Ben')];

function Host({ assigneeId, onList }: { assigneeId: string | null; onList?: (ids: string[]) => void }) {
  const [value, setValue] = useState<string[]>([]);
  return (
    <ParticipantPicker
      members={members}
      assigneeId={assigneeId}
      value={value}
      onChange={(next) => {
        setValue(next);
        onList?.(next);
      }}
      label="Co-contractors"
    />
  );
}

describe('ParticipantPicker', () => {
  it('offers every member except the assignee, and marks the current user', async () => {
    await render(<Host assigneeId="ann" />);

    expect(screen.getByText('Co-contractors (optional):')).toBeTruthy();
    expect(screen.queryByTestId('participant-chip-ann')).toBeNull();
    expect(screen.getByText('Mia (you)')).toBeTruthy();
    expect(screen.getByTestId('participant-chip-ben')).toBeTruthy();
  });

  it('toggles a person on and off', async () => {
    const onList = jest.fn();
    await render(<Host assigneeId={null} onList={onList} />);

    await fireEvent.press(screen.getByTestId('participant-chip-ann'));
    await fireEvent.press(screen.getByTestId('participant-chip-ben'));
    expect(onList).toHaveBeenLastCalledWith(['ann', 'ben']);

    await fireEvent.press(screen.getByTestId('participant-chip-ann'));
    expect(onList).toHaveBeenLastCalledWith(['ben']);
  });

  it('shows nothing when nobody else could be invited', async () => {
    await render(
      <ParticipantPicker members={[member('me', 'Mia')]} assigneeId="me" value={[]} onChange={jest.fn()} label="Guests" />,
    );

    expect(screen.queryByTestId('participant-picker')).toBeNull();
  });
});
