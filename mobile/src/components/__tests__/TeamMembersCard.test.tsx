import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { TeamMembersCard } from '../TeamMembersCard';
import { getCurrentUserId } from '../../services/authService';
import { changeMemberRole, getTeamMembers } from '../../services/teamService';
import { TeamRole, type TeamMemberDto } from '../../types/team';

jest.mock('../../services/authService');
jest.mock('../../services/teamService');

const mockedUserId = getCurrentUserId as jest.MockedFunction<typeof getCurrentUserId>;
const mockedMembers = getTeamMembers as jest.MockedFunction<typeof getTeamMembers>;
const mockedChangeRole = changeMemberRole as jest.MockedFunction<typeof changeMemberRole>;

const member = (userId: string, role: TeamRole): TeamMemberDto => ({
  id: `m-${userId}`,
  userId,
  userDisplayName: userId,
  role,
  joinedAtUtc: '2026-01-01T00:00:00Z',
});

describe('TeamMembersCard role changes', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedChangeRole.mockResolvedValue(member('x', TeamRole.Admin));
  });

  it('lets the owner promote a member and demote an admin', async () => {
    mockedUserId.mockReturnValue('owner');
    mockedMembers.mockResolvedValue([member('owner', TeamRole.Owner), member('ann', TeamRole.Member), member('adam', TeamRole.Admin)]);
    await render(<TeamMembersCard teamId="t1" onChanged={jest.fn()} />);
    await waitFor(() => screen.getByTestId('change-role-ann'));

    expect(screen.getByText('Make admin')).toBeTruthy();
    expect(screen.getByText('Make member')).toBeTruthy();
    expect(screen.queryByTestId('change-role-owner')).toBeNull(); // the owner's own role is fixed

    await fireEvent.press(screen.getByTestId('change-role-ann'));
    await waitFor(() => expect(mockedChangeRole).toHaveBeenCalledWith('t1', 'ann', TeamRole.Admin));

    await fireEvent.press(screen.getByTestId('change-role-adam'));
    await waitFor(() => expect(mockedChangeRole).toHaveBeenCalledWith('t1', 'adam', TeamRole.Member));
  });

  it('hides role buttons from an admin (only the owner hands out admin rights)', async () => {
    mockedUserId.mockReturnValue('adam');
    mockedMembers.mockResolvedValue([member('owner', TeamRole.Owner), member('ann', TeamRole.Member), member('adam', TeamRole.Admin)]);
    await render(<TeamMembersCard teamId="t1" onChanged={jest.fn()} />);
    await waitFor(() => screen.getByTestId('member-row-ann'));

    expect(screen.queryByTestId('change-role-ann')).toBeNull();
  });

  it('shows an error when the server refuses', async () => {
    mockedUserId.mockReturnValue('owner');
    mockedMembers.mockResolvedValue([member('owner', TeamRole.Owner), member('ann', TeamRole.Member)]);
    mockedChangeRole.mockRejectedValue({ status: 403, message: 'no', isNetworkError: false });
    await render(<TeamMembersCard teamId="t1" onChanged={jest.fn()} />);
    await waitFor(() => screen.getByTestId('change-role-ann'));

    await fireEvent.press(screen.getByTestId('change-role-ann'));

    await waitFor(() => expect(screen.getByText("You don't have permission to do that.")).toBeTruthy());
  });
});
