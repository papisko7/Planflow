export interface ChatMessageDto {
  id: string;
  teamId: string;
  senderUserId: string;
  senderDisplayName: string;
  content: string;
  createdAtUtc: string;
}
