import { useCallback, useLayoutEffect, useRef, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useFocusEffect, useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { getInbox, markAllNotificationsRead, markNotificationRead } from '../services/notificationService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { notificationIcon, timeAgo } from '../utils/notificationDisplay';
import type { RootStackParamList } from '../navigation/types';
import type { ApiError } from '../types/api';
import type { InboxNotification } from '../types/notification';

const PAGE_SIZE = 30;

// The in-app inbox: everything PlanFlow told you, newest first. Tapping an entry marks it read and opens the task it is about.
export default function NotificationsScreen() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const [items, setItems] = useState<InboxNotification[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [hasMore, setHasMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const loadingMoreRef = useRef(false);

  const loadFirstPage = useCallback(async () => {
    try {
      const page = await getInbox(undefined, PAGE_SIZE);
      setItems(page);
      setHasMore(page.length === PAGE_SIZE);
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

  const markAllRead = useCallback(async () => {
    setItems((current) => current.map((n) => ({ ...n, isRead: true })));
    try {
      await markAllNotificationsRead();
    } catch {
      void loadFirstPage(); // the server refused: show the truth again
    }
  }, [loadFirstPage]);

  const hasUnread = items.some((n) => !n.isRead);
  useLayoutEffect(() => {
    navigation.setOptions({
      headerRight: () =>
        hasUnread ? (
          <Pressable testID="mark-all-read" onPress={markAllRead} hitSlop={8}>
            <Text style={styles.headerAction}>Mark all read</Text>
          </Pressable>
        ) : null,
    });
  }, [navigation, hasUnread, markAllRead]);

  const open = (notification: InboxNotification) => {
    if (!notification.isRead) {
      setItems((current) => current.map((n) => (n.id === notification.id ? { ...n, isRead: true } : n)));
      markNotificationRead(notification.id).catch(() => undefined);
    }
    if (notification.taskId && notification.teamId) {
      navigation.navigate('TaskDetail', { taskId: notification.taskId, teamId: notification.teamId });
    }
  };

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  return (
    <FlatList
      testID="notifications-list"
      style={styles.list}
      contentContainerStyle={items.length === 0 ? styles.emptyContainer : styles.listContent}
      data={items}
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
        return (
          <Pressable testID={`notification-${item.id}`} style={[styles.row, item.isRead ? null : styles.rowUnread]} onPress={() => open(item)}>
            <Ionicons name={icon.name} size={24} color={icon.color} />
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
        );
      }}
    />
  );
}

const styles = StyleSheet.create({
  list: { flex: 1, backgroundColor: colors.background },
  listContent: { padding: spacing.md, gap: spacing.sm },
  emptyContainer: { flexGrow: 1 },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.sm, backgroundColor: colors.background },
  emptyTitle: { ...typography.subtitle, color: colors.text },
  emptyText: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  headerAction: { ...typography.body, color: colors.primary, fontWeight: '600' },
  row: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.md,
    backgroundColor: colors.surface,
    borderRadius: radius.md,
    padding: spacing.md,
  },
  rowUnread: { borderLeftWidth: 3, borderLeftColor: colors.primary },
  rowText: { flex: 1, gap: 2 },
  title: { ...typography.body, color: colors.text },
  titleUnread: { fontWeight: '700' },
  body: { ...typography.body, color: colors.textMuted },
  time: { ...typography.body, fontSize: 12, color: colors.textMuted, marginTop: 2 },
  dot: { width: 10, height: 10, borderRadius: 5, backgroundColor: colors.primary, marginTop: 6 },
  footer: { paddingVertical: spacing.md },
});
