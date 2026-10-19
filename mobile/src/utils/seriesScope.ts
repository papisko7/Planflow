import { Alert, Platform, type AlertButton } from 'react-native';
import { SeriesScope } from '../types/task';

// The "This / This and following / All" pop-up of a repeating item: asked before saving a change or deleting. Resolves to the choice,
// or to null when the person backed out. Android shows at most three buttons, so there "Cancel" is the back button / a tap outside.
export function askSeriesScope(title: string, message?: string): Promise<SeriesScope | null> {
  return new Promise((resolve) => {
    const choose = (scope: SeriesScope) => () => resolve(scope);
    const buttons: AlertButton[] = [
      { text: 'This item', onPress: choose(SeriesScope.This) },
      { text: 'This and following', onPress: choose(SeriesScope.ThisAndFollowing) },
      { text: 'All items', onPress: choose(SeriesScope.All) },
    ];
    if (Platform.OS === 'ios') buttons.push({ text: 'Cancel', style: 'cancel', onPress: () => resolve(null) });
    Alert.alert(title, message, buttons, { cancelable: true, onDismiss: () => resolve(null) });
  });
}
