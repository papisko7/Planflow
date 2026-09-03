import { fireEvent, render, screen, waitFor, act } from '@testing-library/react-native';
import ChatScreen from '../ChatScreen';
import { createChatConnection, getChatMessages } from '../../services/chatService';
import { getCurrentUserId } from '../../services/authService';

jest.mock('../../services/chatService');
jest.mock('../../services/authService');
jest.mock('@react-navigation/native', () => ({
  useRoute: () => ({ params: { teamId: 'team-1', teamName: 'Team One' } }),
}));

const mockedGetChatMessages = getChatMessages as jest.MockedFunction<typeof getChatMessages>;
const mockedCreateChatConnection = createChatConnection as jest.MockedFunction<typeof createChatConnection>;
const mockedGetCurrentUserId = getCurrentUserId as jest.MockedFunction<typeof getCurrentUserId>;

function buildConnectionMock() {
  const handlers: Record<string, (...args: any[]) => void> = {};
  return {
    on: jest.fn((event: string, handler: (...args: any[]) => void) => {
      handlers[event] = handler;
    }),
    invoke: jest.fn().mockResolvedValue(undefined),
    start: jest.fn().mockResolvedValue(undefined),
    stop: jest.fn().mockResolvedValue(undefined),
    emit(event: string, ...args: any[]) {
      handlers[event]?.(...args);
    },
  };
}

describe('ChatScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedGetCurrentUserId.mockReturnValue('user-1');
  });

  it('loads history and renders it, then applies a live ReceiveMessage event', async () => {
    mockedGetChatMessages.mockResolvedValue([
      {
        id: 'm1',
        teamId: 'team-1',
        senderUserId: 'user-2',
        senderDisplayName: 'Alice',
        content: 'Hi there',
        createdAtUtc: new Date().toISOString(),
      },
    ]);
    const connection = buildConnectionMock();
    mockedCreateChatConnection.mockReturnValue(connection as any);

    await render(<ChatScreen />);

    await waitFor(() => expect(screen.getByText('Hi there')).toBeTruthy());
    expect(connection.start).toHaveBeenCalled();

    await act(async () => {
      connection.emit('ReceiveMessage', {
        id: 'm2',
        teamId: 'team-1',
        senderUserId: 'user-1',
        senderDisplayName: 'Me',
        content: 'New live message',
        createdAtUtc: new Date().toISOString(),
      });
    });

    await waitFor(() => expect(screen.getByText('New live message')).toBeTruthy());
  });

  it('sends the drafted message via SendMessage and clears the input', async () => {
    mockedGetChatMessages.mockResolvedValue([]);
    const connection = buildConnectionMock();
    mockedCreateChatConnection.mockReturnValue(connection as any);

    await render(<ChatScreen />);
    await waitFor(() => expect(screen.getByTestId('chat-input')).toBeTruthy());
    await waitFor(() => expect(connection.start).toHaveBeenCalled());

    const input = screen.getByTestId('chat-input');
    await fireEvent.changeText(input, 'Hello team');
    await fireEvent.press(screen.getByTestId('chat-send-button'));

    await waitFor(() => expect(connection.invoke).toHaveBeenCalledWith('SendMessage', 'team-1', 'Hello team'));
    expect(input.props.value).toBe('');
  });

  it('shows the typing indicator on a UserTyping event from another user', async () => {
    mockedGetChatMessages.mockResolvedValue([]);
    const connection = buildConnectionMock();
    mockedCreateChatConnection.mockReturnValue(connection as any);

    await render(<ChatScreen />);
    await waitFor(() => expect(screen.getByTestId('chat-input')).toBeTruthy());
    await waitFor(() => expect(connection.start).toHaveBeenCalled());

    await act(async () => {
      connection.emit('UserTyping', 'user-2', true);
    });

    await waitFor(() => expect(screen.getByText('Someone is typing…')).toBeTruthy());
  });
});
