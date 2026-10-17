/* Browser exercise ONLY for the marked disposable server used by smoke-local.py.
 * Run smoke-local.py first, then this script with --root <disposable-root>.
 * Requires Playwright (JELLYP_PLAYWRIGHT_MODULE may point to its installed module)
 * and a browser (JELLYP_BROWSER_CHANNEL optionally selects msedge/chrome).
 * Optional --screenshots <directory> stores synthetic desktop/mobile screenshots.
 * It saves settings and queues simulations. --apply-fixture also converts and
 * restores the single synthetic image, checking the restored source checksum.
 */
'use strict';
const { chromium, request } = require(process.env.JELLYP_PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const arguments_ = process.argv.slice(2);
const argument = name => arguments_.includes(name) ? arguments_[arguments_.indexOf(name)+1] : undefined;
if (!argument('--root')) throw new Error('--root is required');
const root = fs.realpathSync(argument('--root'));
if (!fs.existsSync(path.join(root, '.jellyp-smoke-root'))) throw new Error('Refusing unmarked disposable root');
const password = fs.readFileSync(path.join(root, 'test-password'), 'utf8');
const base = 'http://127.0.0.1:18096';
const plugin = '0928e334-8971-4205-a455-f42a76527f9e';
const identification = 'MediaBrowser Client="JellyP UI fixture", Device="disposable browser", DeviceId="jellyp-ui-fixture", Version="0.2"';
(async () => {
    const api = await request.newContext();
    let browser, original, headers;
    const errors = [];
    const checks = [];
    async function get(endpoint) {
        const result = await api.get(base+'/'+endpoint, { headers });
        assert.ok(result.ok(), 'API GET failed: '+endpoint);
        return result.json();
    }
    async function setConfig(configuration) {
        const result = await api.post(base+'/Plugins/'+plugin+'/Configuration', { headers, data: configuration });
        assert.ok(result.ok(), 'Saving disposable configuration failed');
    }
    async function waitUntil(callback) {
        for (let index=0; index<150; index++) {
            const result = await callback();
            if (result) return result;
            await new Promise(resolve => setTimeout(resolve, 100));
        }
        throw new Error('Timed out waiting for test state');
    }
    try {
        const response = await api.post(base+'/Users/AuthenticateByName', {
            headers: { Authorization: identification }, data: { Username: 'jellyp-test-admin', Pw: password }
        });
        assert.ok(response.ok(), 'Disposable authentication failed');
        const auth = await response.json();
        headers = { Authorization: identification+', Token="'+auth.AccessToken+'"' };
        assert.equal(path.resolve((await get('System/Info')).ProgramDataPath), path.join(root, 'data'), 'Server/root mismatch');
        original = await get('Plugins/'+plugin+'/Configuration');
        const fixtureItems = (await get('Items?Recursive=true&IncludeItemTypes=Movie')).Items;
        assert.equal(fixtureItems.length, 1, 'Expected exactly one generated movie fixture');
        assert.equal(fixtureItems[0].Name, 'JellyP Canary (2026)');
        const fixture = fixtureItems[0];
        const baseline = { ...original, Enabled:false, DryRun:true, MaintenanceWindowConfirmed:false,
            IncludedItemIds:[fixture.Id], BatchSize:1, Quality:85, Lossless:false, ResizeImages:false,
            PreserveOriginals:true, BackupRetentionDays:0,
            ImagePolicies:original.ImagePolicies.map(policy=>({ ...policy, OverrideGlobal:false,
                Enabled:['People','Primary','Thumb','Logo'].includes(policy.Category) })) };
        await setConfig(baseline);
        browser = await chromium.launch({ headless:true, ...(process.env.JELLYP_BROWSER_CHANNEL ? {channel:process.env.JELLYP_BROWSER_CHANNEL} : {}) });
        const page = await browser.newPage({ viewport:{width:1440,height:1000} });
        page.on('pageerror', error => errors.push(error.message));
        // Local development only: the release validation uses the installed resource.
        if (process.env.JELLYP_UI_SOURCE) await page.route(/\/configurationpage\?name=jellyp/i,
            route=>route.fulfill({contentType:'text/html',body:fs.readFileSync(process.env.JELLYP_UI_SOURCE,'utf8')}));
        await page.goto(base+'/web/', {waitUntil:'domcontentloaded'});
        await page.locator('#txtManualName').fill('jellyp-test-admin');
        await page.locator('#txtManualPassword').fill(password);
        await page.locator('button[type="submit"]').click();
        await page.waitForURL(url=>!url.hash.includes('/login'), {waitUntil:'domcontentloaded'});
        await page.goto(base+'/web/#/configurationpage?name=jellyp', {waitUntil:'domcontentloaded'});
        await page.waitForFunction(()=>document.querySelector('#Quality')?.value==='85');
        async function check(selector, checked) {
            const input = page.locator(selector);
            if (await input.isChecked() !== checked) await input.locator('..').click();
            assert.equal(await input.isChecked(), checked);
        }
        async function save() {
            await page.locator('#Save').click();
            await waitUntil(async()=>await page.locator('#DirtyState').textContent()==='Settings saved.');
        }
        await page.locator('#Language').selectOption('en');
        assert.equal(await page.locator('[data-policy-category]').count(), 11);
        assert.equal(await page.locator('#JellyPPolicies .checkboxOutline').count(), 44);
        assert.match(await page.locator('#Quality').getAttribute('class'), /emby-input/);
        checks.push('native Jellyfin inputs, selects and dynamic checkboxes');

        await page.locator('#Quality').fill('81');
        await page.locator('#AdditionalCategories > summary').click();
        await check('#PolicyEnabled4', true);
        await page.locator('[data-category="Backdrop"] > details > summary').click();
        await check('#PolicyOverride4', true);
        await page.locator('#PolicyQuality4').fill('60');
        await page.locator('#PolicyMinimumSsim4').fill('0.90');
        await page.locator('#Language').selectOption('pt');
        assert.equal(await page.locator('#Quality').inputValue(), '81');
        assert.equal(await page.locator('#PolicyQuality4').inputValue(), '60');
        assert.match(await page.locator('#Run').innerText(), /Simular/);
        await page.locator('#Language').selectOption('en');
        checks.push('language change preserves unsaved global and per-type edits');

        await page.locator('#Encoding').selectOption('lossless');
        assert.equal(await page.locator('#Quality').isDisabled(), true);
        await page.locator('#Encoding').selectOption('lossy');
        assert.equal(await page.locator('#Quality').isDisabled(), false);
        await check('#ResizeImages', true);
        await page.locator('#MaximumDimension').fill('1800');
        await check('#ResizeImages', false);
        assert.equal(await page.locator('#MaximumDimension').isDisabled(), true);
        checks.push('lossless and downscaling conditional controls');

        await page.locator('#AdvancedSettings > summary').click();
        await page.locator('#SavingsPercent').fill('7.5');
        await page.locator('#SavingsKiB').fill('8');
        await page.locator('#ReserveGiB').fill('2.25');
        await page.locator('#QuotaMiB').fill('640');
        await save();
        const saved = await get('Plugins/'+plugin+'/Configuration');
        assert.equal(saved.MinimumSavingsRatio, .075);
        assert.equal(saved.MinimumSavingsBytes, 8192);
        assert.equal(saved.MinimumFreeSpaceBytes, 2415919104);
        assert.equal(saved.MaximumBackupBytes, 671088640);
        assert.equal(saved.Quality, 81);
        assert.equal(saved.ImagePolicies.find(p=>p.Category==='Backdrop').Quality, 60);
        assert.equal(saved.MaximumDimension, 1800);
        await page.reload({waitUntil:'domcontentloaded'});
        await page.waitForFunction(()=>document.querySelector('#Quality')?.value==='81');
        assert.equal(await page.locator('#PolicyQuality4').inputValue(), '60');
        assert.equal(await page.locator('#ReserveGiB').inputValue(), '2.25');
        checks.push('global and per-type settings, byte units and reload persistence');

        await page.locator('#BatchSize').fill('0');
        await page.locator('#Save').click();
        await waitUntil(async()=> (await page.locator('#JellyPMessage').textContent()).includes('highlighted'));
        assert.equal((await get('Plugins/'+plugin+'/Configuration')).BatchSize, 1);
        await page.locator('#BatchSize').fill('1');
        await page.locator('#SelectedItems button').click();
        await page.locator('#Save').click();
        await waitUntil(async()=> (await page.locator('#JellyPMessage').textContent()).includes('at least one item'));
        assert.deepEqual((await get('Plugins/'+plugin+'/Configuration')).IncludedItemIds, saved.IncludedItemIds);
        await page.locator('#ItemSearch').fill('JellyP Canary');
        await page.locator('#SearchResults button').first().click();
        assert.match(await page.locator('#SelectedItems').innerText(), /JellyP Canary/);
        checks.push('numeric validation, empty restricted scope blocked, item-name search');

        await page.locator('#Mode').selectOption('apply');
        assert.equal(await page.locator('#Run').isDisabled(), true);
        await check('#Maintenance', true);
        assert.equal(await page.locator('#Run').isDisabled(), false);
        await page.locator('#Storage').selectOption('discard');
        assert.equal(await page.locator('#Run').isDisabled(), true);
        await check('#PermanentAck', true);
        assert.equal(await page.locator('#Run').isDisabled(), false);
        const previousApplication = (await get('JellyP/Status')).LastRun.FinishedUtc;
        page.once('dialog', dialog=>dialog.dismiss());
        await page.locator('#Run').click();
        await waitUntil(async()=>!(await page.locator('#Run').isDisabled()));
        assert.equal((await get('JellyP/Status')).LastRun.FinishedUtc, previousApplication);
        assert.equal((await get('Plugins/'+plugin+'/Configuration')).PreserveOriginals, true);
        await page.locator('#Mode').selectOption('preview');
        await page.locator('#Storage').selectOption('keep');
        checks.push('maintenance/permanence gates and canceling permanent conversion');

        const before = (await get('JellyP/Status')).LastRun.FinishedUtc;
        await page.locator('#Quality').fill('85');
        await page.locator('#Run').click();
        const status = await waitUntil(async()=>{
            const current = await get('JellyP/Status');
            return !current.LastRun.Running && current.LastRun.FinishedUtc!==before ? current : null;
        });
        assert.equal(status.LastRun.DryRun, true);
        assert.equal(status.LastRun.Converted, 0);
        assert.equal(status.LastRun.Failed, 0);
        assert.equal((await get('Plugins/'+plugin+'/Configuration')).Quality, 85);
        await waitUntil(async()=> (await page.locator('#PhaseText').innerText()).includes('Last batch: simulation'));
        checks.push('run saves displayed edits, queues simulation and automatically refreshes completion');

        if (arguments_.includes('--apply-fixture')) {
            await page.locator('#Mode').selectOption('apply');
            await check('#Maintenance', true);
            const previous = (await get('JellyP/Status')).LastRun.FinishedUtc;
            await page.locator('#Run').click();
            const applied = await waitUntil(async()=>{
                const current=await get('JellyP/Status');
                return !current.LastRun.Running&&current.LastRun.FinishedUtc!==previous?current:null;
            });
            assert.equal(applied.LastRun.DryRun, false);
            assert.equal(applied.LastRun.Converted, 1);
            assert.equal(applied.LastRun.Failed, 0);
            const record=applied.RecentRecords.find(r=>r.State==='Committed');
            assert.ok(record && !record.BackupDeleted);
            await waitUntil(async()=> (await page.locator('#PhaseText').innerText()).includes('Last batch: application'));
            await page.locator('#HistoryDetails > summary').click();
            page.once('dialog', dialog=>dialog.accept());
            await page.locator('[data-restore]').first().click();
            await waitUntil(async()=> (await get('JellyP/Status')).RecentRecords.some(r=>r.Id===record.Id&&r.State==='RolledBack'));
            assert.ok(path.resolve(record.Source.Path).startsWith(path.join(root,'data')+path.sep));
            const digest=require('node:crypto').createHash('sha256').update(fs.readFileSync(record.Source.Path)).digest('hex');
            assert.equal(digest.toLowerCase(), record.SourceHash.toLowerCase());
            await waitUntil(async()=>!(await page.locator('#Mode').isDisabled()));
            await page.locator('#Mode').selectOption('preview');
            await page.locator('#HistoryDetails > summary').click();
            checks.push('UI conversion and exact original restoration on the single generated image');
        }

        await page.locator('#QuickGuide > summary').click();
        await page.locator('#PrepareEmergency').click();
        assert.equal(await page.locator('#BatchSize').inputValue(), '25');
        assert.equal(await page.locator('#Storage').inputValue(), 'discard');
        assert.equal(await page.locator('#Mode').inputValue(), 'preview');
        assert.equal(await page.locator('#Quality').inputValue(), '85');
        assert.equal(await page.locator('#Scope').inputValue(), 'items');
        assert.equal((await get('Plugins/'+plugin+'/Configuration')).PreserveOriginals, true);
        checks.push('emergency shortcut preserves scope/quality and neither saves nor runs');

        await page.locator('#Language').selectOption('pt');
        await page.locator('h1').scrollIntoViewIfNeeded();
        await page.evaluate(()=>window.scrollTo(0,0));
        await page.waitForTimeout(400);
        const screenshots = argument('--screenshots');
        if (screenshots) fs.mkdirSync(screenshots, {recursive:true});
        if (screenshots) await page.screenshot({path:path.join(screenshots,'desktop.png'), fullPage:true});
        await page.setViewportSize({width:390,height:844});
        assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth), false);
        if (screenshots) await page.screenshot({path:path.join(screenshots,'mobile.png'), fullPage:true});
        checks.push('desktop/mobile layout and no horizontal overflow');
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({passed:checks, browserErrors:errors}, null, 2));
    } finally {
        if (original) await setConfig({...original, Enabled:false, DryRun:true, MaintenanceWindowConfirmed:false});
        if (browser) await browser.close();
        await api.dispose();
    }
})().catch(error=>{console.error(String(error.message).split(password).join('[redacted]'));process.exitCode=1;});
