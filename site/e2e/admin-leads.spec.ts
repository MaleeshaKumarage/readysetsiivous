import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';

const headers = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, PUT, DELETE, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type, Authorization',
};

type LeadRecord = {
  id: string;
  name: string;
  businessId?: string | null;
  mainContactName?: string | null;
  mainContactEmail?: string | null;
  mainContactPhone?: string | null;
  emails: string[];
  contactPersons: { name: string; email?: string | null; phone?: string | null; role?: string | null }[];
  notes?: string | null;
  isActive: boolean;
};

function installLeadsApi(
  page: import('@playwright/test').Page,
  store: { leads: LeadRecord[] },
  options: {
    failList?: () => boolean;
    failMutations?: () => boolean;
    listTotal?: () => number | undefined;
    duplicateFirstId?: () => boolean;
  } = {},
) {
  return page.route('**/api/v1/admin/leads**', async (route) => {
    const method = route.request().method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });

    const url = new URL(route.request().url());
    const path = url.pathname;
    const idMatch = path.match(/\/api\/v1\/admin\/leads\/([^/]+)(?:\/(deactivate|reactivate))?$/);

    if (method === 'GET' && path.endsWith('/leads')) {
      if (options.failList?.()) {
        return route.fulfill({ status: 500, headers, json: { title: 'Server error' } });
      }
      const skip = Number(url.searchParams.get('skip') || 0);
      const take = Number(url.searchParams.get('take') || 100);
      let items = store.leads.slice(skip, skip + take);
      if (options.duplicateFirstId?.() && items.length > 0) {
        items = [items[0], ...items];
      }
      const total = options.listTotal?.() ?? store.leads.length;
      return route.fulfill({ headers, json: { items, total } });
    }

    if (method === 'POST' && path.endsWith('/leads')) {
      if (options.failMutations?.()) {
        return route.fulfill({ status: 500, headers, json: { detail: 'Create failed on server.' } });
      }
      const body = route.request().postDataJSON() as Omit<LeadRecord, 'id' | 'isActive'>;
      const created: LeadRecord = {
        id: `lead-${store.leads.length + 1}`,
        name: body.name,
        businessId: body.businessId ?? null,
        mainContactName: body.mainContactName ?? null,
        mainContactEmail: body.mainContactEmail ?? null,
        mainContactPhone: body.mainContactPhone ?? null,
        emails: body.emails ?? [],
        contactPersons: body.contactPersons ?? [],
        notes: body.notes ?? null,
        isActive: true,
      };
      store.leads.push(created);
      return route.fulfill({ status: 201, headers, json: created });
    }

    if (idMatch) {
      const id = idMatch[1];
      const action = idMatch[2];
      const lead = store.leads.find((l) => l.id === id);
      if (!lead) return route.fulfill({ status: 404, headers, json: { title: 'Not found' } });

      if (method === 'GET') return route.fulfill({ headers, json: lead });

      if (method === 'PUT') {
        if (options.failMutations?.()) {
          return route.fulfill({ status: 500, headers, json: { detail: 'Update failed on server.' } });
        }
        const body = route.request().postDataJSON();
        Object.assign(lead, {
          name: body.name,
          businessId: body.businessId ?? null,
          mainContactName: body.mainContactName ?? null,
          mainContactEmail: body.mainContactEmail ?? null,
          mainContactPhone: body.mainContactPhone ?? null,
          emails: body.emails ?? lead.emails,
          contactPersons: body.contactPersons ?? lead.contactPersons,
          notes: body.notes ?? null,
          isActive: body.isActive ?? lead.isActive,
        });
        return route.fulfill({ headers, json: lead });
      }

      if (method === 'POST' && action === 'deactivate') {
        if (options.failMutations?.()) {
          return route.fulfill({ status: 500, headers, json: { detail: 'Deactivate failed on server.' } });
        }
        lead.isActive = false;
        return route.fulfill({ status: 204, headers });
      }
      if (method === 'POST' && action === 'reactivate') {
        if (options.failMutations?.()) {
          return route.fulfill({ status: 500, headers, json: { detail: 'Reactivate failed on server.' } });
        }
        lead.isActive = true;
        return route.fulfill({ status: 204, headers });
      }
      if (method === 'DELETE') {
        if (options.failMutations?.()) {
          return route.fulfill({ status: 500, headers, json: { detail: 'Delete failed on server.' } });
        }
        store.leads = store.leads.filter((l) => l.id !== id);
        return route.fulfill({ status: 204, headers });
      }
    }

    return route.fulfill({ status: 404, headers, json: {} });
  });
}

