// Mirrors PlanFlow.Application.Users.Common.UserProfileDto
export interface UserProfileDto {
  id: string;
  email: string;
  displayName: string;
  // False until the owner types the code from the confirmation e-mail; optional mails only reach confirmed addresses.
  emailVerified: boolean;
}

// Mirrors PlanFlow.Api.Contracts.UpdateProfileRequest; currentPassword is only needed when the e-mail changes.
export interface UpdateProfileRequest {
  displayName: string;
  email: string;
  currentPassword: string | null;
}
