import { createFileRoute } from '@tanstack/react-router';
import { ExtensionAuthPage } from '../pages/ExtensionAuthPage';

export const Route = createFileRoute('/extension-auth')({
  component: ExtensionAuthPage,
});
