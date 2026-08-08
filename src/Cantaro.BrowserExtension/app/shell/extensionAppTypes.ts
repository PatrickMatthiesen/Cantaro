export type AppSection = 'music' | 'media' | 'settings';
export type PrimaryAppSection = Exclude<AppSection, 'settings'>;

export type PopupNotice = {
  message: string;
  tone: 'success' | 'error';
};

export type UnsupportedTabContextSnapshot = {
  feature: 'unsupported';
  pageKind: 'unsupported';
  pageUrl: string;
};

export type PopupTabContextSnapshot = ContentTabContextSnapshot | UnsupportedTabContextSnapshot;

export type ActiveTabContextState =
  | { status: 'loading' }
  | { status: 'available'; snapshot: PopupTabContextSnapshot }
  | { status: 'unavailable'; reason: string };

export type TabContextMessageResult = MessageResult<ContentTabContextSnapshot>;

export interface ExtensionAppServices {
  readActiveTabContext: () => Promise<PopupTabContextSnapshot>;
}
import type { TabContextSnapshot as ContentTabContextSnapshot } from '../../platform/messaging/tabContext';
import type { MessageResult } from '../../platform/messaging/messageResult';
