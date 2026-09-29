import { useEffect, useState } from 'react';

function relativeLabel(seconds: number) {
  if (Math.abs(seconds) < 60) return seconds > 0 ? 'shortly' : 'just now';
  const units: Array<[Intl.RelativeTimeFormatUnit, number]> = [['year', 31536000], ['month', 2592000], ['day', 86400], ['hour', 3600], ['minute', 60]];
  const [unit, divisor] = units.find(([, size]) => Math.abs(seconds) >= size) ?? ['minute', 60];
  return new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' }).format(Math.round(seconds / divisor), unit);
}

export function MusicRelativeTime({ value }: { value: string }) {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return null;
  const seconds = (date.getTime() - now) / 1000;
  return <time dateTime={value} title={date.toLocaleString()}>{relativeLabel(seconds)}</time>;
}
