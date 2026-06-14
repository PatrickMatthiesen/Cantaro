import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import type { AppRouteTo } from '../routerTypes';

export interface PageNavigationItem {
  label: string;
  to: AppRouteTo;
  detail?: string;
  icon?: ReactNode;
  exact?: boolean;
  matchPrefix?: string;
  params?: Record<string, string>;
  search?: Record<string, unknown>;
  disabled?: boolean;
}

export interface PageNavigationSection {
  title: string;
  titleAction?: {
    label: string;
    to: AppRouteTo;
    icon?: ReactNode;
    params?: Record<string, string>;
    search?: Record<string, unknown>;
    ariaLabel?: string;
  };
  items: PageNavigationItem[];
}

interface PageSideNavigationProps {
  activePathname: string;
  sections: PageNavigationSection[];
  subtitle: string;
  footer?: ReactNode;
}

function isNavigationItemActive(pathname: string, item: PageNavigationItem): boolean {
  if (item.exact) {
    return pathname === item.to;
  }

  if (item.matchPrefix) {
    return pathname === item.matchPrefix || pathname.startsWith(`${item.matchPrefix}/`);
  }

  return pathname === item.to || pathname.startsWith(`${item.to}/`);
}

function NavigationIcon({ icon, label }: { icon?: ReactNode; label: string }) {
  return (
    <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-xl border border-[#e3def8] bg-white/55 text-xs font-black text-violet-600">
      {icon ?? label.charAt(0)}
    </span>
  );
}

export function PageSideNavigation({
  activePathname,
  sections,
  subtitle,
  footer,
}: PageSideNavigationProps) {
  return (
    <aside className="hidden-scrollbar-until-hover sticky top-0 h-screen w-full overflow-y-auto border-r border-[#e8e4fb] bg-white/55 px-5 py-6 shadow-[12px_0_40px_rgba(88,74,150,0.05)] backdrop-blur-xl">
      <div className="min-h-full pb-32">
        <Link to="/" className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-violet-600 text-lg font-black text-white shadow-[0_12px_30px_rgba(124,92,255,0.3)]">
            C
          </div>
          <div>
            <p className="text-lg font-black tracking-[0.04em] text-slate-950">CANTARO</p>
            <p className="text-xs font-bold tracking-[0.32em] text-slate-500 uppercase">{subtitle}</p>
          </div>
        </Link>

        <div className="mt-8 space-y-8">
          {sections.map((section) => (
            <section key={section.title}>
              <div className="mb-3 flex items-center justify-between gap-2">
                <h2 className="text-xs font-black tracking-[0.22em] text-slate-500 uppercase">
                  {section.title}
                </h2>
                {section.titleAction ? (
                  <Link
                    to={section.titleAction.to}
                    params={section.titleAction.params as never}
                    search={section.titleAction.search as never}
                    className="flex h-7 items-center justify-center gap-1.5 rounded-xl px-2.5 text-[11px] leading-none font-black text-violet-600 transition hover:bg-white hover:text-violet-500"
                    aria-label={section.titleAction.ariaLabel ?? section.titleAction.label}
                  >
                    {section.titleAction.icon ? <span className="shrink-0">{section.titleAction.icon}</span> : null}
                    {section.titleAction.label}
                  </Link>
                ) : null}
              </div>
              <div className="space-y-1.5">
                {section.items.map((item) => {
                  const isActive = isNavigationItemActive(activePathname, item);
                  const itemClassName = `group flex w-full items-center gap-3 rounded-2xl px-3 py-2.5 text-left text-sm font-semibold transition ${
                    isActive ? 'bg-white text-slate-950 shadow-[0_12px_34px_rgba(88,74,150,0.08)]' : 'text-slate-700 hover:bg-white'
                  }`;

                  if (item.disabled) {
                    return (
                      <button
                        key={`${item.to}-${item.label}`}
                        type="button"
                        className={`${itemClassName} cursor-not-allowed opacity-55`}
                        disabled
                      >
                        <NavigationIcon icon={item.icon} label={item.label} />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate">{item.label}</span>
                          {item.detail ? <span className="block truncate text-xs font-semibold text-slate-400">{item.detail}</span> : null}
                        </span>
                      </button>
                    );
                  }

                  return (
                    <Link
                      key={`${item.to}-${item.label}`}
                      to={item.to}
                      params={item.params as never}
                      search={item.search as never}
                      className={itemClassName}
                      aria-current={isActive ? 'page' : undefined}
                    >
                      <NavigationIcon icon={item.icon} label={item.label} />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate">{item.label}</span>
                        {item.detail ? <span className="block truncate text-xs font-semibold text-slate-400">{item.detail}</span> : null}
                      </span>
                    </Link>
                  );
                })}
              </div>
            </section>
          ))}
        </div>

        {footer ? <div className="mt-8">{footer}</div> : null}
      </div>
    </aside>
  );
}
