import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { PinSettingsCard } from '../PinSettingsCard';
import { clearPin, isPinEnabled, setPin, verifyPin } from '../../services/pinService';

jest.mock('../../services/pinService');

// The real modal is a keyboard-driven PIN pad; a stand-in with two fixed "PINs" is enough to walk the flows.
jest.mock('../PinEntryModal', () => {
  const { createElement: h } = jest.requireActual('react');
  const { Pressable, Text, View } = jest.requireActual('react-native');
  return {
    PinEntryModal: ({ visible, title, error, onComplete }: { visible: boolean; title: string; error?: string | null; onComplete: (pin: string) => void }) =>
      visible
        ? h(
            View,
            null,
            h(Text, { testID: 'pin-title' }, title),
            error ? h(Text, { testID: 'pin-error' }, error) : null,
            h(Pressable, { testID: 'enter-1111', onPress: () => onComplete('1111') }),
            h(Pressable, { testID: 'enter-2222', onPress: () => onComplete('2222') }),
          )
        : null,
  };
});

const mockedEnabled = isPinEnabled as jest.MockedFunction<typeof isPinEnabled>;
const mockedSet = setPin as jest.MockedFunction<typeof setPin>;
const mockedVerify = verifyPin as jest.MockedFunction<typeof verifyPin>;
const mockedClear = clearPin as jest.MockedFunction<typeof clearPin>;

describe('PinSettingsCard', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedSet.mockResolvedValue(undefined);
    mockedClear.mockResolvedValue(undefined);
    mockedVerify.mockImplementation(async (pin) => pin === '1111'); // the current PIN is 1111
  });

  it('has no "Change PIN" while PIN login is off, and reports that upward', async () => {
    mockedEnabled.mockResolvedValue(false);
    const onEnabledChange = jest.fn();
    await render(<PinSettingsCard onEnabledChange={onEnabledChange} />);

    await waitFor(() => expect(screen.getByTestId('pin-toggle')).toBeTruthy());
    expect(screen.queryByTestId('pin-change-button')).toBeNull();
    expect(onEnabledChange).toHaveBeenLastCalledWith(false);
  });

  it('offers "Change PIN" while PIN login is on', async () => {
    mockedEnabled.mockResolvedValue(true);
    const onEnabledChange = jest.fn();
    await render(<PinSettingsCard onEnabledChange={onEnabledChange} />);

    await waitFor(() => expect(screen.getByTestId('pin-change-button')).toBeTruthy());
    expect(onEnabledChange).toHaveBeenLastCalledWith(true);
  });

  it('changes the PIN: current PIN first, then the new one twice', async () => {
    mockedEnabled.mockResolvedValue(true);
    await render(<PinSettingsCard />);
    await fireEvent.press(await screen.findByTestId('pin-change-button'));

    expect(screen.getByTestId('pin-title').props.children).toBe('Enter your current PIN');
    await fireEvent.press(screen.getByTestId('enter-2222')); // wrong current PIN
    await waitFor(() => expect(screen.getByTestId('pin-error').props.children).toBe('Incorrect PIN.'));
    expect(mockedSet).not.toHaveBeenCalled();

    await fireEvent.press(screen.getByTestId('enter-1111'));
    await waitFor(() => expect(screen.getByTestId('pin-title').props.children).toBe('Choose a new 4-digit PIN'));

    await fireEvent.press(screen.getByTestId('enter-2222'));
    await waitFor(() => expect(screen.getByTestId('pin-title').props.children).toBe('Confirm your new PIN'));

    await fireEvent.press(screen.getByTestId('enter-1111')); // does not match
    await waitFor(() => expect(screen.getByTestId('pin-error').props.children).toBe("PINs don't match. Try again."));
    expect(mockedSet).not.toHaveBeenCalled();

    await fireEvent.press(screen.getByTestId('enter-2222'));
    await waitFor(() => expect(mockedSet).toHaveBeenCalledWith('2222'));
    await waitFor(() => expect(screen.getByText('PIN changed.')).toBeTruthy());
    expect(screen.queryByTestId('pin-title')).toBeNull();
  });

  it('turning PIN login off needs the current PIN', async () => {
    mockedEnabled.mockResolvedValue(true);
    const onEnabledChange = jest.fn();
    await render(<PinSettingsCard onEnabledChange={onEnabledChange} />);
    await waitFor(() => screen.getByTestId('pin-toggle'));

    await fireEvent(screen.getByTestId('pin-toggle'), 'valueChange', false);
    await fireEvent.press(screen.getByTestId('enter-1111'));

    await waitFor(() => expect(mockedClear).toHaveBeenCalled());
    await waitFor(() => expect(onEnabledChange).toHaveBeenLastCalledWith(false));
    expect(screen.queryByTestId('pin-change-button')).toBeNull();
  });
});
