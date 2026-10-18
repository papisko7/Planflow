import { useCallback, useEffect, useMemo, useState } from 'react';
import { BackHandler } from 'react-native';

// The ticked entries of a list (selection mode). It is "on" while at least one entry is ticked: a double tap ticks the first, tapping ticks
// more, unticking the last one leaves the mode. Entries that disappear from `ids` (deleted, filtered out, a reload) are dropped, and
// while ticking the phone's back button leaves the mode first instead of the screen.
export function useSelectionSet(ids: string[]) {
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const selecting = selected.size > 0;

  // The ids as one string, so a new array with the same content does not count as a change.
  const key = useMemo(() => ids.join('|'), [ids]);
  useEffect(() => {
    const present = new Set(key ? key.split('|') : []);
    setSelected((current) => {
      const kept = [...current].filter((id) => present.has(id));
      return kept.length === current.size ? current : new Set(kept);
    });
  }, [key]);

  const toggle = useCallback(
    (id: string) =>
      setSelected((current) => {
        const next = new Set(current);
        if (next.has(id)) next.delete(id);
        else next.add(id);
        return next;
      }),
    [],
  );
  const selectAll = useCallback(() => setSelected(new Set(ids)), [ids]);
  const clear = useCallback(() => setSelected(new Set()), []);

  useEffect(() => {
    if (!selecting) return undefined;
    const subscription = BackHandler.addEventListener('hardwareBackPress', () => {
      clear();
      return true;
    });
    return () => subscription.remove();
  }, [selecting, clear]);

  return { selected, selecting, toggle, selectAll, clear };
}
