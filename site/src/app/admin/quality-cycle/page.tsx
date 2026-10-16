'use client';

import { useState, useEffect } from 'react';
import {
  Title,
  Button,
  Table,
  TextInput,
  Textarea,
  Select,
  NumberInput,
  Group,
  Stack,
  Text,
  Loader,
  Badge,
  ActionIcon,
  Paper,
  SegmentedControl,
  Grid,
  Card,
  Tooltip,
} from '@mantine/core';
import {
  Plus,
  Trash2,
  FileText,
  Download,
  Edit3,
  X,
  CheckCircle2,
  Clock,
  ImageIcon,
} from 'lucide-react';
import { adminQualityCycle, QualityCycleTemplate, QualityCycleForm, MonthlyReport, ReportShare, reportShareUrl } from '@/lib/qualityCycleApi';
import { isAdmin } from '@/lib/auth';
import { adminShifts, Shift } from '@/lib/adminApi';
import { LifecycleTabs, type LifecycleStatus, inLifecycle } from '@/components/LifecycleTabs';

export default function AdminQualityCyclePage() {
  const [activeTab, setActiveTab] = useState<'templates' | 'submissions'>('templates');

  // Templates state
  const [templates, setTemplates] = useState<QualityCycleTemplate[]>([]);
  const [templateStatus, setTemplateStatus] = useState<LifecycleStatus>('active');
  const visibleTemplates = templates.filter(template => inLifecycle(template, templateStatus));
  const [loadingTemplates, setLoadingTemplates] = useState(true);
  const [editingTemplate, setEditingTemplate] = useState<Partial<QualityCycleTemplate> | null>(null);
  const [templateTitle, setTemplateTitle] = useState('');
  const [templateDesc, setTemplateDesc] = useState('');
  const [templateItems, setTemplateItems] = useState<string[]>(['', '', '']);
  const [savingTemplate, setSavingTemplate] = useState(false);
  const [activityBusy, setActivityBusy] = useState<string | null>(null);
  const [activityError, setActivityError] = useState('');

  // Submissions state
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [selectedShiftId, setSelectedShiftId] = useState<string>('');
  const [forms, setForms] = useState<QualityCycleForm[]>([]);
  const [loadingForms, setLoadingForms] = useState(false);
  const [exportingPdf, setExportingPdf] = useState(false);

  const [shares, setShares] = useState<ReportShare[]>([]);
  const allowed = isAdmin();
  const [report, setReport] = useState<MonthlyReport | null>(null);
  const [reportLoading, setReportLoading] = useState(false);
  const [reportError, setReportError] = useState('');
  // Report state
  const [reportYear, setReportYear] = useState<number>(new Date().getFullYear());
  const [reportMonth, setReportMonth] = useState<number>(new Date().getMonth() + 1);

  useEffect(() => {
    if (!allowed) return;
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
    if (allowed && activeTab === 'submissions') {
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
    const validItems = templateItems.map((i) => i.trim()).filter(Boolean);
    if (!templateTitle.trim() || validItems.length === 0) {
      alert('Please provide a template title and at least one checklist item.');
      return;
    }

    setSavingTemplate(true);
    try {
      let saved;
      if (editingTemplate?.id) {
        saved = await adminQualityCycle.updateTemplate(editingTemplate.id, {
          title: templateTitle,
          description: templateDesc,
          items: validItems,
          isActive: editingTemplate.isActive ?? true,
          companyId: editingTemplate.companyId || undefined,
          branchId: editingTemplate.branchId || undefined,
        });
      } else {
        saved = await adminQualityCycle.createTemplate({
          title: templateTitle,
          description: templateDesc,
          items: validItems,
        });
      }

      if (!saved) { alert('Template could not be saved. Please retry.'); return; }
      if (!editingTemplate?.id) setTemplateStatus('active');
      setEditingTemplate(null);
      setTemplateTitle('');
      setTemplateDesc('');
      setTemplateItems(['', '', '']);
      await loadTemplates();
    } finally {
      setSavingTemplate(false);
    }
  }

  function handleEditTemplate(t: QualityCycleTemplate) {
    setEditingTemplate(t);
    setTemplateTitle(t.title);
    setTemplateDesc(t.description || '');
    setTemplateItems(t.items.length > 0 ? t.items : ['', '', '']);
  }

  async function handleTemplateActivity(t: QualityCycleTemplate) {
    setActivityBusy(t.id); setActivityError('');
    try {
      await (t.isActive ? adminQualityCycle.deactivateTemplate(t.id) : adminQualityCycle.reactivateTemplate(t.id));
      // Keep an open editor consistent with the newly saved lifecycle state.
      setEditingTemplate(current => current?.id === t.id ? { ...current, isActive: !t.isActive } : current);
      await loadTemplates();
    } catch (failure) {
      setActivityError(failure instanceof Error ? failure.message : 'Could not change template activity. Please retry.');
    } finally { setActivityBusy(null); }
  }

  async function handleDeleteTemplate(t: QualityCycleTemplate) {
    if (!window.confirm(`Permanently delete inactive checklist template “${t.title}”? Templates referenced by a shift or checklist history are retained.`)) return;
    setActivityBusy(t.id); setActivityError('');
    try {
      await adminQualityCycle.deleteTemplate(t.id);
      if (editingTemplate?.id === t.id) setEditingTemplate(null);
      await loadTemplates();
    } catch (failure) {
      setActivityError(failure instanceof Error ? failure.message : 'Could not delete the template. Please retry.');
    } finally { setActivityBusy(null); }
  }

  async function handleDownloadPdf() {
    if (!selectedShiftId) {
      alert('Please select a shift to generate the monthly summary PDF.');
      return;
    }
    setExportingPdf(true);
    try {
      await adminQualityCycle.downloadSummaryPdf(selectedShiftId, reportYear, reportMonth);
    } catch { setReportError('Report download failed.');
    } finally {
      setExportingPdf(false);
    }
  }

  async function shareReport(shiftId?: string, branchId?: string) {
    if (!report) return;
    setReportError('');
    try { const created = await adminQualityCycle.createReportShare(report.year, report.month, shiftId, branchId);
      if (created) setShares(current => [created, ...current]); else setReportError('Could not create the report link.');
    } catch { setReportError('Could not create the report link.'); }
  }
  async function loadReport() {
    setReportLoading(true); setReportError(''); setReport(null);
    try { const result = await adminQualityCycle.monthlyReport(reportYear, reportMonth, undefined, undefined, selectedShiftId || undefined);
      if (result) setReport(result); else setReportError('Could not load the monthly report.');
    } catch { setReportError('Could not load the monthly report.'); }
    finally { setReportLoading(false); }
  }
  if (!allowed) return <Text role="alert">Only admins can manage checklist templates and view reports.</Text>;
  const monthOptions = Array.from({ length: 12 }, (_, i) => ({
    value: String(i + 1),
    label: new Date(2025, i, 1).toLocaleString('default', { month: 'short' }),
  }));

  return (
    <Stack gap="lg">
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Title order={2}>Quality Cycle Management</Title>
          <Text size="sm" c="dimmed" mt={2}>
            Create checklist templates, inspect submissions, and generate monthly client summary reports.
          </Text>
        </div>
        <SegmentedControl
          value={activeTab}
          onChange={(val) => setActiveTab(val as 'templates' | 'submissions')}
          data={[
            { label: 'Checklist Templates', value: 'templates' },
            { label: 'Submissions & PDF Reports', value: 'submissions' },
          ]}
        />
      </Group>

      {activeTab === 'templates' && (
        <Grid gutter="md">
          <Grid.Col span={{ base: 12, md: 5, lg: 4 }}>
            <Paper p="md" radius="md" withBorder>
              <Stack gap="md">
                <Text fw={600} size="lg">
                  {editingTemplate?.id ? 'Edit Template' : 'Create New Template'}
                </Text>
                <form onSubmit={handleSaveTemplate}>
                  <Stack gap="sm">
                    <TextInput
                      label="Template Title"
                      placeholder="e.g. Daily Office Cleaning Checklist"
                      required
                      value={templateTitle}
                      onChange={(e) => setTemplateTitle(e.target.value)}
                    />

                    <Textarea
                      label="Description"
                      placeholder="Optional description or guidance..."
                      rows={2}
                      value={templateDesc}
                      onChange={(e) => setTemplateDesc(e.target.value)}
                    />

                    <div>
                      <Group justify="space-between" mb={6}>
                        <Text size="xs" fw={500}>
                          Checklist Items (up to 10)
                        </Text>
                        {templateItems.length < 10 && (
                          <Button
                            size="xs"
                            variant="subtle"
                            leftSection={<Plus size={12} />}
                            onClick={handleAddItemField}
                          >
                            Add Item
                          </Button>
                        )}
                      </Group>
                      <Stack gap="xs" style={{ maxHeight: 260, overflowY: 'auto' }}>
                        {templateItems.map((item, index) => (
                          <Group key={index} gap="xs" wrap="nowrap">
                            <Text size="xs" c="dimmed" w={16}>
                              {index + 1}.
                            </Text>
                            <TextInput
                              placeholder={`Item ${index + 1} task...`}
                              size="xs"
                              style={{ flex: 1 }}
                              value={item}
                              onChange={(e) => handleItemChange(index, e.target.value)}
                            />
                            {templateItems.length > 1 && (
                              <ActionIcon
                                variant="subtle"
                                color="red"
                                size="sm"
                                onClick={() => handleRemoveItemField(index)}
                              >
                                <Trash2 size={14} />
                              </ActionIcon>
                            )}
                          </Group>
                        ))}
                      </Stack>
                    </div>

                    <Group justify="flex-end" gap="xs" mt="sm">
                      {editingTemplate && (
                        <Button
                          variant="default"
                          size="sm"
                          onClick={() => {
                            setEditingTemplate(null);
                            setTemplateTitle('');
                            setTemplateDesc('');
                            setTemplateItems(['', '', '']);
                          }}
                        >
                          Cancel
                        </Button>
                      )}
                      <Button type="submit" size="sm" loading={savingTemplate}>
                        {editingTemplate?.id ? 'Update Template' : 'Create Template'}
                      </Button>
                    </Group>
                  </Stack>
                </form>
              </Stack>
            </Paper>
          </Grid.Col>

          <Grid.Col span={{ base: 12, md: 7, lg: 8 }}>
            <Paper p="md" radius="md" withBorder>
              <Stack gap="md">
                <Text fw={600} size="lg">
                  Existing Templates
                </Text>
                {activityError && <Text c="red" role="alert">{activityError}</Text>}
                <LifecycleTabs value={templateStatus} onChange={setTemplateStatus} label="Checklist template activity"
                  activeCount={loadingTemplates ? undefined : templates.filter(template => template.isActive).length}
                  inactiveCount={loadingTemplates ? undefined : templates.filter(template => !template.isActive).length}>
                {loadingTemplates ? (
                  <Group justify="center" py="xl">
                    <Loader size="sm" />
                    <Text size="sm" c="dimmed">
                      Loading templates...
                    </Text>
                  </Group>
                ) : templates.length === 0 ? (
                  <Text size="sm" c="dimmed" fs="italic">
                    No quality cycle templates created yet.
                  </Text>
                ) : visibleTemplates.length === 0 ? (
                  <Text size="sm" c="dimmed" fs="italic">
                    No {templateStatus} checklist templates. Select {templateStatus === 'active' ? 'Inactive' : 'Active'} to view your other templates.
                  </Text>
                ) : (
                  <Stack gap="sm">
                    {visibleTemplates.map((t) => (
                      <Card key={t.id} padding="sm" radius="md" withBorder>
                        <Group justify="space-between" align="flex-start">
                          <div>
                            <Text fw={600} size="sm">
                              {t.title}
                            </Text>
                            {t.description && (
                              <Text size="xs" c="dimmed" mt={2}>
                                {t.description}
                              </Text>
                            )}
                          </div>
                          <Group gap="xs">
                            <Button
                              size="xs"
                              variant="light"
                              leftSection={<Edit3 size={12} />}
                              onClick={() => handleEditTemplate(t)}
                            >
                              Edit
                            </Button>
                            <Button size="xs" variant="light" color={t.isActive ? 'red' : undefined}
                              loading={activityBusy === t.id} disabled={activityBusy !== null && activityBusy !== t.id}
                              onClick={() => void handleTemplateActivity(t)}>
                              {t.isActive ? 'Deactivate' : 'Reactivate'}
                            </Button>
                            {!t.isActive && <Button size="xs" variant="subtle" color="red" loading={activityBusy === t.id} disabled={activityBusy !== null && activityBusy !== t.id} onClick={() => void handleDeleteTemplate(t)}>Delete</Button>}
                          </Group>
                        </Group>

                        <Stack gap={4} mt="xs">
                          <Text size="xs" fw={600} c="dimmed">
                            Checklist Items ({t.items.length}):
                          </Text>
                          <Grid gutter="xs">
                            {t.items.map((item, idx) => (
                              <Grid.Col key={idx} span={{ base: 12, sm: 6 }}>
                                <Group gap={6} align="center" wrap="nowrap">
                                  <Text size="xs" c="indigo.4" fw={700}>
                                    •
                                  </Text>
                                  <Text size="xs" truncate>
                                    {item}
                                  </Text>
                                </Group>
                              </Grid.Col>
                            ))}
                          </Grid>
                        </Stack>
                      </Card>
                    ))}
                  </Stack>
                )}
                </LifecycleTabs>
              </Stack>
            </Paper>
          </Grid.Col>
        </Grid>
      )}

      {activeTab === 'submissions' && (
        <Stack gap="md">
          <Paper p="md" withBorder><Stack>
            <Title order={3}>Monthly branch and shift reports</Title>
            <Button onClick={loadReport} loading={reportLoading}>Load monthly report</Button>
            {reportError && <Text c="red" role="alert">{reportError}</Text>}
            {report && <>
              <Text size="sm">{report.year}-{String(report.month).padStart(2, '0')} · {report.timeZoneId}. Hours count each scheduled occurrence once, regardless of cleaner count.</Text>
              <Table.ScrollContainer minWidth={800}><Table withTableBorder>
                <Table.Thead><Table.Tr>{['Company / branch', 'Shift', 'Occurrences / hours', 'Recorded / missing / pending', 'Tasks submitted / expected', 'Activities performed', 'Export'].map(h => <Table.Th key={h}>{h}</Table.Th>)}</Table.Tr></Table.Thead>
                <Table.Tbody>{report.shifts.map(row => <Table.Tr key={`${row.shiftId}-${row.branchId}`}>
                  <Table.Td>{row.companyName} / {row.branchName}</Table.Td><Table.Td>{row.shiftName}</Table.Td>
                  <Table.Td>{row.scheduledOccurrences} / {row.scheduledHours}</Table.Td>
                  <Table.Td>{row.completedOccurrences} / {row.missingOccurrences} / {row.pendingOccurrences}</Table.Td>
                  <Table.Td>{row.submittedTasks} / {row.totalTasks}</Table.Td><Table.Td>{row.items?.map(item => <Text key={item.itemText} size="sm">{item.itemText}: {item.daysPerformed} days ({item.recordedOccurrences} occurrences)</Text>)}</Table.Td>
                  <Table.Td><Button size="xs" onClick={async () => { try { await adminQualityCycle.downloadSummaryPdf(row.shiftId, report.year, report.month); } catch { setReportError('Shift report download failed.'); } }}>Shift PDF</Button>
                  <Button size="xs" variant="light" onClick={async () => { try { await adminQualityCycle.downloadBranchPdf(row.branchId, report.year, report.month); } catch { setReportError('Branch report download failed.'); } }}>Branch PDF</Button>
                  <Button size="xs" variant="subtle" onClick={() => shareReport(row.shiftId)}>Share shift report</Button>
                  <Button size="xs" variant="subtle" onClick={() => shareReport(undefined, row.branchId)}>Share branch report</Button></Table.Td>
                </Table.Tr>)}</Table.Tbody>
              </Table></Table.ScrollContainer>
              {report.shifts.length === 0 && <Text>No scheduled quality cycle tasks for this month and filter.</Text>}
            </>}
            <Text size="sm">New report links include shift photos and notes. Anyone with the link can view them for 30 days, unless you revoke it.</Text>
            <Button variant="light" onClick={async () => { const result = await adminQualityCycle.listReportShares(); if (result) setShares(result); else setReportError('Could not load shared reports.'); }}>Manage shared report links</Button>
            {shares.map(share => <Group key={share.id}>
              <Text size="sm">{share.year}-{String(share.month).padStart(2, '0')} · expires {new Date(share.expiresUtc).toLocaleDateString()}</Text>
              {share.isRevoked || new Date(share.expiresUtc).getTime() <= Date.now() ? <Text size="sm">Inactive link</Text> : <>
                <Text component="a" href={reportShareUrl(share)} target="_blank" rel="noreferrer">Open report</Text>
                <TextInput aria-label="Shareable report link" readOnly value={reportShareUrl(share)} style={{ flex: 1, minWidth: 220 }} />
                <Button size="xs" onClick={async () => { try { await navigator.clipboard.writeText(reportShareUrl(share)); } catch { setReportError('Select and copy the report link.'); } }}>Copy link</Button>
                <Button size="xs" color="red" onClick={async () => { if (await adminQualityCycle.revokeReportShare(share.id)) setShares(current => current.map(item => item.id === share.id ? { ...item, isRevoked: true } : item)); else setReportError('Could not revoke the link.'); }}>Revoke link</Button>
              </>}
            </Group>)}
          </Stack></Paper>
          <Paper p="md" radius="md" withBorder>
            <Group justify="space-between" align="flex-end" wrap="wrap" gap="md">
              <Group gap="md" align="flex-end" wrap="wrap">
                <Select
                  label="Filter by Shift"
                  placeholder="-- All Shifts --"
                  data={[
                    { value: '', label: '-- All Shifts --' },
                    ...shifts.map((s) => ({ value: s.id, label: s.name })),
                  ]}
                  value={selectedShiftId}
                  onChange={(v) => setSelectedShiftId(v ?? '')}
                  style={{ minWidth: 220 }}
                  clearable
                  searchable
                />
              </Group>

              <Group gap="xs" align="flex-end" wrap="wrap">
                <NumberInput
                  label="Year"
                  value={reportYear}
                  onChange={(v) => setReportYear(Number(v) || new Date().getFullYear())}
                  style={{ width: 100 }}
                />
                <Select
                  label="Month"
                  data={monthOptions}
                  value={String(reportMonth)}
                  onChange={(v) => setReportMonth(Number(v) || 1)}
                  style={{ width: 110 }}
                />
                <Button
                  leftSection={<Download size={16} />}
                  color="green"
                  loading={exportingPdf}
                  onClick={handleDownloadPdf}
                >
                  Export PDF Summary
                </Button>
              </Group>
            </Group>
          </Paper>

          <Paper p="md" radius="md" withBorder>
            <Stack gap="md">
              <Text fw={600} size="lg">
                Quality Cycle Submissions Log
              </Text>

              {loadingForms ? (
                <Group justify="center" py="xl">
                  <Loader size="sm" />
                  <Text size="sm" c="dimmed">
                    Loading form submissions...
                  </Text>
                </Group>
              ) : forms.length === 0 ? (
                <Text size="sm" c="dimmed" fs="italic">
                  No quality cycle forms logged for the selected filter.
                </Text>
              ) : (
                <Table.ScrollContainer minWidth={1050}>
                  <Table striped highlightOnHover withTableBorder>
                    <Table.Thead>
                      <Table.Tr>
                        <Table.Th>Scheduled shift</Table.Th>
                        <Table.Th>Actual start</Table.Th>
                        <Table.Th>Actual end</Table.Th>
                        <Table.Th>Shift</Table.Th>
                        <Table.Th>Cleaner</Table.Th>
                        <Table.Th>Status</Table.Th>
                        <Table.Th>Selected Activities</Table.Th>
                        <Table.Th>Notes / Photos</Table.Th>
                      </Table.Tr>
                    </Table.Thead>
                    <Table.Tbody>
                      {forms.map((f) => {
                        const completedCount = f.items.filter((i) => i.isChecked).length;
                        return (
                          <Table.Tr key={f.id}>
                            <Table.Td fw={500}>
                              {new Date(f.shiftOccurrenceUtc).toLocaleString(undefined, { timeZone: report?.timeZoneId ?? 'Europe/Helsinki' })}
                            </Table.Td>
                            <Table.Td>{f.startedAtUtc ? new Date(f.startedAtUtc).toLocaleString(undefined, { timeZone: report?.timeZoneId ?? 'Europe/Helsinki' }) : '—'}</Table.Td>
                            <Table.Td>{f.endedAtUtc ? new Date(f.endedAtUtc).toLocaleString(undefined, { timeZone: report?.timeZoneId ?? 'Europe/Helsinki' }) : '—'}</Table.Td>
                            <Table.Td>{f.shiftName}</Table.Td>
                            <Table.Td>{f.employeeName}</Table.Td>
                            <Table.Td>
                              {f.isSubmitted ? (
                                <Badge color="green" leftSection={<CheckCircle2 size={12} />}>
                                  Submitted
                                </Badge>
                              ) : (
                                <Badge color="yellow" leftSection={<Clock size={12} />}>
                                  Pending
                                </Badge>
                              )}
                            </Table.Td>
                            <Table.Td>
                              {f.isSubmitted ? `${completedCount} / ${f.items.length}` : '—'}
                            </Table.Td>
                            <Table.Td>
                              <Stack gap={2}>
                                {f.cleanerNotes && (
                                  <Text size="xs" c="dimmed">
                                    Note: {f.cleanerNotes}
                                  </Text>
                                )}
                                {f.photoUrls && f.photoUrls.length > 0 && (
                                  <Group gap={4}>
                                    <ImageIcon size={12} className="text-indigo-400" />
                                    <Text size="xs" c="indigo">
                                      {f.photoUrls.length} photo(s) attached
                                    </Text>
                                  </Group>
                                )}
                              </Stack>
                            </Table.Td>
                          </Table.Tr>
                        );
                      })}
                    </Table.Tbody>
                  </Table>
                </Table.ScrollContainer>
              )}
            </Stack>
          </Paper>
        </Stack>
      )}
    </Stack>
  );
}
