import { useEffect, useState } from 'react';

const mobileSearchQuery = '(max-width: 47.99rem)';

function readMobileSearchViewport(): boolean {
  return typeof window !== 'undefined' && window.matchMedia(mobileSearchQuery).matches;
}

export function useMobileSearchViewport(): boolean {
  const [isMobile, setIsMobile] = useState(readMobileSearchViewport);

  useEffect(() => {
    const mediaQuery = window.matchMedia(mobileSearchQuery);
    const update = () => setIsMobile(mediaQuery.matches);
    update();
    mediaQuery.addEventListener('change', update);
    return () => mediaQuery.removeEventListener('change', update);
  }, []);

  return isMobile;
}
