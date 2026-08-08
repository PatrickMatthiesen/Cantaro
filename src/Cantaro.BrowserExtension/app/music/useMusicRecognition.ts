import { useEffect, useRef, useState } from 'react';
import type { MusicTabContext } from '../../features/music/contracts/musicTabContext';
import { recognizeMusicTab, type MusicRecognitionResult } from './musicService';

export function useMusicRecognition(context: MusicTabContext | null) {
  const [recognition, setRecognition] = useState<MusicRecognitionResult | null>(null);
  const [recognitionLoading, setRecognitionLoading] = useState(false);
  const [recognitionError, setRecognitionError] = useState<string | null>(null);
  const [recognitionRetry, setRecognitionRetry] = useState(0);
  const request = useRef(0);

  useEffect(() => {
    const requestId = ++request.current;
    const controller = new AbortController();
    setRecognition(null);
    setRecognitionError(null);
    if (!context) return () => controller.abort();

    setRecognitionLoading(true);
    void recognizeMusicTab(context, controller.signal)
      .then((result) => {
        if (request.current === requestId) setRecognition(result);
      })
      .catch((reason: unknown) => {
        if (reason instanceof DOMException && reason.name === 'AbortError') return;
        if (request.current === requestId) {
          setRecognitionError(reason instanceof Error ? reason.message : 'Cantaro could not check this page.');
        }
      })
      .finally(() => {
        if (request.current === requestId) setRecognitionLoading(false);
      });
    return () => controller.abort();
  }, [context?.externalId, context?.provider, recognitionRetry]);

  return {
    recognition,
    recognitionLoading,
    recognitionError,
    retryRecognition: () => setRecognitionRetry((value) => value + 1),
  };
}
