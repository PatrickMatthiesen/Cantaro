import { Link } from "@tanstack/react-router";
import type { ReactNode } from "react";
import type { AppRouteTo } from "../routerTypes";

export interface PageNavigationItem {
  label: string;
  to: AppRouteTo;
  hash?: string;
  detail?: string;
  icon?: ReactNode;
  tone?:
    | "violet"
    | "indigo"
    | "sky"
    | "red"
    | "rose"
    | "pink"
    | "emerald"
    | "amber"
    | "slate";
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

function isHashNavigationItemActive(
  pathname: string,
  item: PageNavigationItem,
  hash?: string,
): boolean {
  return pathname === item.to && hash === item.hash;
}

function isNavigationItemActive(
  pathname: string,
  item: PageNavigationItem,
  hash?: string,
): boolean {
  if (item.hash) return isHashNavigationItemActive(pathname, item, hash);
  if (item.exact) {
    return pathname === item.to;
  }

  if (item.matchPrefix) {
    return isPathActive(pathname, item.matchPrefix);
  }

  return isPathActive(pathname, item.to);
}

function NavigationIcon({
  icon,
  label,
  isActive,
}: {
  icon?: ReactNode;
  label: string;
  isActive: boolean;
}) {
  return (
    <span
      className={`flex h-7 w-7 shrink-0 items-center justify-center transition-colors [&_svg]:h-5 [&_svg]:w-5 ${
        isActive
          ? "text-personal-accent-strong"
          : "text-content-muted group-hover/item:text-content"
      }`}
    >
      {icon ?? label.charAt(0)}
    </span>
  );
}

function NavigationItemContent({
  item,
  isActive,
}: {
  item: PageNavigationItem;
  isActive: boolean;
}) {
  return (
    <>
      <NavigationIcon icon={item.icon} label={item.label} isActive={isActive} />
      <span className="min-w-0 flex-1 sm:hidden lg:block group-data-[sidebar=compact]/sidebar:lg:hidden">
        <span className="block truncate">{item.label}</span>
        {item.detail ? (
          <span className="block truncate text-xs font-semibold text-slate-400">
            {item.detail}
          </span>
        ) : null}
      </span>
    </>
  );
}

function getNavigationItemClassName(isActive: boolean): string {
  return `group/item flex min-h-11 w-full items-center gap-3 border-l-2 px-3 py-2.5 text-left text-sm font-semibold transition-colors sm:justify-center sm:gap-0 sm:px-0 lg:justify-start lg:gap-3 lg:px-3 group-data-[sidebar=compact]/sidebar:lg:justify-center group-data-[sidebar=compact]/sidebar:lg:gap-0 group-data-[sidebar=compact]/sidebar:lg:px-0 ${
    isActive
      ? "border-personal-accent bg-personal-accent/10 text-content"
      : "border-transparent text-content-muted hover:bg-surface-hover hover:text-content"
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
  const itemClassName = getNavigationItemClassName(isActive);

  if (item.disabled) {
    return (
      <button
        key={`${item.to}-${item.label}`}
        type="button"
        className={`${itemClassName} cursor-not-allowed opacity-55`}
        title={item.label}
        disabled
      >
        <NavigationItemContent item={item} isActive={isActive} />
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
      aria-current={isActive ? "page" : undefined}
      title={item.label}
    >
      <NavigationItemContent item={item} isActive={isActive} />
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
    <aside className="hidden-scrollbar-until-hover sticky top-0 h-screen w-full overflow-y-auto border-r border-border-subtle bg-canvas px-4 py-5 sm:px-3 lg:px-4 group-data-[sidebar=compact]/sidebar:lg:px-3">
      <div className="min-h-full pb-20">
        <Link
          to="/"
          className="flex items-center gap-3 sm:justify-center lg:justify-start group-data-[sidebar=compact]/sidebar:lg:justify-center"
        >
          <img
            src="/icon-192.png"
            alt=""
            className="h-11 w-11 border border-border-subtle"
          />
          <div className="sm:hidden lg:block group-data-[sidebar=compact]/sidebar:lg:hidden">
            <p className="text-lg font-black tracking-[0.04em] text-content">
              CANTARO
            </p>
            <p className="text-xs font-bold tracking-[0.32em] text-content-muted uppercase">
              {subtitle}
            </p>
          </div>
        </Link>

        <div className="mt-8 space-y-8">
          {sections.map((section) => (
            <section key={section.title}>
              <div className="mb-3 flex items-center justify-between gap-2 sm:hidden lg:flex group-data-[sidebar=compact]/sidebar:lg:hidden">
                <h2 className="text-xs font-black tracking-[0.22em] text-content-muted uppercase">
                  {section.title}
                </h2>
                {section.titleAction ? (
                  <Link
                    to={section.titleAction.to}
                    params={section.titleAction.params as never}
                    search={section.titleAction.search as never}
                    className="flex h-7 items-center justify-center gap-1.5 px-2.5 text-[11px] leading-none font-black text-personal-accent-strong transition-colors hover:bg-surface-hover"
                    aria-label={
                      section.titleAction.ariaLabel ?? section.titleAction.label
                    }
                  >
                    {section.titleAction.icon ? (
                      <span className="shrink-0">
                        {section.titleAction.icon}
                      </span>
                    ) : null}
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

        {footer ? (
          <div className="mt-8 sm:hidden lg:block group-data-[sidebar=compact]/sidebar:lg:hidden">
            {footer}
          </div>
        ) : null}
      </div>
    </aside>
  );
}
