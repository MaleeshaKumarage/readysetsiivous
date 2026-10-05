'use client';

import { useState, useEffect } from 'react';
import { useSearchParams } from 'next/navigation';
import { publicQualityCycle, QualityCycleForm, QualityCycleFormItem } from '@/lib/qualityCycleApi';

export default function QualityCyclePublicClient() {
  const searchParams = useSearchParams();
  const token = searchParams.get('token') || '';
  const tenantSlug = (process.env.NEXT_PUBLIC_TENANT_SLUG || 'readysetsiivous');

  const [form, setForm] = useState<QualityCycleForm | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [items, setItems] = useState<QualityCycleFormItem[]>([]);
  const [cleanerNotes, setCleanerNotes] = useState('');
  const [photoUrls, setPhotoUrls] = useState<string[]>([]);

  useEffect(() => {
    if (token) {
      loadForm();
    } else {
      setLoading(false);
      setError('Missing Quality Cycle Form token.');
    }
  }, [token]);

  async function loadForm() {
    setLoading(true);
    try {
      const data = await publicQualityCycle.getByToken(tenantSlug, token);
      if (data) {
        setForm(data);
        setItems(data.items || []);
        setCleanerNotes(data.cleanerNotes || '');
        setPhotoUrls(data.photoUrls || []);
        if (data.isSubmitted) {
          setSubmitted(true);
        }
      } else {
        setError('Quality Cycle Form not found or link has expired.');
      }
    } catch {
      setError('An error occurred loading the Quality Cycle Form.');
    } finally {
      setLoading(false);
    }
  }

  function handleToggleItem(index: number) {
    const updated = [...items];
    updated[index].isChecked = !updated[index].isChecked;
    setItems(updated);
  }

  function handlePhotoUpload(e: React.ChangeEvent<HTMLInputElement>) {
    const files = e.target.files;
    if (!files) return;

    Array.from(files).forEach((file) => {
      const reader = new FileReader();
      reader.onloadend = () => {
        if (reader.result) {
          setPhotoUrls((prev) => [...prev, reader.result as string]);
        }
      };
      reader.readAsDataURL(file);
    });
  }

  function handleRemovePhoto(index: number) {
    setPhotoUrls(photoUrls.filter((_, i) => i !== index));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      const result = await publicQualityCycle.submit(
        tenantSlug,
        token,
        items,
        photoUrls,
        cleanerNotes
      );

      if (result) {
        setSubmitted(true);
      } else {
        setError('Failed to submit form. Please try again.');
      }
    } catch {
      setError('An error occurred submitting the form.');
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center p-4">
        <p className="text-gray-500 font-medium">Loading Quality Cycle Form...</p>
      </div>
    );
  }

  if (error && !form) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center p-4">
        <div className="bg-white p-6 rounded-lg shadow-md max-w-md w-full text-center space-y-3">
          <div className="text-red-500 text-3xl">⚠️</div>
          <h1 className="text-xl font-bold text-gray-800">Invalid Form Link</h1>
          <p className="text-sm text-gray-600">{error}</p>
        </div>
      </div>
    );
  }

  if (submitted) {
    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center p-4">
        <div className="bg-white p-8 rounded-lg shadow-md max-w-md w-full text-center space-y-4">
          <div className="text-green-500 text-5xl">✓</div>
          <h1 className="text-2xl font-bold text-gray-900">Thank You!</h1>
          <p className="text-sm text-gray-600">
            Your Quality Cycle Form for <strong>{form?.shiftName}</strong> has been successfully submitted.
          </p>
          <div className="pt-2 text-xs text-gray-400">
            Submission Date: {form?.submittedUtc ? new Date(form.submittedUtc).toLocaleString() : new Date().toLocaleString()}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gray-50 py-8 px-4 sm:px-6">
      <div className="max-w-xl mx-auto bg-white rounded-lg shadow-md overflow-hidden">
        <div className="bg-blue-600 p-6 text-white">
          <h1 className="text-xl font-bold">ReadySetSiivous Quality Cycle</h1>
          <p className="text-sm opacity-90 mt-1">{form?.shiftName}</p>
          <p className="text-xs opacity-75 mt-0.5">
            Cleaner: {form?.employeeName} | Date: {form ? new Date(form.shiftOccurrenceUtc).toLocaleDateString() : ''}
          </p>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-6">
          {error && (
            <div className="bg-red-50 text-red-700 p-3 rounded-md text-sm">
              {error}
            </div>
          )}

          <div>
            <h2 className="text-base font-semibold text-gray-800 mb-3">Checklist Items</h2>
            <div className="space-y-3">
              {items.map((item, index) => (
                <label
                  key={index}
                  onClick={() => handleToggleItem(index)}
                  className={`flex items-start p-3 border rounded-md cursor-pointer transition-colors ${
                    item.isChecked ? 'bg-blue-50 border-blue-300' : 'bg-gray-50 hover:bg-gray-100'
                  }`}
                >
                  <input
                    type="checkbox"
                    checked={item.isChecked}
                    onChange={() => {}}
                    className="mt-0.5 h-5 w-5 text-blue-600 rounded border-gray-300 focus:ring-blue-500"
                  />
                  <span className={`ml-3 text-sm font-medium ${item.isChecked ? 'text-blue-900 line-through' : 'text-gray-800'}`}>
                    {item.itemText}
                  </span>
                </label>
              ))}
            </div>
          </div>

          <div>
            <label className="block text-sm font-semibold text-gray-800 mb-2">
              Attach Photos (Optional)
            </label>
            <input
              type="file"
              accept="image/*"
              multiple
              onChange={handlePhotoUpload}
              className="w-full text-sm text-gray-500 file:mr-4 file:py-2 file:px-4 file:rounded-md file:border-0 file:text-sm file:font-semibold file:bg-blue-50 file:text-blue-700 hover:file:bg-blue-100"
            />
            {photoUrls.length > 0 && (
              <div className="mt-3 grid grid-cols-3 gap-2">
                {photoUrls.map((url, idx) => (
                  <div key={idx} className="relative group rounded-md overflow-hidden border">
                    <img src={url} alt={`Upload ${idx + 1}`} className="h-20 w-full object-cover" />
                    <button
                      type="button"
                      onClick={() => handleRemovePhoto(idx)}
                      className="absolute top-1 right-1 bg-red-600 text-white rounded-full h-5 w-5 flex items-center justify-center text-xs"
                    >
                      ✕
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div>
            <label className="block text-sm font-semibold text-gray-800 mb-2">
              Notes / Comments (Optional)
            </label>
            <textarea
              value={cleanerNotes}
              onChange={(e) => setCleanerNotes(e.target.value)}
              placeholder="Any additional notes or observations..."
              rows={3}
              className="w-full p-2.5 border rounded-md text-sm focus:ring-blue-500 focus:border-blue-500"
            />
          </div>

          <button
            type="submit"
            disabled={submitting}
            className="w-full bg-blue-600 hover:bg-blue-700 text-white font-semibold py-3 px-4 rounded-md transition-colors disabled:opacity-50"
          >
            {submitting ? 'Submitting Form...' : 'Submit Quality Cycle Form'}
          </button>
        </form>
      </div>
    </div>
  );
}
