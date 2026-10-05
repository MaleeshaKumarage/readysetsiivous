'use client';

import { useState, useEffect } from 'react';
import { adminQualityCycle, QualityCycleTemplate, QualityCycleForm } from '@/lib/qualityCycleApi';
import { adminShifts, Shift } from '@/lib/adminApi';

export default function AdminQualityCyclePage() {
  const [activeTab, setActiveTab] = useState<'templates' | 'submissions'>('templates');

  // Templates state
  const [templates, setTemplates] = useState<QualityCycleTemplate[]>([]);
  const [loadingTemplates, setLoadingTemplates] = useState(false);
  const [editingTemplate, setEditingTemplate] = useState<Partial<QualityCycleTemplate> | null>(null);
  const [templateTitle, setTemplateTitle] = useState('');
  const [templateDesc, setTemplateDesc] = useState('');
  const [templateItems, setTemplateItems] = useState<string[]>(['', '', '']);

  // Submissions state
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [selectedShiftId, setSelectedShiftId] = useState<string>('');
  const [forms, setForms] = useState<QualityCycleForm[]>([]);
  const [loadingForms, setLoadingForms] = useState(false);

  // Report state
  const [reportYear, setReportYear] = useState<number>(new Date().getFullYear());
  const [reportMonth, setReportMonth] = useState<number>(new Date().getMonth() + 1);

  useEffect(() => {
    loadTemplates();
    loadShifts();
  }, []);

  async function loadTemplates() {
    setLoadingTemplates(true);
    const res = await adminQualityCycle.listTemplates();
    if (res) setTemplates(res);
    setLoadingTemplates(false);
  }

  async function loadShifts() {
    const res = await adminShifts.list();
    if (res) setShifts(res);
  }

  async function loadForms() {
    setLoadingForms(true);
    const res = await adminQualityCycle.listForms(selectedShiftId || undefined);
    if (res) setForms(res);
    setLoadingForms(false);
  }

  useEffect(() => {
    if (activeTab === 'submissions') {
      loadForms();
    }
  }, [activeTab, selectedShiftId]);

  function handleAddItemField() {
    if (templateItems.length < 10) {
      setTemplateItems([...templateItems, '']);
    }
  }

  function handleRemoveItemField(index: number) {
    setTemplateItems(templateItems.filter((_, i) => i !== index));
  }

  function handleItemChange(index: number, val: string) {
    const updated = [...templateItems];
    updated[index] = val;
    setTemplateItems(updated);
  }

  async function handleSaveTemplate(e: React.FormEvent) {
    e.preventDefault();
    const validItems = templateItems.map(i => i.trim()).filter(Boolean);
    if (!templateTitle.trim() || validItems.length === 0) {
      alert('Please provide a template title and at least one checklist item.');
      return;
    }

    if (editingTemplate?.id) {
      await adminQualityCycle.updateTemplate(editingTemplate.id, {
        title: templateTitle,
        description: templateDesc,
        items: validItems,
        isActive: editingTemplate.isActive ?? true,
      });
    } else {
      await adminQualityCycle.createTemplate({
        title: templateTitle,
        description: templateDesc,
        items: validItems,
      });
    }

    setEditingTemplate(null);
    setTemplateTitle('');
    setTemplateDesc('');
    setTemplateItems(['', '', '']);
    loadTemplates();
  }

  function handleEditTemplate(t: QualityCycleTemplate) {
    setEditingTemplate(t);
    setTemplateTitle(t.title);
    setTemplateDesc(t.description || '');
    setTemplateItems(t.items.length > 0 ? t.items : ['', '', '']);
  }

  async function handleDeleteTemplate(id: string) {
    if (confirm('Are you sure you want to delete this quality cycle template?')) {
      await adminQualityCycle.deleteTemplate(id);
      loadTemplates();
    }
  }

  async function handleDispatchNow() {
    const res = await adminQualityCycle.dispatchForms(selectedShiftId || undefined);
    if (res) {
      alert(`Dispatched ${res.dispatchedCount} Quality Cycle forms.`);
      loadForms();
    }
  }

  async function handleDownloadPdf() {
    if (!selectedShiftId) {
      alert('Please select a shift to generate the monthly summary PDF.');
      return;
    }
    await adminQualityCycle.downloadSummaryPdf(selectedShiftId, reportYear, reportMonth);
  }

  return (
    <div className="p-6 max-w-7xl mx-auto space-y-6">
      <div className="flex justify-between items-center border-b pb-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Quality Cycle Management</h1>
          <p className="text-sm text-gray-500">Create checklist templates, inspect submissions, and generate monthly client summary reports.</p>
        </div>
        <div className="flex space-x-2">
          <button
            onClick={() => setActiveTab('templates')}
            className={`px-4 py-2 rounded-md font-medium text-sm ${activeTab === 'templates' ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
          >
            Checklist Templates
          </button>
          <button
            onClick={() => setActiveTab('submissions')}
            className={`px-4 py-2 rounded-md font-medium text-sm ${activeTab === 'submissions' ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200'}`}
          >
            Submissions & PDF Reports
          </button>
        </div>
      </div>

      {activeTab === 'templates' && (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          <div className="md:col-span-1 bg-white p-5 border rounded-lg shadow-sm space-y-4">
            <h2 className="text-lg font-semibold text-gray-800">
              {editingTemplate?.id ? 'Edit Template' : 'Create New Template'}
            </h2>
            <form onSubmit={handleSaveTemplate} className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-gray-700">Template Title *</label>
                <input
                  type="text"
                  value={templateTitle}
                  onChange={(e) => setTemplateTitle(e.target.value)}
                  placeholder="e.g. Daily Office Cleaning Checklist"
                  className="w-full mt-1 p-2 border rounded-md text-sm"
                  required
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-gray-700">Description</label>
                <textarea
                  value={templateDesc}
                  onChange={(e) => setTemplateDesc(e.target.value)}
                  placeholder="Optional description or guidance..."
                  className="w-full mt-1 p-2 border rounded-md text-sm"
                  rows={2}
                />
              </div>

              <div>
                <div className="flex justify-between items-center mb-1">
                  <label className="text-xs font-medium text-gray-700">Checklist Items (up to 10)</label>
                  {templateItems.length < 10 && (
                    <button
                      type="button"
                      onClick={handleAddItemField}
                      className="text-xs text-blue-600 font-semibold hover:underline"
                    >
                      + Add Item
                    </button>
                  )}
                </div>
                <div className="space-y-2 max-h-60 overflow-y-auto">
                  {templateItems.map((item, index) => (
                    <div key={index} className="flex space-x-2 items-center">
                      <span className="text-xs text-gray-400 w-4">{index + 1}.</span>
                      <input
                        type="text"
                        value={item}
                        onChange={(e) => handleItemChange(index, e.target.value)}
                        placeholder={`Item ${index + 1} task...`}
                        className="flex-1 p-1.5 border rounded-md text-sm"
                      />
                      {templateItems.length > 1 && (
                        <button
                          type="button"
                          onClick={() => handleRemoveItemField(index)}
                          className="text-red-500 hover:text-red-700 text-xs px-1"
                        >
                          ✕
                        </button>
                      )}
                    </div>
                  ))}
                </div>
              </div>

              <div className="flex space-x-2 pt-2">
                <button
                  type="submit"
                  className="flex-1 bg-blue-600 text-white py-2 rounded-md font-medium text-sm hover:bg-blue-700"
                >
                  {editingTemplate?.id ? 'Update Template' : 'Create Template'}
                </button>
                {editingTemplate && (
                  <button
                    type="button"
                    onClick={() => {
                      setEditingTemplate(null);
                      setTemplateTitle('');
                      setTemplateDesc('');
                      setTemplateItems(['', '', '']);
                    }}
                    className="px-3 py-2 border rounded-md text-sm text-gray-600 hover:bg-gray-100"
                  >
                    Cancel
                  </button>
                )}
              </div>
            </form>
          </div>

          <div className="md:col-span-2 bg-white p-5 border rounded-lg shadow-sm">
            <h2 className="text-lg font-semibold text-gray-800 mb-4">Existing Templates</h2>
            {loadingTemplates ? (
              <p className="text-sm text-gray-500">Loading templates...</p>
            ) : templates.length === 0 ? (
              <p className="text-sm text-gray-500 italic">No quality cycle templates created yet.</p>
            ) : (
              <div className="space-y-4">
                {templates.map((t) => (
                  <div key={t.id} className="border rounded-md p-4 hover:border-blue-300 transition-colors">
                    <div className="flex justify-between items-start">
                      <div>
                        <h3 className="font-bold text-gray-900">{t.title}</h3>
                        {t.description && <p className="text-xs text-gray-500 mt-1">{t.description}</p>}
                      </div>
                      <div className="flex space-x-2">
                        <button
                          onClick={() => handleEditTemplate(t)}
                          className="text-xs bg-gray-100 hover:bg-gray-200 text-gray-700 px-2.5 py-1 rounded"
                        >
                          Edit
                        </button>
                        <button
                          onClick={() => handleDeleteTemplate(t.id)}
                          className="text-xs bg-red-50 hover:bg-red-100 text-red-600 px-2.5 py-1 rounded"
                        >
                          Delete
                        </button>
                      </div>
                    </div>
                    <div className="mt-3">
                      <span className="text-xs font-semibold text-gray-600">Checklist Items ({t.items.length}):</span>
                      <ul className="mt-1 grid grid-cols-1 sm:grid-cols-2 gap-1 text-xs text-gray-700">
                        {t.items.map((item, idx) => (
                          <li key={idx} className="flex items-center space-x-1.5">
                            <span className="text-blue-500">•</span>
                            <span>{item}</span>
                          </li>
                        ))}
                      </ul>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}

      {activeTab === 'submissions' && (
        <div className="space-y-6">
          <div className="bg-white p-5 border rounded-lg shadow-sm flex flex-wrap gap-4 items-center justify-between">
            <div className="flex flex-wrap gap-4 items-center">
              <div>
                <label className="block text-xs font-medium text-gray-700 mb-1">Filter by Shift</label>
                <select
                  value={selectedShiftId}
                  onChange={(e) => setSelectedShiftId(e.target.value)}
                  className="p-2 border rounded-md text-sm min-w-[200px]"
                >
                  <option value="">-- All Shifts --</option>
                  {shifts.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name}
                    </option>
                  ))}
                </select>
              </div>

              <button
                onClick={handleDispatchNow}
                className="mt-5 bg-indigo-600 hover:bg-indigo-700 text-white text-sm font-medium px-4 py-2 rounded-md"
              >
                Dispatch Forms Now
              </button>
            </div>

            <div className="flex items-center space-x-3 bg-gray-50 p-3 rounded-md border">
              <div>
                <label className="block text-xs font-medium text-gray-700">Year</label>
                <input
                  type="number"
                  value={reportYear}
                  onChange={(e) => setReportYear(Number(e.target.value))}
                  className="w-20 p-1.5 border rounded-md text-sm"
                />
              </div>
              <div>
                <label className="block text-xs font-medium text-gray-700">Month</label>
                <select
                  value={reportMonth}
                  onChange={(e) => setReportMonth(Number(e.target.value))}
                  className="p-1.5 border rounded-md text-sm"
                >
                  {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                    <option key={m} value={m}>
                      {new Date(2025, m - 1, 1).toLocaleString('default', { month: 'short' })}
                    </option>
                  ))}
                </select>
              </div>
              <button
                onClick={handleDownloadPdf}
                className="mt-4 bg-green-600 hover:bg-green-700 text-white text-sm font-medium px-4 py-2 rounded-md flex items-center space-x-1"
              >
                <span>Export PDF Summary</span>
              </button>
            </div>
          </div>

          <div className="bg-white p-5 border rounded-lg shadow-sm">
            <h2 className="text-lg font-semibold text-gray-800 mb-4">Quality Cycle Submissions Log</h2>
            {loadingForms ? (
              <p className="text-sm text-gray-500">Loading form submissions...</p>
            ) : forms.length === 0 ? (
              <p className="text-sm text-gray-500 italic">No quality cycle forms logged for the selected filter.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm text-gray-600">
                  <thead className="bg-gray-50 text-xs text-gray-700 uppercase border-b">
                    <tr>
                      <th className="py-3 px-4">Occurrence Date</th>
                      <th className="py-3 px-4">Shift</th>
                      <th className="py-3 px-4">Cleaner</th>
                      <th className="py-3 px-4">Status</th>
                      <th className="py-3 px-4">Completed Items</th>
                      <th className="py-3 px-4">Notes / Photos</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y">
                    {forms.map((f) => {
                      const completedCount = f.items.filter((i) => i.isChecked).length;
                      return (
                        <tr key={f.id} className="hover:bg-gray-50">
                          <td className="py-3 px-4 font-medium text-gray-900">
                            {new Date(f.shiftOccurrenceUtc).toLocaleString()}
                          </td>
                          <td className="py-3 px-4">{f.shiftName}</td>
                          <td className="py-3 px-4">{f.employeeName}</td>
                          <td className="py-3 px-4">
                            {f.isSubmitted ? (
                              <span className="px-2 py-0.5 text-xs font-semibold rounded bg-green-100 text-green-800">
                                Submitted
                              </span>
                            ) : (
                              <span className="px-2 py-0.5 text-xs font-semibold rounded bg-yellow-100 text-yellow-800">
                                Pending
                              </span>
                            )}
                          </td>
                          <td className="py-3 px-4">
                            {f.isSubmitted ? `${completedCount} / ${f.items.length}` : '-'}
                          </td>
                          <td className="py-3 px-4 text-xs">
                            {f.cleanerNotes && <div>Note: {f.cleanerNotes}</div>}
                            {f.photoUrls && f.photoUrls.length > 0 && (
                              <div className="text-blue-600">{f.photoUrls.length} photo(s) attached</div>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
