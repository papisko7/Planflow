// Mirrors backend PlanFlow.Domain.Enums.ItemListKind (plain int on the wire): what one of the team's own lists holds.
export enum ItemListKind {
  TaskList = 0,
  MeetingList = 1,
}

// Mirrors PlanFlow.Application.ItemLists.ItemListDto: a list of the team's own (not a Google Tasks list).
export interface ItemListDto {
  id: string;
  teamId: string;
  name: string;
  kind: ItemListKind;
  createdByUserId: string | null;
}

// Mirrors PlanFlow.Application.Tasks.Commands.BulkTasks.BulkTaskResult: what a bulk action changed and what it left alone.
export interface BulkResult {
  succeeded: number;
  skippedIds: string[];
}
