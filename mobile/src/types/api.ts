export interface ApiError {
  status: number | null;
  message: string;
  isNetworkError: boolean;
}
