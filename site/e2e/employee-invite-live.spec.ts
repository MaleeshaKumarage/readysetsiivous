import { test, expect } from '@playwright/test';
import { readFileSync } from 'node:fs';
const contextPath = process.env.INVITE_TEST_CONTEXT_PATH;
test.skip(!contextPath, 'Requires isolated invitation services and the .NET-generated fixture.');
test('real Keycloak email completes setup, preserves password on reinvite, and old token is denied after deactivation', async ({ page, request }) => {
  const fixture = JSON.parse(readFileSync(contextPath!, 'utf8'));
  const api = process.env.NEXT_PUBLIC_API_URL!;
  const password = 'InviteCleanerTestOnly2026';
  await page.goto(fixture.setupUrl);
  const proceed = page.getByRole('link', { name: /proceed/i });
  if (await proceed.count()) await proceed.first().click();
  await expect(page.locator('#password-new')).toBeVisible();
  await page.locator('#password-new').fill(password); await page.locator('#password-confirm').fill(password);
  await page.getByRole('button', { name: /Submit/i }).click();
  await page.getByRole('link', { name: /Back to Application/i }).click();
  await expect(page).toHaveURL(new RegExp('/account/\\?tenant=' + fixture.realm));
  // Native action completion may require normal PKCE login; passwords stay on the Keycloak page.
  const signIn = page.getByRole('button', { name: 'Sign in', exact: true });
  await expect(page.getByRole('heading', { name: 'Account ready' }).or(signIn)).toBeVisible({ timeout: 20000 });
  if (await signIn.isVisible()) {
    await signIn.click();
    await expect(page.locator('#username').or(page.getByRole('heading', { name: 'Account ready' }))).toBeVisible({ timeout: 20000 });
    if (await page.locator('#username').isVisible()) {
      await page.locator('#username').fill(fixture.email); await page.locator('#password').fill(password); await page.locator('#kc-login').click();
    }
  }
  await expect(page.getByRole('heading', { name: 'Account ready' })).toBeVisible({ timeout: 20000 });
  await expect(page.getByRole('link', { name: 'Continue to administration' })).toHaveCount(0);
  const employeeTokenResponse = await request.post(`${fixture.keycloakUrl}/realms/${fixture.realm}/protocol/openid-connect/token`, { form: { grant_type: 'password', client_id: 'cleaning-suite-web', username: fixture.email, password, scope: 'openid email' } });
  expect(employeeTokenResponse.status()).toBe(200);
  const employeeJwt = (await employeeTokenResponse.json()).access_token;
  const claims = JSON.parse(Buffer.from(employeeJwt.split('.')[1], 'base64url').toString());
  expect(claims.sub).toBe(fixture.userId); expect(claims.email_verified).toBe(true);
  expect(claims.realm_access.roles).toContain('employee'); expect(claims.realm_access.roles).not.toContain('admin');
  const me = await request.get(`${api}/api/v1/me`, { headers: { Authorization: `Bearer ${employeeJwt}` } });
  expect(me.status()).toBe(200); expect((await me.json()).employee.id).toBe(fixture.employeeId);
  expect((await request.get(`${api}/api/v1/admin/quality-cycle/templates`, { headers: { Authorization: `Bearer ${employeeJwt}` } })).status()).toBe(403);
  await page.getByRole('link', { name: 'My shifts', exact: true }).click();
  const shiftCard = page.getByRole('group', { name: /^Live employee shift / }).first();
  await expect(shiftCard).toBeVisible();
  await shiftCard.getByRole('button', { name: 'Start', exact: true }).click();
  await shiftCard.getByRole('button', { name: 'Record activities & end', exact: true }).click();
  await shiftCard.getByRole('checkbox', { name: 'Corridor', exact: true }).check();
  await shiftCard.getByRole('button', { name: 'End shift', exact: true }).click();
  await expect(shiftCard.getByText('Done', { exact: true })).toBeVisible();
  const ownShifts = await request.get(`${api}/api/v1/me/shifts`, { headers: { Authorization: `Bearer ${employeeJwt}` } });
  expect(ownShifts.status()).toBe(200);
  const saved = (await ownShifts.json()).find((o: any) => o.shiftId === fixture.shiftId && o.form?.isSubmitted);
  expect(saved.form.startedAtUtc).toBeTruthy(); expect(saved.form.endedAtUtc).toBeTruthy();
  expect(saved.form.items).toEqual([{ itemText: 'Corridor', isChecked: true }, { itemText: 'Gym', isChecked: false }]);
  const afterShiftMails = (await (await request.get(`${fixture.mailboxUrl}/api/v1/messages`)).json()).messages.filter((m: any) => m.To.some((to: any) => to.Address === fixture.email));
  expect(afterShiftMails).toHaveLength(1); // Native account setup only; no QC emails.
  const ownerTokenResponse = await request.post(`${fixture.keycloakUrl}/realms/${fixture.realm}/protocol/openid-connect/token`, { form: { grant_type: 'password', client_id: 'cleaning-suite-web', username: fixture.ownerEmail, password: 'InviteOwnerTestOnly2026', scope: 'openid email' } });
  expect(ownerTokenResponse.status()).toBe(200); const ownerJwt = (await ownerTokenResponse.json()).access_token;
  const recordedDate = new Date(saved.startUtc);
  const monthQuery = `year=${recordedDate.getUTCFullYear()}&month=${recordedDate.getUTCMonth() + 1}&shiftId=${fixture.shiftId}`;
  const report = await request.get(`${api}/api/v1/admin/quality-cycle/monthly-summary?${monthQuery}`, { headers: { Authorization: `Bearer ${ownerJwt}` } });
  expect(report.status()).toBe(200);
  const shiftReport = (await report.json()).shifts.find((s: any) => s.shiftId === fixture.shiftId);
  expect(shiftReport.items.find((i: any) => i.itemText === 'Corridor').daysPerformed).toBe(1);
  expect(shiftReport.items.find((i: any) => i.itemText === 'Gym').daysPerformed).toBe(0);
  const pdf = await request.get(`${api}/api/v1/admin/quality-cycle/summary-pdf?${monthQuery}`, { headers: { Authorization: `Bearer ${ownerJwt}` } });
  expect(pdf.status()).toBe(200); expect((await pdf.body()).subarray(0, 4).toString()).toBe('%PDF');
  expect((await request.get(`${api}/api/v1/admin/quality-cycle/monthly-summary?${monthQuery}`, { headers: { Authorization: `Bearer ${employeeJwt}` } })).status()).toBe(403);
  const reinvite = await request.post(`${api}/api/v1/admin/employees/${fixture.employeeId}/invite`, { headers: { Authorization: `Bearer ${ownerJwt}` } });
  expect(reinvite.status()).toBe(200); expect((await reinvite.json()).status).toBe('Sent');
  // Read only the captured test inbox. Decode native action metadata to verify no password reset.
  const mails = (await (await request.get(`${fixture.mailboxUrl}/api/v1/messages`)).json()).messages.filter((m: any) => m.To.some((to: any) => to.Address === fixture.email));
  expect(mails).toHaveLength(2);
  const body = await (await request.get(`${fixture.mailboxUrl}/api/v1/message/${mails[0].ID}`)).json();
  const href = body.HTML.match(/href="([^"]*login-actions\/action-token[^"]*)"/)?.[1].replaceAll('&amp;', '&');
  expect(href).toBeTruthy(); const actionKey = new URL(href).searchParams.get('key')!;
  const action = JSON.parse(Buffer.from(actionKey.split('.')[1], 'base64url').toString());
  expect(action.rqac || []).not.toContain('UPDATE_PASSWORD');
  // Opening a completed-account continuation email does not change the password.
  const continuation = await page.context().newPage(); await continuation.goto(href);
  const continueLink = continuation.getByRole('link', { name: /proceed/i }); if (await continueLink.count()) await continueLink.first().click();
  await expect(continuation.locator('#password-new')).toHaveCount(0);
  await continuation.close();
  const stillWorks = await request.post(`${fixture.keycloakUrl}/realms/${fixture.realm}/protocol/openid-connect/token`, { form: { grant_type: 'password', client_id: 'cleaning-suite-web', username: fixture.email, password, scope: 'openid email' } });
  expect(stillWorks.status()).toBe(200);
  const fields = (role: string) => ({ fields: { email: fixture.email, firstName: 'Invite', lastName: 'Cleaner', phone: '', role, defaultHours: {}, skills: [], serviceAreas: [], certifications: [] }, isActive: true });
  const promoted = await request.put(`${api}/api/v1/admin/employees/${fixture.employeeId}`, { headers: { Authorization: `Bearer ${ownerJwt}` }, data: fields('admin') });
  expect(promoted.status()).toBe(204);
  expect((await request.get(`${api}/api/v1/me`, { headers: { Authorization: `Bearer ${employeeJwt}` } })).status()).toBe(403);
  const elevatedResponse = await request.post(`${fixture.keycloakUrl}/realms/${fixture.realm}/protocol/openid-connect/token`, { form: { grant_type: 'password', client_id: 'cleaning-suite-web', username: fixture.email, password, scope: 'openid email' } });
  expect(elevatedResponse.status()).toBe(200); const elevatedJwt = (await elevatedResponse.json()).access_token;
  expect((await request.get(`${api}/api/v1/admin/quality-cycle/templates`, { headers: { Authorization: `Bearer ${elevatedJwt}` } })).status()).toBe(200);
  const demoted = await request.put(`${api}/api/v1/admin/employees/${fixture.employeeId}`, { headers: { Authorization: `Bearer ${ownerJwt}` }, data: fields('employee') });
  expect(demoted.status()).toBe(204);
  expect((await request.get(`${api}/api/v1/admin/quality-cycle/templates`, { headers: { Authorization: `Bearer ${elevatedJwt}` } })).status()).toBe(403);
  const deactivated = await request.post(`${api}/api/v1/admin/employees/${fixture.employeeId}/deactivate`, { headers: { Authorization: `Bearer ${ownerJwt}` } });
  expect(deactivated.status()).toBe(204);
  const oldAccess = await request.get(`${api}/api/v1/me`, { headers: { Authorization: `Bearer ${employeeJwt}` } });
  expect(oldAccess.status()).toBe(403); expect((await oldAccess.json()).detail).toContain('inactive');
});
