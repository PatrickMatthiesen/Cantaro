export interface SnackbarNotification {
  message: string;
  variant: 'success' | 'error' | 'info';
}

export type ShowSnackbar = (notification: SnackbarNotification) => void;

const variantClass: Record<SnackbarNotification['variant'], string> = {
  success: 'border-success-border bg-success-surface text-success-content',
  error: 'border-danger-border bg-danger-surface text-danger-content',
  info: 'border-info-border bg-info-surface text-info-content',
};

export function Snackbar({ notification }: { notification: SnackbarNotification | null }) {
  if (!notification) {
    return null;
  }

  const { message, variant } = notification;

  return (
    <div className="pointer-events-none fixed right-4 bottom-4 z-50 flex max-w-[calc(100vw-2rem)] justify-end sm:right-6 sm:bottom-6">
      <div
        className={`pointer-events-auto min-w-64 rounded-2xl border px-4 py-3 text-sm font-medium shadow-[0_18px_55px_rgba(15,23,42,0.18)] backdrop-blur ${variantClass[variant]}`}
        role="status"
        aria-live="polite"
      >
        {message}
      </div>
    </div>
  );
}
