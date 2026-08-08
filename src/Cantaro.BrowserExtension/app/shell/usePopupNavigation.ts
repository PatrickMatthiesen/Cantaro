import { useCallback, useEffect, useRef, useState } from 'react';
import type { AppSection, PrimaryAppSection } from './extensionAppTypes';
import { readPreferredSection, rememberPreferredSection } from './popupSectionPreference';

export function usePopupNavigation() {
  const [section, setSection] = useState<AppSection>('music');
  const previousPrimarySection = useRef<PrimaryAppSection>('music');
  const userSelectedSection = useRef(false);

  useEffect(() => {
    let active = true;
    void readPreferredSection().then((stored) => {
      if (!active || userSelectedSection.current) return;
      previousPrimarySection.current = stored;
      setSection(stored);
    });
    return () => { active = false; };
  }, []);

  const selectPrimarySection = useCallback((next: PrimaryAppSection) => {
    userSelectedSection.current = true;
    previousPrimarySection.current = next;
    setSection(next);
    void rememberPreferredSection(next);
  }, []);

  const openSettings = useCallback(() => setSection('settings'), []);
  const closeSettings = useCallback(() => setSection(previousPrimarySection.current), []);

  return { section, selectPrimarySection, openSettings, closeSettings };
}
