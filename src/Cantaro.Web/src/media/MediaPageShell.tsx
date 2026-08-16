import { useRouterState } from "@tanstack/react-router";
import {
  BookOpen,
  Cable,
  ClipboardCheck,
  Clock3,
  LibraryBig,
  Tv,
} from "lucide-react";
import type { ReactNode } from "react";
import { PageShell } from "../components/PageShell";
import {
  PageSideNavigation,
  type PageNavigationSection,
} from "../components/PageNavigation";

function createMediaNavigationSections(): PageNavigationSection[] {
  return [
    {
      title: "Media",
      items: [
        {
          label: "Library",
          to: "/media/library",
          icon: <LibraryBig aria-hidden />,
          matchPrefix: "/media/library",
        },
        {
          label: "Review",
          to: "/media/review",
          icon: <ClipboardCheck aria-hidden />,
          matchPrefix: "/media/review",
        },
        {
          label: "Providers",
          to: "/media/providers",
          icon: <Cable aria-hidden />,
          matchPrefix: "/media/providers",
        },
      ],
    },
    {
      title: "Browse",
      items: [
        {
          label: "Anime",
          to: "/media/library",
          icon: <Tv aria-hidden />,
          search: { mediaKind: "anime", sortBy: "updatedAt", sortDir: "desc" },
          matchPrefix: "/media/browse/anime",
        },
        {
          label: "Manga",
          to: "/media/library",
          icon: <BookOpen aria-hidden />,
          search: { mediaKind: "manga", sortBy: "updatedAt", sortDir: "desc" },
          matchPrefix: "/media/browse/manga",
        },
        {
          label: "Recently Updated",
          to: "/media/library",
          icon: <Clock3 aria-hidden />,
          search: { sortBy: "updatedAt", sortDir: "desc" },
          matchPrefix: "/media/browse/recently-updated",
        },
      ],
    },
  ];
}

function MediaSidebar() {
  const pathname = useRouterState({
    select: (state) => state.location.pathname,
  });

  return (
    <PageSideNavigation
      activePathname={pathname}
      subtitle="Media"
      sections={createMediaNavigationSections()}
      footer={
        <div className="border-t border-border-subtle pt-5">
          <p className="font-bold text-content">Review queue</p>
          <p className="mt-2 text-sm leading-6 text-content-muted">
            Resolve new observations and keep the library tidy.
          </p>
        </div>
      }
    />
  );
}

export function MediaPageShell({ children }: { children: ReactNode }) {
  return <PageShell sidebar={<MediaSidebar />}>{children}</PageShell>;
}
