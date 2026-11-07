/* Real jellyfin-web 12.1 UI; ONLY a marked disposable loopback fixture.
 * No API interception, synthetic responses or production inputs.
 * Run: JELLYP_PLAYWRIGHT_MODULE=... JELLYP_AXE_MODULE=... node
 * scripts/smoke-native-ui.cjs --root /tmp/opencode/jellyp-native
 * --screenshots /tmp/opencode/jellyp-native/ui-evidence [--journey]
 * --journey uploads generated PNGs and exercises conversion/regeneration/restore
 * and permanent retirement of generated backups only, inside the verified root.
 */
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const zlib = require('node:zlib');
const {chromium} = require(process.env.JELLYP_PLAYWRIGHT_MODULE || 'playwright');
const args = process.argv.slice(2);
const arg = key => args.includes(key) ? args[args.indexOf(key) + 1] : undefined;
if (!arg('--root')) throw Error('--root required');
const root = fs.realpathSync(arg('--root'));
const base = 'http://127.0.0.1:18096';
if (!fs.existsSync(path.join(root,'.jellyp-smoke-root'))) throw Error('Unmarked disposable root');
const auth = JSON.parse(fs.readFileSync(path.join(root,'fixture-auth.json'),'utf8'));
const headers = {Authorization:'MediaBrowser Token="' + auth.token + '"'};
const screenshots = arg('--screenshots') || path.join(root,'ui-evidence');
fs.mkdirSync(screenshots,{recursive:true});
const checks = [], errors = [], outside = [], failedLocalRequests = [];
let activePage, activeBrowser;
const check = (truth,label) => { assert.ok(truth,label); checks.push(label); console.log('PASS ' + label); };
async function api(endpoint,method = 'GET',body,extra = {}) {
 const response = await fetch(base + '/' + endpoint,{method,headers:{...headers,...(body === undefined ? {} : {'Content-Type':'application/json'}),...extra},...(body === undefined ? {} : {body:typeof body === 'string' ? body : JSON.stringify(body)})});
 if (!response.ok) throw Error(endpoint.split('?')[0] + ': HTTP ' + response.status);
 const text = await response.text(); return text ? JSON.parse(text) : null;
}
async function until(callback,label = 'state',timeout = 30000) {
 const deadline = Date.now() + timeout;
 while (Date.now() < deadline) { const value = await callback(); if (value) return value; await new Promise(resolve => setTimeout(resolve,100)); }
 throw Error('Timed out: ' + label);
}
async function nativeReady(page) {
 await page.waitForFunction(() => {
  const root = document.querySelector('#JellyPConfigPage:not(.hide)');
  return root && [...root.querySelectorAll('[is="emby-button"],[is="emby-linkbutton"]')].every(node => node.classList.contains('emby-button'));
 });
 await page.locator('#JellyPConfigPage:not(.hide)').evaluate(async root => {
  const animations = root.getAnimations({subtree:true}).filter(animation => Number.isFinite(animation.effect.getTiming().iterations));
  await Promise.all(animations.map(animation => animation.finished.catch(() => {})));
 });
}
async function main() {
 const info = await api('System/Info');
 check(fs.realpathSync(info.ProgramDataPath) === path.join(root,'data'),'marker + real ProgramDataPath verified before any mutation');
 check(info.Version === '12.1.0','official Jellyfin 12.1 host');
 const settings = await api('JellyP/Settings');
 check(!!settings.Automation && !!settings.Defaults && !!settings.Globals,'runtime DTO PascalCase, not documentation camelCase');
 if (!process.env.JELLYP_UI_SOURCE) {
  const response = await fetch(base + '/web/ConfigurationPage?name=jellyp',{headers});
  const embedded = await response.text();
  check(response.ok && embedded === fs.readFileSync(path.join(__dirname,'../Jellyfin.Plugin.JellyP/Web/config.html'),'utf8'),'installed DLL serves exact current embedded page; no source replacement');
 }
 const unauth = await fetch(base + '/JellyP/Settings');
 check(unauth.status === 401,'real unauthenticated API returns 401');
 const browser = await chromium.launch({headless:true});
 activeBrowser = browser;
 const context = await browser.newContext({viewport:{width:1600,height:1100},reducedMotion:'reduce'});
 // Block outbound traffic only. Every localhost response comes from the host.
 await context.route('**/*',route => { const url = new URL(route.request().url()); if (url.origin === base || ['data:','blob:'].includes(url.protocol)) return route.continue(); outside.push(url.origin); return route.abort(); });
 const page = await context.newPage();
 activePage = page;
 // Optional development replacement of this static resource only. Final evidence
 // MUST run without JELLYP_UI_SOURCE, against the installed embedded DLL page.
 if (process.env.JELLYP_UI_SOURCE) await page.route(/\/configurationpage\?name=jellyp(?:&|$)/i,route => route.fulfill({contentType:'text/html',body:fs.readFileSync(process.env.JELLYP_UI_SOURCE,'utf8')}));
  page.on('pageerror',error => errors.push(error.message));
  page.on('requestfailed',request => {
   const url = new URL(request.url());
   if (url.origin === base) failedLocalRequests.push({path:url.pathname,error:request.failure()?.errorText});
  });
 await page.goto(base + '/web/',{waitUntil:'domcontentloaded'});
 await page.locator('#txtManualName').fill('jellyp-test-admin');
 await page.locator('#txtManualPassword').fill(fs.readFileSync(path.join(root,'test-password'),'utf8'));
 await page.locator('button[type=submit]').click();
 await page.waitForURL(url => !url.hash.includes('/login'));
 await page.goto(base + '/web/#/configurationpage?name=jellyp');
 const ui = page.locator('#JellyPConfigPage:not(.hide)');
 await ui.locator('[data-jp-id=draftQuality]').waitFor();
 await ui.locator('[data-jp-id=Language]').selectOption('en');
 await page.waitForFunction(() => document.querySelector('#JellyPConfigPage:not(.hide) #JpTitle')?.textContent === 'Simulate');
 check(await ui.locator('[data-jp-id=draftQuality]').evaluate(node => node.classList.contains('emby-input')),'real native emby-input enhancement');
 check(await ui.locator('[data-jp-id=draftPrimaryEnabled]').getAttribute('data-embycheckbox') === 'true','real native dynamic emby-checkbox enhancement');
 check(await ui.locator('.checkboxOutline').count() >= 11,'host checkbox outlines, not fake controls');
 check(await ui.locator('.selectArrowContainer').count() >= 3,'host select arrows loaded');
 check(await ui.locator('[data-jp-id=draftQuality]').evaluate(node => document.querySelectorAll('label[for="' + node.id + '"]').length === 1),'one native label per input');
 check(await ui.locator('[data-type]').count() === 11,'all eleven backend image categories represented');
 await page.screenshot({path:path.join(screenshots,'01-native-selection-en-dark-desktop.png'),fullPage:true});
 await ui.locator('[data-type=Primary] [data-action=disclose]').focus();
 await page.keyboard.press('Enter');
 check(await ui.locator('[data-jp-id=draftPrimaryEditor]').isVisible(),'keyboard disclosure opens real per-type editor');
 await ui.locator('[data-type=Primary] [data-action=disclose]').click();
 await ui.locator('#JpNav nav a').filter({hasText:'Automation'}).click();
 await ui.locator('#SaveAutomation').waitFor();
 check(page.url().includes('configurationpage?name=jellyp') && page.url().includes('jp=automation'),'internal navigation uses genuine host configurationpage router');
 await ui.locator('[data-jp-id=autoQuality]').fill('84');
 await ui.locator('#JpNav nav a').filter({hasText:'Settings',exact:true}).click();
 await ui.locator('#SaveSettings').waitFor();
 await page.goBack(); await ui.locator('[data-jp-id=autoQuality]').waitFor();
 check(await ui.locator('[data-jp-id=autoQuality]').inputValue() === '84','browser Back preserves unsaved automation draft');
 await page.screenshot({path:path.join(screenshots,'02-native-automation-en-dark-desktop.png'),fullPage:true});
 if (process.env.JELLYP_AXE_MODULE) {
  const AxeBuilder = require(process.env.JELLYP_AXE_MODULE).default;
  const results = await new AxeBuilder({page}).include('#JellyPConfigPage:not(.hide)').withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  fs.writeFileSync(path.join(screenshots,'axe-automation.json'),JSON.stringify(results.violations,null,2));
  check(results.violations.length === 0,'axe native automation WCAG A/AA');
 }
 if (args.includes('--journey')) await journey(page,settings);
 await ui.locator('[data-jp-id=Language]').selectOption('pt');
 await page.waitForFunction(() => document.querySelector('#JellyPConfigPage:not(.hide) #JpTitle')?.textContent === 'Automação');
 await page.screenshot({path:path.join(screenshots,'03-native-automation-pt-dark-desktop.png'),fullPage:true});
 await page.setViewportSize({width:390,height:844});
 await page.screenshot({path:path.join(screenshots,'04-native-automation-pt-dark-mobile.png'),fullPage:true});
 check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),'mobile no horizontal overflow');
 check(await ui.locator('#SaveAutomation').evaluate(node => getComputedStyle(node.closest('aside')).position === 'static'),'mobile save aside is in normal flow');
 async function audit(label) {
  if (!process.env.JELLYP_AXE_MODULE) return;
  const AxeBuilder = require(process.env.JELLYP_AXE_MODULE).default;
  const result = await new AxeBuilder({page}).include('#JellyPConfigPage:not(.hide)').withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  fs.writeFileSync(path.join(screenshots,'axe-' + label + '.json'),JSON.stringify(result.violations,null,2));
  check(result.violations.length === 0,'axe ' + label + ' WCAG A/AA');
 }
 await audit('automation-pt-dark-mobile');
 async function hostTheme(theme) {
  await page.goto(base + '/web/#/mypreferencesdisplay?userId=' + auth.userId);
  await page.locator('#mui-component-select-dashboardTheme').click();
  await page.locator('[role=option][data-value="' + theme + '"]').click();
  await page.locator('button[type=submit]:visible').click();
  await page.waitForLoadState('networkidle');
  await page.goto(base + '/web/#/configurationpage?name=jellyp&jp=automation');
  await ui.locator('#SaveAutomation').waitFor();
 }
 await hostTheme('light');
 await nativeReady(page);
 const light = await ui.locator('.paperList').first().evaluate(node => getComputedStyle(node).backgroundColor);
 check(light !== 'rgb(32, 32, 32)' && light !== 'rgba(0, 0, 0, 0)','real host Light theme supplies plugin paperList surface');
 await page.screenshot({path:path.join(screenshots,'05-native-automation-pt-light-mobile.png'),fullPage:true});
 await audit('automation-pt-light-mobile');
 await page.setViewportSize({width:1600,height:1100});
 await nativeReady(page);
 await page.screenshot({path:path.join(screenshots,'06-native-automation-pt-light-desktop.png'),fullPage:true});
 await audit('automation-pt-light-desktop');
 for (const area of ['simulate','settings','history']) {
  await ui.locator('#JpNav nav a[href*="jp=' + area + '"]').click();
  await until(async () => (await ui.locator('#JpNav nav a[aria-current=page]').getAttribute('href')).includes('jp=' + area) && await ui.locator('#JpTitle').isVisible(),'light ' + area);
  await audit(area + '-pt-light-desktop');
  await nativeReady(page);
  await page.screenshot({path:path.join(screenshots,'07-native-' + area + '-pt-light-desktop.png'),fullPage:true});
 }
 await hostTheme('dark');
 await page.setViewportSize({width:768,height:1024});
 await page.screenshot({path:path.join(screenshots,'08-native-automation-pt-dark-tablet.png'),fullPage:true});
 check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),'tablet no horizontal overflow');
 check(errors.length === 0,'zero browser page errors');
 check(outside.every(origin => origin === 'https://www.gstatic.com'),'all outbound attempts blocked; only official host Cast sender attempted gstatic, no JellyP external dependency');
  fs.writeFileSync(path.join(screenshots,'native-summary.json'),JSON.stringify({checks,errors,outside,failedLocalRequests},null,2));
 await browser.close();
 console.log(checks.length + ' real native checks passed');
}
function png(seed) {
 let state = seed + 1;
 const rows = Buffer.alloc(512 * (512 * 4 + 1));
 for (let y = 0; y < 512; y++) for (let x = 0; x < 512; x++) {
  const i = y * 2049 + 1 + x * 4;
  state = (1664525 * state + 1013904223) >>> 0;
  rows[i] = x >> 1; rows[i + 1] = y >> 1; rows[i + 2] = 90 + seed * 3 + (state % 5); rows[i + 3] = 255;
 }
 function crc(buffer) { let value = 0xffffffff; for (const byte of buffer) { value ^= byte; for (let bit = 0; bit < 8; bit++) value = (value >>> 1) ^ (value & 1 ? 0xedb88320 : 0); } return (value ^ 0xffffffff) >>> 0; }
 function chunk(type,data) { const kind = Buffer.from(type), head = Buffer.alloc(4), tail = Buffer.alloc(4); head.writeUInt32BE(data.length); tail.writeUInt32BE(crc(Buffer.concat([kind,data]))); return Buffer.concat([head,kind,data,tail]); }
 const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(512,0); ihdr.writeUInt32BE(512,4); ihdr[8] = 8; ihdr[9] = 6;
 return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',ihdr),chunk('IDAT',zlib.deflateSync(rows)),chunk('IEND',Buffer.alloc(0))]);
}
const hash = data => crypto.createHash('sha256').update(data).digest('hex').toUpperCase();
function fileHash(file) {
 const real = fs.realpathSync(file);
 assert.ok(real.startsWith(path.join(root,'data') + path.sep),'Refusing nonfixture path');
 return hash(fs.readFileSync(real));
}
function pathFree(value) {
 if (!value || typeof value !== 'object') return true;
 return Object.entries(value).every(([key,child]) => !/path/i.test(key) && pathFree(child));
}
async function journey(page,baseline) {
 const ui = page.locator('#JellyPConfigPage:not(.hide)');
 const field = name => ui.locator('[data-jp-id="' + name + '"]');
 async function checked(locator,value) { if (await locator.isChecked() !== value) await locator.locator('..').click(); assert.equal(await locator.isChecked(),value); }
 async function navigate(area) {
  await ui.locator('#JpNav nav a[href*="jp=' + area + '"]').click();
  await until(async () => (await ui.locator('#JpNav nav a[aria-current=page]').getAttribute('href')).includes('jp=' + area) && await ui.locator('#JpTitle').isVisible(),'host ' + area + ' view');
 }
 async function expandFor(locator) { if (!await locator.isVisible()) await locator.locator('xpath=ancestor::details[1]').locator('summary').first().click(); }
 const nativeBefore = (await api('ScheduledTasks')).filter(t => t.Key.startsWith('JellyP')).map(t => ({Id:t.Id,Key:t.Key,Triggers:t.Triggers}));
 const autoBefore = JSON.stringify(baseline.Automation);
 const fixture = (await api('Items?Recursive=true&IncludeItemTypes=Movie')).Items.find(item => item.Name === 'JellyP Canary (2026)');
 assert.ok(fixture,'Synthetic movie missing');
 assert.equal((await api('JellyP/Backups')).Total,0,'Start with no active backups; refuse to overwrite a previous unfinished journey');
 const mediaSource = path.join(root,'media','JellyP Canary (2026)','JellyP Canary (2026).mp4');
 assert.ok(fs.realpathSync(mediaSource).startsWith(root + path.sep));
 for (let index = 1; index <= 12; index++) {
  const name = 'JellyP Native ' + String(index).padStart(2,'0') + ' (2026)';
  const directory = path.join(root,'media',name); fs.mkdirSync(directory,{recursive:true});
  const destination = path.join(directory,name + '.mp4'); if (!fs.existsSync(destination)) fs.copyFileSync(mediaSource,destination);
 }
 await api('Library/Refresh','POST');
 const movies = await until(async () => { const all = (await api('Items?Recursive=true&IncludeItemTypes=Movie')).Items; return all.filter(item => item.Name.startsWith('JellyP Native ')).length === 12 ? all.filter(item => item.Name.startsWith('JellyP Native ')).sort((a,b) => a.Name.localeCompare(b.Name)) : null; },'generated movie library scan',60000);
 const fixtureIds = new Set([fixture.Id,...movies.map(item => item.Id)]);
 const sourceHashes = new Set();
 const fixtureDir = path.join(root,'native-png'); fs.mkdirSync(fixtureDir,{recursive:true});
 for (let seed = 0; seed < 14; seed++) {
  const data = png(seed); sourceHashes.add(hash(data)); fs.writeFileSync(path.join(fixtureDir,'slot-' + seed + '.png'),data);
  await api('Items/' + (seed < 2 ? fixture.Id : movies[seed - 2].Id) + '/Images/' + (seed === 1 ? 'Thumb' : 'Primary'),'POST',data.toString('base64'),{'Content-Type':'image/png'});
 }
 const registered = await Promise.all([...fixtureIds].map(id => api('Items/' + id + '/Images')));
 check(registered.reduce((sum,images) => sum + images.length,0) === 14,'14 real generated PNG slots registered through Jellyfin across 13 items');
 await navigate('settings'); await ui.locator('#SaveSettings').waitFor();
 await expandFor(field('cfgBatchSize')); await field('cfgBatchSize').fill('20');
 await checked(field('cfgPrimaryEnabled'),true); await checked(field('cfgThumbEnabled'),true);
 await expandFor(field('cfgBackdropEnabled')); await checked(field('cfgBackdropEnabled'),true);
 await field('cfgQuality').fill('85');
 const saveResponse = page.waitForResponse(r => r.url().includes('/JellyP/Settings/Defaults') && r.request().method() === 'PUT');
 await ui.locator('#SaveSettings').click(); check((await saveResponse).ok(),'native Settings explicitly saves manual defaults');
 await until(async () => (await ui.locator('#JpMessage').textContent()).includes('saved'),'settings saved');
 check(JSON.stringify((await api('JellyP/Settings')).Automation) === autoBefore,'saving defaults does not alter automation');
 await navigate('simulate'); await field('draftQuality').waitFor();
 await field('draftScope').selectOption('items');
 await field('draftSearch').fill('JellyP Canary'); await field('draftYear').fill('2026'); await field('draftYear').dispatchEvent('change');
 await field('draftItemType').selectOption('Movie');
 await ui.locator('[data-action=addItem]').filter({hasText:'JellyP Canary'}).first().click();
 await until(async () => (await ui.locator('[data-inspection]').textContent()).includes('Preliminary'),'real image inspection');
 check((await ui.locator('[data-inspection]').textContent()).includes('Thumbnails'),'real name/year/type search and registered-image inspection');
 await expandFor(field('draftIds')); await field('draftIds').fill(movies.map(item => base + '/web/#/details?id=' + item.Id).join(' '));
 await ui.locator('[data-action=addIds]').click();
 await until(async () => await ui.locator('[data-inspection]').count() === 13,'real item-ID helpers');
 await expandFor(field('draftBatchSize')); await field('draftBatchSize').fill('20');
 await expandFor(field('draftBackdropEnabled')); await checked(field('draftBackdropEnabled'),true);
 await field('draftQuality').fill('85');
 async function analyze() {
  const response = page.waitForResponse(r => r.url().endsWith('/JellyP/Plan/Analyze') && r.request().method() === 'POST');
  await ui.locator('#Analyze').click();
  const result = await response; assert.ok(result.ok(),'Analyze HTTP'); const plan = await result.json();
  await ui.locator('#JpSelectedBill').waitFor();
  check(pathFree(plan),'real analysis DTO is path-free'); return plan;
 }
 const initial = await analyze();
 check(initial.TotalImages >= 14 && initial.Proposed >= 14,'real server analyzes all fixture slots, not a simulated report');
 check(await ui.locator('[data-plan-key]:checked').count() === 0,'analysis starts with zero selected images');
 check((await ui.locator('#JpSelectedBill').textContent()).includes('No changes selected'),'zero selection bill is zero, not all proposals');
 check(await ui.locator('#JpSelectedBill').evaluate(node => node.compareDocumentPosition(node.parentElement.querySelector('[data-image-key]')) & Node.DOCUMENT_POSITION_PRECEDING),'calculator follows image list in DOM');
 const allKeys = await api('JellyP/Plan/' + initial.PlanId + '/Keys?kind=selectable');
 const first = ui.locator('[data-plan-key]').first(), firstKey = await first.getAttribute('data-plan-key');
 await checked(first,true);
 await until(async () => (await ui.locator('#JpSelectedBill').textContent()).includes('Only selected images · 1'),'bill 1');
 check(await first.evaluate(node => node === document.activeElement || node.parentElement.contains(document.activeElement)),'selection bill updates preserve focused checkbox node');
 await ui.locator('[data-list=images][data-offset="10"]').click();
 await until(async () => await ui.locator('#JpRangeimages').textContent() === '11–14 of 14','server page 2');
 const second = ui.locator('[data-plan-key]').first(), secondKey = await second.getAttribute('data-plan-key'); await checked(second,true);
 await until(async () => (await ui.locator('#JpSelectedBill').textContent()).includes('Only selected images · 2'),'crosspage bill');
 const review = await api('JellyP/Plan/' + initial.PlanId + '/Review','POST',{Keys:[firstKey,secondKey],Offset:0,Limit:10});
 check(review.TotalSelected === 2 && review.Billing.BackupBytesBefore === 0 && review.Billing.BackupBytesAfter > 0 && review.Billing.DeltaBytes < 0,'server cross-page subset billing counts active + new retained originals correctly');
 await page.screenshot({path:path.join(screenshots,'10-native-analysis-subset-crosspage.png'),fullPage:true});
 await ui.locator('[data-action=stage][data-stage="0"]').click(); await field('draftQuality').waitFor();
 await ui.locator('[data-action=stage][data-stage="1"]').click(); await ui.locator('#JpSelectedCount').waitFor();
 check((await ui.locator('#JpSelectedCount').textContent()).startsWith('2 '),'earlier real stage buttons preserve selection until an edit');
 await ui.locator('#Review').click(); await ui.locator('#Apply').waitFor();
 check(await ui.locator('#Apply').isDisabled(),'application locked until explicit review acknowledgement');
 await ui.locator('a[href*="sub=storage"]').click(); await ui.locator('#SaveSettings').waitFor();
 check(await ui.locator('#JpStorageTitle').evaluate(node => node === document.activeElement),'global protection detour focuses storage heading');
 await ui.locator('a[href*="return"]').count();
 await ui.locator('.jp-back a').click(); await ui.locator('#Apply').waitFor();
 check(await ui.locator('[data-image-key]').count() === 2,'visiting Settings preserves exact review subset');
 await until(async () => await ui.locator('#Apply').evaluate(node => node.classList.contains('emby-button')),'native Apply enhancement');
 await nativeReady(page);
 await page.screenshot({path:path.join(screenshots,'11-native-review-exact-subset.png'),fullPage:true});
 check(await ui.locator('#Apply').evaluate(node => node.classList.contains('emby-button')),'review uses actual native Apply button enhancement');
 check(await ui.locator('#Apply').evaluate(node => node.getBoundingClientRect().height >= 44 && getComputedStyle(node).transitionDuration === '0s'),'native Apply has a 44px touch target and respects reduced motion');
 async function apply(expectedKeys) {
  await checked(field('ReviewAck'),true);
  let attempts = 0;
  const count = request => { if (/\/JellyP\/Plan\/[^/]+\/Apply$/.test(request.url()) && request.method() === 'POST') attempts++; };
  page.on('request',count);
  const response = page.waitForResponse(r => /\/JellyP\/Plan\/[^/]+\/Apply$/.test(r.url()) && r.request().method() === 'POST');
  await ui.locator('#Apply').dblclick({delay:0}); const result = await response; assert.equal(result.status(),202);
  const body = result.request().postDataJSON(); assert.deepEqual([...body.Keys].sort(),[...expectedKeys].sort());
  const receipt = await result.json();
  const detail = await until(async () => { const d = await api('JellyP/Executions/' + receipt.OperationId); return ['Queued','Running'].includes(d.Operation.State) ? null : d; },'native execution',60000);
  assert.equal(detail.Operation.Failed,0); assert.equal(detail.Operation.Invalidated,0); assert.equal(detail.Operation.Changed,expectedKeys.length);
  page.off('request',count); check(attempts === 1,'double-click sends one exact persisted application request');
  await ui.locator('#JpExecutionBody').waitFor();
  return {receipt,detail};
 }
 const applied = await apply([firstKey,secondKey]);
 check(pathFree(applied.detail),'persisted operation and individual result DTOs are path-free');
 check(applied.detail.Items.every(row => row.Status === 'Changed' && sourceHashes.has(row.SourceHash)),'native task applies only two exact selected PNG identities');
 let records = (await api('JellyP/Status')).RecentRecords.filter(row => row.ExecutionId === applied.receipt.OperationId);
 check(records.length === 2 && records.every(row => fileHash(row.OutputPath) === row.OutputHash),'actual stored WebP file hashes match durable journal');
 const firstResult = applied.detail.Items.find(row => row.Key === firstKey);
 const firstRecord = records.find(row => row.Source.ItemId === firstResult.ItemId && row.Source.Type === firstResult.Type && row.Source.Index === firstResult.Index);
 assert.ok(firstRecord);
 await page.reload(); await ui.locator('#JpExecutionBody').waitFor();
 check((await ui.locator('#JpExecutionBody').textContent()).includes('Completed'),'reload recovers real completed persisted receipt');
 await page.screenshot({path:path.join(screenshots,'12-native-completion-reloaded.png'),fullPage:true});
 await ui.locator('[data-action=newSimulation]').click(); await field('draftQuality').waitFor(); await field('draftQuality').fill('80');
 const regenPlan = await analyze();
 const regenRows = await api('JellyP/Plan/' + regenPlan.PlanId + '/Images?limit=200');
 const regen = regenRows.Items.find(row => row.Key === firstKey);
 check(regen.Action === 'Regenerate' && regen.SourceHash === firstRecord.SourceHash,'quality change proposes real regeneration from retained original, not WebP recompression');
 // The first selected slot is on page one in the server's stable ordering.
 await checked(ui.locator('[data-plan-key="' + firstKey + '"]'),true);
 await ui.locator('#Review').click(); await ui.locator('#Apply').waitFor();
 const regenerated = await apply([firstKey]);
 records = (await api('JellyP/Status')).RecentRecords;
 const regenRecord = records.find(row => row.ExecutionId === regenerated.receipt.OperationId);
 check(regenRecord.SourceHash === firstRecord.SourceHash && regenRecord.OutputHash !== firstRecord.OutputHash && fileHash(regenRecord.OutputPath) === regenRecord.OutputHash,'regeneration preserves original hash and writes verified different-quality bytes');
 await navigate('history'); await ui.locator('#ReviewBackups').waitFor();
 const backup = (await api('JellyP/Backups?filter=restorable&limit=200')).Items.find(row => row.Key === firstKey);
 await ui.locator('[data-backup-id="' + backup.RecordId + '"] [data-action=reviewOneBackup]').click();
 await ui.locator('#JpDialog[open]').waitFor(); check((await ui.locator('#JpDialog').textContent()).includes(backup.SourceHash),'native restoration review displays actual stored checksum evidence');
 async function confirmBackup(kind) {
  await checked(field('BackupAck'),true);
  const response = page.waitForResponse(r => r.url().endsWith('/JellyP/Backups/' + kind) && r.request().method() === 'POST');
  await ui.locator('#ConfirmBackup').click(); const result = await response; assert.equal(result.status(),202); const receipt = await result.json();
  const detail = await until(async () => { const d = await api('JellyP/Executions/' + receipt.OperationId); return ['Queued','Running'].includes(d.Operation.State) ? null : d; },kind,60000);
  assert.equal(detail.Operation.Failed,0); assert.equal(detail.Operation.Invalidated,0);
  return detail;
 }
 await confirmBackup('Restore');
 check(fileHash(regenRecord.Source.Path) === firstRecord.SourceHash,'native restoration writes exact original bytes, not a browser WebP representation');
 // Populate >10 active originals using actual conversion, then exercise large
 // review paging, independent retire selection, restore subset and maintenance.
 await ui.locator('#JpExecutionBody').waitFor();
 await until(async () => await ui.locator('[data-action=newSimulation]').count(),'restore UI terminal');
 await ui.locator('[data-action=newSimulation]').click(); await field('draftQuality').waitFor(); await field('draftQuality').fill('85');
 const volume = await analyze(); await ui.locator('[data-action=selectAll]').click();
 await until(async () => (await ui.locator('#JpSelectedCount').textContent()).startsWith(String(volume.Proposed)),'all Keys selection');
 await ui.locator('#Review').click(); await ui.locator('#Apply').waitFor();
 const volumeKeys = (await api('JellyP/Plan/' + volume.PlanId + '/Keys?kind=selectable')).Keys;
 check(await ui.locator('[data-image-key]').count() === 10,'large exact apply review mounts only ten images');
 await ui.locator('[data-list=review][data-offset="10"]').click();
 await until(async () => await ui.locator('[data-image-key]').count() === volumeKeys.length - 10,'review page 2 loaded');
 check(await ui.locator('[data-image-key]').count() === volumeKeys.length - 10,'review pagination exposes remaining authorized identities');
 await apply(volumeKeys);
 await navigate('history'); await ui.locator('[data-backup]').first().waitFor();
 check(await ui.locator('[data-backup]').count() === 10,'history is paginated by image even one large operation');
 await ui.locator('[data-action=selectAllBackups]').click(); await ui.locator('#ReviewBackups').click(); await ui.locator('#JpDialog[open]').waitFor();
 check(await ui.locator('.jp-dialog-row').count() === 10,'large restoration review mounts ten exact originals');
 await ui.locator('[data-list=dialog][data-offset="10"]').click();
 check(await ui.locator('.jp-dialog-row').count() >= 4,'all remaining restoration identities are reviewable without truncation');
 await page.keyboard.press('Escape');
 check(!(await ui.locator('#JpDialog').isVisible()),'Escape cancels native maintenance review without effects');
 await ui.locator('[data-action=clearBackups]').click();
 const restoreFirst = await ui.locator('[data-backup]').first().getAttribute('data-backup'); await checked(ui.locator('[data-backup]').first(),true);
 await ui.locator('[data-list=backups][data-offset="10"]').click();
 await until(async () => (await ui.locator('#JpRangebackups').textContent()).startsWith('11–'),'backup page 2 loaded');
 const restoreSecond = await ui.locator('[data-backup]').first().getAttribute('data-backup'); await checked(ui.locator('[data-backup]').first(),true);
 await ui.locator('#ReviewBackups').click(); await ui.locator('#JpDialog[open]').waitFor();
 await page.screenshot({path:path.join(screenshots,'13-native-batch-restoration-review.png'),fullPage:true});
 const restoredSubset = await confirmBackup('Restore');
 check(restoredSubset.Items.length === 2 && restoredSubset.Items.every(row => [restoreFirst,restoreSecond].includes(row.RecordId) && row.Status === 'Restored'),'crosspage restoration applies only the two reviewed record IDs');
 await navigate('history'); await ui.locator('a[href*="sub=retire"]').click();
 await until(async () => await ui.locator('#JpTitle').textContent() === 'Remove originals · permanent','permanent removal subpage');
 check(await ui.locator('[data-backup]:checked').count() === 0,'permanent removal has its own empty selection and subpage');
 const retireRow = (await api('JellyP/Backups?filter=retirable&limit=200')).Items[0];
 assert.ok(retireRow && fixtureIds.has(retireRow.ItemId) && sourceHashes.has(retireRow.SourceHash),'Refuse retirement except exact generated PNG originals');
 // The prior history page may be at offset 10; choose the explicit matching page.
 if (!await ui.locator('[data-backup="' + retireRow.RecordId + '"]').count()) await ui.locator('[data-list=backups][data-offset="0"]').click();
 await checked(ui.locator('[data-backup="' + retireRow.RecordId + '"]'),true); await ui.locator('#ReviewBackups').click(); await ui.locator('#JpDialog[open]').waitFor();
 check(await ui.locator('#ConfirmBackup').isDisabled(),'permanent removal requires its own explicit acknowledgement');
 await page.screenshot({path:path.join(screenshots,'14-native-permanent-retirement-review.png'),fullPage:true});
 const retired = await confirmBackup('RetireSelected');
 check(retired.Items.length === 1 && retired.Items[0].RecordId === retireRow.RecordId && retired.Items[0].Status === 'Retired','only one generated original permanently retired; active WebP remains');
 await navigate('history'); await ui.locator('#ReviewBackups').waitFor();
 await ui.locator('[data-action=selectAllBackups]').click(); await ui.locator('#ReviewBackups').click(); await ui.locator('#JpDialog[open]').waitFor();
 await confirmBackup('Restore');
 check((await api('JellyP/Backups')).Total === 0,'all remaining generated originals restored; no active backups left');
 check(JSON.stringify((await api('JellyP/Settings')).Automation) === autoBefore,'manual analyze/apply/regenerate/restore/retire never saves automation');
 const nativeAfter = (await api('ScheduledTasks')).filter(t => t.Key.startsWith('JellyP')).map(t => ({Id:t.Id,Key:t.Key,Triggers:t.Triggers}));
 check(JSON.stringify(nativeAfter) === JSON.stringify(nativeBefore),'native task keys, IDs and trigger snapshots unchanged');
 await navigate('simulate'); await field('draftQuality').waitFor();
 await field('draftQuality').fill('101');
 let invalidRequests = 0;
 const invalidRequestListener = request => { if (request.url().endsWith('/JellyP/Plan/Analyze')) invalidRequests++; };
 page.on('request',invalidRequestListener); await ui.locator('#Analyze').click();
 check(await field('draftQuality').evaluate(node => node.validity.rangeOverflow) && invalidRequests === 0,'native input constraints block invalid quality before any API request');
 page.off('request',invalidRequestListener); await field('draftQuality').fill('85');
 // Deliberately send a bad credential to the REAL host, not an intercepted
 // response. Restore the client's getter immediately; never log its secret.
 await page.evaluate(() => { window.__jellypOriginalAccessToken = ApiClient.accessToken; ApiClient.accessToken = () => 'invalid-disposable-auth-test'; });
 try {
  const denied = page.waitForResponse(r => r.url().endsWith('/JellyP/Plan/Analyze') && r.status() === 401);
  await ui.locator('#Analyze').click(); await denied;
  await until(async () => (await ui.locator('#JpMessage').textContent()).includes('Administrator authentication'),'real UI authentication error');
  check((await ui.locator('#JpMessage').textContent()).includes('401'),'native UI presents a real 401 without claiming analysis success');
 } finally { await page.evaluate(() => { ApiClient.accessToken = window.__jellypOriginalAccessToken; delete window.__jellypOriginalAccessToken; }); }
 const previousPlanId = await page.evaluate(() => JSON.parse(sessionStorage.getItem('jellyp-native-v1')).planId);
 await ui.locator('#Analyze').click();
 const abort = ui.locator('[data-action=abortAnalyze]');
 if (await abort.isVisible()) {
  await abort.click();
  await until(async () => (await ui.locator('#JpMessage').textContent()).includes('cancelled'),'HTTP analysis abort');
  check(await page.evaluate(id => JSON.parse(sessionStorage.getItem('jellyp-native-v1')).planId === id,previousPlanId),'HTTP analysis cancellation preserves the prior report and never fabricates a new result');
 } else throw Error('Analysis finished before cancel control could be exercised; no cancellation claim');
 const stalePlan = await analyze();
 const staleKey = (await api('JellyP/Plan/' + stalePlan.PlanId + '/Keys?kind=selectable')).Keys[0];
 await checked(ui.locator('[data-plan-key="' + staleKey + '"]'),true); await ui.locator('#Review').click(); await ui.locator('#Apply').waitFor();
 await ui.locator('a[href*="sub=storage"]').click(); await ui.locator('#SaveSettings').waitFor(); await field('cfgQuality').fill('86');
 const savedNewDefaults = page.waitForResponse(r => r.url().endsWith('/JellyP/Settings/Defaults') && r.request().method() === 'PUT');
 await ui.locator('#SaveSettings').click(); assert.equal((await savedNewDefaults).status(),200);
 await until(async () => (await ui.locator('#JpMessage').textContent()).includes('saved'),'new defaults saved');
 await ui.locator('.jp-back a').click();
 await until(async () => (await ui.locator('#JpMain').textContent()).includes('An application requires'),'review invalidated');
 check(await ui.locator('#Apply').count() === 0,'saving defaults revokes review and requires new analysis; merely visiting did not');
 await navigate('automation'); await ui.locator('#SaveAutomation').waitFor();
 await field('autoScope').selectOption('libraries');
 await ui.locator('#SaveAutomation').click();
 await until(async () => (await ui.locator('#JpMessage').textContent()).includes('Select available libraries'),'empty automation scope validation');
 check(JSON.stringify((await api('JellyP/Settings')).Automation) === autoBefore,'empty automation libraries never silently widen or save');
 await checked(ui.locator('[data-library][data-owner=auto]').first(),true);
 const beforeAutoDefaults = JSON.stringify((await api('JellyP/Settings')).Defaults);
 async function saveAutomation() {
  const result = page.waitForResponse(r => r.url().endsWith('/JellyP/Settings/Automation') && r.request().method() === 'PUT');
  await ui.locator('#SaveAutomation').click(); assert.equal((await result).status(),200);
  await until(async () => (await ui.locator('#JpMessage').textContent()).includes('Automation saved'),'automation explicit save');
 }
 await saveAutomation();
 check((await api('JellyP/Settings')).Automation.ScopeMode === 'libraries','native automation explicitly saves selected libraries independently');
 await field('autoScope').selectOption('all'); await saveAutomation();
 const afterAuto = await api('JellyP/Settings');
 check(afterAuto.Automation.ScopeMode === 'all' && JSON.stringify(afterAuto.Defaults) === beforeAutoDefaults && afterAuto.Automation.DryRun,'native automation all-libraries choice is explicit; defaults unchanged and diagnostic mode saved');
 check(!(await api('JellyP/Tasks')).ActiveOperationId,'saving automation starts no native operation');
 // Leave the disposable host with the original explicit safe settings. This is
 // not production cleanup and does not touch task triggers or media files.
 await api('JellyP/Settings/Defaults','PUT',{Defaults:baseline.DefaultsSource === 'legacy' ? null : baseline.Defaults,Globals:baseline.Globals});
 await api('JellyP/Settings/Automation','PUT',{Automation:baseline.Automation,AuthorizePermanentRemoval:false});
 await navigate('automation'); await ui.locator('#SaveAutomation').waitFor();
}
main().catch(async error => {
 console.error(error.message);
 if (activePage) await activePage.screenshot({path:path.join(screenshots,'failure.png'),fullPage:true}).catch(() => {});
 fs.writeFileSync(path.join(screenshots,'native-summary.json'),JSON.stringify({checks,errors,outside,failedLocalRequests,failure:error.message},null,2));
 await activeBrowser?.close(); process.exitCode = 1;
});
