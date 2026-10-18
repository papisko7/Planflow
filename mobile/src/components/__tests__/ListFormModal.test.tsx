import { fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { ListFormModal } from '../ListFormModal';
import { createList, renameList } from '../../services/listService';
import { ItemListKind, type ItemListDto } from '../../types/itemList';
import type { TeamDto } from '../../types/team';

jest.mock('../../services/listService');

const mockedCreate = createList as jest.MockedFunction<typeof createList>;
const mockedRename = renameList as jest.MockedFunction<typeof renameList>;

const team = (id: string, name: string, isPersonal = false): TeamDto => ({ id, name, description: null, memberCount: 2, isPersonal, muteNotifications: false, muteChat: false });
const saved: ItemListDto = { id: 'l1', teamId: 's', name: 'Sprint', kind: ItemListKind.TaskList, createdByUserId: 'me' };

describe('ListFormModal', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockedCreate.mockResolvedValue(saved);
    mockedRename.mockResolvedValue({ ...saved, name: 'Sprint 2' });
  });

  it('makes a task list in the only team, closes and reports the new list', async () => {
    const onSaved = jest.fn();
    const onClose = jest.fn();
    await render(<ListFormModal visible teams={[team('s', 'Squad')]} onClose={onClose} onSaved={onSaved} />);

    await fireEvent.changeText(screen.getByTestId('list-name-input'), '  Sprint ');
    await fireEvent.press(screen.getByTestId('list-form-submit'));

    await waitFor(() => expect(mockedCreate).toHaveBeenCalledWith('s', 'Sprint', ItemListKind.TaskList));
    expect(onSaved).toHaveBeenCalledWith(saved);
    expect(onClose).toHaveBeenCalled();
    expect(screen.queryByTestId('list-team-s')).toBeNull(); // one team: nothing to ask
  });

  it('makes a meeting list when that kind is picked, and asks for the team when there are several', async () => {
    await render(<ListFormModal visible teams={[team('p', 'Me', true), team('s', 'Squad')]} defaultTeamId="p" onClose={jest.fn()} onSaved={jest.fn()} />);

    await fireEvent.changeText(screen.getByTestId('list-name-input'), 'Standups');
    await fireEvent.press(screen.getByTestId(`list-kind-${ItemListKind.MeetingList}`));
    await fireEvent.press(screen.getByTestId('list-team-s'));
    await fireEvent.press(screen.getByTestId('list-form-submit'));

    await waitFor(() => expect(mockedCreate).toHaveBeenCalledWith('s', 'Standups', ItemListKind.MeetingList));
  });

  it('starts as the kind it was opened for (the Events tab opens it for meetings)', async () => {
    await render(<ListFormModal visible teams={[team('s', 'Squad')]} defaultKind={ItemListKind.MeetingList} onClose={jest.fn()} onSaved={jest.fn()} />);

    await fireEvent.changeText(screen.getByTestId('list-name-input'), 'Standups');
    await fireEvent.press(screen.getByTestId('list-form-submit'));

    await waitFor(() => expect(mockedCreate).toHaveBeenCalledWith('s', 'Standups', ItemListKind.MeetingList));
  });

  it('wants a name and does not call the server without one', async () => {
    await render(<ListFormModal visible teams={[team('s', 'Squad')]} onClose={jest.fn()} onSaved={jest.fn()} />);

    await fireEvent.press(screen.getByTestId('list-form-submit'));

    expect(screen.getByText('Name is required.')).toBeTruthy();
    expect(mockedCreate).not.toHaveBeenCalled();
  });

  it('shows the server\'s reason (a name that is taken) and stays open', async () => {
    mockedCreate.mockRejectedValue({ status: 409, message: 'A list named "Sprint" already exists.', isNetworkError: false });
    const onClose = jest.fn();
    await render(<ListFormModal visible teams={[team('s', 'Squad')]} onClose={onClose} onSaved={jest.fn()} />);

    await fireEvent.changeText(screen.getByTestId('list-name-input'), 'Sprint');
    await fireEvent.press(screen.getByTestId('list-form-submit'));

    expect(await screen.findByText('A list named "Sprint" already exists.')).toBeTruthy();
    expect(onClose).not.toHaveBeenCalled();
  });

  it('renames a list: the name is filled in, the kind and the team are not asked again', async () => {
    const onSaved = jest.fn();
    await render(<ListFormModal visible teams={[team('s', 'Squad')]} list={saved} onClose={jest.fn()} onSaved={onSaved} />);

    expect(screen.getByText('Rename list')).toBeTruthy();
    expect(screen.getByTestId('list-name-input').props.value).toBe('Sprint');
    expect(screen.queryByTestId(`list-kind-${ItemListKind.TaskList}`)).toBeNull();
    await fireEvent.changeText(screen.getByTestId('list-name-input'), 'Sprint 2');
    await fireEvent.press(screen.getByTestId('list-form-submit'));

    await waitFor(() => expect(mockedRename).toHaveBeenCalledWith('s', 'l1', 'Sprint 2'));
    expect(mockedCreate).not.toHaveBeenCalled();
    expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ name: 'Sprint 2' }));
  });

  it('explains a refusal to rename (not the creator, not an admin)', async () => {
    mockedRename.mockRejectedValue({ status: 403, message: 'Forbidden', isNetworkError: false });
    await render(<ListFormModal visible teams={[team('s', 'Squad')]} list={saved} onClose={jest.fn()} onSaved={jest.fn()} />);

    await fireEvent.press(screen.getByTestId('list-form-submit'));

    expect(await screen.findByText(/Only the person who made the list, or a team owner or admin/)).toBeTruthy();
  });
});
