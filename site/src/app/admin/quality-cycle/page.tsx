'use client';

import { useState, useEffect } from 'react';
import {
  Title,
  Button,
  TextInput,
  Textarea,
  Select,
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
} from 'lucide-react';
import { adminQualityCycle, QualityCycleTemplate } from '@/lib/qualityCycleApi';
import ReportWorkspace from './ReportWorkspace';
import { isAdmin } from '@/lib/auth';
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

  const allowed = isAdmin();

  useEffect(() => { if (allowed) void loadTemplates(); }, []);

  async function loadTemplates() {
    setLoadingTemplates(true);
    const res = await adminQualityCycle.listTemplates();
    if (res) setTemplates(res);
    setLoadingTemplates(false);
  }

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

  if (!allowed) return <Text role="alert">Only admins can manage checklist templates and view reports.</Text>;

  return (
    <Stack gap="lg" p="md" style={{ color: 'var(--mantine-color-text)', background: 'var(--mantine-color-body)', borderRadius: 12 }}>
      <Group justify="space-between" align="flex-start" wrap="wrap">
        <div>
          <Title order={2}>Quality Cycle Management</Title>
          <Text size="sm" c="dimmed" mt={2}>
            Create checklist templates and prepare, export or share client quality reports.
          </Text>
        </div>
        <SegmentedControl
          value={activeTab}
          onChange={(val) => setActiveTab(val as 'templates' | 'submissions')}
          data={[
            { label: 'Checklist Templates', value: 'templates' },
            { label: 'Reports', value: 'submissions' },
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
                                aria-label={`Remove item ${index + 1}`}
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

      {activeTab === 'submissions' && <ReportWorkspace />}
    </Stack>
  );
}
