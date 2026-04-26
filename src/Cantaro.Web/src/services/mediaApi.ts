export * from './mediaApi.types';
export { MediaApiClient } from './mediaApi.client';
export { configureMediaApi, type MediaApiConfigResolver } from './mediaApi.runtime';

import { MediaApiClient } from './mediaApi.client';

export const mediaApi = new MediaApiClient();
