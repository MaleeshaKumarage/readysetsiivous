const supportedTypes = new Set(['image/png', 'image/jpeg', 'image/webp', 'image/bmp']);
export const photoNoteMessage = 'Add a note describing the attached photos.';

export async function readShiftPhotos(files: File[]): Promise<string[]> {
  for (const file of files) {
    if (!supportedTypes.has(file.type)) throw new Error('Choose a PNG, JPEG, WebP or BMP photo.');
    if (file.size > 10 * 1024 * 1024) throw new Error('Each photo must be 10 MB or smaller.');
  }
  return await Promise.all(files.map(file => new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => typeof reader.result === 'string'
      ? resolve(reader.result) : reject(new Error('Could not read the photo. Please select it again.'));
    reader.onerror = reader.onabort = () => reject(new Error('Could not read the photo. Please select it again.'));
    reader.readAsDataURL(file);
  })));
}
