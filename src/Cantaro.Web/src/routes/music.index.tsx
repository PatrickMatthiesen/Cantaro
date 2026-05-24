import { createFileRoute, redirect } from '@tanstack/react-router';

export const Route = createFileRoute('/music/')({
  beforeLoad: () => {
    throw redirect({ to: '/music/songs', replace: true });
  },
});
