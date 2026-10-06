import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import ForgotPasswordScreen from '../ForgotPasswordScreen';
import { requestPasswordReset, resetPassword } from '../../services/authService';

jest.mock('../../services/authService');
const mockNavigate = jest.fn();
jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ navigate: mockNavigate }),
}));

const mockedRequest = requestPasswordReset as jest.MockedFunction<typeof requestPasswordReset>;
const mockedReset = resetPassword as jest.MockedFunction<typeof resetPassword>;

async function goToCodeStep() {
  await fireEvent.changeText(screen.getByTestId('forgot-email-input'), 'ann@example.com');
  await fireEvent.press(screen.getByTestId('forgot-send-code'));
  await waitFor(() => screen.getByTestId('forgot-code-input'));
}

describe('ForgotPasswordScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedRequest.mockResolvedValue(undefined);
    mockedReset.mockResolvedValue(undefined);
  });

  it('asks for the e-mail first and does not call the server without one', async () => {
    await render(<ForgotPasswordScreen />);

    await fireEvent.press(screen.getByTestId('forgot-send-code'));

    expect(screen.getByText('Enter your e-mail address.')).toBeTruthy();
    expect(mockedRequest).not.toHaveBeenCalled();
  });

  it('requests a code, then moves to the code step with a non-revealing message', async () => {
    await render(<ForgotPasswordScreen />);

    await goToCodeStep();

    expect(mockedRequest).toHaveBeenCalledWith('ann@example.com');
    expect(screen.getByText(/If an account exists/)).toBeTruthy();
  });

  it('checks the code format and password length before calling the server', async () => {
    await render(<ForgotPasswordScreen />);
    await goToCodeStep();

    await fireEvent.changeText(screen.getByTestId('forgot-code-input'), '12ab');
    await fireEvent.press(screen.getByTestId('forgot-submit'));
    expect(screen.getByText('The code has 6 digits.')).toBeTruthy();

    await fireEvent.changeText(screen.getByTestId('forgot-code-input'), '123456');
    await fireEvent.changeText(screen.getByTestId('forgot-password-input'), 'short');
    await fireEvent.press(screen.getByTestId('forgot-submit'));
    expect(screen.getByText('The new password needs at least 8 characters.')).toBeTruthy();

    expect(mockedReset).not.toHaveBeenCalled();
  });

  it('resets the password and offers to go back to sign in', async () => {
    await render(<ForgotPasswordScreen />);
    await goToCodeStep();

    await fireEvent.changeText(screen.getByTestId('forgot-code-input'), '123456');
    await fireEvent.changeText(screen.getByTestId('forgot-password-input'), 'brand-new-pass');
    await fireEvent.press(screen.getByTestId('forgot-submit'));

    await waitFor(() => expect(screen.getByText('Password changed')).toBeTruthy());
    expect(mockedReset).toHaveBeenCalledWith({ email: 'ann@example.com', code: '123456', newPassword: 'brand-new-pass' });

    await fireEvent.press(screen.getByTestId('forgot-back-to-login'));
    expect(mockNavigate).toHaveBeenCalledWith('Login');
  });

  it('explains a wrong or expired code', async () => {
    mockedReset.mockRejectedValue({ status: 400, message: 'x', isNetworkError: false });
    await render(<ForgotPasswordScreen />);
    await goToCodeStep();

    await fireEvent.changeText(screen.getByTestId('forgot-code-input'), '000000');
    await fireEvent.changeText(screen.getByTestId('forgot-password-input'), 'brand-new-pass');
    await fireEvent.press(screen.getByTestId('forgot-submit'));

    await waitFor(() => expect(screen.getByText(/invalid or has expired/)).toBeTruthy());
    expect(screen.queryByText('Password changed')).toBeNull();
  });

  it('lets the user go back and ask for a new code', async () => {
    await render(<ForgotPasswordScreen />);
    await goToCodeStep();

    await fireEvent.press(screen.getByTestId('forgot-resend'));

    expect(screen.getByTestId('forgot-send-code')).toBeTruthy();
  });
});
