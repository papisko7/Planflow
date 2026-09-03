import { useCallback, useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  FlatList,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  View,
} from 'react-native';
import { useRoute, type RouteProp } from '@react-navigation/native';
import type { HubConnection } from '@microsoft/signalr';
import { createChatConnection, getChatMessages } from '../services/chatService';
import { getCurrentUserId } from '../services/authService';
import { colors, radius, spacing, typography } from '../theme/theme';
import type { RootStackParamList } from '../navigation/types';
import type { ChatMessageDto } from '../types/chat';

type LoadState = 'loading' | 'ready' | 'error';

// Typing indicator UI is cleared this long after the last UserTyping(true) event, in case a
// stop event never arrives (e.g. sender's connection drops mid-type).
const TYPING_TIMEOUT_MS = 3000;
const TYPING_DEBOUNCE_MS = 400;

export default function ChatScreen() {
  const route = useRoute<RouteProp<RootStackParamList, 'Chat'>>();
  const { teamId } = route.params;

  const [state, setState] = useState<LoadState>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessageDto[]>([]);
  const [draft, setDraft] = useState('');
  const [typingUser, setTypingUser] = useState<string | null>(null);

  const connectionRef = useRef<HubConnection | null>(null);
  const typingClearTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const typingSendTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const listRef = useRef<FlatList<ChatMessageDto>>(null);
  const currentUserId = getCurrentUserId();

  useEffect(() => {
    let cancelled = false;

    async function connect() {
      try {
        const history = await getChatMessages(teamId);
        if (cancelled) return;
        setMessages(history.slice().reverse());
        setState('ready');

        const connection = createChatConnection();
        connectionRef.current = connection;

        connection.on('ReceiveMessage', (message: ChatMessageDto) => {
          setMessages((current) => [...current, message]);
        });

        connection.on('UserTyping', (userId: string, isTyping: boolean) => {
          if (userId === currentUserId) return;
          if (typingClearTimer.current) clearTimeout(typingClearTimer.current);
          if (isTyping) {
            setTypingUser(userId);
            typingClearTimer.current = setTimeout(() => setTypingUser(null), TYPING_TIMEOUT_MS);
          } else {
            setTypingUser(null);
          }
        });

        await connection.start();
      } catch {
        if (!cancelled) {
          setErrorMessage("Couldn't load chat.");
          setState('error');
        }
      }
    }

    connect();

    return () => {
      cancelled = true;
      if (typingClearTimer.current) clearTimeout(typingClearTimer.current);
      if (typingSendTimer.current) clearTimeout(typingSendTimer.current);
      connectionRef.current?.stop();
      connectionRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [teamId]);

  const onChangeDraft = useCallback(
    (text: string) => {
      setDraft(text);
      if (typingSendTimer.current) clearTimeout(typingSendTimer.current);
      typingSendTimer.current = setTimeout(() => {
        connectionRef.current?.invoke('TypingIndicator', teamId, text.length > 0).catch(() => {});
      }, TYPING_DEBOUNCE_MS);
    },
    [teamId],
  );

  const onSend = useCallback(async () => {
    const content = draft.trim();
    if (!content || !connectionRef.current) return;
    setDraft('');
    try {
      await connectionRef.current.invoke('SendMessage', teamId, content);
      await connectionRef.current.invoke('TypingIndicator', teamId, false);
    } catch {
      setErrorMessage('Message failed to send.');
    }
  }, [draft, teamId]);

  if (state === 'loading') {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  if (state === 'error') {
    return (
      <View style={styles.centered}>
        <Text style={styles.title}>Chat unavailable</Text>
        <Text style={styles.subtitle}>{errorMessage}</Text>
      </View>
    );
  }

  return (
    <KeyboardAvoidingView
      style={styles.flex}
      behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      keyboardVerticalOffset={Platform.OS === 'ios' ? 90 : 0}
    >
      <FlatList
        ref={listRef}
        style={styles.list}
        contentContainerStyle={styles.listContent}
        data={messages}
        keyExtractor={(item) => item.id}
        onContentSizeChange={() => listRef.current?.scrollToEnd({ animated: true })}
        ListEmptyComponent={
          <View style={styles.centered}>
            <Text style={styles.subtitle}>No messages yet. Say hello.</Text>
          </View>
        }
        renderItem={({ item }) => {
          const isMine = item.senderUserId === currentUserId;
          return (
            <View style={[styles.bubble, isMine ? styles.bubbleMine : styles.bubbleOther]}>
              {!isMine ? <Text style={styles.sender}>{item.senderDisplayName}</Text> : null}
              <Text style={styles.bubbleText}>{item.content}</Text>
              <Text style={styles.timestamp}>
                {new Date(item.createdAtUtc).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}
              </Text>
            </View>
          );
        }}
      />
      {typingUser ? <Text style={styles.typingIndicator}>Someone is typing…</Text> : null}
      <View style={styles.inputBar}>
        <TextInput
          testID="chat-input"
          style={styles.input}
          placeholder="Message"
          placeholderTextColor={colors.textMuted}
          value={draft}
          onChangeText={onChangeDraft}
          multiline
        />
        <Pressable testID="chat-send-button" style={styles.sendButton} onPress={onSend} disabled={!draft.trim()}>
          <Text style={styles.sendButtonText}>Send</Text>
        </Pressable>
      </View>
    </KeyboardAvoidingView>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: colors.background },
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.lg, gap: spacing.xs },
  title: { ...typography.title, color: colors.text },
  subtitle: { ...typography.body, color: colors.textMuted, textAlign: 'center' },
  list: { flex: 1 },
  listContent: { padding: spacing.md, gap: spacing.sm, flexGrow: 1 },
  bubble: {
    maxWidth: '80%',
    borderRadius: radius.md,
    padding: spacing.sm,
    marginBottom: spacing.sm,
  },
  bubbleMine: { alignSelf: 'flex-end', backgroundColor: colors.primaryMuted },
  bubbleOther: { alignSelf: 'flex-start', backgroundColor: colors.surface, borderWidth: 1, borderColor: colors.border },
  sender: { ...typography.body, color: colors.primary, fontWeight: '700', fontSize: 12 },
  bubbleText: { ...typography.body, color: colors.text },
  timestamp: { ...typography.body, color: colors.textMuted, fontSize: 10, marginTop: 2, alignSelf: 'flex-end' },
  typingIndicator: {
    ...typography.body,
    color: colors.textMuted,
    fontSize: 12,
    paddingHorizontal: spacing.md,
    paddingBottom: spacing.xs,
  },
  inputBar: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: spacing.sm,
    padding: spacing.md,
    backgroundColor: colors.surface,
    borderTopWidth: 1,
    borderTopColor: colors.border,
  },
  input: {
    flex: 1,
    backgroundColor: colors.background,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: radius.md,
    padding: spacing.sm,
    color: colors.text,
    maxHeight: 100,
  },
  sendButton: {
    backgroundColor: colors.primary,
    borderRadius: radius.sm,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
  },
  sendButtonText: { color: colors.surface, fontWeight: '700' },
});
