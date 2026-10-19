export enum ItemListKind {
  TaskList = 0,
  MeetingList = 1,
}

export interface ItemListDto {
  id: string;
  teamId: string;
  name: string;
  kind: ItemListKind;
  createdByUserId: string | null;
}

export interface BulkResult {
  succeeded: number;
  skippedIds: string[];
}
