import { useRef, type ReactNode } from 'react';
import { Animated, PanResponder, StyleSheet, Text, View } from 'react-native';
import { colors, radius, spacing } from '../theme/theme';

// How far (px) a row must be dragged before releasing triggers the action.
const TRIGGER_DISTANCE = 90;
const MAX_DISTANCE = 130;

type Props = {
  children: ReactNode;
  // Swipe right -> reveals the red "Delete" action.
  onSwipeRight?: () => void;
  // Swipe left -> reveals the blue "Edit" action.
  onSwipeLeft?: () => void;
  rightLabel?: string;
  leftLabel?: string;
};

// Plain React Native gesture (PanResponder + Animated), no extra native library: the row follows the
// finger, and releasing past TRIGGER_DISTANCE fires the action; the row always springs back afterwards,
// so the list itself is only changed once the action really succeeds. Mostly-vertical drags are ignored
// so scrolling the list is unaffected.
export function SwipeableRow({ children, onSwipeRight, onSwipeLeft, rightLabel = 'Delete', leftLabel = 'Edit' }: Props) {
  const x = useRef(new Animated.Value(0)).current;
  // The PanResponder is created once, so it reads the latest callbacks through a ref.
  const handlers = useRef({ onSwipeRight, onSwipeLeft });
  handlers.current = { onSwipeRight, onSwipeLeft };

  const springBack = () => Animated.spring(x, { toValue: 0, useNativeDriver: true, bounciness: 0 }).start();

  const pan = useRef(
    PanResponder.create({
      onMoveShouldSetPanResponder: (_, g) => Math.abs(g.dx) > 12 && Math.abs(g.dx) > Math.abs(g.dy) * 2,
      onPanResponderTerminationRequest: () => false,
      onPanResponderMove: (_, g) => {
        const allowRight = !!handlers.current.onSwipeRight;
        const allowLeft = !!handlers.current.onSwipeLeft;
        const dx = Math.min(MAX_DISTANCE, Math.max(-MAX_DISTANCE, g.dx));
        x.setValue((dx > 0 && allowRight) || (dx < 0 && allowLeft) ? dx : 0);
      },
      onPanResponderRelease: (_, g) => {
        if (g.dx > TRIGGER_DISTANCE) handlers.current.onSwipeRight?.();
        else if (g.dx < -TRIGGER_DISTANCE) handlers.current.onSwipeLeft?.();
        springBack();
      },
      onPanResponderTerminate: springBack,
    }),
  ).current;

  const rightOpacity = x.interpolate({ inputRange: [0, TRIGGER_DISTANCE], outputRange: [0.35, 1], extrapolate: 'clamp' });
  const leftOpacity = x.interpolate({ inputRange: [-TRIGGER_DISTANCE, 0], outputRange: [1, 0.35], extrapolate: 'clamp' });

  return (
    <View>
      {onSwipeRight ? (
        <Animated.View style={[styles.action, styles.deleteAction, { opacity: rightOpacity }]}>
          <Text style={styles.actionText}>{rightLabel}</Text>
        </Animated.View>
      ) : null}
      {onSwipeLeft ? (
        <Animated.View style={[styles.action, styles.editAction, { opacity: leftOpacity }]}>
          <Text style={styles.actionText}>{leftLabel}</Text>
        </Animated.View>
      ) : null}
      <Animated.View style={{ transform: [{ translateX: x }] }} {...pan.panHandlers}>
        {children}
      </Animated.View>
    </View>
  );
}

const styles = StyleSheet.create({
  action: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    left: 0,
    right: 0,
    borderRadius: radius.md,
    justifyContent: 'center',
    paddingHorizontal: spacing.lg,
  },
  deleteAction: { backgroundColor: colors.danger, alignItems: 'flex-start' },
  editAction: { backgroundColor: colors.primary, alignItems: 'flex-end' },
  actionText: { color: colors.surface, fontWeight: '700' },
});
