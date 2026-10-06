import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { ChangePasswordCard } from '../ChangePasswordCard';
import { EmailVerificationCard } from '../EmailVerificationCard';
import { changePassword, resendEmailVerification, verifyEmail } from '../../services/userService';

jest.mock('../../services/userService');

const mockedChange = changePassword as jest.MockedFunction<typeof changePassword>;
const mockedVerify = verifyEmail as jest.MockedFunction<typeof verifyEmail>;
const mockedResend = resendEmailVerification as jest.MockedFunction<typeof resendEmailVerification>;

describe('EmailVerificationCard', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedVerify.mockResolvedValue(undefined);
    mockedResend.mockResolvedValue(undefined);
  });

  it('confirms the address with the typed code and tells the parent', async () => {
    const onVerified = jest.fn();
    await render(<EmailVerificationCard email="ann@example.com" onVerified={onVerified} />);

    expect(screen.getByText(/ann@example.com/)).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('verify-code-input'), '123456');
    await fireEvent.press(screen.getByTestId('verify-submit'));

    await waitFor(() => expect(mockedVerify).toHaveBeenCalledWith('123456'));
    expect(onVerified).toHaveBeenCalled();
  });

  it('rejects a code that is not 6 digits without calling the server', async () => {
    await render(<EmailVerificationCard email="ann@example.com" onVerified={jest.fn()} />);

    await fireEvent.changeText(screen.getByTestId('verify-code-input'), '12');
    await fireEvent.press(screen.getByTestId('verify-submit'));

    expect(screen.getByText('The code has 6 digits.')).toBeTruthy();
    expect(mockedVerify).not.toHaveBeenCalled();
  });

  it('shows a clear message for a wrong or expired code', async () => {
    mockedVerify.mockRejectedValue({ status: 400, message: 'x', isNetworkError: false });
    const onVerified = jest.fn();
    await render(<EmailVerificationCard email="ann@example.com" onVerified={onVerified} />);

    await fireEvent.changeText(screen.getByTestId('verify-code-input'), '000000');
    await fireEvent.press(screen.getByTestId('verify-submit'));

    await waitFor(() => expect(screen.getByText('The code is invalid or has expired. Send a new one.')).toBeTruthy());
    expect(onVerified).not.toHaveBeenCalled();
  });

  it('can ask for a new code', async () => {
    await render(<EmailVerificationCard email="ann@example.com" onVerified={jest.fn()} />);

    await fireEvent.press(screen.getByTestId('verify-resend'));

    await waitFor(() => expect(mockedResend).toHaveBeenCalled());
    expect(screen.getByText(/new one is on its way/)).toBeTruthy();
  });
});

describe('ChangePasswordCard', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedChange.mockResolvedValue(undefined);
  });

  const fill = async (current: string, next: string, repeat: string) => {
    await fireEvent.changeText(screen.getByTestId('password-current-input'), current);
    await fireEvent.changeText(screen.getByTestId('password-new-input'), next);
    await fireEvent.changeText(screen.getByTestId('password-repeat-input'), repeat);
    await fireEvent.press(screen.getByTestId('password-submit'));
  };

  it('changes the password and clears the form', async () => {
    await render(<ChangePasswordCard />);

    await fill('old-password-1', 'new-password-2', 'new-password-2');

    await waitFor(() => expect(mockedChange).toHaveBeenCalledWith('old-password-1', 'new-password-2'));
    expect(screen.getByText('Password changed. Your other devices were signed out.')).toBeTruthy();
    expect(screen.getByTestId('password-new-input').props.value).toBe('');
  });

  it('validates on the device before calling the server', async () => {
    await render(<ChangePasswordCard />);

    await fill('old-password-1', 'short', 'short');
    expect(screen.getByText('The new password needs at least 8 characters.')).toBeTruthy();

    await fill('old-password-1', 'new-password-2', 'different-pass');
    expect(screen.getByText('The two new passwords do not match.')).toBeTruthy();

    await fill('old-password-1', 'old-password-1', 'old-password-1');
    expect(screen.getByText('The new password must differ from the current one.')).toBeTruthy();

    expect(mockedChange).not.toHaveBeenCalled();
  });

  it('says so when the current password is wrong', async () => {
    mockedChange.mockRejectedValue({ status: 400, message: 'x', isNetworkError: false });
    await render(<ChangePasswordCard />);

    await fill('wrong-password', 'new-password-2', 'new-password-2');

    await waitFor(() => expect(screen.getByText('The current password is incorrect.')).toBeTruthy());
  });
});
