import type { ElementType, SVGProps } from 'react';
import AppleMusic from '@thesvg/react/apple-music';
import Spotify from '@thesvg/react/spotify';
import Tidal from '@thesvg/react/tidal';
import YouTubeMusic from '@thesvg/react/youtube-music';
import {
  Activity,
  ArrowRight,
  Bell,
  Cable,
  ChartNoAxesColumn,
  CircleAlert,
  Clock3,
  CloudSync,
  Compass,
  Database,
  Grid3x3,
  Headphones,
  Library,
  ListMusic,
  LoaderCircle,
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
  User,
  type LucideIcon,
} from 'lucide-react';
import type { PlatformId } from './types';

type SvgIconComponent = ElementType<SVGProps<SVGSVGElement>>;

const platformIconById: Record<PlatformId, SvgIconComponent> = {
  youtube: YouTubeMusic,
  spotify: Spotify,
  apple: AppleMusic,
  tidal: Tidal,
};

export type MusicUiIconName =
  | 'activity'
  | 'arrowRight'
  | 'bell'
  | 'cable'
  | 'chart'
  | 'clock'
  | 'cloudSync'
  | 'compass'
  | 'database'
  | 'grid'
  | 'headphones'
  | 'library'
  | 'listMusic'
  | 'loader'
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
  | 'warning'
  | 'user';

const uiIconByName: Record<MusicUiIconName, LucideIcon> = {
  activity: Activity,
  arrowRight: ArrowRight,
  bell: Bell,
  cable: Cable,
  chart: ChartNoAxesColumn,
  clock: Clock3,
  cloudSync: CloudSync,
  compass: Compass,
  database: Database,
  grid: Grid3x3,
  headphones: Headphones,
  library: Library,
  listMusic: ListMusic,
  loader: LoaderCircle,
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
