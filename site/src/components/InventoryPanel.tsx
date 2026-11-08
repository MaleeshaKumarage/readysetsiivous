'use client';

import { useEffect, useRef, useState, type FormEvent } from 'react';
import { initAuth, login } from '@/lib/auth';
import {
  inventoryPhotoUrl,
  meInventory,
  takenLabel,
  type InventoryItem,
  type InventoryLocation,
  type InventoryUsage,
} from '@/lib/inventoryApi';
import { Button } from '@/ds/Button';

const MAX_PHOTOS = 5;
const MAX_QUANTITY = 10000;
const ADD_NEW_ITEM = '__add_new__';

type PendingPhoto = { file: File; url: string };

function PhotoThumb({ path, label }: { path: string; label: string }) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let active = true;
    let created: string | null = null;
    inventoryPhotoUrl(path)
      .then(blobUrl => {
        if (active) { created = blobUrl; setUrl(blobUrl); } else URL.revokeObjectURL(blobUrl);
      })
      .catch(() => { if (active) setFailed(true); });
    return () => {
      active = false;
      if (created) URL.revokeObjectURL(created);
    };
  }, [path]);

  if (failed) return <span className="text-xs text-gray-500">Photo unavailable</span>;
  if (!url) return <div className="h-16 w-16 animate-pulse rounded bg-gray-200" role="img" aria-label="Loading photo" />;
  return <img src={url} alt={label} className="h-16 w-16 rounded object-cover" />;
}

// Photos are fetched only when the employee opens them, so a long history does not request every image up front.
function PhotoPanel({ entry }: { entry: InventoryUsage }) {
  const [open, setOpen] = useState(false);
  return (
    <div className="mt-2">
      <button type="button" className="text-sm underline" aria-expanded={open} onClick={() => setOpen(value => !value)}>
        {open ? 'Hide photos' : `Show photos (${entry.photoIds.length})`}
      </button>
      {open && (
        <div className="mt-2 flex flex-wrap gap-2">
          {entry.photoIds.map((photoId, index) => (
            <PhotoThumb key={photoId} path={meInventory.photoPath(entry.id, photoId)} label={`${entry.itemName} photo ${index + 1}`} />
          ))}
        </div>
      )}
    </div>
  );
}

