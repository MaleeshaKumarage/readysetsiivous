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
