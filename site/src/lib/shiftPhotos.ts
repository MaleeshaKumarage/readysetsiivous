const supportedTypes = new Set(['image/png', 'image/jpeg', 'image/webp', 'image/bmp']);
export interface ShiftPhoto { url: string; note: string }
export const photoNoteMessage = 'Add a separate note for each photo.';
const maxPhotos = 10;
const maxPhotoBytes = 10 * 1024 * 1024;
const maxTotalBytes = 20 * 1024 * 1024;

function decodedDataUrlBytes(value: string): number {
  const comma = value.indexOf(',');
  if (comma < 0 || !value.slice(0, comma).toLowerCase().endsWith(';base64')) return 0;
  const encoded = value.length - comma - 1;
  const padding = value.endsWith('==') ? 2 : value.endsWith('=') ? 1 : 0;
  return Math.max(0, Math.floor(encoded * 3 / 4) - padding);
}

export async function readShiftPhotos(files: File[], existing: ShiftPhoto[] = []): Promise<string[]> {
  if (existing.length + files.length > maxPhotos) throw new Error(`A shift submission can include at most ${maxPhotos} photos.`);
  const existingBytes = existing.reduce((total, photo) => total + decodedDataUrlBytes(photo.url), 0);
  let selectedBytes = 0;
  for (const file of files) {
    if (!supportedTypes.has(file.type)) throw new Error('Choose a PNG, JPEG, WebP or BMP photo.');
    if (file.size > maxPhotoBytes) throw new Error('Each photo must be 10 MiB or smaller.');
    selectedBytes += file.size;
  }
  if (existingBytes + selectedBytes > maxTotalBytes) throw new Error('Photos in one shift submission must total 20 MiB or less.');
  return await Promise.all(files.map(file => new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => typeof reader.result === 'string'
      ? resolve(reader.result) : reject(new Error('Could not read the photo. Please select it again.'));
    reader.onerror = reader.onabort = () => reject(new Error('Could not read the photo. Please select it again.'));
    reader.readAsDataURL(file);
  })));
}