/** Log an item taken and see your own history. Used on its own page and inside the shifts modal. */
export function InventoryPanel() {
  const [ready, setReady] = useState(false);
  const [authed, setAuthed] = useState(false);
  const [locations, setLocations] = useState<InventoryLocation[]>([]);
  const [items, setItems] = useState<InventoryItem[]>([]);
  const [history, setHistory] = useState<InventoryUsage[] | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [historyError, setHistoryError] = useState<string | null>(null);

  const [locationId, setLocationId] = useState('');
  const [itemChoice, setItemChoice] = useState('');
  const [newItemName, setNewItemName] = useState('');
  const [newItemUnit, setNewItemUnit] = useState('');
  const [quantity, setQuantity] = useState('1');
  const [note, setNote] = useState('');
  const [photos, setPhotos] = useState<PendingPhoto[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [savedMessage, setSavedMessage] = useState<string | null>(null);

  const addingNew = itemChoice === ADD_NEW_ITEM;
  const selectedItem = items.find(item => item.id === itemChoice);
  const quantityNumber = Number(quantity);
  const quantityValid = Number.isInteger(quantityNumber) && quantityNumber >= 1 && quantityNumber <= MAX_QUANTITY;
  const newNameLength = newItemName.trim().length;
  const itemValid = addingNew ? newNameLength >= 2 && newNameLength <= 80 : itemChoice !== '';
  const canSave = locationId !== '' && itemValid && quantityValid && photos.length >= 1 && !submitting;

  useEffect(() => {
    let active = true;
    initAuth()
      .then(ok => {
        if (!active) return;
        setReady(true);
        setAuthed(ok);
        if (ok) loadAll();
      })
      .catch(() => {
        // An offline or failed session check shows the signed-out prompt, rather than loading forever.
        if (!active) return;
        setAuthed(false);
        setReady(true);
      });
    return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Release preview URLs when the panel unmounts. Individual removals and resets revoke their own URLs.
  const photosRef = useRef<PendingPhoto[]>([]);
  photosRef.current = photos;
  useEffect(() => () => photosRef.current.forEach(photo => URL.revokeObjectURL(photo.url)), []);

  async function loadAll() {
    setLoadError(null);
    try {
      const [loadedLocations, loadedItems, loadedHistory] = await Promise.all([
        meInventory.locations(),
        meInventory.items(),
        meInventory.usage(),
      ]);
      setLocations(loadedLocations);
      setItems(loadedItems);
      setHistory(loadedHistory);
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Could not load inventory.');
    }
  }

  function addPhotos(fileList: FileList | null) {
    if (!fileList) return;
    const room = MAX_PHOTOS - photos.length;
    const chosen = Array.from(fileList).slice(0, Math.max(room, 0));
    if (fileList.length > room) setFormError(`You can attach up to ${MAX_PHOTOS} photos per entry.`);
    setPhotos(prev => [...prev, ...chosen.map(file => ({ file, url: URL.createObjectURL(file) }))]);
  }

  function removePhoto(index: number) {
    setPhotos(prev => {
      const removed = prev[index];
      if (removed) URL.revokeObjectURL(removed.url);
      return prev.filter((_, i) => i !== index);
    });
  }

  function resetForm() {
    setItemChoice('');
    setNewItemName('');
    setNewItemUnit('');
    setQuantity('1');
    setNote('');
    photos.forEach(photo => URL.revokeObjectURL(photo.url));
    setPhotos([]);
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canSave) return;
    setSubmitting(true);
    setFormError(null);
    setSavedMessage(null);
    let recorded = false;
    try {
      let itemId = itemChoice;
      if (addingNew) {
        // The server returns the existing item when the name is already in the catalog, so a retry does not duplicate it.
        const created = await meInventory.addItem(newItemName.trim(), newItemUnit.trim());
        itemId = created.id;
        setItems(prev => (prev.some(item => item.id === created.id) ? prev : [...prev, created]));
      }
      const form = new FormData();
      form.append('LocationId', locationId);
      form.append('ItemId', itemId);
      form.append('Quantity', String(quantityNumber));
      form.append('Note', note);
      photos.forEach(photo => form.append('Photos', photo.file, photo.file.name));
      await meInventory.record(form);
      recorded = true;
      resetForm();
      setSavedMessage('Entry saved.');
    } catch (err) {
      setFormError(err instanceof Error ? err.message : 'The entry was not saved. Try again.');
    } finally {
      setSubmitting(false);
    }
    // Refresh separately: a failed reload must not be reported as a failed save.
    if (recorded) await refreshHistory();
  }

  async function refreshHistory() {
    setHistoryError(null);
    try {
      setHistory(await meInventory.usage());
    } catch (err) {
      setHistoryError(err instanceof Error ? err.message : 'Your history could not be loaded.');
    }
  }

  return (
    <div className="space-y-6 text-gray-900 dark:text-gray-100">
      {!ready && <p className="text-sm text-gray-500">Loading…</p>}

      {ready && !authed && (
        <div className="space-y-3">
          <p>Sign in to log items you have taken.</p>
          <Button onClick={() => login()}>Sign in</Button>
        </div>
      )}

      {ready && authed && (
        <>
          {loadError && (
            <div role="alert" className="mb-4 rounded bg-red-50 p-3 text-sm text-red-700 dark:bg-red-900/30 dark:text-red-200">
              {loadError} <button className="underline" onClick={() => loadAll()}>Try again</button>
            </div>
          )}

          <section aria-labelledby="log-heading" className="rounded-lg border border-gray-200 p-4 dark:border-gray-700">
            <h2 id="log-heading" className="mb-4 text-lg font-semibold">Log an item taken</h2>
            <form onSubmit={save} className="space-y-4" noValidate>
              <div>
                <label htmlFor="inventory-location" className="mb-1 block text-sm font-medium">Where did you take it from?</label>
                <select
                  id="inventory-location"
                  className="w-full rounded border border-gray-300 bg-white p-2 dark:border-gray-600 dark:bg-gray-900"
                  value={locationId}
                  onChange={e => setLocationId(e.target.value)}
                >
                  <option value="">Choose a location</option>
                  {locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
                </select>
              </div>

              <div>
                <label htmlFor="inventory-item" className="mb-1 block text-sm font-medium">What did you take?</label>
                <select
                  id="inventory-item"
                  className="w-full rounded border border-gray-300 bg-white p-2 dark:border-gray-600 dark:bg-gray-900"
                  value={itemChoice}
                  onChange={e => setItemChoice(e.target.value)}
                >
                  <option value="">Choose an item</option>
                  {items.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
                  <option value={ADD_NEW_ITEM}>+ Add new item</option>
                </select>
              </div>

              {addingNew && (
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <div className="sm:col-span-2">
                    <label htmlFor="inventory-new-item" className="mb-1 block text-sm font-medium">New item name</label>
                    <input
                      id="inventory-new-item"
                      className="w-full rounded border border-gray-300 bg-white p-2 dark:border-gray-600 dark:bg-gray-900"
                      value={newItemName}
                      maxLength={80}
                      onChange={e => setNewItemName(e.target.value)}
                    />
                  </div>
                  <div>
                    <label htmlFor="inventory-new-unit" className="mb-1 block text-sm font-medium">Unit</label>
                    <input
                      id="inventory-new-unit"
                      placeholder="pcs"
                      className="w-full rounded border border-gray-300 bg-white p-2 dark:border-gray-600 dark:bg-gray-900"
                      value={newItemUnit}
                      maxLength={20}
                      onChange={e => setNewItemUnit(e.target.value)}
                    />
                  </div>
                  <p className="text-xs text-gray-500 sm:col-span-3">New items are available right away. An admin will review them.</p>
                </div>
              )}

              <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                <div>
                  <label htmlFor="inventory-quantity" className="mb-1 block text-sm font-medium">
                    Quantity{selectedItem && !addingNew ? ` (${selectedItem.unit})` : ''}
                  </label>
                  <input
                    id="inventory-quantity"
                    type="number"
                    inputMode="numeric"
                    min={1}
                    max={MAX_QUANTITY}
                    className="w-full rounded border border-gray-300 bg-white p-2 dark:border-gray-600 dark:bg-gray-900"
                    value={quantity}
                    onChange={e => setQuantity(e.target.value)}
                  />
                </div>
              </div>

              <div>
                <label htmlFor="inventory-note" className="mb-1 block text-sm font-medium">Note (optional)</label>
                <textarea
                  id="inventory-note"
                  rows={2}
                  maxLength={500}
                  className="w-full rounded border border-gray-300 bg-white p-2 dark:border-gray-600 dark:bg-gray-900"
                  value={note}
                  onChange={e => setNote(e.target.value)}
                />
              </div>

              <div>
                <label htmlFor="inventory-photos" className="mb-1 block text-sm font-medium">
                  Photos (at least 1, up to {MAX_PHOTOS})
                </label>
                <input
                  id="inventory-photos"
                  type="file"
                  accept="image/*"
                  multiple
                  capture="environment"
                  className="block w-full text-sm"
                  onChange={e => { addPhotos(e.target.files); e.target.value = ''; }}
                  disabled={photos.length >= MAX_PHOTOS}
                />
                {photos.length > 0 && (
                  <ul className="mt-3 flex flex-wrap gap-3" aria-label="Selected photos">
                    {photos.map((photo, index) => (
                      <li key={photo.url} className="flex flex-col items-center gap-1">
                        <img src={photo.url} alt={`Selected photo ${index + 1}`} className="h-20 w-20 rounded object-cover" />
                        <button type="button" className="text-xs underline" onClick={() => removePhoto(index)}>
                          Remove
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
                {photos.length === 0 && <p className="mt-2 text-xs text-gray-500">Attach at least one photo to save the entry.</p>}
              </div>

              {formError && <p role="alert" className="text-sm text-red-700 dark:text-red-300">{formError}</p>}
              {savedMessage && <p role="status" className="text-sm text-emerald-700 dark:text-emerald-300">{savedMessage}</p>}

              <Button type="submit" disabled={!canSave} fullWidth>
                {submitting ? 'Saving…' : 'Save entry'}
              </Button>
            </form>
          </section>

          <section aria-labelledby="history-heading">
            <h2 id="history-heading" className="mb-3 text-lg font-semibold">My history</h2>
            {historyError && (
              <p role="alert" className="mb-3 text-sm text-red-700 dark:text-red-300">
                {historyError} <button type="button" className="underline" onClick={() => refreshHistory()}>Try again</button>
              </p>
            )}
            {history === null && !loadError && <p className="text-sm text-gray-500">Loading…</p>}
            {history !== null && history.length === 0 && <p className="text-sm text-gray-500">No entries yet.</p>}
            {history !== null && history.length > 0 && (
              <ul className="space-y-3">
                {history.map(entry => (
                  <li key={entry.id} className="rounded-lg border border-gray-200 p-3 dark:border-gray-700">
                    <div className="flex flex-wrap justify-between gap-2 text-sm">
                      <span className="font-medium">{entry.itemName}: {entry.quantity} {entry.unit}</span>
                      <span className="text-gray-500">{takenLabel(entry.takenUtc)}</span>
                    </div>
                    <p className="text-sm text-gray-600 dark:text-gray-300">From {entry.locationName}</p>
                    {entry.note && <p className="mt-1 text-sm">{entry.note}</p>}
                    {entry.photosPurged ? (
                      <p className="mt-2 text-xs text-gray-500">Photos were deleted after 3 months.</p>
                    ) : (
                      <PhotoPanel entry={entry} />
                    )}
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}
    </div>
  );
}
