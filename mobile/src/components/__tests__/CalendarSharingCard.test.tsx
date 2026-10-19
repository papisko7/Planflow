import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { CalendarSharingCard } from '../CalendarSharingCard';
import { getProfile, updateCalendarSharing } from '../../services/userService';
import { CalendarSharingLevel } from '../../types/availability';

jest.mock('../../services/userService');

const mockedProfile = getProfile as jest.MockedFunction<typeof getProfile>;
const mockedUpdate = updateCalendarSharing as jest.MockedFunction<typeof updateCalendarSharing>;

const open = async () => {
  await waitFor(() => expect(screen.getByText('Availability only')).toBeTruthy());
  await fireEvent.press(screen.getByTestId('calendar-sharing-header'));
};

describe('CalendarSharingCard', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedProfile.mockResolvedValue({
      id: 'u', email: 'a@example.com', displayName: 'Ann', emailVerified: true, calendarSharingLevel: CalendarSharingLevel.Availability,
    });
    mockedUpdate.mockResolvedValue();
  });

  it('shows the current level in the collapsed summary', async () => {
    await render(<CalendarSharingCard />);

    await waitFor(() => expect(screen.getByText('Availability only')).toBeTruthy());
  });

  it('saves the level the person picks', async () => {
    await render(<CalendarSharingCard />);
    await open();
    await waitFor(() => expect(screen.getByTestId('sharing-option-2')).toBeTruthy());

    await fireEvent.press(screen.getByTestId('sharing-option-2'));

    expect(mockedUpdate).toHaveBeenCalledWith(CalendarSharingLevel.Details);
    await waitFor(() => expect(screen.getAllByText('Details').length).toBeGreaterThan(0));
  });

  it('puts the previous level back and says so when saving fails', async () => {
    mockedUpdate.mockRejectedValue({ message: 'Server unavailable' });
    await render(<CalendarSharingCard />);
    await open();
    await waitFor(() => expect(screen.getByTestId('sharing-option-0')).toBeTruthy());

    await fireEvent.press(screen.getByTestId('sharing-option-0'));

    await waitFor(() => expect(screen.getByText('Server unavailable')).toBeTruthy());
    expect(screen.getByText('Availability only')).toBeTruthy();
  });

  it('does not call the server when the chosen level is already set', async () => {
    await render(<CalendarSharingCard />);
    await open();
    await waitFor(() => expect(screen.getByTestId('sharing-option-1')).toBeTruthy());

    await fireEvent.press(screen.getByTestId('sharing-option-1'));

    expect(mockedUpdate).not.toHaveBeenCalled();
  });
});
