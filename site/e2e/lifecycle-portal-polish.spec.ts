import { join } from 'node:path';
import { chooseEmployeeSection } from './employee-section';
import { test, expect, type Page, type Locator } from '@playwright/test';
import { CORS, PartnerFlowBackend, OWNER_COMPANY, prepareRolePage } from './partner-flow-backend';

async function noOverflow(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
}
async function touchTarget(control: Locator) {
  const rect = await control.boundingBox();
  expect(rect?.height).toBeGreaterThanOrEqual(44);
  expect(rect?.width).toBeGreaterThanOrEqual(44);
}
async function owner(page: Page, longName = false) {
  const backend = new PartnerFlowBackend({ priced: true });
  if (longName) backend.branches[0].name = 'Lake apartment ' + 'LongPropertyName'.repeat(12);
  await prepareRolePage(page, backend, 'owner');
  await page.route(/\/api\/v1\/partner\/inventory-acknowledgments\?/, route => route.fulfill({headers:CORS,json:{items:[],pendingCount:0,hasMore:false}}));
  return backend;
}

for (const language of ['en', 'fi']) {
  test(`partner ${language} dialog contains its title at every viewport and preserves focus/dismissal`, async ({page}) => {
    const backend = await owner(page, true);
    await page.goto('/partner/');
    if(language === 'fi') await page.getByRole('button',{name:'Suomi',exact:true}).click();
    const opener = page.getByRole('button',{name:language === 'en' ? 'Request cleaning' : 'Pyydä siivous',exact:true}).first();
    await opener.click();
    const dialog = page.getByRole('dialog');
    const title = dialog.getByRole('heading').first();
    for(const width of [320,390,768,1440]) {
      await page.setViewportSize({width,height:667});
      await title.scrollIntoViewIfNeeded();
      const bounds = await dialog.boundingBox(), heading = await title.boundingBox();
      expect(bounds!.x).toBeGreaterThanOrEqual(15);
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width - 15);
      expect(heading!.y).toBeGreaterThan(bounds!.y);
      expect(heading!.y + heading!.height).toBeLessThan(bounds!.y + bounds!.height);
      expect(await dialog.evaluate(el => getComputedStyle(el).backgroundColor)).toBe('rgb(255, 255, 255)');
      expect(await dialog.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
      await noOverflow(page);
    }
    const date = dialog.locator('[name=fromDate]');
    await date.fill('2026-12-01');
    await expect(date).toHaveAttribute('aria-describedby','partner-window-help');
    await expect(dialog.locator('#partner-window-help')).toContainText(language === 'en' ? 'first and last' : 'ensimmäinen ja viimeinen');
    const last = dialog.getByRole('button').last();
    await last.focus(); await page.keyboard.press('Tab'); await expect(date).toBeFocused();
    await page.keyboard.press('Shift+Tab'); await expect(last).toBeFocused();
    // A reduced viewport models the available space with a keyboard; it is not an iOS keyboard test.
    await page.setViewportSize({width:390,height:360});
    await last.scrollIntoViewIfNeeded(); await expect(last).toBeInViewport();
    await page.keyboard.press('Escape'); await expect(dialog).toHaveCount(0); await expect(opener).toBeFocused();
    expect(backend.writes).toHaveLength(0);
  });
}

for(const fixed of [false,true]) {
  test(`partner ${fixed ? 'fixed' : 'flexible'} request retains its exact API contract`, async ({page}) => {
    const backend = await owner(page);
    await page.goto('/partner/');
    await page.getByRole('button',{name:'Request cleaning',exact:true}).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel('From date',{exact:true}).fill('2026-12-01');
    if(fixed) {
      await dialog.getByLabel('Fixed appointment on this date').check();
      await dialog.getByLabel('Exact start time').fill('10:30');
    } else await dialog.getByLabel('To date',{exact:true}).fill('2026-12-02');
    await dialog.getByLabel('Cleaning instructions',{exact:true}).fill('Prepare fresh linen.');
    await dialog.getByRole('button',{name:'Save cleaning request',exact:true}).click();
    await expect(page.getByRole('tab',{name:'Cleaning requests',exact:true})).toHaveAttribute('aria-selected','true');
    const write = backend.writes.find(w => w.path === 'partner/requests');
    expect(write).toMatchObject({method:'POST',status:200});
    expect(write!.body).toEqual({clientRequestId:expect.stringMatching(/^[a-f0-9-]{36}$/),branchId:backend.branches[0].id,fromDate:'2026-12-01',toDate:fixed?'2026-12-01':'2026-12-02',exactStartLocalTime:fixed?'10:30':null,cleaningType:'Normal',notes:'Prepare fresh linen.'});
  });
}

