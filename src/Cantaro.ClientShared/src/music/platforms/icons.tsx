import { useId, type ElementType, type SVGProps } from 'react';
import Spotify from '@thesvg/react/spotify';
import YouTubeMusic from '@thesvg/react/youtube-music';
import {
  Activity,
  Album,
  ArrowLeft,
  ArrowRight,
  Bell,
  Cable,
  ChartNoAxesColumn,
  CheckCircle2,
  CircleAlert,
  Clock3,
  CloudSync,
  Compass,
  Database,
  ExternalLink,
  Grid3x3,
  Heart,
  Headphones,
  Library,
  ListMusic,
  LoaderCircle,
  MoreHorizontal,
  Music,
  Pause,
  Play,
  Plus,
  Radio,
  RefreshCw,
  Repeat2,
  Rows3,
  Save,
  Search,
  Settings,
  ShieldCheck,
  SlidersHorizontal,
  Sparkles,
  Square,
  SquareCheck,
  Trash2,
  User,
  type LucideIcon,
} from 'lucide-react';
import type { PlatformId } from './types';

type SvgIconComponent = ElementType<SVGProps<SVGSVGElement>>;

function AppleMusicIcon({ className, ...props }: SVGProps<SVGSVGElement>) {
  const gradientId = useId();
  return (
    <svg viewBox="0 0 361 361" fill="none" xmlns="http://www.w3.org/2000/svg" className={className} {...props}>
      <defs>
        <linearGradient id={gradientId} x1="54" y1="18" x2="307" y2="343" gradientUnits="userSpaceOnUse">
          <stop stopColor="#fb5c74" />
          <stop offset="1" stopColor="#fa233b" />
        </linearGradient>
      </defs>
      <rect width="361" height="361" rx="82" fill={`url(#${gradientId})`} />
      <path fill="#fff" d="M254.5 55c-.87.08-8.6 1.45-9.53 1.64l-107 21.59-.04.01c-2.79.59-4.98 1.58-6.67 3-2.04 1.71-3.17 4.13-3.6 6.95-.09.6-.24 1.82-.24 3.62v133.92c0 3.13-.25 6.17-2.37 8.76s-4.74 3.37-7.81 3.99l-6.99 1.41c-8.84 1.78-14.59 2.99-19.8 5.01-4.98 1.93-8.71 4.39-11.68 7.51-5.89 6.17-8.28 14.54-7.46 22.38.7 6.69 3.71 13.09 8.88 17.82 3.49 3.2 7.85 5.63 12.99 6.66 5.33 1.07 11.01.7 19.31-.98 4.42-.89 8.56-2.28 12.5-4.61 3.9-2.3 7.24-5.37 9.85-9.11 2.62-3.75 4.31-7.92 5.24-12.35.96-4.57 1.19-8.7 1.19-13.26V142.81c0-6.22 1.76-7.86 6.78-9.08 0 0 88.94-17.94 93.09-18.75 5.79-1.11 8.52.54 8.52 6.61v79.29c0 3.14-.03 6.32-2.17 8.92-2.12 2.59-4.74 3.37-7.81 3.99l-6.99 1.41c-8.84 1.78-14.59 2.99-19.8 5.01-4.98 1.93-8.71 4.39-11.68 7.51-5.89 6.17-8.49 14.54-7.67 22.38.7 6.69 3.92 13.09 9.09 17.82 3.49 3.2 7.85 5.56 12.99 6.6 5.33 1.07 11.01.69 19.31-.98 4.42-.89 8.56-2.22 12.5-4.55 3.9-2.3 7.24-5.37 9.85-9.11 2.62-3.75 4.31-7.92 5.24-12.35.96-4.57 1-8.7 1-13.26V64.46c.02-6.16-3.23-9.96-9.02-9.46z" />
    </svg>
  );
}

function TidalIcon({ className, ...props }: SVGProps<SVGSVGElement>) {
  return (
    <svg viewBox="0 0 1001 667" fill="none" xmlns="http://www.w3.org/2000/svg" className={className} {...props}>
      <path
        fill="currentColor"
        d="M84 83.5 0.5 167l83 83c45.6 45.6 83.4 83 84 83 .5 0 38.3-37.4 84-83l83-83 83 83 83 83-83.5 83.5L333.5 500l83.5 83.5 83.5 83.5 83.5-83.5 83.5-83.5-83.3-83.3-83.2-83.2 83.3-83.3 83.2-83.2 83.3 83.2 83.2 83.3 83.5-83.5 83.5-83.5-83.5-83.5-83.5-83.5-83.5 83.5-83.5 83.5-83.3-83.3L500.5 0 417.2 83.2 334 166.5l-83.3-83.3L167.5 0 84 83.5Z"
      />
    </svg>
  );
}

const platformIconById: Record<PlatformId, SvgIconComponent> = {
  youtube: YouTubeMusic,
  spotify: Spotify,
  apple: AppleMusicIcon,
  tidal: TidalIcon,
};

export type MusicUiIconName =
  | 'activity'
  | 'album'
  | 'arrowLeft'
  | 'arrowRight'
  | 'bell'
  | 'cable'
  | 'chart'
  | 'checkCircle'
  | 'clock'
  | 'cloudSync'
  | 'compass'
  | 'database'
  | 'externalLink'
  | 'grid'
  | 'heart'
  | 'headphones'
  | 'library'
  | 'listMusic'
  | 'loader'
  | 'more'
  | 'music'
  | 'pause'
  | 'play'
  | 'plus'
  | 'radio'
  | 'refresh'
  | 'repeat'
  | 'rows'
  | 'save'
  | 'search'
  | 'settings'
  | 'shieldCheck'
  | 'sliders'
  | 'sparkles'
  | 'square'
  | 'squareCheck'
  | 'trash'
  | 'warning'
  | 'user';

const uiIconByName: Record<MusicUiIconName, LucideIcon> = {
  activity: Activity,
  album: Album,
  arrowLeft: ArrowLeft,
  arrowRight: ArrowRight,
  bell: Bell,
  cable: Cable,
  chart: ChartNoAxesColumn,
  checkCircle: CheckCircle2,
  clock: Clock3,
  cloudSync: CloudSync,
  compass: Compass,
  database: Database,
  externalLink: ExternalLink,
  grid: Grid3x3,
  heart: Heart,
  headphones: Headphones,
  library: Library,
  listMusic: ListMusic,
  loader: LoaderCircle,
  more: MoreHorizontal,
  music: Music,
  pause: Pause,
  play: Play,
  plus: Plus,
  radio: Radio,
  refresh: RefreshCw,
  repeat: Repeat2,
  rows: Rows3,
  save: Save,
  search: Search,
  settings: Settings,
  shieldCheck: ShieldCheck,
  sliders: SlidersHorizontal,
  sparkles: Sparkles,
  square: Square,
  squareCheck: SquareCheck,
  trash: Trash2,
  warning: CircleAlert,
  user: User,
};

export function MusicPlatformIcon({
  platformId,
  className = 'h-5 w-5',
  title,
}: {
  platformId: PlatformId;
  className?: string;
  title?: string;
}) {
  const Icon = platformIconById[platformId];
  return <Icon aria-hidden={title ? undefined : true} aria-label={title} className={className} />;
}

export function MusicUiIcon({
  name,
  className = 'h-5 w-5',
  title,
  strokeWidth = 2,
}: {
  name: MusicUiIconName;
  className?: string;
  title?: string;
  strokeWidth?: number;
}) {
  const Icon = uiIconByName[name];
  return <Icon aria-hidden={title ? undefined : true} aria-label={title} className={className} strokeWidth={strokeWidth} />;
}
