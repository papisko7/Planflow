import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { ActivityIndicator, Alert, BackHandler, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { SwipeableRow } from '../components/SwipeableRow';
import {
  clearNotifications,
  deleteNotification,
  deleteNotifications,
  getInbox,
  markAllNotificationsRead,
  markNotificationRead,
  markNotificationsRead,
} from '../services/notificationService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { DOUBLE_TAP_MS } from '../utils/gestures';
import { notificationIcon, timeAgo } from '../utils/notificationDisplay';
import type { RootStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';
import type { InboxNotification } from '../types/notification';

const PAGE_SIZE = 30;

// The in-app inbox: everything PlanFlow told you, newest first.
// Tap opens the item it is about (and marks the entry read). Swipe right deletes, swipe left marks read.
// A long press or a quick double tap ticks an entry: then tapping ticks more, and the bar at the bottom reads or deletes them all at once.
export default function NotificationsScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [items, setItems] = useState<InboxNotification[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [hasMore, setHasMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set());
  const loadingMoreRef = useRef(false);
  // The first tap of a possible double tap: the entry opens only if no second tap follows within DOUBLE_TAP_MS.
  const pendingTap = useRef<{ id: string; timer: ReturnType<typeof setTimeout>; open: () => void } | null>(null);
  useEffect(
    () => () => {
      if (pendingTap.current) clearTimeout(pendingTap.current.timer);
    },
    [],
  );

  const selecting = selected.size > 0;

  const loadFirstPage = useCallback(async () => {
    try {
      const page = await getInbox(undefined, PAGE_SIZE);
      setItems(page);
      setHasMore(page.length === PAGE_SIZE);
      // An entry that vanished (deleted elsewhere, or by "clear all") can no longer stay ticked.
      setSelected((current) => {
        const stillThere = new Set(page.map((n) => n.id));
        const kept = [...current].filter((id) => stillThere.has(id));
        return kept.length === current.size ? current : new Set(kept);
      });
      setError(null);
    } catch (err) {
      setError((err as ApiError).message ?? 'Could not load notifications.');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  // Reload on every visit, so entries that arrived while the screen was in the background show up.
  useFocusEffect(
    useCallback(() => {
      void loadFirstPage();
    }, [loadFirstPage]),
  );

  const loadMore = async () => {
    if (!hasMore || loadingMoreRef.current || items.length === 0) return;
    loadingMoreRef.current = true;
    setLoadingMore(true);
    try {
      const page = await getInbox(items[items.length - 1].createdAtUtc, PAGE_SIZE);
      setItems((current) => [...current, ...page]);
      setHasMore(page.length === PAGE_SIZE);
    } catch {
      // keep what is shown; pulling down retries
    } finally {
      loadingMoreRef.current = false;
      setLoadingMore(false);
    }
  };

  // Every action below changes the list at once and, if the server refuses, reloads it so the screen shows the truth again.
  const markAllRead = useCallback(async () => {
    setItems((current) => current.map((n) => ({ ...n, isRead: true })));
    try {
      await markAllNotificationsRead();
    } catch {
      void loadFirstPage();
    }
  }, [loadFirstPage]);

  const clearSelection = useCallback(() => setSelected(new Set()), []);

  const removeFromList = (ids: string[]) => {
    const gone = new Set(ids);
    setItems((current) => current.filter((n) => !gone.has(n.id)));
    setSelected((current) => new Set([...current].filter((id) => !gone.has(id))));
  };

  const deleteOne = async (notification: InboxNotification) => {
    removeFromList([notification.id]);
    try {
      await deleteNotification(notification.id);
    } catch {
      void loadFirstPage();
    }
  };

  const markOneRead = async (notification: InboxNotification) => {
    setItems((current) => current.map((n) => (n.id === notification.id ? { ...n, isRead: true } : n)));
    try {
      await markNotificationRead(notification.id);
    } catch {
      void loadFirstPage();
    }
  };

  const markSelectedRead = async () => {
    const ids = [...selected];
    setItems((current) => current.map((n) => (selected.has(n.id) ? { ...n, isRead: true } : n)));
    clearSelection();
    try {
      await markNotificationsRead(ids);
    } catch {
      void loadFirstPage();
    }
  };

  const deleteSelected = () => {
    const ids = [...selected];
    Alert.alert(ids.length === 1 ? 'Delete this notification?' : `Delete ${ids.length} notifications?`, 'This cannot be undone.', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Delete',
        style: 'destructive',
        onPress: async () => {
          removeFromList(ids);
          try {
            await deleteNotifications(ids);
          } catch {
            void loadFirstPage();
          }
        },
      },
    ]);
  };

  const clearAll = useCallback(() => {
    Alert.alert('Clear all notifications?', 'Every notification is removed from this list. This cannot be undone.', [
      { text: 'Cancel', style: 'cancel' },
      {
        text: 'Clear all',
        style: 'destructive',
        onPress: async () => {
          try {
            await clearNotifications();
          } finally {
            // Not "set to empty": the server keeps an entry whose push has not been delivered yet.
            void loadFirstPage();
          }
        },
      },
    ]);
  }, [loadFirstPage]);

  const toggle = (id: string) =>
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const selectAll = () => setSelected(new Set(items.map((n) => n.id)));

  const open = (notification: InboxNotification) => {
    if (!notification.isRead) {
      setItems((current) => current.map((n) => (n.id === notification.id ? { ...n, isRead: true } : n)));
      markNotificationRead(notification.id).catch(() => undefined);
    }
    if (notification.taskId && notification.teamId) {
      navigation.navigate('TaskDetail', { taskId: notification.taskId, teamId: notification.teamId });
    }
  };

  const onRowPress = (notification: InboxNotification) => {
    if (selecting) {
      toggle(notification.id);
      return;
    }
    const pending = pendingTap.current;
    if (pending && pending.id === notification.id) {
      clearTimeout(pending.timer);
      pendingTap.current = null;
      toggle(notification.id); // the second tap: start ticking with this one
      return;
    }
    if (pending) {
      clearTimeout(pending.timer); // another entry was tapped before the wait ran out: the earlier one opens now
      pending.open();
    }
    const timer = setTimeout(() => {
      pendingTap.current = null;
      open(notification);
    }, DOUBLE_TAP_MS);
    pendingTap.current = { id: notification.id, timer, open: () => open(notification) };
  };

  // While ticking, the phone's back button first leaves the ticking mode instead of the screen.
  useEffect(() => {
    if (!selecting) return undefined;
    const subscription = BackHandler.addEventListener('hardwareBackPress', () => {
      clearSelection();
      return true;
    });
    return () => subscription.remove();
  }, [selecting, clearSelection]);

  const hasUnread = items.some((n) => !n.isRead);
  const hasItems = items.length > 0;
  const selectedCount = selected.size;
  useLayoutEffect(() => {
    navigation.setOptions({
      title: selecting ? `${selectedCount} selected` : 'Notifications',
      headerLeft: selecting
        ? () => (
            <Pressable testID="selection-cancel" onPress={clearSelection} hitSlop={8}>
              <Ionicons name="close" size={24} color={colors.text} />
            </Pressable>
          )
        : undefined,
      headerRight: selecting
        ? undefined
        : () =>
            hasItems ? (
              <View style={styles.headerActions}>
                {hasUnread ? (
                  <Pressable testID="mark-all-read" onPress={markAllRead} hitSlop={8}>
                    <Text style={styles.headerAction}>Mark all read</Text>
                  </Pressable>
                ) : null}
                <Pressable testID="clear-all" onPress={clearAll} hitSlop={8}>
                  <Text style={[styles.headerAction, styles.headerActionDanger]}>Clear all</Text>
                </Pressable>
              </View>
            ) : null,
    });
  }, [navigation, selecting, selectedCount, hasUnread, hasItems, markAllRead, clearAll, clearSelection]);

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  return (
    <View style={styles.screen}>
      <FlatList
        testID="notifications-list"
        style={styles.list}
        contentContainerStyle={items.length === 0 ? styles.emptyContainer : styles.listContent}
        data={items}
        extraData={selected}
        keyExtractor={(item) => item.id}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => {
              setRefreshing(true);
              void loadFirstPage();
            }}
            tintColor={colors.primary}
          />
        }
        onEndReached={loadMore}
        onEndReachedThreshold={0.4}
        ListEmptyComponent={
          <View style={styles.centered}>
            <Ionicons name="notifications-off-outline" size={44} color={colors.textMuted} />
            <Text style={styles.emptyTitle}>{error ? "Couldn't load notifications" : 'No notifications yet'}</Text>
            <Text style={styles.emptyText}>
              {error ?? 'When someone assigns you a task or changes your team, it shows up here.'}
            </Text>
          </View>
        }
        ListFooterComponent={loadingMore ? <ActivityIndicator style={styles.footer} color={colors.primary} /> : null}
        renderItem={({ item }) => {
          const icon = notificationIcon(item.event);
          const ticked = selected.has(item.id);
          return (
            // Always wrapped (swipes are simply switched off while ticking), so the row is not re-created under the finger on long-press.
            <SwipeableRow
              rightLabel="Delete"
              leftLabel="Mark read"
              onSwipeRight={selecting ? undefined : () => void deleteOne(item)}
              onSwipeLeft={selecting || item.isRead ? undefined : () => void markOneRead(item)}
            >
              <Pressable
                testID={`notification-${item.id}`}
                style={[styles.row, item.isRead ? null : styles.rowUnread, ticked ? styles.rowTicked : null]}
                onPress={() => onRowPress(item)}
                onLongPress={() => toggle(item.id)}
                delayLongPress={350}
              >
                {selecting ? (
                  <View testID={ticked ? `notification-ticked-${item.id}` : undefined}>
                    <Ionicons name={ticked ? 'checkbox' : 'square-outline'} size={24} color={ticked ? colors.primary : colors.textMuted} />
                  </View>
                ) : (
                  <Ionicons name={icon.name} size={24} color={icon.color} />
                )}
                <View style={styles.rowText}>
                  <Text style={[styles.title, item.isRead ? null : styles.titleUnread]} numberOfLines={2}>
                    {item.title}
                  </Text>
                  <Text style={styles.body} numberOfLines={3}>
                    {item.body}
                  </Text>
                  <Text style={styles.time}>{timeAgo(item.createdAtUtc)}</Text>
                </View>
                {item.isRead ? null : <View testID={`notification-unread-${item.id}`} style={styles.dot} />}
              </Pressable>
            </SwipeableRow>
          );
        }}
      />

      {selecting ? (
        <View testID="selection-bar" style={styles.selectionBar}>
          <Pressable testID="selection-select-all" onPress={selectAll} hitSlop={6}>
            <Text style={styles.barAction}>Select all</Text>
          </Pressable>
          <Pressable testID="selection-mark-read" onPress={markSelectedRead} hitSlop={6}>
            <Text style={styles.barAction}>Mark read</Text>
          </Pressable>
          <Pressable testID="selection-delete" onPress={deleteSelected} hitSlop={6}>
            <Text style={[styles.barAction, styles.barActionDanger]}>Delete</Text>
          </Pressable>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: colors.background },
  list: { flex: 1, backgroundColor: colors.background },
  listContent: { padding: spacing.md, gap: spacing.sm },
  emptyContainer: { flexGrow: 1 },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.sm, backgroundColor: colors.background },
  emptyTitle: { ...typography.subtitle, color: colors.text },
  emptyText: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  headerActions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  headerAction: { ...typography.body, color: colors.primary, fontWeight: '600' },
  headerActionDanger: { color: colors.danger },
  row: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  rowUnread: { borderLeftWidth: 3, borderLeftColor: colors.primary },
  rowTicked: { backgroundColor: colors.primaryMuted },
  rowText: { flex: 1, gap: 2 },
  title: { ...typography.body, color: colors.text },
  titleUnread: { fontWeight: '700' },
  body: { ...typography.body, color: colors.textMuted },
  time: { ...typography.body, fontSize: 12, color: colors.textMuted, marginTop: 2 },
  dot: { width: 10, height: 10, borderRadius: 5, backgroundColor: colors.primary, marginTop: 6 },
  footer: { paddingVertical: spacing.md },
  selectionBar: {
    flexDirection: 'row',
    justifyContent: 'space-around',
    alignItems: 'center',
    paddingVertical: spacing.md,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: colors.border,
    backgroundColor: colors.surface,
  },
  barAction: { ...typography.body, color: colors.primary, fontWeight: '700' },
  barActionDanger: { color: colors.danger },
});
