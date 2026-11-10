'use client';

import { useEffect, useRef, useState } from 'react';
import type { KeyRequestPhoto } from '@/lib/partnerApi';

const photoLimit = 10;
const photoTypes = 'image/jpeg,image/png,image/webp';

// Thumbnails for a key request. Each effect run owns the object URLs it creates. They are released when the
// photo set changes or the component unmounts, including URLs that resolve after the cleanup has run.
export function KeyRequestPhotos({ heading, alt, photos, loadUrl, onUpload, uploadLabel, busy = false }: {
  heading: string; alt: string; photos: KeyRequestPhoto[]; loadUrl: (photoId: string) => Promise<string>;
  onUpload?: (file: File) => void; uploadLabel?: string; busy?: boolean;
}) {
  const [urls, setUrls] = useState<Record<string, string>>({});
  const loader = useRef(loadUrl);
  loader.current = loadUrl;
  const ids = photos.map(photo => photo.id).join(',');

  useEffect(() => {
    let active = true;
    const owned: string[] = [];
    for (const id of ids ? ids.split(',') : []) {
      loader.current(id).then(url => {
        if (!active) { URL.revokeObjectURL(url); return; }
        owned.push(url);
        setUrls(previous => ({ ...previous, [id]: url }));
      }, () => { /* Shown as loading. Reloads when the photo set next changes. */ });
    }
    return () => {
      active = false;
      owned.forEach(url => URL.revokeObjectURL(url));
      setUrls({});
    };
  }, [ids]);

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
