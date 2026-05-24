import { createFileRoute, redirect } from '@tanstack/react-router';

export const Route = createFileRoute('/youtube/playlists/$playlistId')({
  beforeLoad: ({ params }) => {
    throw redirect({
      to: '/music/platforms/$platformId/playlists/$playlistId',
      params: { platformId: 'youtube', playlistId: params.playlistId },
      replace: true,
    });
  },
});
