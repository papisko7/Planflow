import { Alert, Platform, type AlertButton } from 'react-native';
import { SeriesScope } from '../../types/task';
import { askSeriesScope } from '../seriesScope';

let alertSpy: jest.SpyInstance;
const lastCall = () => alertSpy.mock.calls[0] as [string, string | undefined, AlertButton[], { cancelable?: boolean; onDismiss?: () => void }];
const press = (label: string) => lastCall()[2].find((b) => b.text === label)!.onPress!();

beforeEach(() => {
  alertSpy = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
});

afterEach(() => {
  alertSpy.mockRestore();
  Platform.OS = 'ios';
});

describe('askSeriesScope', () => {
  it('offers this item, this and following, all items and (on iOS) cancel', async () => {
    const answer = askSeriesScope('Edit repeating item', 'Which ones?');

    const [title, message, buttons] = lastCall();
    expect(title).toBe('Edit repeating item');
    expect(message).toBe('Which ones?');
    expect(buttons.map((b) => b.text)).toEqual(['This item', 'This and following', 'All items', 'Cancel']);
    press('Cancel');
    await expect(answer).resolves.toBeNull();
  });

  it.each([
    ['This item', SeriesScope.This],
    ['This and following', SeriesScope.ThisAndFollowing],
    ['All items', SeriesScope.All],
  ])('answers with the scope of "%s"', async (label, scope) => {
    const answer = askSeriesScope('Delete', undefined);
    press(label);
    await expect(answer).resolves.toBe(scope);
  });

  it('answers null when the pop-up is dismissed', async () => {
    const answer = askSeriesScope('Delete');
    lastCall()[3].onDismiss!();
    await expect(answer).resolves.toBeNull();
  });

  it('leaves Cancel to the back button on Android, which shows at most three buttons', () => {
    Platform.OS = 'android';
    askSeriesScope('Delete');
    expect(lastCall()[2].map((b) => b.text)).toEqual(['This item', 'This and following', 'All items']);
    expect(lastCall()[3].cancelable).toBe(true);
  });
});