// A synthetic in-progress visit. No production identities, photos or API writes are used.
async function cleaner(page: Page) {
  const backend = new PartnerFlowBackend();
  await prepareRolePage(page, backend, 'cleaner');
  const form: any = {id:'form-one',shiftId:'shift-one',employeeId:'cleaner',templateId:'template-one',templateTitle:'Apartment checklist',items:[{itemText:'Kitchen',isChecked:false}],photos:[],photoUrls:[],isSubmitted:false,startedAtUtc:new Date().toISOString(),cleanerNotes:''};
  const occurrence = {shiftId:'shift-one',shiftName:'Lake apartment – checkout cleaning',startUtc:new Date(Date.now()-3600000).toISOString(),endUtc:new Date(Date.now()+3600000).toISOString(),timeZoneId:'Europe/Helsinki',form,notes:'Prepare fresh linen.',accessInstructions:'Use the side door.',contactName:'Property manager',contactPhone:'+358400000000',requiredPhotoLabels:['After']};
  const writes: any[] = [];
  await page.route(/\/api\/v1\/me\/shifts(?:\/calendar)?(?:\?.*)?$/, route => route.fulfill({headers:CORS,json:route.request().url().includes('/calendar')?{occurrences:[occurrence],timeZoneId:'Europe/Helsinki',fromLocalDate:'2026-11-09',toLocalDate:'2026-11-10'}:[occurrence]}));
  await page.route(/\/api\/v1\/me\/quality-cycle\/form-one\/end$/, route => {
    if(route.request().method()==='OPTIONS') return route.fulfill({headers:CORS});
    writes.push(route.request().postDataJSON());
    Object.assign(form,writes.at(-1),{isSubmitted:true,submittedUtc:new Date().toISOString(),endedAtUtc:new Date().toISOString()});
    return route.fulfill({headers:CORS,json:form});
  });
  return {writes};
}
const photo = {name:'after.png',mimeType:'image/png',buffer:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=','base64')};

test('cleaner sections remain fully visible at 320px, with keyboard navigation and dark theme', async ({page}) => {
  await cleaner(page);
  await page.addInitScript(() => localStorage.setItem('theme','dark'));
  await page.goto('/me/shifts/');
  await page.setViewportSize({width:320,height:667});
  if (await page.getByRole('tab',{name:'Schedules',exact:true}).isVisible()) {
    for(const name of ['Schedules','Offers','Inventory','Settings']) {
      const tab = page.getByRole('tab',{name,exact:true});
      await expect(tab).toBeInViewport(); await touchTarget(tab);
    }
  } else {
    // Phones: the sections open from the menu button, and each option is a full touch target.
    await page.locator('#employee-section-menu-button').click();
    for(const name of ['Schedules','Offers','Inventory','Settings']) {
      const option = page.getByRole('navigation',{name:'My account sections'}).getByRole('button',{name,exact:true});
      await expect(option).toBeInViewport(); await touchTarget(option);
    }
    await page.keyboard.press('Escape');
  }
  // The tab row is shown from the small-screen breakpoint up; phones use the section menu instead.
  if (await page.getByRole('tab',{name:'Schedules',exact:true}).isVisible()) {
    await page.getByRole('tab',{name:'Schedules',exact:true}).focus();
    await page.keyboard.press('End'); await expect(page.getByRole('tab',{name:'Settings',exact:true})).toBeFocused();
  }
  await expect(page.getByRole('link',{name:'Change work preferences in My account'})).toBeVisible();
  expect(await page.locator('.employee-portal').evaluate(el=>getComputedStyle(el).backgroundColor)).toBe('rgb(3, 7, 18)');
  await noOverflow(page);
});

test('cleaner evidence is touch-friendly and preserves required photos, optional checklist and end payload', async ({page},info) => {
  const state = await cleaner(page);
  await page.setViewportSize({width:390,height:667});
  await page.goto('/me/shifts/');
  await expect(page.getByRole('region',{name:'Visit instructions'})).toContainText('Cleaning: Prepare fresh linen.');
  await page.getByRole('button',{name:'Record activities & end',exact:true}).click();
  const addPhoto = page.locator('.employee-photo-add');
  await touchTarget(addPhoto);
  await page.getByLabel('Add shift photos',{exact:true}).focus();
  await expect(page.getByLabel('Add shift photos',{exact:true})).toBeFocused();
  expect(await addPhoto.evaluate(el=>getComputedStyle(el).outlineStyle)).toBe('solid');
  const end = page.getByRole('button',{name:'End shift',exact:true});
  await expect(end).toBeDisabled();
  await page.getByLabel('Attach After photo',{exact:true}).setInputFiles(photo);
  const remove = page.getByRole('button',{name:'Remove photo',exact:true});
  await touchTarget(remove);
  await expect(page.getByLabel('Photo 1 note',{exact:true})).toHaveValue('After');
  await page.getByLabel('Photo 1 note',{exact:true}).fill('');
  await end.click();
  await expect(page.getByLabel('Photo 1 note',{exact:true})).toBeFocused(); expect(state.writes).toHaveLength(0);
  await page.getByLabel('Photo 1 note',{exact:true}).fill('Kitchen after cleaning');
  await noOverflow(page);
  const evidencePath = process.env.PORTAL_UI_EVIDENCE_DIR ? join(process.env.PORTAL_UI_EVIDENCE_DIR, `${info.project.name}-evidence.png`) : info.outputPath('cleaner-evidence.png');
  await page.screenshot({path:evidencePath,fullPage:true,scale:'css'});
  await info.attach('cleaner-evidence',{path:evidencePath,contentType:'image/png'});
  await page.setViewportSize({width:390,height:360});
  await end.scrollIntoViewIfNeeded(); await expect(end).toBeInViewport();
  await end.click(); await expect(page.getByText('Done',{exact:true})).toBeVisible();
  expect(state.writes).toHaveLength(1);
  expect(state.writes[0]).toMatchObject({items:[{itemText:'Kitchen',isChecked:false}],cleanerNotes:'',photos:[{note:'Kitchen after cleaning',category:'After'}]});
});

test('admin pending counts, request ordering, statuses and quote links keep their current meaning', async ({page}) => {
  const backend = new PartnerFlowBackend({priced:true});
  backend.seedRequest(backend.branches[0],{fromDate:'2026-12-01',toDate:'2026-12-02'});
  const second = backend.seedRequest(backend.branches[0],{fromDate:'2026-12-03',toDate:'2026-12-04'});
  backend.seedOffer(second,'cleaner');
  backend.addBranch({name:'Harbour studio',companyId:OWNER_COMPANY.id,quoteRequestedUtc:'2026-11-01T10:00:00Z'});
  await prepareRolePage(page,backend,'admin');
  await page.goto('/admin/partner-requests/');
  const requestsHelp = page.getByText('The Requests badge counts requests needing arrangement; this list also includes offers awaiting a cleaner’s response.');
  await expect(requestsHelp).toBeVisible();
  await expect(page.getByRole('tab',{name:'Requests',exact:true})).toContainText('1');
  await page.getByRole('tab',{name:'Assigned',exact:true}).click();
  await expect(requestsHelp).toHaveCount(0);
  await page.getByRole('tab',{name:'Requests',exact:true}).click();
  await expect(requestsHelp).toBeVisible();
  await expect(page.locator('.partner-staff-status')).toHaveText(['Requested','Offered']);
  await expect(page.getByRole('status',{name:'Pending quote requests'})).toContainText('1 branch awaiting a quote');
  await expect(page.getByRole('link',{name:'Review quotes'})).toHaveAttribute('href','/admin/partner-requests/?tab=quotes');
  await noOverflow(page); expect(backend.writes).toHaveLength(0);
});

test('employee PWA preserves native prompt dismissal, iOS instructions and installed state', async ({page}) => {
  await cleaner(page);
  await page.goto('/me/shifts/'); await chooseEmployeeSection(page, 'Settings');
  const install = page.getByRole('button',{name:'Install My shifts',exact:true});
  await touchTarget(install);
  await page.evaluate(() => {
    const event = new Event('beforeinstallprompt',{cancelable:true});
    Object.assign(event,{prompt:async()=>{},userChoice:Promise.resolve({outcome:'dismissed',platform:'web'})});
    window.dispatchEvent(event);
  });
  await install.click(); await expect(install).toBeVisible();
  await expect(page.getByText('Use your browser’s Install app or Add to Home Screen menu to install My shifts.')).toBeVisible();
  await page.evaluate(()=>window.dispatchEvent(new Event('appinstalled')));
  await expect(page.getByText('My shifts app installed',{exact:true})).toBeVisible(); await expect(install).toHaveCount(0);
  // Same user-initiated iOS guidance; browser emulation does not prove physical-device install.
  await page.addInitScript(()=>Object.defineProperty(navigator,'userAgent',{configurable:true,value:'iPhone'}));
  await page.reload(); await chooseEmployeeSection(page, 'Settings');
  await page.getByRole('button',{name:'Install My shifts',exact:true}).click();
  await expect(page.getByRole('status').filter({hasText:'In Safari, tap Share'})).toBeVisible();
});

test('standalone cleaner retains app identity, safe layout and preference content without an install prompt', async ({page}) => {
  await cleaner(page);
  await page.addInitScript(()=>{
    Object.defineProperty(navigator,'standalone',{configurable:true,value:true});
    const original=window.matchMedia.bind(window);
    window.matchMedia=query=>{const result=original(query);if(query==='(display-mode: standalone)')Object.defineProperty(result,'matches',{value:true});return result;};
  });
  await page.setViewportSize({width:390,height:667});
  await page.goto('/me/shifts/');
  await expect(page.locator('link[rel=manifest]')).toHaveAttribute('href','/employee-manifest.json');
  await chooseEmployeeSection(page, 'Settings');
  await expect(page.getByText('My shifts app installed',{exact:true})).toBeVisible();
  await expect(page.getByRole('button',{name:'Install My shifts'})).toHaveCount(0);
  await expect(page.getByText('Automatic acceptance is off. You review each offer yourself.')).toBeVisible();
  await noOverflow(page);
  expect(await page.locator('.employee-portal').evaluate(el=>getComputedStyle(el).minHeight)).toBe('667px');
});

for (const role of ['owner','admin','cleaner'] as const) {
  test(`${role} workspace honors display-cutout insets without obscuring controls`, async ({page,context}) => {
    const backend = new PartnerFlowBackend({priced:true});
    await prepareRolePage(page,backend,role);
    await page.route(/\/api\/v1\/partner\/inventory-acknowledgments\?/,route=>route.fulfill({headers:CORS,json:{items:[],pendingCount:0,hasMore:false}}));
    const cdp = await context.newCDPSession(page);
    await cdp.send('Emulation.setSafeAreaInsetsOverride',{insets:{top:32,left:20,right:20,bottom:40}});
    await page.setViewportSize({width:390,height:667});
    await page.goto(role==='owner'?'/partner/':role==='admin'?'/admin/':'/me/shifts/');
    const shell = page.locator(role==='owner'?'.partner-header-inner':role==='admin'?'.admin-header':'.employee-portal');
    await expect(shell).toBeVisible();
    expect(await shell.evaluate(el=>parseFloat(getComputedStyle(el).paddingTop))).toBeGreaterThanOrEqual(32);
    const footer = page.locator(role==='owner'?'.partner-main':role==='admin'?'.admin-content':'.employee-portal');
    expect(await footer.evaluate(el=>parseFloat(getComputedStyle(el).paddingBottom))).toBeGreaterThanOrEqual(40);
    await noOverflow(page);
    if(role==='owner') {
      await page.getByRole('button',{name:'Request cleaning',exact:true}).click();
      const rect=await page.getByRole('dialog').boundingBox();
      expect(rect!.y).toBeGreaterThanOrEqual(32); expect(rect!.x).toBeGreaterThanOrEqual(20);
    }
    await cdp.detach();
  });
}

test('workspace viewport changes do not extend into the public website', async ({page}) => {
  await page.goto('/en/');
  await expect(page.locator('.public-site')).toBeVisible();
  await expect(page.locator('meta[name=viewport]')).not.toHaveAttribute('content',/viewport-fit=cover/);
  await expect(page.locator('.employee-portal, .partner-portal, .admin-shell')).toHaveCount(0);
});
