import { http } from './httpClient';
import { loadSession } from './tokenStorage';
import type { UpdateProfileRequest, UserProfileDto } from '../types/user';

export async function getProfile(): Promise<UserProfileDto> {
  const { data } = await http.get<UserProfileDto>('/api/users/me');
  return data;
}

export async function updateProfile(request: UpdateProfileRequest): Promise<UserProfileDto> {
  const { data } = await http.put<UserProfileDto>('/api/users/me', request);
  return data;
}

// Changes the password of the signed-in user. This device's own refresh token is sent along so the server keeps this
// device signed in and signs out all the others.
export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const session = await loadSession();
  await http.post('/api/users/me/change-password', {
    currentPassword,
    newPassword,
    refreshToken: session?.refreshToken ?? null,
  });
}

// Confirms the e-mail address with the 6-digit code from the confirmation e-mail.
export async function verifyEmail(code: string): Promise<void> {
  await http.post('/api/users/me/verify-email', { code });
}

// Mails a fresh code (the server ignores a request that comes right after the previous one).
export async function resendEmailVerification(): Promise<void> {
  await http.post('/api/users/me/resend-verification');
}
