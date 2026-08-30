// Standardized shape every service call throws on failure, so screens can render
// one error path regardless of whether the failure was network, 4xx, or 5xx.
export interface ApiError {
  status: number | null;
  message: string;
  isNetworkError: boolean;
}
