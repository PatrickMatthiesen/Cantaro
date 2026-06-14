import { createFileRoute } from '@tanstack/react-router';
import { MusicPlatformSyncSetupPage } from '../pages/MusicPage';

export const Route = createFileRoute('/music/platforms/sync')({
  component: MusicPlatformSyncSetupPage,
});
