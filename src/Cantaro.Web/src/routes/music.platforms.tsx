import { createFileRoute, Outlet } from '@tanstack/react-router';

export const Route = createFileRoute('/music/platforms')({
  component: () => <Outlet />,
});
