import { MediaPage } from '../media/MediaPage';
import { MusicPage } from '../music/MusicPage';
import { SettingsPage } from '../settings/SettingsPage';
import { useSettings } from '../settings/useSettings';
import { CollectionConsent, type CollectionConsentProps } from '../settings/CollectionConsent';
import { useCollectionConsent } from '../settings/useCollectionConsent';
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
  const consent = useCollectionConsent(settings.savedSettings.baseUrl, settings.configured);
  const consentProps = createConsentProps(settings, consent);
  const anyCollection = consentProps.choices.watchTracking || consentProps.choices.catalogCollection;
  const activeTabContext = useActiveTabContext(services, anyCollection);
  const consentKey = JSON.stringify([consentProps.baseUrl, consentProps.authenticated, consentProps.choices]);

  return (
    <div className="h-screen overflow-hidden bg-canvas text-content">
      <div className="flex h-full flex-col">
        <PopupHeader
          section={navigation.section}
          configured={settings.configured}
          loading={settings.loading}
          onSelectSection={navigation.selectPrimarySection}
          onOpenSettings={navigation.openSettings}
        />
        {navigation.section === 'media' && anyCollection ? <TabContextSummary context={activeTabContext.state} /> : null}
        <main className="min-h-0 flex-1 overflow-x-hidden overflow-y-auto bg-canvas" aria-label={`${navigation.section} content`}>
          <PopupContent settings={settings} consentLoading={consent.loading}
            navigation={navigation} consentProps={consentProps} consentKey={consentKey}
            activeTabContext={activeTabContext} showNotice={showNotice} />
        </main>
      </div>
      <StatusToast notice={notice} />
    </div>
  );
}

function createConsentProps(settings: ReturnType<typeof useSettings>, consent: ReturnType<typeof useCollectionConsent>): CollectionConsentProps {
  return {
    choices: {
      watchTracking: consent.status?.watchTrackingAllowed === true,
      catalogCollection: consent.status?.catalogCollectionAllowed === true,
    },
    needsReview: consent.status?.needsReview !== false,
    authenticated: consent.status?.authenticated === true,
    baseUrl: settings.savedSettings.baseUrl,
    busy: consent.busy || settings.isSigningIn,
    error: consent.error,
    onSave: consent.save,
    onRevoke: consent.revoke,
    onReset: consent.reset,
    onSignIn: settings.signIn,
  };
}

function PopupContent({ settings, consentLoading, navigation, consentProps, consentKey, activeTabContext, showNotice }: {
  settings: ReturnType<typeof useSettings>;
  consentLoading: boolean;
  navigation: ReturnType<typeof usePopupNavigation>;
  consentProps: CollectionConsentProps;
  consentKey: string;
  activeTabContext: ReturnType<typeof useActiveTabContext>;
  showNotice: ReturnType<typeof usePopupNotice>['showNotice'];
}) {
  if (settings.loading || consentLoading) return <PopupLoadingState />;
  const collectionConsent = <CollectionConsent key={consentKey} {...consentProps} />;
  if (navigation.section === 'settings') {
    return <SettingsPage controller={settings} onClose={navigation.closeSettings} collectionConsent={collectionConsent} />;
  }
  if (navigation.section === 'music') {
    return <MusicPage configured={settings.configured} isSigningIn={settings.isSigningIn}
      activeTabContext={activeTabContext.state} onSignIn={settings.signIn} />;
  }
  if (consentProps.needsReview) return <div className="mx-auto max-w-2xl px-5">{collectionConsent}</div>;
  return <MediaPage configured={settings.configured} watchCollectionAllowed={consentProps.choices.watchTracking}
    isSigningIn={settings.isSigningIn} activeTabContext={activeTabContext.state} onSignIn={settings.signIn}
    onOpenSettings={navigation.openSettings} onNotice={showNotice} />;
}

function PopupLoadingState() {
  return (
    <div className="space-y-3 p-5" aria-label="Loading extension">
      <div className="cantaro-popup-loading-line h-5 w-40" />
      <div className="cantaro-popup-loading-line h-3 w-72" />
      <div className="cantaro-popup-loading-line h-20 w-full" />
    </div>
  );
}
