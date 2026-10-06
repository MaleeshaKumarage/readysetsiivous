'use client';

import { useState, useEffect } from 'react';
import { useSearchParams } from 'next/navigation';
import {
  CheckCircle2,
  AlertTriangle,
  Upload,
  Trash2,
  FileText,
  Loader2,
  Sparkles,
  Calendar,
  User,
} from 'lucide-react';
import { publicQualityCycle, QualityCycleForm, QualityCycleFormItem } from '@/lib/qualityCycleApi';
import { Logo } from '@/ds/Logo';
import { Card } from '@/ds/Card';
import { Button } from '@/ds/Button';

export default function QualityCyclePublicClient() {
  const searchParams = useSearchParams();
  const token = searchParams.get('token') || '';
  const tenantSlug = process.env.NEXT_PUBLIC_TENANT_SLUG || 'readysetsiivous';

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
      <div className="min-h-screen bg-gradient-to-br from-accent-50 via-white to-brand-50 dark:from-gray-950 dark:via-gray-950 dark:to-gray-900 flex flex-col items-center justify-center p-4">
        <div className="flex items-center gap-3 text-accent-800 dark:text-gray-200 font-medium">
          <Loader2 className="w-6 h-6 animate-spin text-brand-500" />
          <span>Loading Quality Cycle Form...</span>
        </div>
      </div>
    );
  }

  if (error && !form) {
    return (
      <div className="min-h-screen bg-gradient-to-br from-accent-50 via-white to-brand-50 dark:from-gray-950 dark:via-gray-950 dark:to-gray-900 flex items-center justify-center p-4">
        <Card variant="elevated" padding="lg" className="max-w-md w-full text-center space-y-4">
          <div className="w-12 h-12 rounded-full bg-red-100 text-red-600 flex items-center justify-center mx-auto">
            <AlertTriangle className="w-6 h-6" />
          </div>
          <h1 className="text-xl font-bold text-gray-900 dark:text-gray-100">Invalid Form Link</h1>
          <p className="text-sm text-gray-600 dark:text-gray-400">{error}</p>
        </Card>
      </div>
    );
  }

  if (submitted) {
    return (
      <div className="min-h-screen bg-gradient-to-br from-accent-50 via-white to-brand-50 dark:from-gray-950 dark:via-gray-950 dark:to-gray-900 flex flex-col items-center justify-center p-4">
        <div className="mb-6">
          <Logo size="lg" />
        </div>
        <Card variant="elevated" padding="lg" className="max-w-md w-full text-center space-y-4">
          <div className="w-16 h-16 rounded-full bg-emerald-100 text-emerald-600 flex items-center justify-center mx-auto">
            <CheckCircle2 className="w-10 h-10" />
          </div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">Thank You!</h1>
          <p className="text-sm text-gray-600 dark:text-gray-300">
            Your Quality Cycle Form for <strong>{form?.shiftName}</strong> has been successfully submitted.
          </p>
          <div className="pt-2 text-xs text-gray-400 dark:text-gray-500 border-t border-gray-100 dark:border-gray-700">
            Submission Date: {form?.submittedUtc ? new Date(form.submittedUtc).toLocaleString() : new Date().toLocaleString()}
          </div>
        </Card>
      </div>
    );
  }

  const checkedCount = items.filter((i) => i.isChecked).length;

  return (
    <div className="min-h-screen bg-gradient-to-br from-accent-50 via-white to-brand-50 dark:from-gray-950 dark:via-gray-950 dark:to-gray-900 py-10 px-4 sm:px-6">
      <div className="max-w-xl mx-auto space-y-6">
        <div className="flex justify-center">
          <Logo size="lg" />
        </div>

        <Card variant="elevated" padding="none" className="overflow-hidden">
          {/* Header */}
          <div className="bg-gradient-to-r from-accent-900 to-accent-800 p-6 text-white space-y-2">
            <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-brand-500/20 text-brand-300 text-xs font-semibold">
              <Sparkles className="w-3.5 h-3.5" /> Quality Assurance
            </div>
            <h1 className="text-xl font-extrabold tracking-tight">{form?.shiftName}</h1>
            <div className="flex flex-wrap gap-4 text-xs text-accent-200 pt-1">
              <span className="flex items-center gap-1">
                <User className="w-3.5 h-3.5 text-brand-400" />
                {form?.employeeName}
              </span>
              <span className="flex items-center gap-1">
                <Calendar className="w-3.5 h-3.5 text-brand-400" />
                {form ? new Date(form.shiftOccurrenceUtc).toLocaleDateString() : ''}
              </span>
            </div>
          </div>

          <form onSubmit={handleSubmit} className="p-6 space-y-6">
            {error && (
              <div className="flex items-center gap-2 p-3 rounded-xl bg-red-50 border border-red-200 text-red-700 text-sm">
                <AlertTriangle className="w-4 h-4 shrink-0" />
                <span>{error}</span>
              </div>
            )}

            {/* Checklist items */}
            <div className="space-y-3">
              <div className="flex justify-between items-center">
                <h2 className="text-sm font-bold text-gray-900 dark:text-gray-100 uppercase tracking-wider">
                  Checklist Items
                </h2>
                <span className="text-xs font-medium text-brand-700 dark:text-brand-400 bg-brand-50 dark:bg-brand-950 px-2.5 py-1 rounded-full">
                  {checkedCount} / {items.length} Completed
                </span>
              </div>

              <div className="space-y-2.5">
                {items.map((item, index) => (
                  <div
                    key={index}
                    onClick={() => handleToggleItem(index)}
                    className={`flex items-center p-3.5 rounded-xl border transition-all cursor-pointer select-none ${
                      item.isChecked
                        ? 'bg-brand-50/60 dark:bg-brand-950/30 border-brand-300 dark:border-brand-800 text-brand-950 dark:text-brand-200'
                        : 'bg-white dark:bg-gray-800 border-gray-200 dark:border-gray-700 hover:border-brand-200 text-gray-800 dark:text-gray-200'
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={item.isChecked}
                      onChange={() => {}}
                      className="h-5 w-5 rounded border-gray-300 text-accent-900 focus:ring-brand-500 cursor-pointer"
                    />
                    <span
                      className={`ml-3 text-sm font-medium transition-all ${
                        item.isChecked ? 'line-through opacity-75' : ''
                      }`}
                    >
                      {item.itemText}
                    </span>
                  </div>
                ))}
              </div>
            </div>

            {/* Photo Attachment */}
            <div className="space-y-3 pt-2 border-t border-gray-100 dark:border-gray-800">
              <label className="block text-sm font-bold text-gray-900 dark:text-gray-100 uppercase tracking-wider">
                Attach Photos (Optional)
              </label>

              <label className="flex flex-col items-center justify-center p-4 border-2 border-dashed border-gray-300 dark:border-gray-700 hover:border-brand-400 dark:hover:border-brand-500 rounded-2xl cursor-pointer bg-gray-50/50 dark:bg-gray-800/50 hover:bg-brand-50/30 transition-colors group">
                <Upload className="w-6 h-6 text-gray-400 group-hover:text-brand-600 mb-1 transition-colors" />
                <span className="text-xs font-semibold text-gray-700 dark:text-gray-300">
                  Click or drag images to upload
                </span>
                <span className="text-[11px] text-gray-400">PNG, JPG up to 10MB</span>
                <input
                  type="file"
                  accept="image/*"
                  multiple
                  onChange={handlePhotoUpload}
                  className="hidden"
                />
              </label>

              {photoUrls.length > 0 && (
                <div className="grid grid-cols-3 gap-3 pt-1">
                  {photoUrls.map((url, idx) => (
                    <div key={idx} className="relative group rounded-xl overflow-hidden border border-gray-200 dark:border-gray-700 shadow-sm">
                      <img src={url} alt={`Upload ${idx + 1}`} className="h-24 w-full object-cover" />
                      <button
                        type="button"
                        onClick={() => handleRemovePhoto(idx)}
                        className="absolute top-1.5 right-1.5 bg-red-600 hover:bg-red-700 text-white rounded-full p-1 shadow-md transition-colors"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* Cleaner Notes */}
            <div className="space-y-2 pt-2 border-t border-gray-100 dark:border-gray-800">
              <label className="block text-sm font-bold text-gray-900 dark:text-gray-100 uppercase tracking-wider">
                Notes / Comments (Optional)
              </label>
              <textarea
                value={cleanerNotes}
                onChange={(e) => setCleanerNotes(e.target.value)}
                placeholder="Any additional notes or observations..."
                rows={3}
                className="w-full p-3 rounded-xl border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 text-sm focus:ring-2 focus:ring-brand-500 focus:border-brand-500 outline-none transition-all text-gray-800 dark:text-gray-200 placeholder-gray-400"
              />
            </div>

            {/* Submit Button */}
            <Button
              type="submit"
              variant="primary"
              size="lg"
              fullWidth
              disabled={submitting}
              className="mt-4"
            >
              {submitting ? (
                <span className="flex items-center gap-2">
                  <Loader2 className="w-5 h-5 animate-spin" /> Submitting Form...
                </span>
              ) : (
                'Submit Quality Cycle Form'
              )}
            </Button>
          </form>
        </Card>
      </div>
    </div>
  );
}
