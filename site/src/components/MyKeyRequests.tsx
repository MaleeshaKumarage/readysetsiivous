'use client';

import { useCallback, useEffect, useState } from 'react';
import { KeyRequestPhotos } from '@/components/KeyRequestPhotos';
import { myKeyRequestApi, type KeyRequest } from '@/lib/partnerApi';

// Key pickups and handovers assigned to the signed-in employee. Photos can be added while the request is open
// and after completion. Load failures and action failures are shown separately, so a successful reload never hides
// the reason an upload or completion was refused.
export function KeyRequestsSection() {
  const [items, setItems] = useState<KeyRequest[] | null>(null);
  const [loadError, setLoadError] = useState(''), [error, setError] = useState(''), [notice, setNotice] = useState(''), [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    try { setItems(await myKeyRequestApi.assigned()); setLoadError(''); }
    catch (e) { setLoadError(e instanceof Error ? e.message : 'Key requests could not be loaded.'); }
  }, []);
  useEffect(() => { void refresh(); }, [refresh]);

  async function run(work: () => Promise<unknown>, success: string) {
    if (busy) return;
    setBusy(true); setError(''); setNotice('');
    try { await work(); setNotice(success); }
    catch (e) { setError(e instanceof Error ? e.message : 'The change could not be saved. Refresh before retrying.'); }
    finally { setBusy(false); await refresh(); }
  }

  return <div id="employee-panel-keys" role="tabpanel" aria-labelledby="employee-tab-keys" className="space-y-6">
    <section className="space-y-1">
      <h2 className="text-xl font-semibold">Key requests</h2>
      <p className="text-sm text-gray-600 dark:text-gray-300">Keys you collect or hand over. Add photos during the visit, then mark the request completed.</p>
    </section>
    {loadError && <p role="alert" className="text-red-600">{loadError}</p>}
    {error && <p role="alert" className="text-red-600">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    {items && items.length === 0 && <p>No key requests are assigned to you.</p>}
    <ul className="space-y-4">
      {(items ?? []).map(item => <li key={item.id} className="border rounded-xl p-4 space-y-3">
        <p className="font-medium">{item.branchName} · {item.kind === 'Handover' ? 'Key handover' : 'Key pickup'} · {item.date}</p>
        <p className="text-sm">{item.status === 'Completed' ? 'Completed' : 'To do'}</p>
        {item.notes && <p className="text-sm">{item.notes}</p>}
        {item.status === 'Requested' && <button className="border rounded px-3 py-2" type="button" disabled={busy}
          onClick={() => void run(() => myKeyRequestApi.complete(item), 'Key request marked completed.')}>Mark completed</button>}
        <KeyRequestPhotos heading="Photos" alt="Key request photo" photos={item.photos ?? []} busy={busy}
          uploadLabel={`Add photo for ${item.branchName} ${item.date}`} loadUrl={photoId => myKeyRequestApi.photoUrl(item.id, photoId)}
          onUpload={file => void run(() => myKeyRequestApi.addPhoto(item, file), 'Photo added.')} />
      </li>)}
    </ul>
  </div>;
}
