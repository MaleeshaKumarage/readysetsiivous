'use client';

import { useEffect, useState } from 'react';
import { inventoryPhotoUrl } from '@/lib/inventoryApi';

/** Blob URLs are never persisted. Partial failures keep the available evidence. */
export function InventoryEvidence({ paths, itemName, language = 'en' }: { paths: string[]; itemName: string; language?: 'fi' | 'en' }) {
  const [state, setState] = useState<{ urls: string[]; loading: boolean; failed: boolean }>({ urls: [], loading: true, failed: false });
  const key = JSON.stringify(paths);
  useEffect(() => {
    let active = true;
    setState({ urls: [], loading: true, failed: false });
    Promise.allSettled((JSON.parse(key) as string[]).map(path => inventoryPhotoUrl(path))).then(results => {
      const urls = results.flatMap(result => result.status === 'fulfilled' ? [result.value] : []);
      if (!active) { urls.forEach(url => URL.revokeObjectURL(url)); return; }
      setState({ urls, loading: false, failed: results.some(result => result.status === 'rejected') });
    });
    return () => { active = false; };
  }, [key]);
  // Runs after the next render replaces these URLs, and on unmount, so no painted image points at a revoked URL.
  useEffect(() => {
    const urls = state.urls;
    return () => urls.forEach(url => URL.revokeObjectURL(url));
  }, [state.urls]);
  return <div>
    {state.loading && <p role="status">{language === 'fi' ? 'Ladataan kuvia…' : 'Loading photos…'}</p>}
    {state.failed && <p role="status">{language === 'fi' ? 'Osa kuvista ei ole enää saatavilla. Kuvat säilytetään kolme kuukautta.' : 'Some photos are unavailable. Photos are retained for three months.'}</p>}
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 12 }}>
      {state.urls.map((url, index) => <a key={url} href={url} target="_blank" rel="noreferrer"><img src={url} alt={`${itemName} ${language === 'fi' ? 'kuva' : 'photo'} ${index + 1}`} style={{ width: '100%', maxHeight: 300, objectFit: 'contain' }} /></a>)}
    </div>
  </div>;
}
