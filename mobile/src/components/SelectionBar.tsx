import { useMemo, useState } from 'react';
import { Alert, Pressable, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { ListFormModal } from './ListFormModal';
import { OptionSheet, type SheetOption } from './OptionSheet';
import { useBulkActions } from '../hooks/useBulkActions';
import { colors, spacing, typography } from '../theme/theme';
import { ItemListKind, type ItemListDto } from '../types/itemList';
import type { TaskStatus, TaskWithTeam } from '../types/task';
import type { TeamDto } from '../types/team';
import { commonStatuses, expandToSeries, groupByTeam, listKindFor } from '../utils/selection';
import { STATUS_LABEL } from '../utils/taskStatus';

type Props = {
  selected: TaskWithTeam[];
  allItems: TaskWithTeam[];
  lists: ItemListDto[];
  teams: TeamDto[];
  onSelectAll: () => void;
  onCancel: () => void;
  reload: () => unknown;
};

const NONE = '__none';
const NEW_LIST = '__new';

export function SelectionBar({ selected, allItems, lists, teams, onSelectAll, onCancel, reload }: Props) {
  const bulk = useBulkActions(reload);
  const [sheet, setSheet] = useState<'status' | 'move' | null>(null);
  const [creatingList, setCreatingList] = useState(false);

  const targets = useMemo(() => expandToSeries(selected, allItems), [selected, allItems]);
  const statuses = useMemo(() => commonStatuses(targets), [targets]);
  const kind = listKindFor(targets);
  const teamIds = [...groupByTeam(targets).keys()];
  const hiddenCount = targets.length - selected.length;

  const statusOptions: SheetOption[] = statuses.map((status) => ({ key: String(status), label: STATUS_LABEL[status], testID: `status-option-${status}` }));

  const openMove = () => {
    if (kind === null) {
      Alert.alert('Pick one kind of item', 'Tasks and events go into different lists. Select only tasks or only events to move them.');
    } else if (teamIds.length !== 1) {
      Alert.alert('Pick items of one team', 'A list belongs to one team. Select items of a single team to move them.');
    } else {
      setSheet('move');
    }
  };

  const teamId = teamIds[0];
  const teamLists = lists.filter((l) => l.teamId === teamId && l.kind === kind);
  const inEveryList = (list: ItemListDto) => targets.every((t) => t.itemListId === list.id);
  const moveOptions: SheetOption[] = [
    ...teamLists.map((list) => ({ key: list.id, label: list.name, testID: `move-option-${list.id}`, checked: inEveryList(list) })),
    { key: NONE, label: 'No list', testID: 'move-option-none' },
    { key: NEW_LIST, label: '+ New list…', testID: 'move-option-new' },
  ];

  const askDelete = () => {
    const count = targets.length;
    bulk.deleteItems(targets, {
      title: count === 1 ? 'Delete this item?' : `Delete ${count} items?`,
      note: hiddenCount > 0 ? `Includes ${hiddenCount} more occurrence${hiddenCount === 1 ? '' : 's'} of repeating events.` : undefined,
      onDone: onCancel,
    });
  };

  return (
    <View testID="selection-bar" style={styles.bar}>
      <View style={styles.topRow}>
        <Pressable testID="selection-cancel" onPress={onCancel} hitSlop={8} accessibilityLabel="Leave selection mode">
          <Ionicons name="close" size={24} color={colors.text} />
        </Pressable>
        <Text testID="selection-count" style={styles.count}>
          {selected.length} selected
        </Text>
        <Pressable testID="selection-select-all" onPress={onSelectAll} hitSlop={6}>
          <Text style={styles.action}>Select all</Text>
        </Pressable>
      </View>
      <View style={styles.actionRow}>
        <Pressable testID="selection-status" onPress={() => setSheet('status')} hitSlop={6}>
          <Text style={styles.action}>Status</Text>
        </Pressable>
        <Pressable testID="selection-move" onPress={openMove} hitSlop={6}>
          <Text style={styles.action}>Move to list</Text>
        </Pressable>
        <Pressable testID="selection-delete" onPress={askDelete} hitSlop={6}>
          <Text style={[styles.action, styles.danger]}>Delete</Text>
        </Pressable>
      </View>

      <OptionSheet
        visible={sheet === 'status'}
        title={`Set status of ${targets.length} item${targets.length === 1 ? '' : 's'}`}
        options={statusOptions}
        onClose={() => setSheet(null)}
        onSelect={(key) => void bulk.setStatus(targets, Number(key) as TaskStatus, onCancel)}
      />
      <OptionSheet
        visible={sheet === 'move'}
        title="Move to list"
        options={moveOptions}
        onClose={() => setSheet(null)}
        onSelect={(key) => {
          if (key === NEW_LIST) setCreatingList(true);
          else void bulk.moveToList(targets, key === NONE ? null : key, onCancel);
        }}
      />
      <ListFormModal
        visible={creatingList}
        teams={teams.filter((t) => t.id === teamId)}
        defaultKind={kind ?? ItemListKind.TaskList}
        defaultTeamId={teamId}
        onClose={() => setCreatingList(false)}
        onSaved={(list) => void bulk.moveToList(targets, list.id, onCancel)}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  bar: {
    gap: spacing.sm,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.border,
    backgroundColor: colors.surface,
  },
  topRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md },
  count: { ...typography.subtitle, color: colors.text, flex: 1 },
  actionRow: { flexDirection: 'row', justifyContent: 'space-around', alignItems: 'center', paddingBottom: spacing.xs },
  action: { ...typography.body, color: colors.primary, fontWeight: '700' },
  danger: { color: colors.danger },
});