test('leads page empty state and create with multi-email and multi-person', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const store = { leads: [] as LeadRecord[] };
  await installLeadsApi(page, store);

  await page.goto('/admin/leads/');
  await expect(page.getByRole('heading', { name: 'Leads', exact: true })).toBeVisible();
  await expect(page.getByText('No leads yet. Add one to get started.', { exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Add lead', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();

  await dialog.getByRole('button', { name: 'Create lead', exact: true }).click();
  await expect(dialog.getByRole('alert')).toHaveText('Name is required.');

  await dialog.getByRole('textbox', { name: 'Name', exact: true }).first().fill('Acme Prospect');
  await dialog.getByLabel('Business ID', { exact: true }).fill('BI-42');
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).nth(1).fill('Ada Lovelace');
  await dialog.getByLabel('Email', { exact: true }).first().fill('ada@acme.test');
  await dialog.getByLabel('Phone', { exact: true }).first().fill('+358401111111');

  await dialog.getByRole('button', { name: 'Add email', exact: true }).click();
  const placeholders = dialog.getByPlaceholder('email@example.com');
  await placeholders.nth(0).fill('ops@acme.test');
  await placeholders.nth(1).fill('sales@acme.test');
  await dialog.getByRole('button', { name: 'Remove email 1' }).click();
  await expect(placeholders).toHaveCount(1);

  await dialog.getByRole('button', { name: 'Add person', exact: true }).click();
  await expect(dialog.getByText('Person 2', { exact: true })).toBeVisible();
  await dialog.getByRole('button', { name: 'Remove contact person 2' }).click();
  await expect(dialog.getByText('Person 2', { exact: true })).not.toBeVisible();

  const person1 = dialog.getByText('Person 1', { exact: true }).locator('..').locator('..');
  await person1.getByLabel('Name', { exact: true }).fill('Bob Buyer');
  await person1.getByLabel('Email', { exact: true }).fill('bob@acme.test');
  await person1.getByLabel('Phone', { exact: true }).fill('+358402222222');
  await person1.getByLabel('Role / title', { exact: true }).fill('Procurement');

  await dialog.getByLabel('Notes', { exact: true }).fill('Follow up next week');

  await dialog.getByRole('button', { name: 'Create lead', exact: true }).click();
  await expect(dialog).toHaveCount(0);

  await expect(page.getByRole('cell', { name: /Acme Prospect/ })).toBeVisible();
  await expect(page.getByRole('cell', { name: /BI-42/ })).toBeVisible();
  await expect(page.getByRole('cell', { name: /Ada Lovelace/ })).toBeVisible();
  await expect(page.getByRole('cell', { name: /ada@acme\.test/ })).toBeVisible();
  await expect(page.getByText('sales@acme.test', { exact: true })).toBeVisible();
  await expect(page.getByText('Bob Buyer', { exact: true })).toBeVisible();
  expect(store.leads).toHaveLength(1);
  expect(store.leads[0].businessId).toBe('BI-42');
  expect(store.leads[0].mainContactPhone).toBe('+358401111111');
  expect(store.leads[0].emails).toEqual(['sales@acme.test']);
  expect(store.leads[0].contactPersons[0].name).toBe('Bob Buyer');
  expect(store.leads[0].contactPersons[0].phone).toBe('+358402222222');
  expect(store.leads[0].notes).toBe('Follow up next week');
});

test('leads edit, deactivate, reactivate, delete and cancel', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const store = {
    leads: [
      {
        id: 'lead-1',
        name: 'Beta Corp',
        businessId: 'B-1',
        mainContactName: 'Carol',
        mainContactEmail: 'carol@beta.test',
        mainContactPhone: null,
        emails: ['info@beta.test'],
        contactPersons: [{ name: 'Dan', email: 'dan@beta.test', phone: null, role: 'Ops' }],
        notes: null,
        isActive: true,
      },
    ] as LeadRecord[],
  };
  await installLeadsApi(page, store);

  await page.goto('/admin/leads/');
  await expect(page.getByRole('cell', { name: /Beta Corp/ })).toBeVisible();

  await page.getByRole('button', { name: 'Edit', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).first().fill('Beta Corp Updated');
  await dialog.getByLabel('Business ID', { exact: true }).fill('B-2');
  await dialog.getByLabel('Phone', { exact: true }).first().fill('+358409999999');
  await dialog.getByLabel('Notes', { exact: true }).fill('Updated notes');
  const person1 = dialog.getByText('Person 1', { exact: true }).locator('..').locator('..');
  await person1.getByLabel('Phone', { exact: true }).fill('+358408888888');
  await dialog.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.getByRole('cell', { name: /Beta Corp Updated/ })).toBeVisible();
  await expect(page.getByRole('cell', { name: /B-2/ })).toBeVisible();
  expect(store.leads[0].businessId).toBe('B-2');
  expect(store.leads[0].mainContactPhone).toBe('+358409999999');
  expect(store.leads[0].notes).toBe('Updated notes');
  expect(store.leads[0].contactPersons[0].phone).toBe('+358408888888');

  await page.getByRole('button', { name: 'Add lead', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);

  await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.getByText('No active leads.', { exact: true })).toBeVisible();
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByRole('cell', { name: /Beta Corp Updated/ })).toBeVisible();

  page.once('dialog', (d) => d.dismiss());
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByRole('cell', { name: /Beta Corp Updated/ })).toBeVisible();

  await page.getByRole('button', { name: 'Reactivate', exact: true }).click();
  await expect(page.getByRole('cell', { name: /Beta Corp Updated/ })).not.toBeVisible();
  await page.getByRole('tab', { name: /^Active/ }).click();
  await expect(page.getByRole('cell', { name: /Beta Corp Updated/ })).toBeVisible();

  await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  page.once('dialog', (d) => d.accept());
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByText('No leads yet. Add one to get started.', { exact: true })).toBeVisible();
  expect(store.leads).toHaveLength(0);
});

