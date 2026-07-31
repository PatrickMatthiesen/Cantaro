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

const maxAvatarBytes = 1024 * 1024;

function canvasToBlob(canvas: HTMLCanvasElement, type: string, quality?: number): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, quality));
}

function findUploadableBlob(blobs: Array<Blob | null>): Blob | null {
  return blobs.find((blob) => blob && blob.size <= maxAvatarBytes) ?? null;
}

function throwAvatarRenderError(blobs: Array<Blob | null>): never {
  if (blobs.every((blob) => !blob)) {
    throw new Error('Your browser could not prepare this image.');
  }

  if (blobs.some((blob) => blob && blob.size > maxAvatarBytes)) {
    throw new Error('The prepared avatar is too large. Try a simpler image.');
  }

  throw new Error('Your browser prepared an image format Cantaro cannot upload.');
}

async function createAvatarBlob(canvas: HTMLCanvasElement): Promise<Blob> {
  const blobs = [
    await canvasToBlob(canvas, 'image/webp', 0.86),
    await canvasToBlob(canvas, 'image/jpeg', 0.88),
    await canvasToBlob(canvas, 'image/png'),
  ];

  return findUploadableBlob(blobs) ?? throwAvatarRenderError(blobs);
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

  return createAvatarBlob(canvas);
}

function focusableElements(dialog: HTMLDivElement | null) {
  return Array.from(dialog?.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])') ?? []);
}

function handleEscapeKey(event: KeyboardEvent, isSaving: boolean, onCancel: () => void) {
  if (event.key === 'Escape' && !isSaving) {
    onCancel();
    return true;
  }

  return false;
}

function focusWrapTarget(event: KeyboardEvent, first?: HTMLElement, last?: HTMLElement) {
  if (event.shiftKey) {
    return document.activeElement === first ? last : null;
  }

  return document.activeElement === last ? first : null;
}

function trapTabFocus(event: KeyboardEvent, dialog: HTMLDivElement | null) {
  if (event.key !== 'Tab') return;

  const focusable = focusableElements(dialog);
  const target = focusWrapTarget(event, focusable[0], focusable.at(-1));

  if (!target) return;

  event.preventDefault();
  target.focus();
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
      if (!handleEscapeKey(event, isSaving, onCancel)) {
        trapTabFocus(event, dialogRef.current);
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
        <header className="flex items-start justify-between gap-4 border-b border-border-subtle px-6 py-5">
          <div>
            <p className="text-[10px] font-black tracking-[.25em] text-violet-600 uppercase">Profile picture</p>
            <h2 id="avatar-crop-title" className="mt-1 text-xl font-black text-content">Choose what people see</h2>
            <p className="mt-1 text-sm text-content-muted">Drag the image and zoom until the portrait feels right.</p>
          </div>
          <button ref={cancelButtonRef} type="button" disabled={isSaving} onClick={onCancel} className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl text-content-muted transition hover:bg-surface-subtle hover:text-content disabled:opacity-50" aria-label="Close image cropper">
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
            <button type="button" disabled={isSaving} onClick={onCancel} className="rounded-2xl px-5 py-3 text-sm font-black text-content-muted transition hover:bg-surface-subtle disabled:opacity-50">Cancel</button>
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
