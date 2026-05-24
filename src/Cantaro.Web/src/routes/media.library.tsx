import { createFileRoute, Outlet } from '@tanstack/react-router';

export const Route = createFileRoute('/media/library')({
  component: () => <Outlet />,
});