test('leads list error shows refresh and recovers', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const store = {
    leads: [
      {
        id: 'lead-1',
        name: 'Gamma LLC',
        emails: [],
        contactPersons: [],
        isActive: true,
      },
    ] as LeadRecord[],
  };
  let fail = true;
  await installLeadsApi(page, store, { failList: () => fail });

  await page.goto('/admin/leads/');
  await expect(page.getByRole('alert')).toHaveText('Leads could not be loaded completely. Refresh to retry.');
  await expect(page.getByRole('cell', { name: /Gamma LLC/ })).not.toBeVisible();

  fail = false;
  await page.getByRole('button', { name: 'Refresh leads', exact: true }).click();
  await expect(page.getByRole('cell', { name: /Gamma LLC/ })).toBeVisible();
  await expect(page.getByRole('alert')).not.toBeVisible();
});

test('admin sidebar Leads nav opens the leads page', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const store = { leads: [] as LeadRecord[] };
  await installLeadsApi(page, store);
  await page.route('**/api/v1/admin/**', async (route) => {
    if (route.request().url().includes('/leads')) return route.fallback();
    if (route.request().method() === 'OPTIONS') return route.fulfill({ status: 200, headers });
    return route.fulfill({ headers, json: { items: [], total: 0 } });
  });

  await page.goto('/admin/');
  await page.getByRole('navigation').getByRole('link', { name: 'Leads', exact: true }).click();
  await expect(page).toHaveURL(/\/admin\/leads\/?$/);
  await expect(page.getByRole('heading', { name: 'Leads', exact: true })).toBeVisible();
  await expect(page.getByText('No leads yet. Add one to get started.', { exact: true })).toBeVisible();
});

