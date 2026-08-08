import { MediaPage } from '../media/MediaPage';
import { MusicPage } from '../music/MusicPage';
import { SettingsPage } from '../settings/SettingsPage';
import { useSettings } from '../settings/useSettings';
import { browserPopupServices } from './browserPopupServices';
import type { ExtensionAppServices } from './extensionAppTypes';
import { PopupHeader } from './PopupHeader';
import { StatusToast } from './StatusToast';
import { TabContextSummary } from './TabContextSummary';
import { useActiveTabContext } from './useActiveTabContext';
import { usePopupNavigation } from './usePopupNavigation';
import { usePopupNotice } from './usePopupNotice';

export interface ExtensionAppProps {
  services?: ExtensionAppServices;
}

export function ExtensionApp({ services = browserPopupServices }: ExtensionAppProps) {
  const { notice, showNotice } = usePopupNotice();
  const navigation = usePopupNavigation();
  const settings = useSettings(showNotice);
  const activeTabContext = useActiveTabContext(services);

  return (
    <div className="h-screen overflow-hidden bg-canvas text-content">
      <div className="flex h-full flex-col px-3 pt-3">
        <PopupHeader
          section={navigation.section}
          configured={settings.configured}
          loading={settings.loading}
          onSelectSection={navigation.selectPrimarySection}
          onOpenSettings={navigation.openSettings}
        />
        {navigation.section === 'media' ? <TabContextSummary context={activeTabContext.state} /> : null}
        <main className="mt-2 min-h-0 flex-1 overflow-x-hidden overflow-y-auto rounded-t-xl bg-surface-translucent" aria-label={`${navigation.section} content`}>
          {settings.loading ? (
            <PopupLoadingState />
          ) : navigation.section === 'settings' ? (
            <SettingsPage controller={settings} onClose={navigation.closeSettings} />
          ) : navigation.section === 'music' ? (
            <MusicPage
              configured={settings.configured}
              isSigningIn={settings.isSigningIn}
              activeTabContext={activeTabContext.state}
              onSignIn={settings.signIn}
            />
          ) : (
            <MediaPage
              configured={settings.configured}
              isSigningIn={settings.isSigningIn}
              activeTabContext={activeTabContext.state}
              onSignIn={settings.signIn}
              onOpenSettings={navigation.openSettings}
              onNotice={showNotice}
            />
          )}
        </main>
      </div>
      <StatusToast notice={notice} />
    </div>
  );
}

function PopupLoadingState() {
  return (
    <div className="space-y-3 p-5" aria-label="Loading extension">
      <div className="cantaro-popup-loading-line h-5 w-40 rounded-md" />
      <div className="cantaro-popup-loading-line h-3 w-72 rounded-md" />
      <div className="cantaro-popup-loading-line h-20 w-full rounded-xl" />
    </div>
  );
}
