import { createFileRoute } from '@tanstack/react-router';
import { DesignDirectionPage } from '../design/DesignDirectionPage';

export const Route = createFileRoute('/3')({
  component: () => <DesignDirectionPage designId={3} />,
});