test('leads listAll paginates past 100 records', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const store = {
    leads: Array.from({ length: 105 }, (_, i) => ({
      id: `lead-${i + 1}`,
      name: `Lead ${String(i + 1).padStart(3, '0')}`,
      emails: [] as string[],
      contactPersons: [] as LeadRecord['contactPersons'],
      isActive: true,
    })),
  };
  await installLeadsApi(page, store);

  await page.goto('/admin/leads/');
  await expect(page.getByRole('cell', { name: /Lead 001/ })).toBeVisible();
  await expect(page.getByRole('cell', { name: /Lead 105/ })).toBeVisible();
  await expect(page.getByRole('tab', { name: /Active.*105/ })).toBeVisible();
});

test('leads listAll rejects inconsistent total and shows refresh', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const store = {
    leads: Array.from({ length: 3 }, (_, i) => ({
      id: `lead-${i + 1}`,
      name: `Lead ${i + 1}`,
      emails: [] as string[],
      contactPersons: [] as LeadRecord['contactPersons'],
      isActive: true,
    })),
  };
  await page.route('**/api/v1/admin/leads**', async (route) => {
    const method = route.request().method();
    if (method === 'OPTIONS') return route.fulfill({ status: 200, headers });
    const url = new URL(route.request().url());
    if (method === 'GET' && url.pathname.endsWith('/leads')) {
      const skip = Number(url.searchParams.get('skip') || 0);
      if (skip === 0) {
        return route.fulfill({ headers, json: { items: store.leads.slice(0, 2), total: 4 } });
      }
      return route.fulfill({ headers, json: { items: store.leads.slice(2), total: 99 } });
    }
    return route.fulfill({ status: 404, headers, json: {} });
  });

  await page.goto('/admin/leads/');
  await expect(page.getByRole('alert')).toHaveText('Leads could not be loaded completely. Refresh to retry.');
});

test('leads listAll rejects duplicate ids and shows refresh', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const store = {
    leads: [
      {
        id: 'lead-1',
        name: 'Dup Lead',
        emails: [] as string[],
        contactPersons: [] as LeadRecord['contactPersons'],
        isActive: true,
      },
    ],
  };
  await installLeadsApi(page, store, { duplicateFirstId: () => true });

  await page.goto('/admin/leads/');
  await expect(page.getByRole('alert')).toHaveText('Leads could not be loaded completely. Refresh to retry.');
});

test('leads actionError shows when deactivate or delete fails', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const store = {
    leads: [
      {
        id: 'lead-1',
        name: 'Fail Corp',
        emails: [] as string[],
        contactPersons: [] as LeadRecord['contactPersons'],
        isActive: true,
      },
    ],
  };
  let fail = true;
  await installLeadsApi(page, store, { failMutations: () => fail });

  await page.goto('/admin/leads/');
  await expect(page.getByRole('cell', { name: /Fail Corp/ })).toBeVisible();

  await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText('Deactivate failed on server.');
  expect(store.leads[0].isActive).toBe(true);

  fail = false;
  await page.getByRole('button', { name: 'Deactivate', exact: true }).click();
  await page.getByRole('tab', { name: /^Inactive/ }).click();
  await expect(page.getByRole('cell', { name: /Fail Corp/ })).toBeVisible();

  fail = true;
  page.once('dialog', (d) => d.accept());
  await page.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText('Delete failed on server.');
  expect(store.leads).toHaveLength(1);
});

test('leads modal stays open with server error on create failure', async ({ page }) => {
  await setupAdminMocks(page, { authenticated: true });
  const store = { leads: [] as LeadRecord[] };
  await installLeadsApi(page, store, { failMutations: () => true });

  await page.goto('/admin/leads/');
  await page.getByRole('button', { name: 'Add lead', exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox', { name: 'Name', exact: true }).first().fill('Broken Lead');
  await dialog.getByRole('button', { name: 'Create lead', exact: true }).click();

  await expect(dialog).toBeVisible();
  await expect(dialog.getByRole('alert')).toHaveText('Create failed on server.');
  expect(store.leads).toHaveLength(0);
});
