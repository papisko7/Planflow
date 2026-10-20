// The explanations shown under the captions of the fields that feed the Urgency Score, so the create and edit forms say the same thing.

export const IMPACT_LABEL = 'Impact (0-10)';
export const IMPACT_HINT = 'How much it matters, from 0 (hardly) to 10 (critical). Empty = not rated, so it does not count in the score.';

export const EFFORT_LABEL = 'Estimated effort (optional)';
export const TASK_EFFORT_HINT = 'How long the work takes. The deadline is counted back by this much, so a bigger effort makes the task urgent sooner.';
export const EVENT_EFFORT_HINT = 'Work to do before it starts (preparation, travel). Urgency counts back from the start by this much.';
