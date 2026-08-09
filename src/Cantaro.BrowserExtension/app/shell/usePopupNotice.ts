import { useCallback, useEffect, useRef, useState } from 'react';
import type { PopupNotice } from './extensionAppTypes';

export function usePopupNotice() {
  const [notice, setNotice] = useState<PopupNotice | null>(null);
  const timeout = useRef<number | null>(null);

  const showNotice = useCallback((message: string, tone: PopupNotice['tone']) => {
    setNotice({ message, tone });
    if (timeout.current) window.clearTimeout(timeout.current);
    timeout.current = window.setTimeout(() => setNotice(null), 3200);
  }, []);

  useEffect(() => () => {
    if (timeout.current) window.clearTimeout(timeout.current);
  }, []);

  return { notice, showNotice };
}
