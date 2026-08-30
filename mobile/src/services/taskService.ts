import { http } from './httpClient';
import type { CreateTaskRequest, TaskDetailDto, TaskDto, UpdateTaskRequest } from '../types/task';

export async function getTeamTasks(teamId: string): Promise<TaskDto[]> {
  const { data } = await http.get<TaskDto[]>(`/api/teams/${teamId}/tasks`);
  return data;
}

export async function createTask(teamId: string, request: CreateTaskRequest): Promise<TaskDto> {
  const { data } = await http.post<TaskDto>(`/api/teams/${teamId}/tasks`, request);
  return data;
}

export async function getTask(taskId: string): Promise<TaskDetailDto> {
  const { data } = await http.get<TaskDetailDto>(`/api/tasks/${taskId}`);
  return data;
}

export async function updateTask(taskId: string, request: UpdateTaskRequest): Promise<TaskDto> {
  const { data } = await http.put<TaskDto>(`/api/tasks/${taskId}`, request);
  return data;
}

export async function deleteTask(taskId: string): Promise<void> {
  await http.delete(`/api/tasks/${taskId}`);
}
