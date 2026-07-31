import { Link } from '@tanstack/react-router';
import type { ReactNode } from 'react';
import type { AppRouteTo } from '../routerTypes';

export interface PageNavigationItem {
  label: string;
  to: AppRouteTo;
  hash?: string;
  detail?: string;
  icon?: ReactNode;
  tone?: 'violet' | 'indigo' | 'sky' | 'red' | 'rose' | 'pink' | 'emerald' | 'amber' | 'slate';
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
  activeHash?: string;
  sections: PageNavigationSection[];
  subtitle: string;
  footer?: ReactNode;
}

function isPathActive(pathname: string, path: string): boolean {
  return pathname === path || pathname.startsWith(`${path}/`);
}

function isHashNavigationItemActive(pathname: string, item: PageNavigationItem, hash?: string): boolean {
  return pathname === item.to && hash === item.hash;
}

function isNavigationItemActive(pathname: string, item: PageNavigationItem, hash?: string): boolean {
  if (item.hash) return isHashNavigationItemActive(pathname, item, hash);
  if (item.exact) {
    return pathname === item.to;
  }

  if (item.matchPrefix) {
    return isPathActive(pathname, item.matchPrefix);
  }

  return isPathActive(pathname, item.to);
}

function NavigationIcon({ icon, label }: { icon?: ReactNode; label: string }) {
  return (
    <span className="app-side-nav-icon flex h-7 w-7 shrink-0 items-center justify-center rounded-xl text-violet-600 transition [&_svg]:h-5 [&_svg]:w-5">
      {icon ?? label.charAt(0)}
    </span>
  );
}

function NavigationItemContent({ item }: { item: PageNavigationItem }) {
  return (
    <>
      <NavigationIcon icon={item.icon} label={item.label} />
      <span className="min-w-0 flex-1">
        <span className="block truncate">{item.label}</span>
        {item.detail ? <span className="block truncate text-xs font-semibold text-slate-400">{item.detail}</span> : null}
      </span>
    </>
  );
}

function getNavigationItemClassName(item: PageNavigationItem, isActive: boolean): string {
  const itemTone = item.tone ?? 'violet';

  return `app-side-nav-item app-side-nav-item--${itemTone} group flex w-full items-center gap-3 rounded-2xl px-3 py-2.5 text-left text-sm font-semibold transition ${
    isActive ? 'app-side-nav-item--active text-content' : 'text-content-muted'
  }`;
}

function PageSideNavigationItem({
  activeHash,
  activePathname,
  item,
}: {
  activeHash?: string;
  activePathname: string;
  item: PageNavigationItem;
}) {
  const isActive = isNavigationItemActive(activePathname, item, activeHash);
  const itemClassName = getNavigationItemClassName(item, isActive);

  if (item.disabled) {
    return (
      <button
        key={`${item.to}-${item.label}`}
        type="button"
        className={`${itemClassName} cursor-not-allowed opacity-55`}
        disabled
      >
        <NavigationItemContent item={item} />
      </button>
    );
  }

  return (
    <Link
      key={`${item.to}-${item.label}`}
      to={item.to}
      hash={item.hash}
      params={item.params as never}
      search={item.search as never}
      className={itemClassName}
      aria-current={isActive ? 'page' : undefined}
    >
      <NavigationItemContent item={item} />
    </Link>
  );
}

export function PageSideNavigation({
  activeHash,
  activePathname,
  sections,
  subtitle,
  footer,
}: PageSideNavigationProps) {
  return (
    <aside className="app-sidebar hidden-scrollbar-until-hover sticky top-0 h-screen w-full overflow-y-auto border-r border-border-subtle bg-surface/55 px-5 py-6 shadow-[12px_0_40px_rgba(88,74,150,0.05)] backdrop-blur-xl">
      <div className="min-h-full pb-32">
        <Link to="/" className="flex items-center gap-3">
          <img
            src="/icon-192.png"
            alt=""
            className="h-12 w-12 rounded-2xl shadow-[0_12px_30px_rgba(124,92,255,0.24)]"
          />
          <div>
            <p className="text-lg font-black tracking-[0.04em] text-content">CANTARO</p>
            <p className="text-xs font-bold tracking-[0.32em] text-content-muted uppercase">{subtitle}</p>
          </div>
        </Link>

        <div className="mt-8 space-y-8">
          {sections.map((section) => (
            <section key={section.title}>
              <div className="mb-3 flex items-center justify-between gap-2">
                <h2 className="text-xs font-black tracking-[0.22em] text-content-muted uppercase">
                  {section.title}
                </h2>
                {section.titleAction ? (
                  <Link
                    to={section.titleAction.to}
                    params={section.titleAction.params as never}
                    search={section.titleAction.search as never}
                    className="flex h-7 items-center justify-center gap-1.5 rounded-xl px-2.5 text-[11px] leading-none font-black text-accent transition hover:bg-surface hover:text-accent-strong"
                    aria-label={section.titleAction.ariaLabel ?? section.titleAction.label}
                  >
                    {section.titleAction.icon ? <span className="shrink-0">{section.titleAction.icon}</span> : null}
                    {section.titleAction.label}
                  </Link>
                ) : null}
              </div>
              <div className="space-y-1.5">
                {section.items.map((item) => (
                  <PageSideNavigationItem
                    key={`${item.to}-${item.label}`}
                    activeHash={activeHash}
                    activePathname={activePathname}
                    item={item}
                  />
                ))}
              </div>
            </section>
          ))}
        </div>

        {footer ? <div className="mt-8">{footer}</div> : null}
      </div>
    </aside>
  );
}
