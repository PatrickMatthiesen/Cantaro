export * from './mediaApi.types';
export { configureMediaApi } from './mediaApi.runtime';
export type { MediaApiConfigResolver } from './mediaApi.runtime';

import { MediaApiClient } from './mediaApi.client';

export const mediaApi = new MediaApiClient();
