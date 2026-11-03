import { test, expect } from '@playwright/test';
import { setupAdminMocks } from './admin-helpers';
import { compareBranchesByPostalCode } from '../src/lib/branchOrder';
const headers = {'Access-Control-Allow-Origin':'*','Access-Control-Allow-Methods':'GET,POST,PUT,OPTIONS','Access-Control-Allow-Headers':'Authorization,Content-Type'};
test('postcode sorting retains leading zeros and puts missing codes last', () => {
 const rows = [{id:'missing',name:'A'}, {id:'later',postalCode:'33100'}, {id:'first',postalCode:'00100'}, {id:'short',postalCode:'100'}, {id:'spaced',postalCode:' 00 200 '}];
 expect(rows.sort(compareBranchesByPostalCode).map(row => row.id)).toEqual(['first','spaced','short','later','missing']);
});
test('partner properties sort by postcode and request summaries use stored details', async ({page}) => {
 await setupAdminMocks(page);await page.addInitScript(()=>{(window as any).__MOCK_ROLES__=['partner'];});
 const branch={id:'first',companyId:'owner',name:'Lake road 1, 00100 Helsinki',street:'Lake road 1',postalCode:'00100',city:'Helsinki',roomCount:2,bedCount:3,sizeSquareMeters:55.5,priceNet:70,vatRatePercent:25.5,estimatedDurationMinutes:120,pricingReady:true,isActive:true};
 const branches=[{...branch,id:'missing',name:'A missing code',postalCode:null},{...branch,id:'later',name:'Later property',postalCode:'33100'},branch];
 await page.route(/\/api\/v1\/partner\//,r=>{const path=new URL(r.request().url()).pathname.split('/partner')[1];if(r.request().method()==='OPTIONS')return r.fulfill({headers});return r.fulfill({headers,json:path==='/me'?{realm:'readysetsiivous',person:{id:'owner',firstName:'Test',lastName:'Owner',role:'partner',isActive:true},company:{id:'owner',name:'Owner company',isActive:true,isPartnerEnabled:true},timeZoneId:'Europe/Helsinki'}:path==='/branches'?branches:[]});});
 await page.goto('/partner/');await expect(page.getByRole('article').first()).toHaveAttribute('aria-label',branch.name);
 await expect(page.getByRole('article').last()).toHaveAttribute('aria-label','A missing code');
 await page.getByRole('article',{name:branch.name,exact:true}).getByRole('button',{name:'Request cleaning',exact:true}).click();
 const form=page.getByRole('region',{name:'Cleaning request form'});const summary=page.locator('.partner-form-panel').filter({has:page.getByRole('heading',{name:new RegExp('New cleaning')})});
 await expect(summary).toContainText('Owner company');await expect(summary).toContainText('55.5');await expect(summary).toContainText('€70.00');
 await page.getByRole('button',{name:'Add property',exact:true}).click();await page.getByLabel('Street',{exact:true}).fill('Forest road 8');await page.getByLabel('Postal code',{exact:true}).fill('00200');await page.getByLabel('City',{exact:true}).fill('Helsinki');
 await expect(page.getByLabel('Property name',{exact:true})).toHaveValue('Forest road 8, 00200 Helsinki');await expect(page.getByLabel('Property name',{exact:true})).toHaveAttribute('readonly','');
});


for (const role of ['admin', 'manager'] as const) test(`${role} property defaults route future work and failed save keeps entered details`, async ({page}) => {
 await setupAdminMocks(page);await page.addInitScript(role=>{(window as any).__MOCK_ROLES__=[role];},role);const companyId='owner';
 if(role==='manager') await page.route(/\/api\/v1\/me$/,r=>r.fulfill({headers,json:{realm:'readysetsiivous',roles:[JSON.stringify({roles:['manager']})],employee:{id:'manager',role:'manager',isActive:true}}}));
 await page.route(new RegExp(`/api/v1/${role}/companies(?:\\?.*)?$`),r=>r.fulfill({headers,json:{items:[{id:companyId,name:'Owner company',isActive:true,isPartnerEnabled:true}],total:1}}));
 await page.route(new RegExp(`/api/v1/${role}/employees$`),r=>r.fulfill({headers,json:[{id:'cleaner',firstName:'Active',lastName:'Cleaner',role:'employee',isActive:true},{id:'inactive',firstName:'Inactive',lastName:'Cleaner',role:'employee',isActive:false},{id:'partner',firstName:'Partner',lastName:'Person',role:'partner',isActive:true}]}));
 const writes:any[]=[];let fail=true;
 await page.route(new RegExp(`/api/v1/${role}/partner/`),r=>{if(r.request().method()==='OPTIONS')return r.fulfill({headers});if(r.request().method()==='GET')return r.fulfill({headers,json:[]});writes.push(r.request().postDataJSON());return r.fulfill({headers,status:fail?503:200,json:fail?{detail:'Property save unavailable'}:{id:'saved'}});});
 await page.goto(`/${role}/partner-requests/`);await page.getByRole('tab',{name:'Properties',exact:true}).click();await page.getByRole('button',{name:'Add property',exact:true}).click();
 await page.getByLabel('Company',{exact:true}).selectOption(companyId);await expect(page.getByLabel('street',{exact:true})).not.toHaveAttribute('required','');await page.getByLabel('street',{exact:true}).fill('Forest road 8');await page.getByLabel('Postal code',{exact:true}).fill('00200');await page.getByLabel('city',{exact:true}).fill('Helsinki');
 await expect(page.getByLabel('Property name',{exact:true})).toHaveValue('Forest road 8, 00200 Helsinki');await expect(page.getByLabel('Property name',{exact:true})).toHaveAttribute('readonly','');
 await page.getByLabel('Size (m², optional)',{exact:true}).fill('55.5');const cleaner=page.getByLabel('Preferred cleaner (optional)',{exact:true});await expect(cleaner.locator('option')).toHaveCount(2);await cleaner.selectOption('cleaner');
 await page.getByRole('button',{name:'Save property',exact:true}).click();await expect(page.getByRole('alert').filter({hasText:'Property save unavailable'})).toBeVisible();await expect(cleaner).toHaveValue('cleaner');await expect(page.getByLabel('Size (m², optional)',{exact:true})).toHaveValue('55.5');
 expect(writes[0]).toMatchObject({name:'Forest road 8, 00200 Helsinki',companyId,sizeSquareMeters:55.5,preferredCleanerEmployeeId:'cleaner'});fail=false;await page.getByRole('button',{name:'Save property',exact:true}).click();await expect(page.getByText('Property saved.',{exact:true})).toBeVisible();
});

test('unavailable preferred cleaner survives unrelated staff property edits and can be explicitly cleared', async ({page}) => {
 await setupAdminMocks(page);const company={id:'owner',name:'Owner company',isActive:true,isPartnerEnabled:true};
 const branch={id:'property',companyId:'owner',name:'Lake road 1, 00100 Helsinki',street:'Lake road 1',postalCode:'00100',city:'Helsinki',isActive:true,preferredCleanerEmployeeId:'unavailable',sizeSquareMeters:40};const writes:any[]=[];
 await page.route(/\/api\/v1\/admin\/companies(?:\?.*)?$/,r=>r.fulfill({headers,json:{items:[company],total:1}}));
 await page.route(/\/api\/v1\/admin\/employees$/,r=>r.fulfill({headers,json:[]}));
 await page.route(/\/api\/v1\/admin\/partner\//,r=>{if(r.request().method()==='OPTIONS')return r.fulfill({headers});if(r.request().method()==='GET')return r.fulfill({headers,json:r.request().url().endsWith('/branches')?[branch]:[]});const body=r.request().postDataJSON();writes.push(body);Object.assign(branch,body);return r.fulfill({headers,json:branch});});
 await page.goto('/admin/partner-requests/');await page.getByRole('tab',{name:'Properties',exact:true}).click();await page.getByRole('button',{name:'Edit property',exact:true}).click();
 await expect(page.getByLabel('Preferred cleaner (optional)',{exact:true})).toHaveValue('unavailable');await page.getByLabel('Size (m², optional)',{exact:true}).fill('55.5');await page.getByRole('button',{name:'Save property',exact:true}).click();await expect(page.getByText('Property saved.',{exact:true})).toBeVisible();expect(writes[0]).toMatchObject({sizeSquareMeters:55.5});expect(writes[0]).not.toHaveProperty('preferredCleanerEmployeeId');
 await page.getByRole('button',{name:'Edit property',exact:true}).click();await page.getByLabel('Preferred cleaner (optional)',{exact:true}).selectOption('');await page.getByRole('button',{name:'Save property',exact:true}).click();await expect.poll(()=>writes.length).toBe(2);expect(writes[1]).toHaveProperty('preferredCleanerEmployeeId',null);
});

test('admin partner branch form generates its address title and persists cleaner and size', async ({page},testInfo) => {
 await setupAdminMocks(page);const company={id:'owner',name:'Owner company',isActive:true,isPartnerEnabled:true};let branches:any[]=[];const writes:any[]=[];
 await page.route(/\/api\/v1\/admin\/employees$/,r=>r.fulfill({headers,json:[{id:'cleaner',firstName:'Active',lastName:'Cleaner',isActive:true,role:'employee'}]}));
 await page.route(/\/api\/v1\/admin\/companies(?:\/[^?]+)?(?:\?.*)?$/,r=>{if(r.request().method()==='OPTIONS')return r.fulfill({headers});if(r.request().method()==='GET')return r.fulfill({headers,json:new URL(r.request().url()).pathname.endsWith('/owner')?{company,branches}:{items:[company],total:1}});const body=r.request().postDataJSON();writes.push(body);branches=[{id:'branch',companyId:'owner',isActive:true,...body}];return r.fulfill({headers,json:branches[0]});});
 await page.goto('/admin/companies/');await page.getByRole('button',{name:'Branches',exact:true}).click();await page.getByRole('button',{name:'Add branch',exact:true}).click();const dialog=page.getByRole('dialog',{name:'Add branch'});
 await dialog.getByLabel('Street',{exact:true}).fill('Forest road 8');await dialog.getByLabel('Postal code',{exact:true}).fill('00200');await dialog.getByLabel('City',{exact:true}).fill('Helsinki');await expect(dialog.getByRole('textbox',{name:'Property name (from address)',exact:true})).toHaveValue('Forest road 8, 00200 Helsinki');await expect(dialog.getByRole('textbox',{name:'Property name (from address)',exact:true})).toHaveAttribute('readonly','');await dialog.getByLabel('Size (m², optional)',{exact:true}).fill('55.5');await dialog.getByLabel('Preferred cleaner (optional)',{exact:true}).click();await page.getByRole('option',{name:'Active Cleaner',exact:true}).click();
 await page.screenshot({path:testInfo.outputPath('partner-branch-form.png'),fullPage:true});await dialog.getByRole('button',{name:'Add branch',exact:true}).click();await expect(dialog).not.toBeVisible();expect(writes[0]).toMatchObject({name:'Forest road 8, 00200 Helsinki',sizeSquareMeters:55.5,preferredCleanerEmployeeId:'cleaner'});await page.getByRole('group',{name:'Branch Forest road 8, 00200 Helsinki',exact:true}).getByRole('button',{name:'Edit',exact:true}).click();await expect(page.getByRole('textbox',{name:'Preferred cleaner (optional)',exact:true})).toHaveValue('Active Cleaner');await expect(page.getByLabel('Size (m², optional)',{exact:true})).toHaveValue('55.5');
});


test('automatic preference still loads and exposes flexible pending offers, then hides confirmed empty panel', async ({page}) => {
 await setupAdminMocks(page); await page.addInitScript(() => { (window as any).__MOCK_ROLES__=['employee']; });
 await page.route(/\/api\/v1\/me\/work-preferences$/,r=>r.fulfill({headers,json:{autoAcceptWork:true}}));
 await page.route(/\/api\/v1\/me\/shifts\/calendar(?:\?.*)?$/,r=>r.fulfill({headers,json:{occurrences:[],timeZoneId:'Europe/Helsinki',fromLocalDate:'2026-12-01',toLocalDate:'2026-12-01'}}));
 let offer:any={id:'flexible',version:3,branchName:'Flexible apartment',fromDate:'2026-12-01',toDate:'2026-12-02',plannedStartUtc:null,estimatedDurationMinutes:120,timeZoneId:'Europe/Helsinki'};const decisions:any[]=[];let reads=0;let holdFocusRead=false;let releaseStale:(()=>Promise<unknown>)|undefined;
 await page.route(/\/api\/v1\/me\/partner-offers(?:\/.*)?$/,r=>{if(r.request().method()==='OPTIONS')return r.fulfill({headers});if(r.request().method()==='GET'){reads++;if(holdFocusRead){holdFocusRead=false;const snapshot={...offer};releaseStale=()=>r.fulfill({headers,json:[snapshot]});return;}return r.fulfill({headers,json:offer?[offer]:[]});}decisions.push(r.request().postDataJSON());offer=null;return r.fulfill({headers,json:{}});});
 await page.goto('/me/shifts/');await expect(page.getByRole('article',{name:'Flexible apartment offer'})).toBeVisible();expect(reads).toBeGreaterThan(0);
 await page.getByRole('button',{name:'Accept cleaning',exact:true}).click();await expect(page.getByText('Choose a planned start time in the requested window.')).toBeVisible();expect(decisions).toHaveLength(0);
 await page.getByLabel('Planned start for Flexible apartment',{exact:true}).fill('2026-12-02T10:00');
 holdFocusRead=true;await page.evaluate(()=>window.dispatchEvent(new Event('focus')));await expect.poll(()=>!!releaseStale).toBe(true);
 await page.getByRole('button',{name:'Accept cleaning',exact:true}).click();
 await expect(page.getByRole('region',{name:'Cleaning offers',exact:true})).toHaveCount(0);expect(decisions).toEqual([{version:3,plannedStartLocal:'2026-12-02T10:00'}]);
 const staleResponse=page.waitForResponse(response=>response.url().endsWith('/partner-offers')&&response.request().method()==='GET');
 await releaseStale!();await (await staleResponse).finished();
 await page.evaluate(()=>new Promise<void>(resolve=>requestAnimationFrame(()=>requestAnimationFrame(()=>resolve()))));
 await expect(page.getByRole('region',{name:'Cleaning offers',exact:true})).toHaveCount(0);expect(decisions).toHaveLength(1);
 offer={id:'new-flexible',version:1,branchName:'New flexible apartment',fromDate:'2026-12-03',toDate:'2026-12-04',plannedStartUtc:null,estimatedDurationMinutes:60,timeZoneId:'Europe/Helsinki'};
 await page.evaluate(()=>window.dispatchEvent(new Event('focus')));await expect(page.getByRole('article',{name:'New flexible apartment offer'})).toBeVisible();
});
