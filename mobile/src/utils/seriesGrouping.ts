import { TaskType, type TaskDto } from '../types/task';
import { isPastEvent } from './schedule';
import { isFinishedStatus } from './taskStatus';

// Google hands a repeating event over as one row per occurrence, so a weekly meeting is a dozen cards and a yearly birthday used to be
// a hundred. Lists that are about "what is there" (Items, the Dashboard, the "Blocked by" choices) show ONE entry per series instead:
// the next occurrence that is still open, or - when the whole series is over - the latest one. The entry carries `seriesMore`, how many
// other occurrences it stands for. The Agenda is about days, so it keeps every occurrence on its own day and does not use this.
export function collapseSeries<T extends TaskDto>(items: T[], now: Date = new Date()): T[] {
  const bySeries = new Map<string, T[]>();
  for (const item of items) {
    if (item.taskType === TaskType.Event && item.seriesId) {
      const group = bySeries.get(item.seriesId);
      if (group) group.push(item);
      else bySeries.set(item.seriesId, [item]);
    }
  }

  const start = (t: T) => new Date(t.startUtc ?? t.createdAtUtc).getTime();
  const shown = new Map<string, T>(); // id of the entry that stands for its series -> that entry with the count
  for (const group of bySeries.values()) {
    if (group.length < 2) continue;
    const ahead = group.filter((t) => !isPastEvent(t, now));
    const open = ahead.filter((t) => !isFinishedStatus(t.status));
    const representative =
      [...(open.length > 0 ? open : ahead)].sort((a, b) => start(a) - start(b))[0] ?? [...group].sort((a, b) => start(b) - start(a))[0];
    shown.set(representative.id, { ...representative, seriesMore: group.length - 1 });
  }

  const hidden = new Set<string>();
  for (const group of bySeries.values()) {
    if (group.length < 2) continue;
    for (const t of group) hidden.add(t.id);
  }
  // The original order is kept; each series appears where its entry was.
  return items.flatMap((item) => (!hidden.has(item.id) ? [item] : shown.has(item.id) ? [shown.get(item.id)!] : []));
}

// "+3 more" for the card of a series entry.
export function formatSeriesMore(more: number | undefined): string | null {
  return more && more > 0 ? `+${more} more` : null;
}
