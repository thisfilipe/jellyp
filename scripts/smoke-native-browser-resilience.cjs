/* Browser navigation/disconnect/focus resilience during a real JellyP operation.
 * Requires a marked disposable fixture prepared by scripts/smoke-host-gates.py
 * --phase setup (Jellyfin 12.1, candidate DLL, test slow-metadata plugin and the
 * host-gates-state.json target/barrier libraries).
 *
 * Playwright's context.setOffline() emulates a browser-only network loss; it does
 * not alter the host network, firewall or OS. The real server-side operation must
 * remain Running/WaitingForMetadata and keep the same operationId. Navigating,
 * browser Back, reload and reconnect must not cancel it or re-dispatch Apply.
 *
 * Run: JELLYP_PLAYWRIGHT_MODULE=... node scripts/smoke-native-browser-resilience.cjs
 *      --root <fixture> [--screenshots <dir>]
 */
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const {chromium} = require(process.env.JELLYP_PLAYWRIGHT_MODULE || 'playwright');
const args = process.argv.slice(2);
const arg = key => args.includes(key) ? args[args.indexOf(key) + 1] : undefined;
assert.ok(arg('--root'), '--root required');
const root = fs.realpathSync(arg('--root'));
assert.ok(fs.existsSync(path.join(root, '.jellyp-smoke-root')), 'Unmarked disposable root');
const base = 'http://127.0.0.1:18096';
const auth = JSON.parse(fs.readFileSync(path.join(root, 'fixture-auth.json'), 'utf8'));
const state = JSON.parse(fs.readFileSync(path.join(root, 'host-gates-state.json'), 'utf8'));
const headers = {Authorization: 'MediaBrowser Token="' + auth.token + '"'};
const shots = arg('--screenshots') || path.join(root, 'browser-resilience-evidence');
fs.mkdirSync(shots, {recursive: true});
const checks = [], errors = [], outside = [];
const check = (truth, label) => { assert.ok(truth, label); checks.push(label); console.log('PASS ' + label); };
const id32 = value => String(value).replaceAll('-', '').toLowerCase();
const normalized = value => String(value).replaceAll('-', '').toLowerCase();
let browser;
async function api(endpoint, method = 'GET', body) {
 const response = await fetch(base + '/' + endpoint, {
  method,
  headers: {...headers, ...(body === undefined ? {} : {'Content-Type': 'application/json'})},
  ...(body === undefined ? {} : {body: JSON.stringify(body)}),
 });
 if (!response.ok) throw Error(endpoint.split('?')[0] + ': HTTP ' + response.status + ' ' + (await response.text()).slice(0, 240));
 const text = await response.text();
 return text ? JSON.parse(text) : null;
}
async function until(callback, label = 'state', timeout = 120000) {
 const deadline = Date.now() + timeout;
 while (Date.now() < deadline) {
  const value = await callback();
  if (value) return value;
  await new Promise(resolve => setTimeout(resolve, 100));
 }
 throw Error('Timed out: ' + label);
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
 if (area === 'simulate') await ui.locator('[data-jp-id=draftScope]').waitFor({state: 'visible'});
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
function orderedPair() {
 const targets = [...state.items.target].sort((a, b) => normalized(a.Id).localeCompare(normalized(b.Id)));
 const barriers = [...state.items.barrier].sort((a, b) => normalized(a.Id).localeCompare(normalized(b.Id)));
 for (const target of targets) for (const barrier of barriers)
  if (normalized(target.Id) < normalized(barrier.Id)) return {target, barrier};
 throw Error('No target/barrier pair has the required order');
}
async function libraryStatus(name) {
 const folders = await api('Library/VirtualFolders');
 return folders.find(folder => folder.Name === name);
}
async function waitLibraryStatus(name, status, timeout = 60000) {
 return until(async () => {
  const folder = await libraryStatus(name);
  return folder?.RefreshStatus === status ? folder : null;
 }, name + ' ' + status, timeout);
}
function writePrivate(file, text) {
 const descriptor = fs.openSync(file, fs.constants.O_WRONLY | fs.constants.O_CREAT | fs.constants.O_TRUNC, 0o600);
 try { fs.writeFileSync(descriptor, text); fs.fchmodSync(descriptor, 0o600); } finally { fs.closeSync(descriptor); }
}
async function operationDetail(operationId) {
 return api('JellyP/Executions/' + operationId);
}
async function planStatus(planId) {
 return api('JellyP/Plan/' + planId);
}
async function operationsForPlan(planId) {
 const page = await api('JellyP/Executions?limit=100');
 return page.Items.filter(item => id32(item.PlanId || '') === id32(planId));
}
async function assertStable(page, operationId, planId, applyRequests, label) {
 const detail = await operationDetail(operationId);
 const op = detail.Operation;
 check(op.OperationId === operationId && op.State === 'Running' && op.Phase === 'WaitingForMetadata' && op.MutationStarted && op.Changed >= 1, label + ' keeps the same operationId Running in WaitingForMetadata after a real mutation');
 check((await planStatus(planId)).Status === 'Consumed', label + ' keeps the consumed plan consumed');
 const operations = await operationsForPlan(planId);
 check(operations.length === 1 && id32(operations[0].OperationId) === id32(operationId), label + ' has exactly one persisted operation for the plan, no double dispatch');
 check(applyRequests === 1, label + ' sent exactly one Apply request');
 const ui = page.locator('#JellyPConfigPage:not(.hide)');
 if (await ui.locator('#JpOperationTitle').count()) {
  check(await ui.locator('#JpOperationTitle').getAttribute('data-operation-id') === operationId, label + ' UI still identifies the same persisted operation');
  await page.waitForFunction(() => /Waiting for scans|Aguardando varredura/.test(document.querySelector('#JellyPConfigPage:not(.hide) #JpExecutionBody')?.innerText || ''), {timeout: 15000}).catch(() => {});
  const text = await ui.locator('#JpExecutionBody').innerText();
  check(/Waiting for scans|Aguardando varredura/.test(text), label + ' UI reports the real metadata wait');
  check(await ui.locator('#Apply').count() === 0 && await ui.locator('[data-plan-key]:checked').count() === 0, label + ' UI does not resurrect the consumed selection or an Apply control');
 }
 return detail;
}
async function main() {
 const info = await api('System/Info');
 check(info.Version === '12.1.0' && fs.realpathSync(info.ProgramDataPath) === path.join(root, 'data'), 'marked real 12.1 host and exact ProgramDataPath before mutation');
 const installed = path.join(root, 'data/plugins/JellyP/Jellyfin.Plugin.JellyP.dll');
 const installedHash = crypto.createHash('sha256').update(fs.readFileSync(installed)).digest('hex').toUpperCase();
 check(installedHash === state.installedDllSha256 && state.installedDllSha256 === state.candidateDllSha256, 'installed DLL matches the host-gates candidate identity');
 const pair = orderedPair();
 const barrierLibrary = state.libraries['JellyP barrier'];
 assert.ok(barrierLibrary, 'barrier library identity is present');
 writePrivate(path.join(root, 'test-provider-delay'), '180\n');
 try { fs.unlinkSync(path.join(root, 'test-provider-release')); } catch (error) { if (error.code !== 'ENOENT') throw error; }
 await api('Items/' + barrierLibrary + '/Refresh?metadataRefreshMode=FullRefresh', 'POST');
 await waitLibraryStatus('JellyP barrier', 'Active', 30000);

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

 await navigate(page, 'simulate');
 await field('draftQuality').waitFor();
 await field('draftScope').selectOption('items');
 await nativeReady(page);
 await field('draftSearch').waitFor({state: 'visible'});
 await expandFor(field('draftIds'));
 await field('draftIds').fill([pair.target, pair.barrier].map(item => base + '/web/#/details?id=' + item.Id).join(' '));
 await ui.locator('[data-action=addIds]').click();
 await until(async () => (await ui.locator('[data-inspection]').count()) === 2, 'real pair inspection');
 const analyzeResponse = page.waitForResponse(r => r.url().endsWith('/JellyP/Plan/Analyze') && r.request().method() === 'POST');
 await ui.locator('#Analyze').click();
 const analyzeResult = await analyzeResponse;
 assert.ok(analyzeResult.ok(), 'Analyze HTTP');
 const plan = await analyzeResult.json();
 await ui.locator('#JpSelectedBill').waitFor();
 const keys = (await api('JellyP/Plan/' + plan.PlanId + '/Keys?kind=selectable')).Keys;
 check(keys.length === 2, 'real analysis proposes both exact item transactions');
 await checked(field('SelectPage'), true);
 await ui.locator('#Review').click();
 await ui.locator('#Apply').waitFor();
 await checked(field('ReviewAck'), true);
 let applyRequests = 0;
 const countApply = request => { if (/\/JellyP\/Plan\/[^/]+\/Apply$/.test(request.url()) && request.method() === 'POST') applyRequests++; };
 page.on('request', countApply);
 const applyResponse = page.waitForResponse(r => /\/JellyP\/Plan\/[^/]+\/Apply$/.test(r.url()) && r.request().method() === 'POST');
 await ui.locator('#Apply').click();
 const applyResult = await applyResponse;
 assert.equal(applyResult.status(), 202);
 const receipt = await applyResult.json();
 const operationId = receipt.OperationId;
 await until(async () => {
  const detail = await operationDetail(operationId);
  const op = detail.Operation;
  return op.Changed >= 1 && op.Phase === 'WaitingForMetadata' && op.MutationStarted ? detail : null;
 }, 'real mutation then WaitingForMetadata', 60000);
 await ui.locator('#JpOperationTitle').waitFor();
 await nativeReady(page);
 check(await ui.locator('#JpOperationTitle').getAttribute('data-operation-id') === operationId, 'native apply navigates to the exact persisted operation receipt');
 await assertStable(page, operationId, plan.PlanId, applyRequests, 'initial wait');

 // Navigate to another real area, then browser Back to the receipt.
 await ui.locator('#JpNav nav a[href*="jp=history"]').click();
 await ui.locator('#ReviewBackups').waitFor();
 await assertStable(page, operationId, plan.PlanId, applyRequests, 'after navigation to history');
 await page.goBack();
 await ui.locator('#JpOperationTitle').waitFor();
 await nativeReady(page);
 await assertStable(page, operationId, plan.PlanId, applyRequests, 'after browser Back');

 // Reload the explicit receipt URL.
 await page.reload();
 await ui.locator('#JpOperationTitle').waitFor();
 await nativeReady(page);
 check(await page.evaluate(() => {
  try { return JSON.parse(sessionStorage.getItem('jellyp-native-v1')).selection.length; } catch (_) { return null; }
 }) === 0, 'reload restores the receipt with an empty consumed selection');
 await assertStable(page, operationId, plan.PlanId, applyRequests, 'after reload');

 // Browser-only network loss. The host and the server-side operation stay up.
 await context.setOffline(true);
 check(await page.evaluate(() => navigator.onLine === false), 'Playwright browser context reports offline without changing the host network');
 await page.reload({timeout: 10000}).catch(() => {});
 await assertStable(page, operationId, plan.PlanId, applyRequests, 'while browser is offline');
 await context.setOffline(false);
 await page.reload();
 await ui.locator('#JpOperationTitle').waitFor();
 await nativeReady(page);
 await assertStable(page, operationId, plan.PlanId, applyRequests, 'after reconnect');

 // Release the real metadata refresh and let the same operation finish.
 writePrivate(path.join(root, 'test-provider-release'), 'release\n');
 await waitLibraryStatus('JellyP barrier', 'Idle', 90000);
 const terminal = await until(async () => {
  const detail = await operationDetail(operationId);
  return ['Queued', 'Running'].includes(detail.Operation.State) ? null : detail;
 }, 'operation terminal after release', 120000);
 check(terminal.Operation.State === 'Completed' && terminal.Operation.Changed === 2 && terminal.Operation.Failed === 0, 'the same operation completes both transactions after reconnect');
 check((await operationsForPlan(plan.PlanId)).length === 1 && applyRequests === 1, 'completion still has one operation and one Apply request');
 const records = await api('JellyP/Backups?filter=restorable&limit=200');
 const own = records.Items.filter(record => [pair.target.Id, pair.barrier.Id].some(id => id32(id) === id32(record.ItemId)));
 check(own.length === 2, 'both real conversions retained their exact originals for cleanup');
 const restore = await api('JellyP/Backups/Restore', 'POST', {RecordIds: own.map(record => record.RecordId)});
 const restoreDetail = await until(async () => {
  const detail = await operationDetail(restore.OperationId);
  return ['Queued', 'Running'].includes(detail.Operation.State) ? null : detail;
 }, 'cleanup restoration', 120000);
 check(restoreDetail.Operation.Changed === 2 && restoreDetail.Operation.Failed === 0, 'cleanup restores both synthetic originals through the real API');
 for (const name of ['test-provider-delay', 'test-provider-release']) {
  try { fs.unlinkSync(path.join(root, name)); } catch (error) { if (error.code !== 'ENOENT') throw error; }
 }
 check(errors.length === 0, 'zero browser page errors');
 check(outside.every(origin => origin === 'https://www.gstatic.com'), 'all non-host attempts are the official host Cast sender only');
 fs.writeFileSync(path.join(shots, 'browser-resilience-summary.json'), JSON.stringify({checks, errors, outside, operationId, planId: plan.PlanId, applyRequests}, null, 2));
 await browser.close();
 console.log(checks.length + ' real browser resilience checks passed');
}
main().catch(async error => {
 console.error(error.stack || error.message);
 fs.writeFileSync(path.join(shots, 'browser-resilience-summary.json'), JSON.stringify({checks, errors, outside, failure: error.message}, null, 2));
 await browser?.close();
 process.exitCode = 1;
});
