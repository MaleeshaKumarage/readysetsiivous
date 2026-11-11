import { join } from 'node:path';
import { test, expect } from '@playwright/test';
import { PartnerFlowBackend, OWNER_COMPANY, prepareRolePage } from './partner-flow-backend';
for (const role of ['owner', 'admin', 'cleaner'] as const) {
  test(`${role} workspace keeps its existing view`, async ({page}, info) => {
    const backend = new PartnerFlowBackend({priced:true});
    backend.addBranch({name:'Harbour studio', companyId:OWNER_COMPANY.id, city:'Helsinki'});
    backend.seedRequest(backend.branches[0], {fromDate:'2026-12-01',toDate:'2026-12-02',notes:'Clean the kitchen and prepare fresh linen.'});
    await prepareRolePage(page, backend, role);
    await page.route(/\/api\/v1\/partner\/inventory-acknowledgments\?/, route => route.fulfill({json:{items:[],pendingCount:0,hasMore:false},headers:{'Access-Control-Allow-Origin':'*'}}));
    await page.goto(role === 'owner' ? '/partner/' : role === 'admin' ? '/admin/partner-requests/' : '/me/shifts/');
    await expect(page.getByRole('heading', {name:role === 'owner' ? 'Properties' : role === 'admin' ? 'Partner operations' : 'My Shifts',exact:true})).toBeVisible();
    await expect(page.locator('meta[name=viewport]')).toHaveAttribute('content',/viewport-fit=cover/);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    const imagePath = (name: string) => process.env.PORTAL_UI_EVIDENCE_DIR ? join(process.env.PORTAL_UI_EVIDENCE_DIR, `${info.project.name}-${name}.png`) : info.outputPath(`${name}.png`);
    const screenshot = imagePath(role);
    await page.screenshot({path:screenshot,fullPage:true,scale:'css'});
    await info.attach(role,{path:screenshot,contentType:'image/png'});
    if(role === 'owner') {
      await page.getByRole('button',{name:'Request cleaning',exact:true}).first().click();
      await expect(page.getByRole('dialog')).toBeVisible();
      const dialogScreenshot = imagePath('dialog');
      await page.screenshot({path:dialogScreenshot,scale:'css'});
      await info.attach('dialog',{path:dialogScreenshot,contentType:'image/png'});
    }
  });
}
