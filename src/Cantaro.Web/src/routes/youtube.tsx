import { createFileRoute, redirect } from '@tanstack/react-router';

export const Route = createFileRoute('/youtube')({
  beforeLoad: ({ location }) => {
    if (location.pathname === '/youtube') {
      throw redirect({ to: '/music/platforms/$platformId', params: { platformId: 'youtube' }, replace: true });
    }
  },
});
