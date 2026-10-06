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
  Send,
  Download,
  Edit3,
  X,
  CheckCircle2,
  Clock,
  ImageIcon,
} from 'lucide-react';
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
  const [savingTemplate, setSavingTemplate] = useState(false);

  // Submissions state
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [selectedShiftId, setSelectedShiftId] = useState<string>('');
  const [forms, setForms] = useState<QualityCycleForm[]>([]);
  const [loadingForms, setLoadingForms] = useState(false);
  const [dispatching, setDispatching] = useState(false);
  const [exportingPdf, setExportingPdf] = useState(false);

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
    const validItems = templateItems.map((i) => i.trim()).filter(Boolean);
    if (!templateTitle.trim() || validItems.length === 0) {
      alert('Please provide a template title and at least one checklist item.');
      return;
    }

    setSavingTemplate(true);
    try {
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

  async function handleDeleteTemplate(id: string) {
    if (confirm('Are you sure you want to delete this quality cycle template?')) {
      await adminQualityCycle.deleteTemplate(id);
      loadTemplates();
    }
  }

  async function handleDispatchNow() {
    setDispatching(true);
    try {
      const res = await adminQualityCycle.dispatchForms(selectedShiftId || undefined);
      if (res) {
        alert(`Dispatched ${res.dispatchedCount} Quality Cycle forms.`);
        loadForms();
      }
    } finally {
      setDispatching(false);
    }
  }

  async function handleDownloadPdf() {
    if (!selectedShiftId) {
      alert('Please select a shift to generate the monthly summary PDF.');
      return;
    }
    setExportingPdf(true);
    try {
      await adminQualityCycle.downloadSummaryPdf(selectedShiftId, reportYear, reportMonth);
    } finally {
      setExportingPdf(false);
    }
  }

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
                ) : (
                  <Stack gap="sm">
                    {templates.map((t) => (
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
                            <ActionIcon
                              size="sm"
                              variant="light"
                              color="red"
                              onClick={() => handleDeleteTemplate(t.id)}
                            >
                              <Trash2 size={14} />
                            </ActionIcon>
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
              </Stack>
            </Paper>
          </Grid.Col>
        </Grid>
      )}

      {activeTab === 'submissions' && (
        <Stack gap="md">
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

                <Button
                  leftSection={<Send size={16} />}
                  variant="filled"
                  color="indigo"
                  loading={dispatching}
                  onClick={handleDispatchNow}
                >
                  Dispatch Forms Now
                </Button>
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
                <Table striped highlightOnHover withTableBorder>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Occurrence Date</Table.Th>
                      <Table.Th>Shift</Table.Th>
                      <Table.Th>Cleaner</Table.Th>
                      <Table.Th>Status</Table.Th>
                      <Table.Th>Completed Items</Table.Th>
                      <Table.Th>Notes / Photos</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {forms.map((f) => {
                      const completedCount = f.items.filter((i) => i.isChecked).length;
                      return (
                        <Table.Tr key={f.id}>
                          <Table.Td fw={500}>
                            {new Date(f.shiftOccurrenceUtc).toLocaleString()}
                          </Table.Td>
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
              )}
            </Stack>
          </Paper>
        </Stack>
      )}
    </Stack>
  );
}
