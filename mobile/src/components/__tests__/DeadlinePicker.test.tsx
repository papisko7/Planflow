import { fireEvent, render, screen } from '@testing-library/react-native';
import { DeadlinePicker } from '../DeadlinePicker';
import { addDays, dayKey, splitDeadline, startOfDay } from '../../utils/dateUtils';

describe('DeadlinePicker', () => {
  it('sets "tomorrow" as an all-day deadline', async () => {
    const onChange = jest.fn();
    await render(<DeadlinePicker value={null} onChange={onChange} futureOnly />);

    await fireEvent.press(screen.getByTestId('deadline-picker-button'));
    await fireEvent.press(screen.getByTestId('deadline-picker-quick-tomorrow'));
    await fireEvent.press(screen.getByTestId('deadline-picker-set'));

    const iso = onChange.mock.calls[0][0] as string;
    const { day, time } = splitDeadline(iso);
    expect(dayKey(day)).toBe(dayKey(addDays(startOfDay(new Date()), 1)));
    expect(time).toBeNull();
  });

  it('applies a chosen time to the picked day', async () => {
    const onChange = jest.fn();
    await render(<DeadlinePicker value={null} onChange={onChange} futureOnly />);

    await fireEvent.press(screen.getByTestId('deadline-picker-button'));
    await fireEvent.press(screen.getByTestId('deadline-picker-quick-week'));
    await fireEvent.press(screen.getByTestId('deadline-picker-time-1500'));
    await fireEvent.press(screen.getByTestId('deadline-picker-set'));

    expect(splitDeadline(onChange.mock.calls[0][0] as string).time).toBe('15:00');
  });

  it('clears an existing deadline', async () => {
    const onChange = jest.fn();
    const existing = new Date(Date.now() + 3 * 86_400_000).toISOString();
    await render(<DeadlinePicker value={existing} onChange={onChange} />);

    await fireEvent.press(screen.getByTestId('deadline-picker-button'));
    await fireEvent.press(screen.getByTestId('deadline-picker-clear'));

    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('does not offer "Set" until a day is chosen', async () => {
    const onChange = jest.fn();
    await render(<DeadlinePicker value={null} onChange={onChange} futureOnly />);

    await fireEvent.press(screen.getByTestId('deadline-picker-button'));
    await fireEvent.press(screen.getByTestId('deadline-picker-set'));

    expect(onChange).not.toHaveBeenCalled();
  });
});
