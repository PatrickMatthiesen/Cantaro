import { Link } from '@tanstack/react-router';
import type { AppRouteTo } from '../routerTypes';

export interface SubjectNavItem {
  label: string;
  to: AppRouteTo;
}

export function SubjectNav({ label, items }: { label: string; items: SubjectNavItem[] }) {
  return (
    <nav
      className="inline-flex flex-wrap gap-2 rounded-2xl border border-white/80 bg-white/60 p-2 shadow-[0_8px_24px_rgba(31,41,55,0.05)] backdrop-blur"
      aria-label={label}
    >
      {items.map((item) => (
        <Link
          key={item.to}
          to={item.to}
          className="rounded-xl px-4 py-2 text-sm font-semibold text-gray-700 transition hover:bg-white"
          activeProps={{
            className: 'rounded-xl bg-gray-900 px-4 py-2 text-sm font-semibold text-white shadow-sm',
          }}
        >
          {item.label}
        </Link>
      ))}
    </nav>
  );
}
