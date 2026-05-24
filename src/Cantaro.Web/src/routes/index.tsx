import { createFileRoute } from '@tanstack/react-router';
import { WorkspaceHomePage } from '../pages/WorkspaceHomePage';

export const Route = createFileRoute('/')({
  component: WorkspaceHomePage,
});
