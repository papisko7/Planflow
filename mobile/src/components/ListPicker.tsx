import { useEffect, useState } from 'react';
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { createList, getTeamLists } from '../services/listService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { ItemListKind, type ItemListDto } from '../types/itemList';
import type { ApiError } from '../types/api';
import { FieldLabel } from './FieldLabel';

type Props = {
  teamId: string | null;
  kind: ItemListKind;
  value: string | null;
  onChange: (listId: string | null) => void;
};

export function ListPicker({ teamId, kind, value, onChange }: Props) {
  const [lists, setLists] = useState<ItemListDto[] | null>(null); // null = not loaded yet
  const [adding, setAdding] = useState(false);
  const [name, setName] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!teamId) return undefined;
    let cancelled = false;
    setLists(null);
    // Started inside a promise, so even a failure before the request (no session yet) ends up as "no lists" instead of an error.
    Promise.resolve()
      .then(() => getTeamLists(teamId))
      .then((data) => {
        if (!cancelled) setLists(data);
      })
      .catch(() => {
        if (!cancelled) setLists([]); // an older server without lists: just nothing to pick
      });
    return () => {
      cancelled = true;
    };
  }, [teamId]);

  const choices = (lists ?? []).filter((l) => l.kind === kind);

  useEffect(() => {
    if (value && lists && !choices.some((l) => l.id === value)) onChange(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lists, kind, value]);

  const add = async () => {
    const trimmed = name.trim();
    if (!trimmed || !teamId) {
      setError('Give the list a name.');
      return;
    }
    setError(null);
    try {
      const created = await createList(teamId, trimmed, kind);
      setLists((current) => [...(current ?? []), created]);
      onChange(created.id);
      setAdding(false);
      setName('');
    } catch (err) {
      setError((err as ApiError).message ?? 'Could not create the list.');
    }
  };

  if (!teamId) return null;

  return (
    <View>
      <Text style={styles.label}>List:</Text>
      <View style={styles.chipRow}>
        <Pressable testID="list-chip-none" onPress={() => onChange(null)} style={[styles.chip, value === null ? styles.chipActive : null]}>
          <Text style={value === null ? styles.chipTextActive : styles.chipText}>None</Text>
        </Pressable>
        {choices.map((list) => (
          <Pressable
            key={list.id}
            testID={`list-chip-${list.id}`}
            onPress={() => onChange(list.id)}
            style={[styles.chip, value === list.id ? styles.chipActive : null]}
          >
            <Text style={value === list.id ? styles.chipTextActive : styles.chipText} numberOfLines={1}>
              {list.name}
            </Text>
          </Pressable>
        ))}
        <Pressable testID="list-chip-new" onPress={() => setAdding((current) => !current)} style={styles.chip}>
          <Text style={styles.chipText}>+ New list</Text>
        </Pressable>
      </View>
      {adding ? (
        <View>
          <FieldLabel title={kind === ItemListKind.MeetingList ? 'Name of the new meeting list' : 'Name of the new task list'} />
          <View style={styles.addRow}>
            <TextInput testID="list-picker-name" style={styles.input} value={name} onChangeText={setName} maxLength={100} />
            <Pressable testID="list-picker-add" onPress={add} style={styles.addButton}>
              <Text style={styles.addButtonText}>Add</Text>
            </Pressable>
          </View>
        </View>
      ) : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  label: { ...typography.body, color: colors.textMuted },
  chipRow: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs, marginTop: spacing.xs },
  chip: { borderWidth: 1, borderColor: colors.border, borderRadius: radius.lg, paddingHorizontal: spacing.sm, paddingVertical: spacing.xs, maxWidth: 160 },
  chipActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  chipText: { ...typography.body, fontSize: 12, color: colors.textMuted },
  chipTextActive: { ...typography.body, fontSize: 12, color: colors.primary, fontWeight: '600' },
  addRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, marginTop: spacing.xs },
  input: {
    flex: 1,
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.sm,
    color: colors.text,
  },
  addButton: { backgroundColor: colors.primary, borderRadius: radius.sm, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  addButtonText: { color: colors.surface, fontWeight: '700' },
  error: { ...typography.body, color: colors.danger },
});
