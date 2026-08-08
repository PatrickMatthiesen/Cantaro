export type ThresholdSubmissionDecision = 'submit' | 'paused' | 'stale';

export async function decideThresholdSubmission(
  isTrackingPaused: () => Promise<boolean>,
  isCurrentWatchPage: () => boolean,
): Promise<ThresholdSubmissionDecision> {
  if (await isTrackingPaused()) return 'paused';
  return isCurrentWatchPage() ? 'submit' : 'stale';
}
