// Mirrors the backend Task entity's client-facing shape; extend as PlanFlow.Domain evolves.
export interface Task {
  id: string;
  title: string;
  deadline: string | null;
  urgencyScore: number;
  isBlocking: boolean;
}
