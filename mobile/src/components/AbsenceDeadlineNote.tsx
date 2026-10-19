import { StyleSheet, Text } from 'react-native';
import { useMyAbsences } from '../hooks/useMyAbsences';
import { colors, typography } from '../theme/theme';
import type { TaskDto } from '../types/task';
import { DEADLINE_IN_ABSENCE_TEXT, deadlineInAbsence } from '../utils/absences';

// "Deadline falls during your time off" - shown on a task of the signed-in user whose deadline lies inside one of their Out of office
// events. Its own component so the shared absence list is fetched only when such a task is on screen.
export function AbsenceDeadlineNote({ task, testID }: { task: TaskDto; testID?: string }) {
  const absences = useMyAbsences();
  if (!deadlineInAbsence(task, absences)) return null;
  return (
    <Text testID={testID} style={styles.note}>
      🏖 {DEADLINE_IN_ABSENCE_TEXT}
    </Text>
  );
}

const styles = StyleSheet.create({
  note: { ...typography.body, fontSize: 12, color: colors.urgencyHigh },
});
