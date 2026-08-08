import type { PopupNotice } from './extensionAppTypes';

export function StatusToast({ notice }: { notice: PopupNotice | null }) {
  if (!notice) return null;

  return (
    <div className="pointer-events-none absolute right-4 bottom-4 z-50">
      <div
        className={`rounded-xl border px-4 py-3 text-sm font-medium shadow-md ${notice.tone === 'success'
          ? 'border-success-border bg-success-surface text-success-content'
          : 'border-danger-border bg-danger-surface text-danger-content'}`}
        role="status"
        aria-live="polite"
      >
        {notice.message}
      </div>
    </div>
  );
}
