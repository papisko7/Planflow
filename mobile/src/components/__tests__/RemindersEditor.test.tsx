import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react-native';
import { RemindersEditor } from '../RemindersEditor';
import { ReminderAnchor, TaskType, type ReminderInput } from '../../types/task';
import { dayReminder, offsetReminder } from '../../utils/reminders';

// A small host so the editor works the way the forms use it: the list lives in the parent.
function Host({ initial = [], taskType = TaskType.Event, isAllDay = false, onList }: {
  initial?: ReminderInput[];
  taskType?: TaskType;
  isAllDay?: boolean;
  onList?: (list: ReminderInput[]) => void;
}) {
  const [list, setList] = useState(initial);
  return (
    <RemindersEditor
      value={list}
      onChange={(next) => {
        setList(next);
        onList?.(next);
      }}
      taskType={taskType}
      isAllDay={isAllDay}
    />
  );
}

describe('RemindersEditor - timed item', () => {
  it('starts empty and adds "10 minutes before" with one tap of Add', async () => {
    const onList = jest.fn();
    await render(<Host onList={onList} />);
    expect(screen.getByText('No alerts yet.')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('reminder-add'));

    expect(onList).toHaveBeenLastCalledWith([offsetReminder(10, ReminderAnchor.Start)]);
    expect(screen.getByText('10 minutes before')).toBeTruthy();
  });

  it('converts hours and days to minutes', async () => {
    const onList = jest.fn();
    await render(<Host onList={onList} />);

    await fireEvent.changeText(screen.getByTestId('reminder-amount-input'), '2');
    await fireEvent.press(screen.getByTestId('reminder-unit-hours'));
    await fireEvent.press(screen.getByTestId('reminder-add'));
    await fireEvent.changeText(screen.getByTestId('reminder-amount-input'), '3');
    await fireEvent.press(screen.getByTestId('reminder-unit-days'));
    await fireEvent.press(screen.getByTestId('reminder-add'));

    expect(onList).toHaveBeenLastCalledWith([offsetReminder(120, ReminderAnchor.Start), offsetReminder(4320, ReminderAnchor.Start)]);
  });

  it('removes an alert', async () => {
    const onList = jest.fn();
    await render(<Host initial={[offsetReminder(30, ReminderAnchor.Start), offsetReminder(60, ReminderAnchor.Start)]} onList={onList} />);

    await fireEvent.press(screen.getByTestId('reminder-remove-0'));

    expect(onList).toHaveBeenLastCalledWith([offsetReminder(60, ReminderAnchor.Start)]);
    expect(screen.queryByText('30 minutes before')).toBeNull();
  });

  it('refuses a duplicate, a nonsense amount and more than four weeks, with a message', async () => {
    const onList = jest.fn();
    await render(<Host initial={[offsetReminder(10, ReminderAnchor.Start)]} onList={onList} />);

    await fireEvent.press(screen.getByTestId('reminder-add')); // 10 minutes again
    expect(screen.getByTestId('reminder-error').props.children).toMatch(/already there/);

    await fireEvent.changeText(screen.getByTestId('reminder-amount-input'), 'abc');
    await fireEvent.press(screen.getByTestId('reminder-add'));
    expect(screen.getByTestId('reminder-error').props.children).toMatch(/whole number/);

    await fireEvent.changeText(screen.getByTestId('reminder-amount-input'), '5');
    await fireEvent.press(screen.getByTestId('reminder-unit-days'));
    await fireEvent.changeText(screen.getByTestId('reminder-amount-input'), '30');
    await fireEvent.press(screen.getByTestId('reminder-add'));
    expect(screen.getByTestId('reminder-error').props.children).toMatch(/At most 28 days/);

    expect(onList).not.toHaveBeenCalled();
  });

  it('stops offering Add at four alerts', async () => {
    await render(<Host initial={[5, 10, 15, 20].map((m) => offsetReminder(m, ReminderAnchor.Start))} />);

    expect(screen.queryByTestId('reminder-add')).toBeNull();
    expect(screen.getByText(/Four alerts is the most/)).toBeTruthy();
  });

  it('an event has no choice of what to count from', async () => {
    await render(<Host taskType={TaskType.Event} />);

    expect(screen.queryByTestId('reminder-anchor-deadline')).toBeNull();
  });
});

describe('RemindersEditor - task', () => {
  it('counts back from the deadline by default, and from the start when asked', async () => {
    const onList = jest.fn();
    await render(<Host taskType={TaskType.Task} onList={onList} />);

    await fireEvent.press(screen.getByTestId('reminder-add'));
    await fireEvent.press(screen.getByTestId('reminder-anchor-start'));
    await fireEvent.changeText(screen.getByTestId('reminder-amount-input'), '1');
    await fireEvent.press(screen.getByTestId('reminder-unit-hours'));
    await fireEvent.press(screen.getByTestId('reminder-add'));

    expect(onList).toHaveBeenLastCalledWith([offsetReminder(10, ReminderAnchor.Deadline), offsetReminder(60, ReminderAnchor.Start)]);
    expect(screen.getByText('10 minutes before the deadline')).toBeTruthy();
    expect(screen.getByText('1 hour before the start')).toBeTruthy();
  });

  it('the same lead from the other anchor is not a duplicate', async () => {
    await render(<Host taskType={TaskType.Task} initial={[offsetReminder(10, ReminderAnchor.Deadline)]} />);

    await fireEvent.press(screen.getByTestId('reminder-anchor-start'));
    await fireEvent.press(screen.getByTestId('reminder-add'));

    expect(screen.queryByTestId('reminder-error')).toBeNull();
    expect(screen.getByTestId('reminder-row-1')).toBeTruthy();
  });
});

describe('RemindersEditor - whole-day item', () => {
  it('asks for days and a time of day instead of minutes', async () => {
    const onList = jest.fn();
    await render(<Host isAllDay onList={onList} />);
    expect(screen.queryByTestId('reminder-unit-minutes')).toBeNull();

    await fireEvent.changeText(screen.getByTestId('reminder-days-input'), '1');
    await fireEvent.changeText(screen.getByTestId('reminder-time-input'), '08:30');
    await fireEvent.press(screen.getByTestId('reminder-add'));

    expect(onList).toHaveBeenLastCalledWith([dayReminder(1, 510, ReminderAnchor.Start)]);
    expect(screen.getByText('1 day before at 08:30')).toBeTruthy();
  });

  it('refuses a time that is not HH:MM and more than 28 days', async () => {
    await render(<Host isAllDay />);

    await fireEvent.changeText(screen.getByTestId('reminder-time-input'), '25:00');
    await fireEvent.press(screen.getByTestId('reminder-add'));
    expect(screen.getByTestId('reminder-error').props.children).toMatch(/HH:MM/);

    await fireEvent.changeText(screen.getByTestId('reminder-time-input'), '09:00');
    await fireEvent.changeText(screen.getByTestId('reminder-days-input'), '40');
    await fireEvent.press(screen.getByTestId('reminder-add'));
    expect(screen.getByTestId('reminder-error').props.children).toMatch(/28 days/);
  });
});
