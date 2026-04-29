export * from './mediaApi.types';
// fallow-ignore-next-line unused-export
export { configureMediaApi } from './mediaApi.runtime';
// fallow-ignore-next-line unused-type
export type { MediaApiConfigResolver } from './mediaApi.runtime';

import { MediaApiClient } from './mediaApi.client';

export const mediaApi = new MediaApiClient();
