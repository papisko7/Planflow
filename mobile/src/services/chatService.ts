import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { http, API_BASE_URL, getAccessToken } from './httpClient';
import { ensureTeamContext } from './authService';
import type { ChatMessageDto } from '../types/chat';

export async function getChatMessages(teamId: string): Promise<ChatMessageDto[]> {
  await ensureTeamContext(teamId);
  const { data } = await http.get<ChatMessageDto[]>(`/api/teams/${teamId}/chat/messages`);
  return data;
}

// One connection per ChatScreen mount; caller owns start/stop via useEffect cleanup.
export function createChatConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/chat`, { accessTokenFactory: () => getAccessToken() ?? '' })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}
