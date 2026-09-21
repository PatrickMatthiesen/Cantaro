import { createFileRoute, Outlet } from '@tanstack/react-router';
import { RequireAuth } from '../components/AppShell';

export const Route = createFileRoute('/media/library')({
  component: () => (
    <RequireAuth>
      <Outlet />
    </RequireAuth>
  ),
});
