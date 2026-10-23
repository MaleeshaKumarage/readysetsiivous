import { test, expect, type Page } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
test.use({ launchOptions: { args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] }, permissions: ['microphone'] });
const id = '11111111-1111-4111-8111-111111111111';
const fields = { name: 'Voice prospect', businessId: '123', contactName: 'Contact', contactPhone: '123', notes: '', description: 'Cleaning lead', emails: ['customer@example.test'], primaryEmail: 'customer@example.test', status: 'New', branchName: 'Office', street: 'Street', postalCode: '00100', city: 'Helsinki', country: 'FI', branchPhone: '', branchEmail: '' };
async function setup(page: Page, role = 'admin') {
  await setupAdminMocks(page);
  await page.addInitScript(() => {
    const original = navigator.mediaDevices?.getUserMedia.bind(navigator.mediaDevices);
    if (original) navigator.mediaDevices.getUserMedia = async constraints => { const stream = await original(constraints); ((window as any).__TEST_AUDIO_STREAMS__ ??= []).push(stream); return stream; };
  });
  if (role !== 'admin') {
    await page.addInitScript(role => { (window as any).__MOCK_ROLES__ = [role]; }, role);
    await page.route(/\/api\/v1\/me$/, route => route.fulfill({ headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' }, json: { realm: 'readysetsiivous', roles: [JSON.stringify({ roles: [role] })], employee: { id: 'manager-1', role, isActive: true } } }));
  }
  const state = { voices: [] as any[], followUps: [] as any[], deliveries: [] as any[], requests: [] as { method: string; path: string; body: any }[], images: [] as { id: string }[], leadRows: [] as any[], imageFailure: false, detailFailure: false, audioFailure: false, uploadFailure: false, recipientFailure: false, saveFailure: false, lastRecording: '', audioBytes: Buffer.from('audio fixture') as Buffer, lead: { id, version: 'v1', fields: { ...fields }, isActive: true, status: 'New', imageCount: 0, companyId: null as string | null, branchId: null, convertedUtc: null, updatedUtc: '2026-10-23T10:00:00Z' } };
  const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'GET,POST,PUT,DELETE,OPTIONS' };
  const queries: string[] = [];
  const detail = () => ({ lead: state.lead, images: state.images, customerAvailable: false, capabilities: { canEditMetadata: role === 'admin', canRecord: state.lead.isActive && !state.lead.companyId && state.lead.status !== 'Lost', canSchedule: state.lead.isActive && !state.lead.companyId, canDeleteVoice: role === 'admin' && !state.lead.companyId } });
  await page.route('**/api/v1/admin/leads**', route => route.fulfill({ headers, json: new URL(route.request().url()).pathname.endsWith('/leads') ? { items: [state.lead], total: 1 } : detail() }));
  await page.route('**/api/v1/manager/leads**', async route => {
    const request = route.request(); const path = new URL(request.url()).pathname; const method = request.method();
    if (method === 'OPTIONS') return route.fulfill({ headers });
    if (method === 'GET') { queries.push(request.url());
      if (path.endsWith('/settings')) return route.fulfill({ headers, json: { timeZoneId: 'Europe/Helsinki' } });
      if (path.endsWith('/recipients')) return route.fulfill({ headers, status: state.recipientFailure ? 503 : 200, json: state.recipientFailure ? { detail: 'Admin directory unavailable' } : [{ id: 'admin-1', name: 'Anna Admin', email: 'anna@example.test' }, { id: 'admin-2', name: 'Other Admin', email: 'other@example.test' }] });
      if (path.includes('/images/')) return route.fulfill({ headers, status: state.imageFailure ? 503 : 200, contentType: state.imageFailure ? 'application/json' : 'image/png', body: state.imageFailure ? JSON.stringify({ detail: 'Image storage temporarily unavailable' }) : Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=', 'base64') });
      if (path === `/api/v1/manager/leads/${id}` && state.detailFailure) return route.fulfill({ headers, status: 503, json: { detail: 'Lead detail temporarily unavailable' } });
      if (path.endsWith('/audio')) return route.fulfill({ headers, status: state.audioFailure ? 503 : 200, contentType: 'audio/webm', body: state.audioFailure ? 'Unavailable' : state.audioBytes });
      const items = path.endsWith('/voice-notes') ? state.voices : path.endsWith('/follow-ups') ? state.followUps : path.endsWith('/deliveries') ? state.deliveries : null;
      return route.fulfill({ headers, json: items ? { items: items.slice(Number(new URL(request.url()).searchParams.get('skip') || 0), Number(new URL(request.url()).searchParams.get('skip') || 0) + 25), total: items.length } : path.endsWith('/leads') ? { items: (state.leadRows.length ? state.leadRows : [state.lead]).slice(Number(new URL(request.url()).searchParams.get('skip') || 0), Number(new URL(request.url()).searchParams.get('skip') || 0) + 25), total: state.leadRows.length || 1 } : detail() });
    }
    const body = path.includes('/voice-notes/') && method === 'POST' ? request.postDataBuffer() : request.postDataJSON(); state.requests.push({ method, path, body });
    if (path.includes('/voice-notes/') && method === 'POST') {
      const recordingId = path.split('/').at(-1)!; state.lastRecording = recordingId;
      if (Buffer.isBuffer(body)) { const start = body.indexOf(Buffer.from('\r\n\r\n')) + 4; const end = body.lastIndexOf(Buffer.from('\r\n--')); state.audioBytes = body.subarray(start, end); }
      expect(request.headers()['content-type']).toContain('multipart/form-data; boundary=');
      if (state.uploadFailure) return route.fulfill({ headers, status: 503, json: { detail: 'Recording storage unavailable' } });
      const note = { id: recordingId, version: 'voice-v1', authorName: role === 'manager' ? 'Manager Person' : 'Admin Person', createdUtc: '2026-10-23T10:00:00Z', durationSeconds: 1, contentType: 'audio/webm' }; if (!state.voices.some(v => v.id === note.id)) state.voices.push(note);
      return route.fulfill({ headers, json: note });
    }
    if (path.includes('/voice-notes/') && method === 'DELETE') { state.voices = state.voices.filter(v => !path.endsWith(v.id)); return route.fulfill({ headers, status: 204 }); }
    if (path.endsWith('/cancel')) { state.followUps.find(f => path.includes(f.id)).state = 'Cancelled'; return route.fulfill({ headers, status: 204 }); }
    if (state.saveFailure) return route.fulfill({ headers, status: 409, json: { detail: 'Follow-up changed. Refresh before editing.' } });
    const input = method === 'PUT' ? body.input : body; const value = { ...input, id: method === 'PUT' ? path.split('/').at(-1) : 'follow-' + (state.followUps.length + 1), version: 'follow-v1', state: 'Active', nextDate: input.firstDate, authorName: 'Manager Person' };
    if (method === 'PUT') state.followUps = state.followUps.map(f => f.id === value.id ? value : f); else state.followUps.push(value);
    return route.fulfill({ headers, json: value });
  });
  return Object.assign(state, { queries });
}
async function open(page: Page, role = 'admin') { await page.goto(role === 'admin' ? `/admin/leads/?lead=${id}` : '/manager/leads/'); if (role !== 'admin') await page.getByRole('button', { name: 'View lead', exact: true }).click(); await expect(page.getByRole('heading', { name: 'Voice notes', exact: true })).toBeVisible(); }
test('real browser recording previews, preserves retry identity, saves attribution and releases microphone', async ({ page }) => {
  const state = await setup(page); await open(page);
  await page.getByRole('button', { name: 'Record voice note', exact: true }).click(); await expect(page.getByText(/Recording 0:/)).toBeVisible();
  await page.waitForTimeout(500); await page.getByRole('button', { name: 'Stop recording', exact: true }).click(); await expect(page.getByLabel('Preview voice recording')).toBeVisible();
  expect(await page.evaluate(() => (window as any).__TEST_AUDIO_STREAMS__.every((stream: MediaStream) => stream.getTracks().every(track => track.readyState === 'ended')))).toBe(true);
  state.uploadFailure = true; await page.getByRole('button', { name: 'Save recording', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'Your recording is kept' })).toBeVisible();
  const recordingId = state.lastRecording; state.uploadFailure = false; await page.getByRole('button', { name: 'Save recording', exact: true }).click();
  await expect(page.getByText('Admin Person', { exact: true })).toBeVisible(); expect(state.lastRecording).toBe(recordingId); expect(state.voices).toHaveLength(1);
  state.audioFailure = true; await page.getByRole('button', { name: 'Load audio', exact: true }).click(); await expect(page.getByRole('button', { name: 'Retry audio', exact: true })).toBeVisible();
  state.audioFailure = false; await page.getByRole('button', { name: 'Retry audio', exact: true }).click(); await expect(page.getByLabel('Voice note by Admin Person')).toBeVisible();
  page.once('dialog', dialog => dialog.dismiss()); await page.getByRole('button', { name: 'Delete voice note' }).click(); expect(state.voices).toHaveLength(1);
  page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Delete voice note' }).click(); await expect(page.getByText('No voice notes yet.')).toBeVisible();
});
test('recording discard and closing a draft request confirmation, unsupported and denied microphones explain recovery', async ({ page }) => {
  await setup(page); await open(page); await page.getByRole('button', { name: 'Record voice note', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stop recording', exact: true })).toBeVisible();
  await page.waitForTimeout(500); await page.getByRole('button', { name: 'Stop recording', exact: true }).click(); await expect(page.getByLabel('Preview voice recording')).toBeVisible();
  page.once('dialog', dialog => dialog.dismiss()); await page.getByRole('dialog').getByRole('button', { name: 'Close lead', exact: true }).click(); await expect(page.getByLabel('Preview voice recording')).toBeVisible();
  await page.getByRole('button', { name: 'Discard recording', exact: true }).click(); await expect(page.getByLabel('Preview voice recording')).toHaveCount(0);
  await page.evaluate(() => { navigator.mediaDevices.getUserMedia = async () => { throw new DOMException('Denied', 'NotAllowedError'); }; });
  await page.getByRole('button', { name: 'Record voice note', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'Microphone permission was denied' })).toBeVisible();
  await page.evaluate(() => { (window as any).MediaRecorder = undefined; }); await page.getByRole('button', { name: 'Record voice note', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'not supported' })).toBeVisible();
});
test('manager creates multiple recurring follow-ups, retries directory, edits, cancels and sees delivery history', async ({ page }) => {
  const state = await setup(page, 'manager'); state.recipientFailure = true; await open(page, 'manager');
  await expect(page.getByRole('button', { name: 'Convert to customer', exact: true })).toHaveCount(0); await expect(page.getByRole('button', { name: 'New lead', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Add follow-up', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'Admin directory unavailable' })).toBeVisible();
  state.recipientFailure = false; await page.getByRole('button', { name: 'Reload admin recipients' }).click();
  await page.getByRole('button', { name: 'Save follow-up', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'Enter a reason' })).toBeVisible();
  await page.getByRole('textbox', { name: 'Follow-up reason', exact: true }).fill('Discuss office cleaning'); await page.getByLabel('First reminder date').fill('2026-11-01');
  await page.getByRole('textbox', { name: 'Repeat follow-up', exact: true }).click(); await page.getByRole('option', { name: 'Monthly', exact: true }).click();
  await page.getByLabel('Repeat every', { exact: true }).fill('2'); await page.getByLabel('Last reminder date (optional)', { exact: true }).fill('2027-03-31');
  await page.getByRole('textbox', { name: 'Notify tenant admins', exact: true }).click(); await page.getByRole('option', { name: 'Anna Admin (anna@example.test)', exact: true }).click(); await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Save follow-up', exact: true }).click(); await expect(page.getByText('Follow-up saved.', { exact: true })).toBeVisible(); expect(state.followUps[0].recurrence).toBe('Monthly'); expect(state.followUps[0].interval).toBe(2);
  await page.getByRole('button', { name: 'Edit follow-up', exact: true }).click(); await page.getByRole('textbox', { name: 'Follow-up reason', exact: true }).fill('Updated reason'); state.saveFailure = true;
  await page.getByRole('button', { name: 'Save follow-up', exact: true }).click(); await expect(page.getByRole('alert').filter({ hasText: 'Follow-up changed' })).toBeVisible(); await expect(page.getByRole('textbox', { name: 'Follow-up reason' })).toHaveValue('Updated reason');
  state.saveFailure = false; await page.getByRole('button', { name: 'Save follow-up', exact: true }).click(); await expect(page.getByText('Updated reason', { exact: true })).toBeVisible();
  page.once('dialog', dialog => dialog.dismiss()); await page.getByRole('button', { name: 'Cancel follow-up', exact: true }).click(); expect(state.followUps[0].state).toBe('Active');
  page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Cancel follow-up', exact: true }).click(); await expect(page.getByText('Cancelled', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Add follow-up', exact: true }).click(); page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Discard follow-up' }).click(); await expect(page.getByRole('button', { name: 'Save follow-up', exact: true })).toHaveCount(0);
  state.deliveries.push({ id: 'delivery-1', recipientName: 'Anna Admin', recipientEmail: 'anna@example.test', dueUtc: '2026-11-01T04:00:00Z', timeZoneId: 'Europe/Helsinki', state: 'Sent', attempts: 1, sentUtc: '2026-11-01T04:00:05Z' });
  await page.getByRole('button', { name: 'Refresh notes and follow-ups' }).click(); await expect(page.getByText('Anna Admin · anna@example.test')).toBeVisible(); await expect(page.getByText('Sent', { exact: true })).toBeVisible();
});
test('converted and inactive lead history stays readable while additions and converted deletion are disabled', async ({ page }) => {
  const state = await setup(page); state.lead.companyId = 'company-1'; state.lead.status = 'Converted'; state.voices.push({ id: 'note-1', version: 'v1', authorName: 'Previous Admin', createdUtc: '2026-10-23T10:00:00Z', durationSeconds: 3 });
  await open(page); await expect(page.getByRole('button', { name: 'Record voice note', exact: true })).toBeDisabled(); await expect(page.getByRole('button', { name: 'Add follow-up', exact: true })).toBeDisabled(); await expect(page.getByRole('button', { name: 'Delete voice note', exact: true })).toHaveCount(0); await expect(page.getByText('Previous Admin', { exact: true })).toBeVisible();
});


test('collaboration histories paginate and manager navigation search filters and retry are usable', async ({ page }) => {
  const state = await setup(page, 'manager');
  for (let i = 0; i < 26; i++) { state.voices.push({ id: `note-${i}`, version: 'v1', authorName: `Recorder ${i}`, createdUtc: '2026-10-23T10:00:00Z', durationSeconds: 1 }); state.followUps.push({ id: `follow-${i}`, reason: `Reason ${i}`, state: 'Cancelled', recurrence: 'Once', interval: 1, recipientIds: ['admin-1'], authorName: 'Manager Person', nextDate: null }); state.deliveries.push({ id: `delivery-${i}`, recipientName: 'Anna', recipientEmail: 'anna@example.test', dueUtc: '2026-10-23T03:00:00Z', timeZoneId: 'Europe/Helsinki', state: 'Sent', attempts: 1 }); }
  await page.goto('/manager/leads/'); await page.getByRole('link', { name: 'Leads', exact: true }).click(); await page.getByRole('textbox', { name: 'Search leads', exact: true }).fill('Voice');
  await expect.poll(() => state.queries.some(q => q.includes('search=Voice'))).toBe(true);
  await page.getByRole('textbox', { name: 'Lead filter', exact: true }).click(); await page.getByRole('option', { name: 'Inactive', exact: true }).click(); await expect.poll(() => state.queries.some(q => q.includes('status=Inactive'))).toBe(true);
  await page.getByRole('button', { name: 'Refresh leads', exact: true }).click(); await page.getByRole('button', { name: 'View lead', exact: true }).click();
  for (const [label, path] of [['voice notes', 'voice-notes'], ['follow-ups', 'follow-ups'], ['notifications', 'deliveries']]) {
    await page.getByRole('button', { name: `Next ${label}`, exact: true }).click(); await expect.poll(() => state.queries.some(q => q.includes(`/${path}?skip=25`))).toBe(true);
    await page.getByRole('button', { name: `Previous ${label}`, exact: true }).click();
  }
  await page.getByRole('button', { name: 'Add follow-up', exact: true }).click();
  page.once('dialog', dialog => dialog.dismiss()); await page.getByRole('button', { name: 'Close lead', exact: true }).click(); await expect(page.getByRole('button', { name: 'Save follow-up', exact: true })).toBeVisible();
  page.once('dialog', dialog => dialog.accept()); await page.getByRole('button', { name: 'Close lead', exact: true }).click(); await expect(page.getByRole('dialog')).toHaveCount(0);
});
test('employee cannot enter manager Leads', async ({ page }) => {
  const state = await setup(page, 'employee'); await page.goto('/manager/leads/'); await expect(page.getByRole('alert').filter({ hasText: 'Access denied' })).toBeVisible(); expect(state.queries).toHaveLength(0);
});


test('recording auto-stops before five minutes and releases every microphone track', async ({ page }) => {
  await setup(page); await open(page); await page.clock.install();
  await page.getByRole('button', { name: 'Record voice note', exact: true }).click(); await expect(page.getByRole('button', { name: 'Stop recording', exact: true })).toBeVisible();
  await page.waitForTimeout(500); await page.clock.fastForward(300000);
  await expect(page.getByLabel('Preview voice recording')).toBeVisible(); await expect(page.getByRole('button', { name: 'Stop recording', exact: true })).toHaveCount(0);
  expect(await page.evaluate(() => (window as any).__TEST_AUDIO_STREAMS__.every((stream: MediaStream) => stream.getTracks().every(track => track.readyState === 'ended')))).toBe(true);
  await page.getByRole('button', { name: 'Discard recording', exact: true }).click();
});


test('manager lead list paginates and recovers failed detail and private image loads', async ({ page }) => {
  const state = await setup(page, 'manager');
  state.leadRows = Array.from({ length: 26 }, (_, index) => ({ ...state.lead, id: index === 0 ? id : `lead-${index}`, fields: { ...fields, name: `Prospect ${index}` } }));
  await page.goto('/manager/leads/');
  await expect(page.getByRole('cell', { name: 'Prospect 0', exact: true })).toBeVisible();
  await expect(page.getByRole('cell', { name: 'Prospect 25', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect.poll(() => state.queries.some(q => q.includes('/leads?') && q.includes('skip=25'))).toBe(true);
  await expect(page.getByRole('cell', { name: 'Prospect 25', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Next', exact: true })).toBeDisabled();
  await page.getByRole('button', { name: 'Previous', exact: true }).click();
  await expect(page.getByRole('cell', { name: 'Prospect 0', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Previous', exact: true })).toBeDisabled();
  state.detailFailure = true;
  await page.getByRole('button', { name: 'View lead', exact: true }).first().click();
  await expect(page.getByRole('alert').filter({ hasText: 'Lead detail temporarily unavailable' })).toBeVisible();
  state.detailFailure = false; state.images = [{ id: 'image-1' }]; state.imageFailure = true;
  await page.getByRole('button', { name: 'Retry loading lead', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Voice notes', exact: true })).toBeVisible();
  await expect(page.getByRole('alert').filter({ hasText: 'Image storage temporarily unavailable' })).toBeVisible();
  state.imageFailure = false;
  await page.getByRole('button', { name: 'Retry lead image', exact: true }).click();
  const image = page.getByRole('img', { name: 'Lead image', exact: true });
  await expect(image).toBeVisible();
  await expect.poll(() => image.evaluate((element: HTMLImageElement) => element.complete && element.naturalWidth > 0)).toBe(true);
  await expect(image).toHaveAttribute('src', /^blob:/);
  await expect(image.locator('..')).toHaveAttribute('target', '_blank');
  await expect(page.getByRole('button', { name: 'Remove image', exact: true })).toHaveCount(0);
});
