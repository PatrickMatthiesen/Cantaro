export interface SnackbarNotification {
  message: string;
  variant: 'success' | 'error' | 'info';
}

export type ShowSnackbar = (notification: SnackbarNotification) => void;

const variantClass: Record<SnackbarNotification['variant'], string> = {
  success: 'border-emerald-200 bg-emerald-50 text-emerald-900',
  error: 'border-rose-200 bg-rose-50 text-rose-900',
  info: 'border-sky-200 bg-sky-50 text-sky-900',
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
