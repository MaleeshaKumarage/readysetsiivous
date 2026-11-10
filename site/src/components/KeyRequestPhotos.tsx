'use client';

import { useEffect, useRef, useState } from 'react';
import type { KeyRequestPhoto } from '@/lib/partnerApi';

const photoLimit = 10;
const photoTypes = 'image/jpeg,image/png,image/webp';

// Thumbnails for a key request. Each photo is fetched once and its object URL is released on unmount.
// Passing onUpload adds the file picker, which is hidden once the limit is reached.
export function KeyRequestPhotos({ heading, alt, photos, loadUrl, onUpload, uploadLabel, busy = false }: {
  heading: string; alt: string; photos: KeyRequestPhoto[]; loadUrl: (photoId: string) => Promise<string>;
  onUpload?: (file: File) => void; uploadLabel?: string; busy?: boolean;
}) {
  const [urls, setUrls] = useState<Record<string, string>>({});
  const requested = useRef(new Set<string>()), created = useRef<string[]>([]);
  const loader = useRef(loadUrl);
  loader.current = loadUrl;

  useEffect(() => {
    for (const photo of photos) {
      if (requested.current.has(photo.id)) continue;
      requested.current.add(photo.id);
      loader.current(photo.id).then(url => {
        created.current.push(url);
        setUrls(previous => ({ ...previous, [photo.id]: url }));
      }, () => requested.current.delete(photo.id));
    }
  }, [photos]);
  useEffect(() => () => { created.current.forEach(url => URL.revokeObjectURL(url)); }, []);

  return <div className="space-y-2">
    <p className="text-sm">{heading}{onUpload ? ` ${photos.length}/${photoLimit}` : ''}</p>
    {onUpload && photos.length < photoLimit && <label className="flex flex-col text-sm">Add photo
      <input type="file" accept={photoTypes} disabled={busy} aria-label={uploadLabel ?? 'Add photo'}
        onChange={event => { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ''; if (file) onUpload(file); }} />
    </label>}
    {photos.length > 0 && <ul className="flex flex-wrap gap-2">{photos.map((photo, index) => <li key={photo.id}>
      {urls[photo.id] ? <img src={urls[photo.id]} alt={`${alt} ${index + 1}`} className="w-24 h-24 object-cover rounded border" /> : <span className="text-sm">Loading photo…</span>}
    </li>)}</ul>}
  </div>;
}
