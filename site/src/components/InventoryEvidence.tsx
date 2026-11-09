'use client';

import { useEffect, useState } from 'react';
import { inventoryPhotoUrl } from '@/lib/inventoryApi';

/** Blob URLs are never persisted. Partial failures retain the available evidence and release every URL. */
export function InventoryEvidence({ paths, itemName, language = 'en' }: { paths: string[]; itemName: string; language?: 'fi' | 'en' }) {
  const [state, setState] = useState<{ urls: string[]; loading: boolean; failed: boolean }>({ urls: [], loading: true, failed: false });
  const key = JSON.stringify(paths);
  useEffect(() => {
    let active = true;
    const created: string[] = [];
    setState({ urls: [], loading: true, failed: false });
    Promise.allSettled((JSON.parse(key) as string[]).map(async path => {
      const url = await inventoryPhotoUrl(path);
      if (!active) { URL.revokeObjectURL(url); return null; }
      created.push(url); return url;
    })).then(results => {
      if (active) setState({ urls: results.flatMap(result => result.status === 'fulfilled' && result.value ? [result.value] : []), loading: false, failed: results.some(result => result.status === 'rejected') });
    });
    return () => { active = false; created.forEach(url => URL.revokeObjectURL(url)); };
  }, [key]);
  return <div>
    {state.loading && <p role="status">{language === 'fi' ? 'Ladataan kuvia…' : 'Loading photos…'}</p>}
    {state.failed && <p role="status">{language === 'fi' ? 'Osa kuvista ei ole enää saatavilla. Kuvat säilytetään kolme kuukautta.' : 'Some photos are unavailable. Photos are retained for three months.'}</p>}
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 12 }}>
      {state.urls.map((url, index) => <a key={url} href={url} target="_blank" rel="noreferrer"><img src={url} alt={`${itemName} ${language === 'fi' ? 'kuva' : 'photo'} ${index + 1}`} style={{ width: '100%', maxHeight: 300, objectFit: 'contain' }} /></a>)}
    </div>
  </div>;
}
