import { GlassCard } from '@cantaro/client-shared/ui';

export function MusicPlaceholder() {
  return (
    <GlassCard className="mx-auto max-w-4xl p-5">
      <p className="text-xs font-semibold text-violet-700">Music</p>
      <h2 className="mt-1 text-xl font-bold text-gray-900">Reserved for the music workflow</h2>
      <p className="mt-2 text-sm text-gray-600">
        This tab is intentionally untouched for now. The popup shell is ready for it, but no music UI changes were added here.
      </p>
    </GlassCard>
  );
}
