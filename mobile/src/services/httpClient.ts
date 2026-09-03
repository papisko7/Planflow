import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import type { ApiError } from '../types/api';

export const API_BASE_URL = process.env.EXPO_PUBLIC_API_URL ?? 'http://localhost:5000';

export const http = axios.create({ baseURL: API_BASE_URL });

let accessToken: string | null = null;

// authService wires this in at module load to avoid a circular import between the two files;
// it attempts a token refresh and returns the new access token, or null if the session is dead.
type UnauthorizedHandler = () => Promise<string | null>;
let onUnauthorized: UnauthorizedHandler | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

// Read by ChatScreen's SignalR `accessTokenFactory`, which can't use the axios interceptor.
export function getAccessToken(): string | null {
  return accessToken;
}

export function setUnauthorizedHandler(handler: UnauthorizedHandler | null): void {
  onUnauthorized = handler;
}

http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  if (accessToken) {
    config.headers.set('Authorization', `Bearer ${accessToken}`);
  }
  return config;
});

function toApiError(error: AxiosError): ApiError {
  if (!error.response) {
    return { status: null, message: 'Network request failed. Check your connection.', isNetworkError: true };
  }
  const data = error.response.data as { message?: string; title?: string } | undefined;
  return {
    status: error.response.status,
    message: data?.message ?? data?.title ?? error.message,
    isNetworkError: false,
  };
}

http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;

    if (error.response?.status === 401 && originalRequest && !originalRequest._retried && onUnauthorized) {
      originalRequest._retried = true;
      const newToken = await onUnauthorized();
      if (newToken) {
        originalRequest.headers.set('Authorization', `Bearer ${newToken}`);
        return http.request(originalRequest);
      }
    }

    return Promise.reject(toApiError(error));
  },
);
