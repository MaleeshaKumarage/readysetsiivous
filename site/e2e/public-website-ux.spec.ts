import { test, expect, type Page } from '@playwright/test';
import { publicUx } from '../src/i18n/publicUx';
import { getDictionary, type Language } from '../src/i18n';

const headers = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*' };
const key = 'A'.repeat(64);
const service = { id: '11111111-1111-1111-1111-111111111111', slug: 'home-cleaning', name: 'Synthetic cleaning', description: 'Kitchen and floors', priceNet: 50, vatRatePercent: 25.5, priceGross: 62.75, currency: 'EUR', durationMinutes: 120 };
async function details(page: Page, lang: Language = 'en') {
  await page.route('**/api/v1/public/*/services?*', r => r.fulfill({ headers, json: [service] }));
  await page.route('**/api/v1/public/*/availability?*', r => r.fulfill({ headers, json: [{ startTime: '12:00', endTime: '14:00' }] }));
  await page.goto(`/${lang}/varaus/`);
  await page.getByRole('button', { name: /Synthetic cleaning/ }).click();
  await page.locator('#booking-date').fill('2030-01-15');
  await page.getByRole('button', { name: '12:00', exact: true }).click();
  for (const [id,value] of Object.entries({ customerName: 'Test customer', customerPhone: '123', customerEmail: 'test@example.invalid', street: 'Test street', city: 'Helsinki' })) await page.locator(`#${id}`).fill(value);
  await page.locator('#accepted').check();
}

for (const lang of ['fi','en','sv'] as const) {
  test(`${lang}: inclusive price, request confirmation and private download`, async ({ page }) => {
    const u = publicUx(lang), d = getDictionary(lang).varaus;
    await page.route('**/api/v1/public/*/bookings', async r => {
      expect(r.request().postDataJSON().expectedPriceGross).toBe(62.75);
      await r.fulfill({ headers, status: 201, json: { bookingNumber: 'RSS-2030-000001', customerReference: 'TEST', managementKey: key, priceGross: 62.75, currency: 'EUR' } });
    });
    await details(page,lang);
    await expect(page.getByText(u.price, { exact: false }).first()).toContainText(/62[,.]75/);
    await expect(page.locator('#customerEmail')).toHaveAttribute('type','email');
    await expect(page.locator('#customerPhone')).toHaveAttribute('autocomplete','tel');
    await page.getByRole('button',{ name:d.confirm,exact:true }).click();
    await expect(page.getByText(u.pending,{exact:true})).toBeVisible();
    const downloadEvent = page.waitForEvent('download');
    await page.getByRole('button',{name:u.download,exact:true}).click();
    expect((await downloadEvent).suggestedFilename()).toBe('readyset-booking.txt');
    expect(await page.evaluate(() => JSON.stringify({ local:{...localStorage},session:{...sessionStorage} }))).not.toContain(key);
    expect(page.url()).not.toContain(key);
  });
}

test('slot conflict retains details and refreshes availability', async ({page}) => {
  await page.route('**/api/v1/public/*/bookings',r => r.fulfill({ headers,status:409,json:{detail:'Slot conflict'} }));
  await details(page); await page.getByRole('button',{name:'Confirm booking',exact:true}).click();
  await expect(page.getByRole('alert').filter({ hasNot: page.locator('#__next-route-announcer__') }).filter({ hasText: /./ })).toContainText(publicUx('en').conflict);
  await page.getByRole('button',{name:'12:00',exact:true}).click();
  await expect(page.locator('#customerName')).toHaveValue('Test customer');
});

for (const [status,message] of [[429,'rateLimit'],[503,'uncertain']] as const) {
  test(`${status}: specific recovery message retains the form`,async ({page}) => {
    await page.route('**/api/v1/public/*/bookings',r => r.fulfill({headers,status,json:{}}));
    await details(page); await page.getByRole('button',{name:'Confirm booking',exact:true}).click();
    await expect(page.getByRole('alert').filter({ hasNot: page.locator('#__next-route-announcer__') }).filter({ hasText: /./ })).toHaveText(publicUx('en')[message]);
    await expect(page.locator('#customerName')).toHaveValue('Test customer');
  });
}

test('lost write response prevents blind resubmission',async ({page}) => {
  let calls=0;
  await page.route('**/api/v1/public/*/bookings',r => { calls++; return r.abort('failed'); });
  await details(page); const submit=page.getByRole('button',{name:'Confirm booking',exact:true}); await submit.click();
  await expect(page.getByRole('alert').filter({ hasNot: page.locator('#__next-route-announcer__') }).filter({ hasText: /./ })).toHaveText(publicUx('en').uncertain);
  await expect(submit).toBeDisabled(); expect(calls).toBe(1);
});

