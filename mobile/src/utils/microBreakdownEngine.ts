import type { TaskDto } from '../types/task';

export interface MicroStep {
  id: string;
  text: string;
}

interface Template {
  id: string;
  keywords: string[];
  steps: string[];
}

// Static template catalog: title/description keywords route to a fixed 3-step atomic plan.
// No generative call — matching is a simple substring lookup, so it's O(templates) and instant.
const TEMPLATES: Template[] = [
  {
    id: 'research',
    keywords: ['research', 'study', 'read', 'learn', 'review', 'literature', 'article', 'paper'],
    steps: [
      'Open the primary document or URL',
      'Skim headings and subheadings for 3 minutes',
      'Write a 1-sentence goal statement for this session',
    ],
  },
  {
    id: 'coding',
    keywords: ['code', 'implement', 'fix', 'bug', 'refactor', 'test', 'api', 'build', 'develop', 'debug', 'feature'],
    steps: [
      'Locate the target source file',
      'Write a failing test or draft the function signature',
      'Run the build to verify the setup',
    ],
  },
];

const HIGH_IMPACT_STEPS = [
  'Clear your desktop / workspace',
  'Write down the immediate first step',
  'Work strictly for 5 minutes without checking notifications',
];

const FALLBACK_STEPS = ['Set up your environment', 'Execute the core 5-minute action', 'Review your progress'];

const HIGH_IMPACT_THRESHOLD = 7;

function matchTemplateSteps(task: TaskDto): string[] {
  const haystack = `${task.title} ${task.description ?? ''}`.toLowerCase();

  for (const template of TEMPLATES) {
    if (template.keywords.some((keyword) => haystack.includes(keyword))) {
      return template.steps;
    }
  }

  if (task.impactScore >= HIGH_IMPACT_THRESHOLD) {
    return HIGH_IMPACT_STEPS;
  }

  return FALLBACK_STEPS;
}

export function generateMicroBreakdown(task: TaskDto): MicroStep[] {
  return matchTemplateSteps(task).map((text, index) => ({
    id: `${task.id}-step-${index + 1}`,
    text,
  }));
}
