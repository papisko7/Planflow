import { useEffect, useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';
import { getProfile, updateCalendarSharing } from '../services/userService';
import { colors, radius, spacing, typography } from '../theme/theme';
import { CalendarSharingLevel } from '../types/availability';
import type { ApiError } from '../types/api';
import { SHARING_HELP, SHARING_LABEL } from '../utils/availability';
import { AccordionSection } from './AccordionSection';

const LEVELS = [CalendarSharingLevel.Off, CalendarSharingLevel.Availability, CalendarSharingLevel.Details];

// How much of the personal (Google) calendar teammates may see. The choice applies to every team, and the server trims the data
// whenever a teammate asks, so a hidden title never leaves the server.
export function CalendarSharingCard() {
  const [level, setLevel] = useState<CalendarSharingLevel | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getProfile()
      .then((profile) => setLevel(profile.calendarSharingLevel ?? CalendarSharingLevel.Availability))
      .catch((err: ApiError) => setError(err.message ?? 'Failed to load the setting.'));
  }, []);

  const choose = async (next: CalendarSharingLevel) => {
    if (saving || next === level) return;
    const previous = level;
    setLevel(next);
    setSaving(true);
    setError(null);
    try {
      await updateCalendarSharing(next);
    } catch (err) {
      setLevel(previous);
      setError((err as ApiError).message ?? 'Failed to save the setting.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <AccordionSection testID="calendar-sharing" title="Calendar sharing" summary={level === null ? undefined : SHARING_LABEL[level]}>
      {level === null && !error ? <ActivityIndicator color={colors.primary} /> : null}
      {level !== null ? (
        <View style={styles.options}>
          {LEVELS.map((option) => (
            <Pressable
              key={option}
              testID={`sharing-option-${option}`}
              onPress={() => choose(option)}
              style={[styles.option, level === option ? styles.optionActive : null]}
            >
              <Text style={level === option ? styles.optionTitleActive : styles.optionTitle}>{SHARING_LABEL[option]}</Text>
              <Text style={styles.optionHelp}>{SHARING_HELP[option]}</Text>
            </Pressable>
          ))}
        </View>
      ) : null}
      {error ? <Text style={styles.error}>{error}</Text> : null}
    </AccordionSection>
  );
}

const styles = StyleSheet.create({
  options: { gap: spacing.xs },
  option: { borderWidth: 1, borderColor: colors.border, borderRadius: radius.md, padding: spacing.sm, gap: 2 },
  optionActive: { backgroundColor: colors.primaryMuted, borderColor: colors.primary },
  optionTitle: { ...typography.subtitle, color: colors.text },
  optionTitleActive: { ...typography.subtitle, color: colors.primary, fontWeight: '700' },
  optionHelp: { ...typography.body, fontSize: 12, color: colors.textMuted },
  error: { ...typography.body, color: colors.danger },
});
