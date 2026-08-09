export interface MusicTabContext {
  feature: 'music';
  provider: 'youtube' | 'youtube_music' | 'spotify';
  pageKind: 'track' | 'placeholder';
  pageUrl: string;
  status: 'starting' | 'ready' | 'unsupported' | 'error';
  externalId?: string;
  title?: string;
  artist?: string;
  message?: string;
}
