import { Outlet, createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/music/songs')({
  component: SongRoutesLayout,
});

function SongRoutesLayout() {
  return <Outlet />;
}
