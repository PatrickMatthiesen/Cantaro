import { GlassCard } from '../cantaroWebUi';

export function MusicPlaceholder() {
  return (
    <GlassCard className="mx-auto max-w-4xl p-8">
      <p className="text-xs tracking-[0.32em] text-gray-500 uppercase">Music</p>
      <h2 className="mt-3 text-3xl font-bold text-gray-900">Reserved for the music workflow</h2>
      <p className="mt-3 text-sm text-gray-600">
        This tab is intentionally untouched for now. The popup shell is ready for it, but no music UI changes were added here.
      </p>
    </GlassCard>
  );
}
