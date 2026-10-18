import { BackHandler } from 'react-native';
import { act, renderHook } from '@testing-library/react-native';
import { useSelectionSet } from '../useSelectionSet';

describe('useSelectionSet', () => {
  it('is on while something is ticked and off again when the last one is unticked', async () => {
    const { result } = await renderHook(() => useSelectionSet(['a', 'b', 'c']));
    expect(result.current.selecting).toBe(false);

    await act(async () => result.current.toggle('a'));
    expect(result.current.selecting).toBe(true);
    expect([...result.current.selected]).toEqual(['a']);

    await act(async () => result.current.toggle('a'));
    expect(result.current.selecting).toBe(false);
  });

  it('"select all" ticks every id it was given, "clear" leaves the mode', async () => {
    const { result } = await renderHook(() => useSelectionSet(['a', 'b', 'c']));

    await act(async () => result.current.selectAll());
    expect(result.current.selected.size).toBe(3);

    await act(async () => result.current.clear());
    expect(result.current.selecting).toBe(false);
  });

  it('drops an entry that disappeared from the list (deleted, filtered out) and leaves the mode with the last one', async () => {
    const { result, rerender } = await renderHook(({ ids }: { ids: string[] }) => useSelectionSet(ids), { initialProps: { ids: ['a', 'b'] } });
    await act(async () => result.current.toggle('a'));
    await act(async () => result.current.toggle('b'));

    await rerender({ ids: ['b'] });
    expect([...result.current.selected]).toEqual(['b']);

    await rerender({ ids: [] });
    expect(result.current.selecting).toBe(false);
  });

  it('a new array with the same ids is not a change', async () => {
    const { result, rerender } = await renderHook(({ ids }: { ids: string[] }) => useSelectionSet(ids), { initialProps: { ids: ['a', 'b'] } });
    await act(async () => result.current.toggle('a'));
    const before = result.current.selected;

    await rerender({ ids: ['a', 'b'] });

    expect(result.current.selected).toBe(before);
  });

  it('while ticking, the phone\'s back button leaves the mode instead of the screen', async () => {
    let onBack: (() => boolean) | undefined;
    const remove = jest.fn();
    const spy = jest.spyOn(BackHandler, 'addEventListener').mockImplementation((_event, handler) => {
      onBack = handler as () => boolean;
      return { remove };
    });
    const { result } = await renderHook(() => useSelectionSet(['a']));
    expect(onBack).toBeUndefined(); // nothing ticked: the back button is not taken over

    await act(async () => result.current.toggle('a'));
    expect(onBack).toBeDefined();
    let handled = false;
    await act(async () => {
      handled = onBack!();
    });

    expect(handled).toBe(true);
    expect(result.current.selecting).toBe(false);
    spy.mockRestore();
  });
});
