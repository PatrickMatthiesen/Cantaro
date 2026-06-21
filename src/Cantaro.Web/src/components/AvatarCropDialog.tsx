import { useEffect, useRef, useState } from 'react';
import Cropper, { type Area } from 'react-easy-crop';
import { ImageIcon, Minus, Plus, X } from 'lucide-react';

interface AvatarCropDialogProps {
  file: File;
  imageUrl: string;
  isSaving: boolean;
  onCancel: () => void;
  onConfirm: (avatar: Blob) => Promise<void>;
}

async function renderAvatar(file: File, crop: Area): Promise<Blob> {
  const bitmap = await createImageBitmap(file);
  const canvas = document.createElement('canvas');
  canvas.width = 512;
  canvas.height = 512;
  const context = canvas.getContext('2d');
  if (!context) {
    bitmap.close();
    throw new Error('Your browser could not prepare this image.');
  }

  context.imageSmoothingEnabled = true;
  context.imageSmoothingQuality = 'high';
  context.drawImage(bitmap, crop.x, crop.y, crop.width, crop.height, 0, 0, 512, 512);
  bitmap.close();

  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/webp', 0.86));
  if (!blob || blob.size > 1024 * 1024) {
    throw new Error('The prepared avatar is too large. Try a simpler image.');
  }
  return blob;
}

export function AvatarCropDialog({ file, imageUrl, isSaving, onCancel, onConfirm }: AvatarCropDialogProps) {
  const [crop, setCrop] = useState({ x: 0, y: 0 });
  const [zoom, setZoom] = useState(1);
  const [croppedArea, setCroppedArea] = useState<Area | null>(null);
  const [error, setError] = useState<string | null>(null);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    cancelButtonRef.current?.focus();
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !isSaving) {
        onCancel();
        return;
      }
      if (event.key !== 'Tab') return;
      const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])') ?? []);
      const first = focusable[0];
      const last = focusable.at(-1);
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last?.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first?.focus();
      }
    };
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, [isSaving, onCancel]);

  const confirm = async () => {
    if (!croppedArea) return;
    setError(null);
    try {
      await onConfirm(await renderAvatar(file, croppedArea));
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not crop this image.');
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/65 p-4 backdrop-blur-md" role="dialog" aria-modal="true" aria-labelledby="avatar-crop-title">
      <div ref={dialogRef} className="settings-card w-full max-w-xl overflow-hidden rounded-[2rem] border shadow-[0_32px_120px_rgba(2,6,23,.45)]">
        <header className="flex items-start justify-between gap-4 border-b border-slate-200/70 px-6 py-5">
          <div>
            <p className="text-[10px] font-black tracking-[.25em] text-violet-600 uppercase">Profile picture</p>
            <h2 id="avatar-crop-title" className="mt-1 text-xl font-black text-slate-950">Choose what people see</h2>
            <p className="mt-1 text-sm text-slate-500">Drag the image and zoom until the portrait feels right.</p>
          </div>
          <button ref={cancelButtonRef} type="button" disabled={isSaving} onClick={onCancel} className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl text-slate-500 transition hover:bg-slate-100 hover:text-slate-900 disabled:opacity-50" aria-label="Close image cropper">
            <X className="h-5 w-5" />
          </button>
        </header>

        <div className="relative h-[min(56vh,25rem)] bg-[#0b1020]">
          <Cropper
            image={imageUrl}
            crop={crop}
            zoom={zoom}
            aspect={1}
            cropShape="round"
            showGrid={false}
            roundCropAreaPixels
            onCropChange={setCrop}
            onZoomChange={setZoom}
            onCropComplete={(_, pixels) => setCroppedArea(pixels)}
            mediaProps={{ 'aria-label': 'Image being cropped' }}
          />
        </div>

        <div className="space-y-5 px-6 py-5">
          <label className="flex items-center gap-3">
            <Minus className="h-4 w-4 text-slate-400" aria-hidden />
            <span className="sr-only">Zoom image</span>
            <input type="range" min={1} max={3} step={0.01} value={zoom} onChange={(event) => setZoom(Number(event.target.value))} className="h-2 flex-1 cursor-pointer accent-violet-600" />
            <Plus className="h-4 w-4 text-slate-400" aria-hidden />
          </label>
          {error ? <p className="text-sm font-semibold text-rose-600" role="alert">{error}</p> : null}
          <div className="flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
            <button type="button" disabled={isSaving} onClick={onCancel} className="rounded-2xl px-5 py-3 text-sm font-black text-slate-600 transition hover:bg-slate-100 disabled:opacity-50">Cancel</button>
            <button type="button" disabled={isSaving || !croppedArea} onClick={() => void confirm()} className="inline-flex items-center justify-center gap-2 rounded-2xl bg-violet-600 px-5 py-3 text-sm font-black text-white shadow-lg shadow-violet-500/20 transition hover:-translate-y-0.5 hover:bg-violet-700 disabled:translate-y-0 disabled:opacity-50">
              <ImageIcon className="h-4 w-4" />
              {isSaving ? 'Uploading…' : 'Use this crop'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
