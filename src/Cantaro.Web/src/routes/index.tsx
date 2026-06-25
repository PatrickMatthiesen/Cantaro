import { createFileRoute, redirect } from '@tanstack/react-router';
import { getLastActiveAreaRoute } from '../appAreaRouting';

export const Route = createFileRoute('/')({
  beforeLoad: () => {
    throw redirect({ to: getLastActiveAreaRoute(), replace: true });
  },
});
