import { createFileRoute } from '@tanstack/react-router';
import { CantaroHomePage } from '../pages/CantaroHomePage';

export const Route = createFileRoute('/')({
  component: CantaroHomePage,
});