test('private management uses POST, confirms cancellation and downloads acknowledgement',async ({page}) => {
  await page.route('**/api/v1/public/*/bookings/lookup',async r => {
    expect(r.request().method()).toBe('POST'); expect(r.request().postDataJSON().managementKey).toBe(key);
    await r.fulfill({headers,json:{bookingNumber:'RSS-2030-000001',status:'New',startLocalDate:'2030-01-15',startLocalTime:'12:00',serviceNameFi:'Siivous',serviceNameEn:'Cleaning',priceGross:62.75,durationMinutes:120}});
  });
  let cancelled=false;
  await page.route('**/api/v1/public/*/bookings/*/cancel',async r => { expect(r.request().postDataJSON().managementKey).toBe(key); cancelled=true; await r.fulfill({headers,status:204}); });
  await page.goto('/en/booking/'); await page.locator('#manage-number').fill('RSS-2030-000001'); await page.locator('#manage-key').fill(key);
  await page.getByRole('button',{name:'View booking',exact:true}).click(); await expect(page.getByText('Cleaning',{exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Cancel this booking',exact:true}).click(); expect(cancelled).toBe(false);
  await page.getByRole('button',{name:'Confirm cancellation',exact:true}).click(); await expect(page.getByText(publicUx('en').cancelDone,{exact:true})).toBeVisible();
  expect(cancelled).toBe(true); expect(page.url()).not.toContain(key);
  const event=page.waitForEvent('download'); await page.getByRole('button',{name:publicUx('en').acknowledgement}).click(); await event;
  await page.getByRole('button',{name:'Clear private details',exact:true}).click(); await expect(page.locator('#manage-key')).toHaveValue('');
});

test('320px public navigation, truthful content, local assets and legal-page anchors',async ({page}) => {
  await page.setViewportSize({width:320,height:800});
  await page.route('**/api/v1/public/*/services?*',r => r.fulfill({headers,json:[]}));
  await page.goto('/en/');
  await expect(page.locator('#reviews')).toHaveCount(0);
  await expect(page.locator('a[href="#checklist"]')).toHaveCount(0);
  await expect(page.locator('link[href*="fonts.googleapis.com"]')).toHaveCount(0);
  await expect(page.locator('img[src*="api.qrserver.com"]')).toHaveCount(0);
  await expect(page.locator('footer')).toContainText('3404861-9');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  expect(await page.evaluate(() => localStorage.getItem('lang'))).toBeNull();
  expect(await page.evaluate(() => localStorage.getItem('theme'))).toBeNull();
  await page.goto('/en/privacy/'); await expect(page.locator('footer a[href="/en/#services"]').first()).toBeVisible();
});

test('booking date boundary uses Helsinki instead of UTC',async ({page}) => {
  await page.clock.install({ time:new Date('2026-10-28T22:30:00Z') });
  await page.route('**/api/v1/public/*/services?*',r=>r.fulfill({headers,json:[service]}));
  await page.goto('/en/varaus/'); await page.getByRole('button',{name:/Synthetic cleaning/}).click();
  await expect(page.locator('#booking-date')).toHaveAttribute('min','2026-10-29');
});

test('stale prices refresh service choice without discarding customer details',async ({page}) => {
  await page.route('**/api/v1/public/*/bookings',r=>r.fulfill({headers,status:409,json:{detail:'Service price changed'}}));
  await details(page); await page.getByRole('button',{name:'Confirm booking',exact:true}).click();
  await expect(page.getByRole('alert').filter({hasText:publicUx('en').priceChanged})).toBeVisible();
  await page.getByRole('button',{name:/Synthetic cleaning/}).click();
  await page.getByRole('button',{name:'12:00',exact:true}).click();
  await expect(page.locator('#customerName')).toHaveValue('Test customer');
});

test('server field validation is associated with the matching field',async ({page}) => {
  await page.route('**/api/v1/public/*/bookings',r=>r.fulfill({headers,status:400,json:{errors:{CustomerPhone:['Invalid phone']}}}));
  await details(page); await page.getByRole('button',{name:'Confirm booking',exact:true}).click();
  await expect(page.locator('#customerPhone')).toHaveAttribute('aria-invalid','true');
  await expect(page.locator('#customerPhone')).toBeFocused();
  await expect(page.locator('#customerPhone-error')).toBeVisible();
});

test('theme contrast and quote overlay clearance',async ({page},info) => {
  await page.route('**/api/v1/public/*/services?*',r=>r.fulfill({headers,json:[]}));
  await page.goto('/en/');
  const contrast = async () => page.locator('#quote button[type="submit"]').evaluate(el=>{
    const rgb=(s:string)=>s.match(/[\d.]+/g)!.slice(0,3).map(Number).map(v=>{const c=v/255;return c<=.04045?c/12.92:((c+.055)/1.055)**2.4;});
    const luminance=(s:string)=>rgb(s).reduce((a,v,i)=>a+v*[.2126,.7152,.0722][i],0);
    const style=getComputedStyle(el), a=luminance(style.color),b=luminance(style.backgroundColor);
    return (Math.max(a,b)+.05)/(Math.min(a,b)+.05);
  });
  expect(await contrast()).toBeGreaterThanOrEqual(4.5);
  const qr=page.locator('img[src$="contact-qr-en.svg"]');
  await qr.scrollIntoViewIfNeeded();
  await expect.poll(()=>qr.evaluate((el:HTMLImageElement)=>el.naturalWidth)).toBeGreaterThan(0);
  await page.getByRole('heading',{level:1}).scrollIntoViewIfNeeded();
  await page.screenshot({path:info.outputPath('public-home-light.png'),fullPage:true});
  await page.getByRole('button',{name:'Switch to dark mode',exact:true}).click();
  expect(await page.evaluate(()=>localStorage.getItem('theme'))).toBe('dark');
  expect(await contrast()).toBeGreaterThanOrEqual(4.5);
  await page.screenshot({path:info.outputPath('public-home-dark.png'),fullPage:true});
  await page.locator('#quote').scrollIntoViewIfNeeded();
  await expect(page.locator('a.whatsapp-pulse')).not.toBeVisible();
});

test('essential text remains visible when JavaScript is disabled',async ({browser})=>{
  const context=await browser.newContext({javaScriptEnabled:false});
  const page=await context.newPage(); await page.goto('http://localhost:3000/en/');
  await expect(page.getByRole('heading',{level:1})).toBeVisible();
  await expect(page.locator('footer a[href="mailto:info@readysetsiivous.fi"]')).toBeVisible();
  await context.close();
});

test('homepage service card uses gross price and its action opens booking',async ({page})=>{
  await page.route('**/api/v1/public/*/services?*',r=>r.fulfill({headers,json:[service]}));
  await page.goto('/en/');
  const services=page.locator('#services');
  await expect(services).toContainText('€62.75');
  await expect(services).not.toContainText('€50');
  await services.getByRole('link',{name:publicUx('en').book,exact:true}).click();
  await expect(page).toHaveURL(/\/en\/varaus\/$/);
});

test('hero and offline service actions reach the quote form',async ({page})=>{
  await page.route('**/api/v1/public/*/services?*',r=>r.fulfill({headers,status:503,json:{}}));
  await page.goto('/en/');
  await page.locator('.public-hero a[href="#quote"]').click();
  await expect(page).toHaveURL(/#quote$/);
  await expect(page.locator('#quote')).toBeInViewport();
  await page.locator('#services a[href="/en/#quote"]').first().click();
  await expect(page).toHaveURL(/\/en\/#quote$/);
  await expect(page.locator('#quote')).toBeInViewport();
});

test('keyboard skip link reaches the main landmark on homepage and legal pages',async ({page})=>{
  for (const path of ['/en/','/fi/privacy/','/sv/terms/']) {
    await page.goto(path);
    await page.keyboard.press('Tab');
    const skip=page.locator('.skip-link');
    await expect(skip).toBeFocused(); await expect(skip).toBeInViewport();
    await page.keyboard.press('Enter');
    await expect(page.locator('#main-content')).toBeFocused();
  }
});

for (const kind of ['notFound','unavailable'] as const) {
  test(`management lookup ${kind} clears results and focuses recovery field`,async ({page})=>{
    await page.route('**/api/v1/public/*/bookings/lookup',r=>kind==='notFound'
      ? r.fulfill({headers,status:404,json:{}}) : r.abort('failed'));
    await page.goto('/en/booking/');
    await page.locator('#manage-number').fill('RSS-2030-000001'); await page.locator('#manage-key').fill(key);
    await page.getByRole('button',{name:'View booking',exact:true}).click();
    await expect(page.getByRole('alert').filter({hasText:publicUx('en')[kind]})).toBeVisible();
    await expect(page.locator(kind==='notFound'?'#manage-key':'#manage-number')).toBeFocused();
    await expect(page.getByRole('button',{name:'Cancel this booking',exact:true})).toHaveCount(0);
    expect(page.url()).not.toContain(key);
  });
}

test('booking header and localized footer open the management journey',async ({page})=>{
  await page.route('**/api/v1/public/*/services?*',r=>r.fulfill({headers,json:[]}));
  await page.goto('/en/varaus/');
  await page.getByRole('link',{name:publicUx('en').manage,exact:true}).first().click();
  await expect(page).toHaveURL(/\/en\/booking\/$/);
  await page.goto('/sv/');
  await page.locator('footer').getByRole('link',{name:publicUx('sv').manage,exact:true}).click();
  await expect(page).toHaveURL(/\/sv\/booking\/$/);
});

test('language disclosure preserves equivalent route and returns keyboard focus',async ({page})=>{
  await page.goto('/en/privacy/');
  await page.getByRole('button',{name:'Switch language',exact:true}).click();
  await page.getByRole('button',{name:/Suomi/}).click();
  await expect(page).toHaveURL(/\/fi\/privacy\/$/);
  await expect(page.getByRole('button',{name:'Vaihda kieltä',exact:true})).toBeFocused();
  expect(await page.evaluate(()=>localStorage.getItem('lang'))).toBe('fi');
});

test('uncertain cancellation prevents a duplicate write and preserves booking status',async ({page})=>{
  await page.route('**/api/v1/public/*/bookings/lookup',r=>r.fulfill({headers,json:{bookingNumber:'RSS-2030-000001',status:'New',startLocalDate:'2030-01-15',startLocalTime:'12:00',serviceNameFi:'Siivous',serviceNameEn:'Cleaning',priceGross:62.75,durationMinutes:120}}));
  let calls=0;
  await page.route('**/api/v1/public/*/bookings/*/cancel',r=>{calls++;return r.abort('failed');});
  await page.goto('/en/booking/');await page.locator('#manage-number').fill('RSS-2030-000001');await page.locator('#manage-key').fill(key);
  await page.getByRole('button',{name:publicUx('en').lookup,exact:true}).click();
  await page.getByRole('button',{name:publicUx('en').cancel,exact:true}).click();
  const confirm=page.getByRole('button',{name:publicUx('en').cancelConfirm,exact:true});await confirm.click();
  await expect(page.getByRole('alert').filter({hasText:publicUx('en').uncertain})).toBeVisible();
  await expect(confirm).toBeDisabled();expect(calls).toBe(1);
  await expect(page.getByText(publicUx('en').cancelDone,{exact:true})).toHaveCount(0);
  await expect(page.getByText(`${publicUx('en').status}: ${publicUx('en').pending}`,{exact:true})).toBeVisible();expect(page.url()).not.toContain(key);
});

test('client validation blocks writes and back navigation preserves details',async ({page})=>{
  let calls=0;await page.route('**/api/v1/public/*/bookings',r=>{calls++;return r.abort();});
  await details(page);
  const submit=page.getByRole('button',{name:'Confirm booking',exact:true});
  for(const id of ['customerName','customerPhone','street','city']) await page.locator(`#${id}`).fill('');
  await submit.click();await expect(page.locator('#customerName')).toBeFocused();
  for(const id of ['customerName','customerPhone','street','city']) await expect(page.locator(`#${id}`)).toHaveAttribute('aria-invalid','true');
  for(const [id,value] of Object.entries({customerName:'Test customer',customerPhone:'123',street:'Test street',city:'Helsinki'}))await page.locator(`#${id}`).fill(value);
  await page.locator('#customerEmail').fill('invalid');await submit.click();
  await expect(page.locator('#customerEmail-error')).toHaveText(publicUx('en').emailInvalid);
  await expect(page.locator('#customerEmail')).toBeFocused();await page.locator('#customerEmail').fill('');
  await page.locator('#accepted').uncheck();await submit.click();await expect(page.locator('#accepted')).toHaveAttribute('aria-invalid','true');await expect(page.locator('#accepted')).toBeFocused();expect(calls).toBe(0);
  await page.getByRole('button',{name:/←.*Back/}).click();await expect(page.locator('#booking-date')).toBeVisible();
  await page.getByRole('button',{name:/←.*Back/}).click();await page.getByRole('button',{name:/Synthetic cleaning/}).click();
  await page.getByRole('button',{name:'12:00',exact:true}).click();await expect(page.locator('#customerName')).toHaveValue('Test customer');
});

test('empty service catalog provides a contact recovery path',async ({page})=>{
  await page.route('**/api/v1/public/*/services?*',r=>r.fulfill({headers,json:[]}));
  await page.goto('/en/');await expect(page.locator('#services')).toContainText(publicUx('en').noServices);
  await page.goto('/en/varaus/');await expect(page.getByText(publicUx('en').noServices,{exact:true})).toBeVisible();
  await expect(page.getByRole('button',{name:getDictionary('en').varaus.contactViaWhatsApp,exact:true})).toBeVisible();
  await expect(page.getByRole('button',{name:/Synthetic cleaning/})).toHaveCount(0);
});
