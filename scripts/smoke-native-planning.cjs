/* Real backend/retention UI regressions. Marked LOCAL disposable Jellyfin only.
 * Requires smoke-native-ui.cjs --journey first (generated PNGs and canary).
 * --root <fixture> [--screenshots <directory>]
 * Uploads 52 generated Backdrops, ages ONLY their generated journal timestamps,
 * and expires ONLY those generated originals through the real API. No database edits.
 */
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.JELLYP_PLAYWRIGHT_MODULE || 'playwright');
const args = process.argv.slice(2);
const arg = key => args.includes(key) ? args[args.indexOf(key) + 1] : undefined;
assert.ok(arg('--root'), '--root required');
const root = fs.realpathSync(arg('--root'));
assert.ok(fs.existsSync(path.join(root, '.jellyp-smoke-root')), 'Unmarked fixture');
const base = 'http://127.0.0.1:18096';
const auth = JSON.parse(fs.readFileSync(path.join(root, 'fixture-auth.json'), 'utf8'));
const headers = { Authorization: 'MediaBrowser Token="' + auth.token + '"' };
const shots = arg('--screenshots') || path.join(root, 'planning-evidence');
const checks = [];
let baseline, canary, browser;
const clean = id => String(id).replaceAll('-', '').toLowerCase();
const check = (value, label) => { assert.ok(value, label); checks.push(label); console.log('PASS ' + label); };
async function api(endpoint, method = 'GET', body, extra = {}) {
 const response = await fetch(base + '/' + endpoint, { method, headers: { ...headers, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...extra }, ...(body === undefined ? {} : { body: typeof body === 'string' ? body : JSON.stringify(body) }) });
 if (!response.ok) throw Error(endpoint.split('?')[0] + ': HTTP ' + response.status + ' ' + (await response.text()).slice(0, 180));
 const text = await response.text(); return text ? JSON.parse(text) : null;
}
async function operation(receipt) {
 const deadline = Date.now() + 120000;
 while (Date.now() < deadline) {
  const detail = await api('JellyP/Executions/' + receipt.OperationId + '?limit=200');
  if (!['Queued', 'Running'].includes(detail.Operation.State)) return detail;
  await new Promise(resolve => setTimeout(resolve, 100)); // Conditional job readiness, not a startup delay.
 }
 throw Error('Timed out waiting for persisted fixture operation');
}
function hashFile(file) {
 const real = fs.realpathSync(file);
 assert.ok(real.startsWith(path.join(root, 'data') + path.sep), 'Nonfixture artwork path');
 return crypto.createHash('sha256').update(fs.readFileSync(real)).digest('hex').toUpperCase();
}
async function images() { return api('Items/' + canary.Id + '/Images'); }
function imageIndex(image) { return image.ImageIndex; }
async function analyze(category) {
 const settings = await api('JellyP/Settings');
 return api('JellyP/Plan/Analyze', 'POST', {
  Scope: 'items', ItemIds: [canary.Id], LibraryIds: [], PreserveOriginals: true, BatchSize: 100,
  GeneralPolicy: { Quality: 85, Lossless: false, ResizeImages: false, MaximumDimension: 2400, MinimumSsim: 0.96 },
  Policies: settings.Defaults.Policies.map(policy => ({ ...policy, Enabled: policy.Category === category, OverrideGlobal: false }))
 });
}
async function upload(index) {
 const data = fs.readFileSync(path.join(root, 'native-png', 'slot-' + (index % 14) + '.png'));
 await api('Items/' + canary.Id + '/Images/Backdrop', 'POST', data.toString('base64'), { 'Content-Type': 'image/png' });
}
async function cleanup() {
 if (!canary) return;
 const active = await api('JellyP/Backups?filter=restorable&limit=200');
 const own = active.Items.filter(record => clean(record.ItemId) === clean(canary.Id));
 if (own.length) await operation(await api('JellyP/Backups/Restore', 'POST', { RecordIds: own.map(record => record.RecordId) }));
 for (const image of (await images()).filter(image => image.ImageType === 'Backdrop').sort((a, b) => imageIndex(b) - imageIndex(a)))
  await api('Items/' + canary.Id + '/Images/Backdrop/' + imageIndex(image), 'DELETE');
}
async function main() {
 const info = await api('System/Info');
 check(info.Version === '12.1.0' && fs.realpathSync(info.ProgramDataPath) === path.join(root, 'data'), 'marked local real 12.1 host before mutation');
 baseline = await api('JellyP/Settings');
 canary = (await api('Items?Recursive=true&IncludeItemTypes=Movie')).Items.find(item => item.Name === 'JellyP Canary (2026)');
 assert.ok(canary, 'Generated canary missing');
 assert.equal((await api('JellyP/Backups?limit=1')).Total, 0, 'Refuse unfinished backup fixture');
 assert.equal((await images()).filter(image => image.ImageType === 'Backdrop').length, 0, 'Refuse existing Backdrops');
 fs.mkdirSync(shots, { recursive: true });
 const defaults = structuredClone(baseline.Defaults);
 Object.assign(defaults, { BatchSize: 100, Quality: 85, PreserveOriginals: true, MinimumSsim: 0.96 });
 const globals = { ...baseline.Globals, BackupRetentionDays: 0 };
 await api('JellyP/Settings/Defaults', 'PUT', { Defaults: defaults, Globals: globals });
 try {
  for (let index = 0; index < 6; index++) await upload(index);
  const nativeBefore = (await images()).filter(image => image.ImageType === 'Backdrop');
  const before = new Map(nativeBefore.map(image => [imageIndex(image), hashFile(image.Path)]));
  let plan = await analyze('Backdrop');
  let rows = (await api('JellyP/Plan/' + plan.PlanId + '/Images?limit=100')).Items.filter(row => row.Category === 'Backdrop');
  check(rows.length === 6 && rows.every(row => row.SourceHash === before.get(row.Index)), 'all six plan indexes agree with real native Backdrop slots');
  const selected = rows.find(row => row.Index === 1);
  assert.ok(selected && selected.Action === 'Convert');
  const applied = await operation(await api('JellyP/Plan/' + plan.PlanId + '/Apply', 'POST', { Keys: [selected.Key] }));
  check(applied.Operation.Changed === 1 && applied.Operation.Invalidated === 0 && applied.Operation.Failed === 0, 'apply native Backdrop slot 1 changes exactly one image without SourceChanged');
  const after = (await images()).filter(image => image.ImageType === 'Backdrop');
  check(after.every(image => imageIndex(image) === 1 ? hashFile(image.Path) !== before.get(1) : hashFile(image.Path) === before.get(imageIndex(image))), 'unselected native Backdrop slots retain their exact hashes');
  const record = (await api('JellyP/Backups?filter=restorable&limit=100')).Items.find(record => record.Index === 1 && clean(record.ItemId) === clean(canary.Id));
  assert.ok(record);
  const restored = await operation(await api('JellyP/Backups/Restore', 'POST', { RecordIds: [record.RecordId] }));
  check(restored.Operation.Changed === 1 && (await images()).filter(image => image.ImageType === 'Backdrop').every(image => hashFile(image.Path) === before.get(imageIndex(image))), 'native Backdrop restoration returns all slots to original bytes');

  for (let index = 6; index < 52; index++) await upload(index);
  plan = await analyze('Backdrop');
  rows = (await api('JellyP/Plan/' + plan.PlanId + '/Images?limit=100')).Items.filter(row => row.Category === 'Backdrop');
  check(rows.length === 52 && rows.every(row => row.Action === 'Convert'), '52 real native Backdrops form one complete convertible plan');
  const batch = await operation(await api('JellyP/Plan/' + plan.PlanId + '/Apply', 'POST', { Keys: rows.map(row => row.Key) }));
  check(batch.Operation.Changed === 52 && batch.Operation.Invalidated === 0 && batch.Operation.Failed === 0, 'all 52 native Backdrops convert through the installed engine');
  const backups = (await api('JellyP/Backups?filter=restorable&limit=200')).Items;
  assert.equal(backups.length, 52);
  const ids = new Set(backups.map(record => clean(record.RecordId)));
  const journals = path.join(root, 'data', 'plugins', 'Jellyfin.Plugin.JellyP', 'migration', 'journal');
  let aged = 0;
  for (const file of fs.readdirSync(journals)) {
   if (!file.endsWith('.json') || !ids.has(clean(file.slice(0, -5)))) continue;
   const filename = path.join(journals, file), journal = JSON.parse(fs.readFileSync(filename, 'utf8'));
   assert.equal(clean(journal.Source.ItemId), clean(canary.Id));
   // Only age these generated records, retaining all source/output evidence unchanged.
   journal.CompletedUtc = new Date(Date.now() - 3 * 86400000).toISOString();
   fs.writeFileSync(filename, JSON.stringify(journal)); aged++;
  }
  check(aged === 52, 'only 52 generated journals aged to exercise real retention');
  defaults.Policies = defaults.Policies.map(policy => ({ ...policy, Enabled: policy.Category === 'Primary', OverrideGlobal: false }));
  await api('JellyP/Settings/Defaults', 'PUT', { Defaults: defaults, Globals: { ...globals, BackupRetentionDays: 1 } });
  plan = await analyze('Primary');
  rows = (await api('JellyP/Plan/' + plan.PlanId + '/Images?limit=100')).Items.filter(row => row.Category === 'Primary');
  assert.equal(rows.length, 1);
  const keys = [rows[0].Key], seen = new Set();
  let fingerprint;
  for (let offset = 0; offset < 52; offset += 10) {
   const review = await api('JellyP/Plan/' + plan.PlanId + '/Review', 'POST', { Keys: keys, RetentionOffset: offset, RetentionLimit: 10 });
   assert.equal(review.Retention.Total, 52);
   fingerprint ||= review.Retention.Fingerprint;
   assert.equal(review.Retention.Fingerprint, fingerprint);
   for (const item of review.Retention.Items) { assert.ok(!seen.has(item.RecordId)); seen.add(item.RecordId); }
  }
  check(seen.size === 52 && [...seen].every(id => ids.has(clean(id))), 'retention pages expose all 52 exact IDs with one stable fingerprint');

  browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  await page.route('**/*', route => new URL(route.request().url()).origin === base ? route.continue() : route.abort());
  await page.goto(base + '/web/');
  await page.locator('#txtManualName').fill('jellyp-test-admin');
  await page.locator('#txtManualPassword').fill(fs.readFileSync(path.join(root, 'test-password'), 'utf8'));
  await page.locator('button[type=submit]').click(); await page.waitForURL(url => !url.hash.includes('/login'));
  await page.goto(base + '/web/#/configurationpage?name=jellyp');
  const ui = page.locator('#JellyPConfigPage:not(.hide)');
  const field = name => ui.locator('[data-jp-id="' + name + '"]');
  await field('draftScope').waitFor(); await field('Language').selectOption('en');
  await field('draftScope').selectOption('items');
  await field('draftSearch').fill('JellyP Canary'); await field('draftSearch').dispatchEvent('change');
  await ui.locator('[data-action=addItem]').filter({ hasText: 'JellyP Canary' }).first().click();
  await ui.locator('#Analyze').click(); await ui.locator('[data-plan-key]').first().waitFor();
  await ui.locator('[data-plan-key]').first().locator('..').click();
  await ui.locator('#Review').click(); await field('RetentionAck').waitFor();
  check((await ui.locator('#JpRangeretention').innerText()).includes('1–10 of 52'), 'installed native UI renders the first retention page');
  await ui.locator('[data-list=retention][data-offset="10"]').click();
  await page.waitForFunction(() => document.querySelector('#JellyPConfigPage:not(.hide) #JpRangeretention')?.textContent.includes('11–20 of 52'));
  for (let offset = 20; offset <= 50; offset += 10) {
   await ui.locator('[data-list=retention][data-offset="' + offset + '"]').click();
   await page.waitForFunction(expected => document.querySelector('#JellyPConfigPage:not(.hide) #JpRangeretention')?.textContent.includes(expected), (offset + 1) + '–' + Math.min(offset + 10, 52) + ' of 52');
  }
   check((await ui.locator('#JpRangeretention').innerText()).includes('51–52 of 52'), 'installed native UI can inspect retention identities beyond the old 50-row sample');
   await page.mouse.move(0, 0);
   for (const [device, viewport] of [['desktop', { width: 1440, height: 1000 }], ['mobile', { width: 390, height: 844 }]]) {
    await page.setViewportSize(viewport);
    await page.waitForFunction(() => [...document.querySelectorAll('#JellyPConfigPage:not(.hide) .jp-pager button')].every(node => node.classList.contains('emby-button')));
    check(await ui.locator('#JpRangeretention').locator('..').evaluate(nav => [...nav.querySelectorAll('button')].every(node => { const style = getComputedStyle(node); return node.getAttribute('is') === 'emby-button' && !node.classList.contains('raised') && style.backgroundColor === 'rgba(0, 0, 0, 0)' && style.boxShadow === 'none' && style.filter === 'none' && node.getBoundingClientRect().height >= 44 && node.querySelector('.material-icons[aria-hidden=true]'); }) && nav.querySelector('[data-offset="60"]').disabled), device + ' real retention pager shares compact native icon/label controls and explicit final-page disabled state');
   }
   await page.setViewportSize({ width: 1440, height: 1000 });
   await page.screenshot({ path: path.join(shots, 'native-retention-page6.png'), fullPage: true });
  await field('ReviewAck').locator('..').click(); await field('RetentionAck').locator('..').click();
  check(await ui.locator('#Apply').isEnabled(), 'paged retention no longer blocks explicitly authorized native application');
  const sent = page.waitForResponse(response => response.url().includes('/Apply') && response.request().method() === 'POST');
  await ui.locator('#Apply').click(); const response = await sent;
  assert.ok(response.ok());
  check(!!response.request().postDataJSON().AuthorizeRetentionFingerprint, 'native UI sends the reviewed retention fingerprint to the real API');
  const finished = await operation(await response.json());
  check(finished.Operation.Changed === 1 && finished.Operation.RetentionPurgedCount === 52 && finished.Operation.Failed === 0, 'native application expires exactly the 52 reviewed originals and converts only the selected Primary');
  const originals = await images();
  check(originals.filter(image => image.ImageType === 'Backdrop').length === 52 && originals.filter(image => image.ImageType === 'Backdrop').every(image => path.extname(image.Path).toLowerCase() === '.webp'), 'retention leaves all active Backdrop WebP images in place');
  check(JSON.stringify((await api('JellyP/Settings')).Automation) === JSON.stringify(baseline.Automation), 'edge cases do not change saved automation');
  fs.writeFileSync(path.join(shots, 'planning-summary.json'), JSON.stringify({ checks }, null, 2));
 } finally {
  if (browser) await browser.close();
  await cleanup();
  await api('JellyP/Settings/Defaults', 'PUT', { Defaults: baseline.DefaultsSource === 'legacy' ? null : baseline.Defaults, Globals: baseline.Globals });
 }
 console.log(checks.length + ' real native planning checks passed');
}
main().catch(error => { console.error(error.stack); process.exitCode = 1; });
