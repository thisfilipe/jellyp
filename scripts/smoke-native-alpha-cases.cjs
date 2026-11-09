/* Real-host alpha cases for a marked disposable Jellyfin 12.1 fixture.
 * Run after scripts/smoke-native-ui.cjs --journey and scripts/smoke-native-planning.cjs.
 * Covers: two real synthetic libraries in the automation scope arrays; a real
 * apply without retained originals; and a protected regeneration under non-zero
 * retention. No API interception or HTML replacement.
 *
 * Run: JELLYP_PLAYWRIGHT_MODULE=... node scripts/smoke-native-alpha-cases.cjs
 *      --root /tmp/opencode/jellyp-alpha-final --screenshots <dir>
 */
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const {execFileSync} = require('node:child_process');
const {chromium} = require(process.env.JELLYP_PLAYWRIGHT_MODULE || 'playwright');
const args = process.argv.slice(2);
const arg = key => args.includes(key) ? args[args.indexOf(key) + 1] : undefined;
assert.ok(arg('--root'), '--root required');
const root = fs.realpathSync(arg('--root'));
assert.ok(fs.existsSync(path.join(root, '.jellyp-smoke-root')), 'Unmarked disposable root');
const base = 'http://127.0.0.1:18096';
const auth = JSON.parse(fs.readFileSync(path.join(root, 'fixture-auth.json'), 'utf8'));
const headers = {Authorization: 'MediaBrowser Token="' + auth.token + '"'};
const shots = arg('--screenshots') || path.join(root, 'alpha-cases-evidence');
fs.mkdirSync(shots, {recursive: true});
const checks = [], errors = [], outside = [];
const check = (truth, label) => { assert.ok(truth, label); checks.push(label); console.log('PASS ' + label); };
const hash = data => crypto.createHash('sha256').update(data).digest('hex').toUpperCase();
const id32 = value => String(value).replaceAll('-', '').toLowerCase();
let browser;
async function api(endpoint, method = 'GET', body, extra = {}) {
 const response = await fetch(base + '/' + endpoint, {
  method,
  headers: {...headers, ...(body === undefined ? {} : {'Content-Type': 'application/json'}), ...extra},
  ...(body === undefined ? {} : {body: typeof body === 'string' ? body : JSON.stringify(body)}),
 });
 if (!response.ok) throw Error(endpoint.split('?')[0] + ': HTTP ' + response.status + ' ' + (await response.text()).slice(0, 240));
 const text = await response.text();
 return text ? JSON.parse(text) : null;
}
async function until(callback, label = 'state', timeout = 60000) {
 const deadline = Date.now() + timeout;
 while (Date.now() < deadline) {
  const value = await callback();
  if (value) return value;
  await new Promise(resolve => setTimeout(resolve, 100));
 }
 throw Error('Timed out: ' + label);
}
async function operation(receipt, timeout = 120000) {
 return until(async () => {
  const detail = await api('JellyP/Executions/' + receipt.OperationId);
  return ['Queued', 'Running'].includes(detail.Operation.State) ? null : detail;
 }, 'operation ' + receipt.OperationId, timeout);
}
function fixturePath(file) {
 const real = fs.realpathSync(file);
 assert.ok(real.startsWith(path.join(root, 'data') + path.sep), 'Refusing nonfixture path');
 return real;
}
function fileHash(file) { return hash(fs.readFileSync(fixturePath(file))); }
function normalizedIds(values) { return [...values].map(id32).sort(); }
function movie(name) {
 return api('Items?Recursive=true&IncludeItemTypes=Movie&Limit=500').then(result => result.Items.find(item => item.Name === name));
}
async function primary(itemId) {
 const images = await api('Items/' + itemId + '/Images');
 const image = images.find(candidate => candidate.ImageType === 'Primary');
 return image ? {path: fixturePath(image.Path), hash: fileHash(image.Path), bytes: fs.readFileSync(image.Path)} : null;
}
async function uploadPrimary(itemId, bytes) {
 await api('Items/' + itemId + '/Images/Primary', 'POST', bytes.toString('base64'), {'Content-Type': 'image/png'});
 return primary(itemId);
}
async function waitLibraryScan(previousStart, timeout = 180000) {
 await until(async () => {
  const tasks = await api('ScheduledTasks');
  const task = tasks.find(candidate => candidate.Key === 'RefreshLibrary');
  const start = task.LastExecutionResult?.StartTimeUtc;
  return task.State === 'Running' || (start && start !== previousStart) ? task : null;
 }, 'library scan start', 30000);
 return until(async () => {
  const tasks = await api('ScheduledTasks');
  const task = tasks.find(candidate => candidate.Key === 'RefreshLibrary');
  const current = task.LastExecutionResult?.StartTimeUtc;
  return task.State === 'Idle' && current && current !== previousStart ? task : null;
 }, 'library scan idle', timeout);
}
async function ensureSecondLibrary() {
 const name = 'JellyP second fixture';
 const media = path.join(root, 'second-media', 'JellyP Second (2026)');
 fs.mkdirSync(media, {recursive: true});
 const source = path.join(root, 'media', 'JellyP Canary (2026)', 'JellyP Canary (2026).mp4');
 const destination = path.join(media, 'JellyP Second (2026).mp4');
 if (!fs.existsSync(destination)) fs.copyFileSync(source, destination);
 let folders = await api('Library/VirtualFolders');
 let folder = folders.find(candidate => candidate.Name === name);
 if (!folder) {
  const options = {
   Enabled: true, EnablePhotos: false, EnableRealtimeMonitor: false, EnableInternetProviders: false,
   EnableLUFSScan: false, EnableChapterImageExtraction: false, ExtractChapterImagesDuringLibraryScan: false,
   EnableTrickplayImageExtraction: false, ExtractTrickplayImagesDuringLibraryScan: false,
   SaveLocalMetadata: false, AutomaticRefreshIntervalDays: 0, PreferredMetadataLanguage: 'en', MetadataCountryCode: 'US',
   TypeOptions: [{Type: 'Movie', MetadataFetchers: [], ImageFetchers: [], MetadataSavers: [], ImageOptions: []}],
   PathInfos: [],
  };
  const query = new URLSearchParams({name, collectionType: 'movies', paths: path.join(root, 'second-media'), refreshLibrary: 'false'});
  await api('Library/VirtualFolders?' + query, 'POST', {LibraryOptions: options});
  folder = (await api('Library/VirtualFolders')).find(candidate => candidate.Name === name);
  assert.ok(folder, 'second real library was created');
  const tasks = await api('ScheduledTasks');
  const previous = tasks.find(candidate => candidate.Key === 'RefreshLibrary').LastExecutionResult?.StartTimeUtc;
  await api('Library/Refresh', 'POST');
  await waitLibraryScan(previous);
 }
 const item = await until(() => movie('JellyP Second (2026)'), 'second library movie', 120000);
 if (!await primary(item.Id)) {
  const generated = fs.readFileSync(path.join(root, 'native-png', 'slot-0.png'));
  await uploadPrimary(item.Id, generated);
 }
 return {name, folder, item};
}
async function nativeReady(page) {
 await page.waitForFunction(() => {
  const root = document.querySelector('#JellyPConfigPage:not(.hide)');
  return root && root.querySelector('#JpMain')?.getAttribute('aria-busy') !== 'true'
   && [...root.querySelectorAll('[is="emby-button"],[is="emby-linkbutton"]')].every(node => node.classList.contains('emby-button'));
 });
 await page.waitForFunction(() => ![...document.querySelectorAll('.toastContainer .toast')].some(node => node.getClientRects().length && getComputedStyle(node).opacity !== '0'));
}
async function login(page) {
 await page.goto(base + '/web/', {waitUntil: 'domcontentloaded'});
 await page.locator('#txtManualName').fill('jellyp-test-admin');
 await page.locator('#txtManualPassword').fill(fs.readFileSync(path.join(root, 'test-password'), 'utf8'));
 await page.locator('button[type=submit]').click();
 await page.waitForURL(url => !url.hash.includes('/login'));
 await page.goto(base + '/web/#/configurationpage?name=jellyp');
 await page.locator('#JellyPConfigPage:not(.hide) [data-jp-id=draftQuality]').waitFor();
 await page.locator('[data-jp-id=Language]').selectOption('en');
 await nativeReady(page);
}
async function navigate(page, area) {
 const ui = page.locator('#JellyPConfigPage:not(.hide)');
 await page.waitForFunction(() => document.querySelectorAll('#JellyPConfigPage:not(.hide)').length === 1);
 await ui.locator('#JpNav nav a[href*="jp=' + area + '"]').click();
 await until(async () => (await ui.locator('#JpNav nav a[aria-current=page]').getAttribute('href')).includes('jp=' + area), 'host ' + area + ' view');
 await nativeReady(page);
}
async function checked(locator, value) {
 if (await locator.isChecked() !== value) await locator.locator('..').click();
 assert.equal(await locator.isChecked(), value);
}
async function expandFor(locator) {
 for (let attempt = 0; attempt < 3 && !await locator.isVisible(); attempt++) {
  const summary = locator.locator('xpath=ancestor::details[1]').locator('summary').first();
  await summary.evaluate(node => node.click());
  await locator.waitFor({state: 'visible', timeout: 5000}).catch(() => {});
 }
 assert.ok(await locator.isVisible(), 'form disclosure did not expose ' + await locator.getAttribute('data-jp-id'));
}
async function analyze(page) {
 const ui = page.locator('#JellyPConfigPage:not(.hide)');
 await page.waitForFunction(() => document.querySelectorAll('#JellyPConfigPage:not(.hide)').length === 1);
 const response = page.waitForResponse(r => r.url().endsWith('/JellyP/Plan/Analyze') && r.request().method() === 'POST');
 await ui.locator('#Analyze').click();
 const result = await response;
 assert.ok(result.ok(), 'Analyze HTTP');
 await ui.locator('#JpSelectedBill').waitFor();
 await nativeReady(page);
 return result.json();
}
async function apply(page) {
 const ui = page.locator('#JellyPConfigPage:not(.hide)');
 await page.waitForFunction(() => document.querySelectorAll('#JellyPConfigPage:not(.hide)').length === 1);
 await checked(ui.locator('[data-jp-id=ReviewAck]'), true);
 if (await ui.locator('[data-jp-id=PermanentAck]').count()) await checked(ui.locator('[data-jp-id=PermanentAck]'), true);
 if (await ui.locator('[data-jp-id=RetentionAck]').count()) await checked(ui.locator('[data-jp-id=RetentionAck]'), true);
 let attempts = 0;
 const count = request => { if (/\/JellyP\/Plan\/[^/]+\/Apply$/.test(request.url()) && request.method() === 'POST') attempts++; };
 page.on('request', count);
 const response = page.waitForResponse(r => /\/JellyP\/Plan\/[^/]+\/Apply$/.test(r.url()) && r.request().method() === 'POST');
 await ui.locator('#Apply').click();
 const result = await response;
 page.off('request', count);
 assert.equal(result.status(), 202);
 check(attempts === 1, 'one explicit apply request');
 const receipt = await result.json();
 const detail = await operation(receipt);
 return {receipt, detail};
}
async function main() {
 const info = await api('System/Info');
 check(info.Version === '12.1.0' && fs.realpathSync(info.ProgramDataPath) === path.join(root, 'data'), 'marked real 12.1 host and exact ProgramDataPath before mutation');
 const baseline = await api('JellyP/Settings');
 const status = await api('JellyP/Status');
 check(!status.ActiveOperationId && status.PendingRecovery === 0, 'fixture starts with no active operation or pending recovery');
 check((await api('JellyP/Backups?limit=1')).Total === 0, 'fixture starts with no retained backups');
 const canary = await movie('JellyP Canary (2026)');
 const regenerationMovie = await movie('JellyP Native 02 (2026)');
 assert.ok(canary && regenerationMovie, 'generated journey movies are present');

 browser = await chromium.launch({headless: true});
 const context = await browser.newContext({viewport: {width: 1440, height: 1000}, reducedMotion: 'reduce'});
 await context.route('**/*', route => {
  const url = new URL(route.request().url());
  if (url.origin === base || ['data:', 'blob:'].includes(url.protocol)) return route.continue();
  outside.push(url.origin);
  return route.abort();
 });
 const page = await context.newPage();
 page.on('pageerror', error => errors.push(error.message));
 await login(page);
 const ui = page.locator('#JellyPConfigPage:not(.hide)');
 const field = name => ui.locator('[data-jp-id="' + name + '"]');

 // 1) Two real synthetic libraries and scope arrays in the automation widening.
 const firstLibrary = (await api('Library/VirtualFolders')).find(folder => folder.Name === 'JellyP fixture');
 const second = await ensureSecondLibrary();
 assert.ok(firstLibrary, 'first real library is present');
 await navigate(page, 'automation');
 await ui.locator('#SaveAutomation').waitFor();
 await field('autoScope').selectOption('libraries');
 await nativeReady(page);
 const firstCheck = ui.locator('[data-library="' + firstLibrary.ItemId + '"][data-owner=auto]');
 const secondCheck = ui.locator('[data-library="' + second.folder.ItemId + '"][data-owner=auto]');
 check(await firstCheck.count() === 1 && await secondCheck.count() === 1, 'automation exposes both real synthetic libraries by exact IDs');
 await checked(firstCheck, true);
 await checked(secondCheck, false);
 const saveOne = page.waitForResponse(r => r.url().endsWith('/JellyP/Settings/Automation') && r.request().method() === 'PUT');
 await ui.locator('#SaveAutomation').click();
 assert.equal((await saveOne).status(), 200);
 await until(async () => (await ui.locator('#JpMessage').textContent()).includes('Automation saved'), 'one-library automation save');
 let saved = await api('JellyP/Settings');
 check(saved.Automation.ScopeMode === 'libraries' && JSON.stringify(normalizedIds(saved.Automation.LibraryIds)) === JSON.stringify([id32(firstLibrary.ItemId)]) && saved.Automation.ItemIds.length === 0, 'saved scope starts with exactly one real library and no item IDs');
 await checked(secondCheck, true);
 const saveTwo = page.waitForResponse(r => r.url().endsWith('/JellyP/Settings/Automation') && r.request().method() === 'PUT');
 await ui.locator('#SaveAutomation').click();
 assert.equal((await saveTwo).status(), 200);
 await until(async () => (await ui.locator('#JpMessage').textContent()).includes('Automation saved'), 'two-library automation save');
 saved = await api('JellyP/Settings');
 check(saved.Automation.ScopeMode === 'libraries' && JSON.stringify(normalizedIds(saved.Automation.LibraryIds)) === JSON.stringify(normalizedIds([firstLibrary.ItemId, second.folder.ItemId])) && saved.Automation.ItemIds.length === 0, 'widening saves the exact array of two real library IDs, never item IDs');
 check(!(await api('JellyP/Tasks')).ActiveOperationId, 'saving the widened automation starts no operation');
 const defaults = saved.Defaults;
 const widened = await api('JellyP/Plan/Analyze', 'POST', {
  Scope: 'libraries', ItemIds: [], LibraryIds: [firstLibrary.ItemId, second.folder.ItemId], PreserveOriginals: true,
  BatchSize: defaults.BatchSize,
  GeneralPolicy: {Quality: defaults.Quality, Lossless: defaults.Lossless, ResizeImages: defaults.ResizeImages, MaximumDimension: defaults.MaximumDimension, MinimumSsim: defaults.MinimumSsim},
  Policies: defaults.Policies,
 });
 const widenedImages = await api('JellyP/Plan/' + widened.PlanId + '/Images?limit=200');
 check(widened.TotalImages >= 2 && new Set(widenedImages.Items.map(row => id32(row.ItemId))).has(id32(canary.Id)) && new Set(widenedImages.Items.map(row => id32(row.ItemId))).has(id32(second.item.Id)), 'analysis accepts the two-library scope array and inspects items from both libraries');
 await api('JellyP/Settings/Automation', 'PUT', {Automation: baseline.Automation, AuthorizePermanentRemoval: false});

 // 2) Apply without retained originals; the source is removed only after verification.
 const canaryBefore = await primary(canary.Id);
 assert.ok(canaryBefore && canaryBefore.bytes.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])), 'canary starts with the exact synthetic PNG');
 await navigate(page, 'simulate');
 await field('draftScope').selectOption('items');
 await field('draftSearch').waitFor();
 await expandFor(field('draftIds'));
 await field('draftIds').fill(base + '/web/#/details?id=' + canary.Id);
 await ui.locator('[data-action=addIds]').click();
 await until(async () => (await ui.locator('[data-inspection]').textContent()).includes('Preliminary'), 'real canary inspection');
 await expandFor(field('draftBatchSize'));
 await field('draftBatchSize').fill('20');
 await field('draftQuality').fill('70');
 await field('draftPreserveOriginals').selectOption('false');
 const discardPlan = await analyze(page);
 const discardRows = await api('JellyP/Plan/' + discardPlan.PlanId + '/Images?limit=200');
 const discardRow = discardRows.Items.find(row => id32(row.ItemId) === id32(canary.Id) && row.Type === 'Primary');
 assert.ok(discardRow && discardRow.Action === 'Convert', 'no-original plan proposes the exact canary Primary');
 await checked(ui.locator('[data-plan-key="' + discardRow.Key + '"]'), true);
 await ui.locator('#Review').click();
 await ui.locator('#Apply').waitFor();
 check(await field('PermanentAck').count() === 1 && await ui.locator('#Apply').isDisabled(), 'permanent removal requires its own explicit acknowledgement');
 const discarded = await apply(page);
 check(discarded.detail.Operation.Changed === 1 && discarded.detail.Operation.Failed === 0 && discarded.detail.Operation.Billing.NewOriginalBytes === 0 && discarded.detail.Operation.Billing.BackupBytesAfter === 0, 'real apply without originals changes exactly one image and stores no backup');
 check(!fs.existsSync(canaryBefore.path), 'verified original PNG was removed instead of retained');
 check((await api('JellyP/Backups?filter=restorable&limit=200')).Items.every(record => id32(record.ItemId) !== id32(canary.Id)), 'no restorable backup was created for the discarded original');
 check((await api('JellyP/Status')).PendingRecovery === 0, 'permanent removal leaves no pending recovery');
 const restoredCanary = await uploadPrimary(canary.Id, canaryBefore.bytes);
 check(restoredCanary.hash === canaryBefore.hash, 'fixture canary PNG restored for the remaining checks');

 // 3) Protected regeneration under non-zero retention.
 const regenBefore = await primary(regenerationMovie.Id);
 assert.ok(regenBefore, 'regeneration target starts with a registered Primary');
 const initial = await api('JellyP/Settings');
 const defaults80 = structuredClone(initial.Defaults);
 defaults80.PreserveOriginals = true;
 defaults80.Quality = 80;
 defaults80.BatchSize = 20;
 defaults80.Policies = defaults80.Policies.map(policy => ({...policy, Enabled: policy.Category === 'Primary', OverrideGlobal: false, Quality: 80}));
 await api('JellyP/Settings/Defaults', 'PUT', {Defaults: defaults80, Globals: initial.Globals});
 const firstPlan = await api('JellyP/Plan/Analyze', 'POST', {
  Scope: 'items', ItemIds: [regenerationMovie.Id], LibraryIds: [], PreserveOriginals: true, BatchSize: 20,
  GeneralPolicy: {Quality: 80, Lossless: false, ResizeImages: false, MaximumDimension: 2400, MinimumSsim: 0.90},
  Policies: defaults80.Policies,
 });
 const firstKeys = (await api('JellyP/Plan/' + firstPlan.PlanId + '/Keys?kind=selectable')).Keys;
 assert.ok(firstKeys.length >= 1, 'initial conversion plan has a real key');
 const firstApply = await api('JellyP/Plan/' + firstPlan.PlanId + '/Apply', 'POST', {Keys: firstKeys, AuthorizePermanentRemoval: false});
 const firstDetail = await operation(firstApply);
 assert.equal(firstDetail.Operation.Changed, firstKeys.length);
 const backup = (await api('JellyP/Backups?filter=restorable&limit=200')).Items.find(record => id32(record.ItemId) === id32(regenerationMovie.Id) && record.Type === 'Primary');
 assert.ok(backup && backup.SourceHash === regenBefore.hash, 'initial conversion retained the exact original');
 const journalPath = path.join(root, 'data', 'plugins', 'Jellyfin.Plugin.JellyP', 'migration', 'journal', id32(backup.RecordId) + '.json');
 const journal = JSON.parse(fs.readFileSync(fixturePath(journalPath), 'utf8'));
 journal.CompletedUtc = new Date(Date.now() - 3 * 86400000).toISOString();
 fs.writeFileSync(journalPath, JSON.stringify(journal));
 const retention = {...initial.Globals, BackupRetentionDays: 1};
 await api('JellyP/Settings/Defaults', 'PUT', {Defaults: defaults80, Globals: retention});

 await navigate(page, 'simulate');
 await field('draftScope').selectOption('items');
 await field('draftSearch').waitFor();
 await expandFor(field('draftIds'));
 await field('draftIds').fill(base + '/web/#/details?id=' + regenerationMovie.Id);
 await ui.locator('[data-action=addIds]').click();
 await until(async () => (await ui.locator('[data-inspection]').textContent()).includes('Preliminary'), 'regeneration target inspection');
 await field('draftQuality').fill('70');
 await field('draftPreserveOriginals').selectOption('true');
 const regenPlan = await analyze(page);
 const regenRows = await api('JellyP/Plan/' + regenPlan.PlanId + '/Images?limit=200');
 const regenRow = regenRows.Items.find(row => id32(row.ItemId) === id32(regenerationMovie.Id) && row.Type === 'Primary');
 assert.ok(regenRow && regenRow.Action === 'Regenerate', 'changed quality proposes regeneration from the retained original');
 await checked(ui.locator('[data-plan-key="' + regenRow.Key + '"]'), true);
 await ui.locator('#Review').click();
 await ui.locator('#Apply').waitFor();
 const review = await api('JellyP/Plan/' + regenPlan.PlanId + '/Review', 'POST', {Keys: [regenRow.Key], Offset: 0, Limit: 10});
 check(review.Retention.ProtectedCount >= 1 && review.Retention.EligibleCount === 0, 'non-zero retention protects the exact regeneration original and expires no other original');
 check(await field('RetentionAck').count() === 0, 'protected regeneration needs no retention-purge consent');
 const regenerated = await apply(page);
 check(regenerated.detail.Operation.Changed === 1 && regenerated.detail.Operation.RetentionPurgedCount === 0, 'protected regeneration changes one image and purges no original');
 const afterBackup = (await api('JellyP/Backups?filter=restorable&limit=200')).Items.find(record => id32(record.ItemId) === id32(regenerationMovie.Id) && record.Type === 'Primary');
 check(afterBackup && afterBackup.SourceHash === regenBefore.hash, 'regeneration keeps an original with the exact source hash for restoration');
 const afterImage = await primary(regenerationMovie.Id);
 check(afterImage.hash !== regenBefore.hash, 'regeneration writes different-quality active bytes');
 const restoreReceipt = await api('JellyP/Backups/Restore', 'POST', {RecordIds: [afterBackup.RecordId]});
 const restoreDetail = await operation(restoreReceipt);
 check(restoreDetail.Operation.Changed === 1 && restoreDetail.Operation.Failed === 0, 'regeneration backup restores through the real API');
 check((await primary(regenerationMovie.Id)).hash === regenBefore.hash, 'restoration returns the exact original PNG');
 await api('JellyP/Settings/Defaults', 'PUT', {Defaults: initial.DefaultsSource === 'legacy' ? null : initial.Defaults, Globals: initial.Globals});
 await api('JellyP/Settings/Automation', 'PUT', {Automation: baseline.Automation, AuthorizePermanentRemoval: false});
 check(errors.length === 0, 'zero browser page errors');
 check(outside.length === 0 || outside.every(origin => origin === 'https://www.gstatic.com'), 'no non-host dependency beyond the official host Cast sender');
 fs.writeFileSync(path.join(shots, 'alpha-cases-summary.json'), JSON.stringify({checks, errors, outside}, null, 2));
 await browser.close();
 console.log(checks.length + ' real native alpha checks passed');
}
main().catch(async error => {
 console.error(error.stack || error.message);
 fs.writeFileSync(path.join(shots, 'alpha-cases-summary.json'), JSON.stringify({checks, errors, outside, failure: error.message}, null, 2));
 await browser?.close();
 process.exitCode = 1;
});
