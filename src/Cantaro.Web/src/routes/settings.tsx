import { createFileRoute } from '@tanstack/react-router';
import { RequireAuth } from '../components/AppShell';
import { SettingsPage } from '../pages/SettingsPage';

export const Route = createFileRoute('/settings')({
  component: () => <RequireAuth><SettingsPage /></RequireAuth>,
});
