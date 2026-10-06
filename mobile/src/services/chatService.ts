import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { http, API_BASE_URL } from './httpClient';
import { getFreshAccessToken, runInTeamContext } from './authService';
import type { ChatMessageDto } from '../types/chat';

export function getChatMessages(teamId: string): Promise<ChatMessageDto[]> {
  return runInTeamContext(teamId, async () => {
    const { data } = await http.get<ChatMessageDto[]>(`/api/teams/${teamId}/chat/messages`);
    return data;
  });
}

// One connection per ChatScreen mount; caller owns start/stop via useEffect cleanup.
export function createChatConnection(): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/chat`, { accessTokenFactory: getFreshAccessToken })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build();
}

// The hub puts the connection in the team's group from the token's team_id claim, so the token must
// be scoped to this team at the moment of the handshake (queued so other screens can't swap it).
export function startChatConnection(connection: HubConnection, teamId: string): Promise<void> {
  return runInTeamContext(teamId, () => connection.start());
}
