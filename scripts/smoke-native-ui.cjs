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
  const root = [...document.querySelectorAll('#JellyPConfigPage:not(.hide)')].at(-1);
   return root && root.querySelector('#JpMain')?.getAttribute('aria-busy') !== 'true' && [...root.querySelectorAll('[is="emby-button"],[is="emby-linkbutton"]')].every(node => node.classList.contains('emby-button')) && [...root.querySelectorAll('.jp-secondary,.jp-link')].every(node => getComputedStyle(node).boxShadow !== '');
 });
  await page.locator('#JellyPConfigPage:not(.hide)').last().evaluate(async root => {
  const animations = root.getAnimations({subtree:true}).filter(animation => Number.isFinite(animation.effect.getTiming().iterations));
  await Promise.all(animations.map(animation => animation.finished.catch(() => {})));
  });
  // Actual 12.1 host save notifications are .toast, not .toastVisible. Wait for
  // their real disappearance instead of capturing a host toast over our CTA.
  await page.waitForFunction(() => ![...document.querySelectorAll('.toastContainer .toast')].some(node => node.getClientRects().length && getComputedStyle(node).opacity !== '0'));
  await page.waitForFunction(() => { const image = [...document.querySelectorAll('#JellyPConfigPage:not(.hide)')].at(-1)?.querySelector('#JpLogo'); return image?.complete && image.naturalWidth === 480; });
}
async function branding(page,theme,label) {
 const ui = page.locator('#JellyPConfigPage:not(.hide)').last();
 await nativeReady(page);
 check(await ui.locator('#JpLogo').evaluate((image,theme) => {
  const rect = image.getBoundingClientRect(), brand = image.parentElement.getBoundingClientRect(), version = image.parentElement.querySelector('#JpVersion'), url = new URL(image.currentSrc);
  return image.alt === 'JellyP' && image.naturalWidth === 480 && image.naturalHeight === 159 && url.origin === location.origin && url.searchParams.get('name') === 'jellyp-wordmark-' + theme + '.png' && rect.height <= 46 && Math.abs(rect.width / rect.height - 480 / 159) < .02 && version.textContent.startsWith('v') && parseFloat(getComputedStyle(version).fontSize) < rect.height / 2 && version.getBoundingClientRect().right <= brand.right + 1 && ![...image.parentElement.childNodes].some(node => node.nodeType === Node.TEXT_NODE && node.textContent.includes('JellyP'));
 },theme),label + ' local theme-correct PNG loads at native dimensions, compact/undistorted; one JellyP alternative and smaller dynamic version fit');
}
async function setHostTheme(page,theme,target) {
 await page.goto(base + '/web/#/mypreferencesdisplay?userId=' + auth.userId);
 await page.locator('#mui-component-select-dashboardTheme').click();
 await page.locator('[role=option][data-value="' + theme + '"]').click();
 await page.locator('button[type=submit]:visible').click();
 await page.waitForLoadState('networkidle');
 await page.goto(target);
 await page.locator('#JellyPConfigPage:not(.hide) #JpTitle').waitFor();
 await page.waitForFunction(theme => document.documentElement.getAttribute('data-theme') === theme,theme);
 await page.waitForFunction(theme => [...document.querySelectorAll('#JellyPConfigPage:not(.hide)')].at(-1)?.dataset.jpDark === String(theme === 'dark'),theme);
 await nativeReady(page);
 await page.waitForFunction(() => !document.querySelector('.toastVisible'));
}
async function auditFidelity(page,label) {
 if (!process.env.JELLYP_AXE_MODULE) return;
 const AxeBuilder = require(process.env.JELLYP_AXE_MODULE).default;
 const result = await new AxeBuilder({page}).include('#JellyPConfigPage:not(.hide)').withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
 fs.writeFileSync(path.join(screenshots,'axe-fidelity-' + label + '.json'),JSON.stringify(result.violations,null,2));
 check(result.violations.length === 0,'axe fidelity ' + label + ' WCAG A/AA');
}
async function captureMatrix(page,label,inspect) {
 const target = page.url(), ui = page.locator('#JellyPConfigPage:not(.hide)').last();
 for (const theme of ['dark','light']) {
  await setHostTheme(page,theme,target);
  for (const language of ['en','pt']) {
   await ui.locator('[data-jp-id=Language]').selectOption(language); await nativeReady(page);
   for (const [device,viewport] of [['desktop',{width:1600,height:1100}],['mobile',{width:390,height:844}]]) {
    await page.setViewportSize(viewport); await nativeReady(page);
    await branding(page,theme,label + ' ' + language + ' ' + theme + ' ' + device);
    await inspect(theme,language,device);
    if (await ui.locator('aside [data-consent]').count()) check(await ui.locator('aside [data-consent]').evaluateAll(inputs => inputs.every(input => { const label=input.closest('label'),text=label.querySelector('.checkboxLabel'),a=label.getBoundingClientRect(),b=text.getBoundingClientRect();return a.height >= 44 && b.bottom <= a.bottom + 1 && b.top >= a.top - 1; })),label + ' ' + theme + ' ' + language + ' ' + device + ' wrapped native consent labels fit their own 44px-minimum boxes');
    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),label + ' ' + theme + ' ' + language + ' ' + device + ' no overflow');
    await page.evaluate(() => scrollTo(0,0));
    await page.screenshot({path:path.join(screenshots,'fidelity-' + label + '-' + language + '-' + theme + '-' + device + '.png'),fullPage:true});
     await page.screenshot({path:path.join(screenshots,'fidelity-' + label + '-' + language + '-' + theme + '-' + device + '-viewport.png')});
     const detail=ui.locator(label==='result'?'.jp-list-tools':'.jp-aside > .emby-button').last();
     if(await detail.count()) {
      await detail.scrollIntoViewIfNeeded();await page.screenshot({path:path.join(screenshots,'detail-'+label+'-'+language+'-'+theme+'-'+device+'-viewport.png')});
     }
   }
  }
  await auditFidelity(page,label + '-' + theme);
 }
 await page.setViewportSize({width:1600,height:1100});
 await setHostTheme(page,'dark',target);
 await ui.locator('[data-jp-id=Language]').selectOption('en'); await nativeReady(page);
}
async function redButton(locator,label) {
 const style = await locator.evaluate(node => { const s = getComputedStyle(node), rgb = value => value.match(/[\d.]+/g).slice(0,3).map(Number); return {bg:rgb(s.backgroundColor),fg:rgb(s.color),height:node.getBoundingClientRect().height}; });
 function luminance(rgb) { return rgb.map(v => v / 255).map(v => v <= .04045 ? v / 12.92 : ((v + .055) / 1.055) ** 2.4).reduce((n,v,i) => n + v * [.2126,.7152,.0722][i],0); }
 const contrast = (luminance(style.fg) + .05) / (luminance(style.bg) + .05);
 check(style.bg[0] > style.bg[1] * 2 && style.bg[0] > style.bg[2] * 2 && style.fg.every(v => v === 255) && contrast >= 4.5 && style.height >= 44,label + ' solid red, white contrast ≥4.5 and 44px touch target');
}
async function navigationCraft(page,label) {
 const ui = page.locator('#JellyPConfigPage:not(.hide)').last(); await nativeReady(page);
 await page.mouse.move(0,0);
 check(await ui.locator('.jp-secondary,.jp-link').evaluateAll(nodes => nodes.every(node => node.classList.contains('emby-button') && !node.classList.contains('raised') && ['none',''].includes(getComputedStyle(node).boxShadow))),label + ' secondary actions/navigation use enhanced native controls without raised hierarchy');
  check(await ui.locator('#JpNav nav a .material-icons[aria-hidden=true]').count() === 5 && await ui.locator('.jp-nav-separator').count() === 1 && await ui.locator('#JpNav nav a[aria-current=page]').count() === 1,label + ' five labelled destinations, real decorative Material icons, manual/management separator and one current page');
  check(await ui.locator('.jp-navigation').evaluate(node => {const neutral=getComputedStyle(node).color;return [...node.querySelectorAll('nav a:not([aria-current=page]),.jp-activity a')].every(link=>getComputedStyle(link).color===neutral && getComputedStyle(link.querySelector('.material-icons')).color===neutral) && getComputedStyle(node.querySelector('nav a[aria-current=page]')).color!==neutral;}),label + ' inactive destinations and icons use neutral host foreground; only current destination uses accent');
  const inactive=ui.locator('#JpNav nav a:not([aria-current=page])').first();
  if(await inactive.isVisible() && !await ui.locator('dialog[open]').count()) { await inactive.hover();check(await inactive.evaluate(node=>getComputedStyle(node).color===getComputedStyle(node.closest('.jp-navigation')).color && getComputedStyle(node.querySelector('.material-icons')).color===getComputedStyle(node).color && getComputedStyle(node).backgroundColor!=='rgba(0, 0, 0, 0)'),label+' inactive navigation hover stays neutral with distinct readable surface');await page.mouse.move(0,0); }
 const aside = ui.locator('.jp-aside');
  if (await aside.count()) check(await aside.evaluate(node => [...node.querySelectorAll(':scope > .emby-button,.jp-summary-actions > .emby-button,.jp-rule > .jp-link')].every(control => {const a=node.getBoundingClientRect(),r=control.getBoundingClientRect();return Math.abs((r.left+r.right-a.left-a.right)/2)<2 && getComputedStyle(control).textAlign==='center';})),label + ' aside primary and secondary CTAs share the panel centre, with controlled wrapping');
  if (await aside.count()) check(await aside.evaluate(node => [...node.querySelectorAll('.emby-button,.jp-link')].filter(control=>control.getClientRects().length).every(control=>{const s=getComputedStyle(control);return s.display==='flex' && s.justifyContent==='center' && s.textAlign==='center' && [...control.querySelectorAll('span:not(.material-icons)')].every(span=>getComputedStyle(span).textAlign==='center');})),label + ' every aside CTA centres its actual native flex/text content, including nested action groups');
  if (await ui.locator('.jp-auto-work').count()) check(await ui.locator('.jp-auto-work').evaluate(node => {const form=node.querySelector('form').getBoundingClientRect(),tasks=node.querySelector('.jp-tasks-panel').getBoundingClientRect(),aside=node.querySelector('aside').getBoundingClientRect();return Math.abs(form.width-tasks.width)<1 && Math.abs(form.left-tasks.left)<1 && (innerWidth>688?tasks.right<=aside.left:tasks.top>=aside.bottom);}),label + ' automation tasks/report belong to the main grid column; mobile follows the save aside');
  if(await ui.locator('.jp-clear').count()) check(await ui.locator('.jp-clear').evaluate(node=>getComputedStyle(node).color===getComputedStyle(node.parentElement).color && getComputedStyle(node).opacity!=='0'),label+' clear selection uses recognizable neutral tertiary styling, not the global-selection accent');
 if (await ui.locator('.jp-pager').count()) check(await ui.locator('.jp-pager').evaluateAll(nodes => nodes.every(node => {const r=node.getBoundingClientRect();return [...node.querySelectorAll('button')].every(button => {const s=getComputedStyle(button);return !button.classList.contains('raised') && s.boxShadow==='none' && s.backgroundColor==='rgba(0, 0, 0, 0)' && s.filter==='none' && button.querySelector('.material-icons[aria-hidden=true]') && button.getBoundingClientRect().height>=44;}) && r.width>0;})),label + ' paginators are compact native navigation with icons/labels, readable disabled states and 44px targets');
 if (await ui.locator('.jp-local-nav').count()) check(await ui.locator('.jp-local-nav').evaluate(node => node.querySelectorAll('[aria-current=page]').length===1 && !node.querySelector('[role=tab]') && [...node.querySelectorAll('a')].every(a => !a.classList.contains('jp-danger-button') && getComputedStyle(a).backgroundColor==='rgba(0, 0, 0, 0)') && getComputedStyle(node.querySelector('[aria-current=page]')).borderBottomStyle==='solid'),label + ' restoration/removal destinations are real current-page links with underline, never destructive fills or fake tabs');
}
async function realSticky(page,label,kind,device='desktop') {
 const ui = page.locator('#JellyPConfigPage:not(.hide)').last();
 await page.setViewportSize(device==='mobile'?{width:390,height:844}:{width:1600,height:900}); await nativeReady(page);
 const selector = kind==='bar'?'.jp-selection-bar':'.jp-aside';
 // Expand a genuine form disclosure to provide a useful scrolling interval even
 // when the initial empty manual scope is shorter than the contextual panel.
 const disclosure=ui.locator('.jp-work > form > section details:not([open]) > summary').first();
 const opened=kind==='aside' && device==='desktop' && await disclosure.count()>0;
 if(opened) await disclosure.click();
 const setup = await ui.locator(selector).evaluate((target,kind) => {
  const normal = kind==='bar'?target.parentElement.querySelector('.jp-row'):document.querySelector('#JellyPConfigPage:not(.hide) .jp-work > :first-child');
  const candidates=[];for(let node=normal.parentElement;node;node=node.parentElement) if(node.scrollHeight>node.clientHeight+10 && /auto|scroll/.test(getComputedStyle(node).overflowY)) candidates.push(node);
  candidates.push(document.scrollingElement);
  const owner=candidates.find(node=>{const before=node.scrollTop;node.scrollTop=before+1;const moved=node.scrollTop!==before;node.scrollTop=before;return moved;});
  if(!owner) throw Error('No active scroll owner');
  owner.dataset.jpSmokeScrollOwner='true';normal.dataset.jpSmokeNormal='true';owner.scrollTop=0;
  const region=kind==='bar'?target.parentElement:target.parentElement;
  const natural=region.getBoundingClientRect().top + owner.scrollTop;
  const stickyTop=parseFloat(getComputedStyle(document.querySelector('#JellyPConfigPage:not(.hide)')).getPropertyValue('--jp-sticky-top'));
  const start=Math.min(Math.max(180,kind==='bar'?natural+100:natural-stickyTop+8),owner.scrollHeight-owner.clientHeight-228);
  owner.scrollTop=start;
  return {owner:owner.tagName,ownerClass:owner.className,start,kind};
 },kind);
 const frame = () => page.evaluate(() => new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 await frame();
 const snapshot = () => ui.locator(selector).evaluate(target => {
  const owner=document.querySelector('[data-jp-smoke-scroll-owner]'),normal=document.querySelector('[data-jp-smoke-normal]'),nav=document.querySelector('#JellyPConfigPage:not(.hide) .jp-navigation');
  const rect=n=>n?.getBoundingClientRect().toJSON();
  return {scrollTop:owner.scrollTop,target:rect(target),normal:rect(normal),nav:rect(nav),headerBottom:Math.max(0,...[...document.querySelectorAll('.skinHeader,header.MuiAppBar-root')].map(n=>n.getBoundingClientRect().bottom)),viewport:innerHeight,innerScroll:target.scrollHeight>target.clientHeight+1};
 });
 const before=await snapshot();await page.screenshot({path:path.join(screenshots,'sticky-'+label+'-'+device+'-before.png')});
 await page.evaluate(()=>{document.querySelector('[data-jp-smoke-scroll-owner]').scrollTop+=220;});await frame();
 const after=await snapshot();await page.screenshot({path:path.join(screenshots,'sticky-'+label+'-'+device+'-after.png')});
 fs.writeFileSync(path.join(screenshots,'sticky-'+label+'-'+device+'.json'),JSON.stringify({setup,before,after},null,2));
 check(after.scrollTop-before.scrollTop>=210 && before.normal.top-after.normal.top>=210,label+' '+device+' actual '+setup.owner+' scroll owner advances while normal content moves');
 if(kind==='bar') check(Math.abs(before.target.bottom-after.target.bottom)<2 && after.target.bottom<=after.viewport+1 && after.target.top>after.headerBottom && after.target.height<after.viewport/3,label+' '+device+' selection summary/CTA stays visible at bounded list bottom, below host header');
 else if(device==='desktop') {
   const tall=after.target.height>after.viewport-after.headerBottom-32;
   check(tall ? before.target.top-after.target.top>=0 && after.target.top<before.headerBottom+16 : Math.abs(before.target.top-after.target.top)<2 && after.target.top>=after.headerBottom+12 && after.target.bottom<=after.viewport-10,label+' desktop contextual aside uses full intrinsic height and natural page movement when tall, top sticky when short');
   check(Math.abs(before.nav.top-after.nav.top)<2 && after.nav.top>=after.headerBottom+12,label+' desktop plugin menu really sticks while form moves');
   check(await ui.locator('.jp-aside,.jp-navigation').evaluateAll(nodes=>nodes.every(node=>{const s=getComputedStyle(node);return !/auto|scroll/.test(s.overflowY) && s.maxHeight==='none' && node.scrollHeight<=node.clientHeight+1;})),label+' no aside/menu nested scroll owner, clipping or maximum height');
   const cta=ui.locator('.jp-aside > button').last();await cta.scrollIntoViewIfNeeded();
   check(await cta.evaluate(n=>{const r=n.getBoundingClientRect(),a=n.closest('aside').getBoundingClientRect(),header=document.querySelector('header.MuiAppBar-root')?.getBoundingClientRect().bottom||0;return r.top>=header && r.bottom<=Math.min(a.bottom,innerHeight) && n.closest('aside').scrollTop===0;}),label+' tall aside actions are reached by PAGE scrolling, never inner scrolling');
   if(tall) {
    const bottom=await ui.locator('.jp-aside').evaluate(node=>{const region=node.parentElement,r=region.getBoundingClientRect(),owner=document.scrollingElement,top=parseFloat(getComputedStyle(node).top),natural=r.top+owner.scrollTop;owner.scrollTop=natural-top+8;return {canStick:region.getBoundingClientRect().height>node.getBoundingClientRect().height+8,top};});await frame();
    if(bottom.canStick) check(await ui.locator('.jp-aside').evaluate(node=>Math.abs(node.getBoundingClientRect().bottom-(innerHeight-16))<2),label+' tall panel reaches and sticks to viewport bottom after natural page scroll');
    await cta.scrollIntoViewIfNeeded();
    const point=await cta.boundingBox();await page.mouse.move(point.x+point.width/2,point.y+point.height/2);
    const scrolled=await page.evaluate(()=>scrollY);await page.mouse.wheel(0,-180);
    await page.waitForFunction(y=>scrollY<y,scrolled);
    check(await ui.locator('.jp-aside').evaluate(n=>n.scrollTop===0),label+' wheel over tall panel returns the PAGE without trapping scroll');
    await page.setViewportSize({width:1600,height:640});await frame();await cta.scrollIntoViewIfNeeded();await frame();
    check(await cta.evaluate(n=>{const a=n.closest('aside'),r=n.getBoundingClientRect(),s=getComputedStyle(a);return a.scrollTop===0 && s.maxHeight==='none' && Math.abs(parseFloat(s.top)-Math.min(parseFloat(getComputedStyle(document.querySelector('#JellyPConfigPage:not(.hide)')).getPropertyValue('--jp-sticky-top')),innerHeight-a.getBoundingClientRect().height-16))<2 && r.top>=48 && r.bottom<=innerHeight;}),label+' viewport resize recomputes intrinsic negative sticky offset and keeps CTA page-accessible');
    await page.setViewportSize({width:1600,height:900});await frame();
   }
  await page.screenshot({path:path.join(screenshots,'sticky-'+label+'-'+device+'-actions.png')});
 } else check(before.target.top-after.target.top>=210,label+' mobile form aside stays in normal flow, never floats over fields');
 if(kind==='bar') {
  const last=ui.locator('.jp-selection-bar').locator('..').locator('.jp-row input').last();
  // Change focus first: focusing the already-focused desktop input after a
  // viewport change does not ask the browser to scroll it again.
  await ui.locator('#JpTitle').focus();await last.focus();await frame();
  check(await last.evaluate(node=>{const r=node.parentElement.querySelector('.checkboxOutline').getBoundingClientRect(),bar=node.closest('.jp-panel').querySelector('.jp-selection-bar').getBoundingClientRect(),header=document.querySelector('header.MuiAppBar-root')?.getBoundingClientRect().bottom||0;return node===document.activeElement && r.top>=header && r.bottom<bar.top;}),label+' '+device+' focused native last-row checkbox outline remains above selection bar and below header');
  check(await ui.locator('.jp-selection-bar').evaluate(node=>{const r=node.getBoundingClientRect(),p=node.parentElement.getBoundingClientRect();return r.top>=p.top && r.bottom<=p.bottom;}),label+' '+device+' sticky bar stays within its own list region');
 }
 await page.evaluate(()=>{const owner=document.querySelector('[data-jp-smoke-scroll-owner]');owner.scrollTop=0;owner.removeAttribute('data-jp-smoke-scroll-owner');document.querySelector('[data-jp-smoke-normal]').removeAttribute('data-jp-smoke-normal');});await frame();
 if(opened) await ui.locator('.jp-work > form > section details[open] > summary').first().click();
 await page.setViewportSize({width:1600,height:1100});
}
function previewGuards() {
 const html = fs.readFileSync(path.join(__dirname,'../Jellyfin.Plugin.JellyP/Web/config.html'),'utf8');
 const body = html.slice(html.indexOf('function preview(row)'),html.indexOf('function inspectedPreview(id)'));
 let calls = 0;
 const render = new Function('ApiClient','tr','cat','itemTitle','esc','categories',body + '; return preview;')({getImageUrl:() => { calls++; return '/local-image'; }},en => en,x => x,() => 'Synthetic <title>',x => String(x).replaceAll('<','&lt;'),['Primary','Thumb']);
 for (const State of ['Remote','Missing','Excluded',undefined]) assert.ok(!render({ItemId:'a'.repeat(32),TypeName:'Thumb',Category:'Thumb',Index:2,State}).includes('<img'));
 check(calls === 0,'preview renderer guards issue no URL for Remote/Missing/Excluded/unconfirmed slots (unit, not mocked API)');
}
async function prototypeReferences(browser) {
 const context = await browser.newContext({viewport:{width:1600,height:1100},reducedMotion:'reduce'});
 const page = await context.newPage();
 await page.route('**/*',route => new URL(route.request().url()).protocol === 'file:' ? route.continue() : route.abort());
 await page.goto('file://' + path.resolve(__dirname,'../docs/ux-prototype/jellyp-p2.html'));
 async function shot(label) {
  for (const [device,viewport] of [['desktop',{width:1600,height:1100}],['mobile',{width:390,height:844}]]) { await page.setViewportSize(viewport); await page.screenshot({path:path.join(screenshots,'p2-reference-' + label + '-pt-dark-' + device + '.png'),fullPage:true}); }
  await page.setViewportSize({width:1600,height:1100});
 }
 await shot('draft');
 await page.locator('[data-act=analyse]').click(); await page.locator('.analysis-summary').waitFor(); await shot('result');
 await page.locator('[data-row]').first().check(); await page.locator('[data-act=review]').click(); await page.locator('.summary-panel .ack').waitFor(); await shot('review');
 for (const [area,label] of [['automacao','automation'],['historico','history'],['configuracoes','settings']]) { await page.goto('file://' + path.resolve(__dirname,'../docs/ux-prototype/jellyp-p2.html') + '#/p2/paginas/' + area); await page.locator('.area-head').waitFor(); await shot(label); }
 await context.close();
}
async function copyScenarios(page) {
 const baseline = await api('JellyP/Settings'), ui = page.locator('#JellyPConfigPage:not(.hide)').last();
 const fixture = (await api('Items?Recursive=true&IncludeItemTypes=Movie')).Items.find(item => item.Name === 'JellyP Canary (2026)');
 const target = base + '/web/#/configurationpage?name=jellyp&jp=automation';
 try {
  await page.goto(base + '/web/#/configurationpage?name=jellyp&jp=simulate'); await ui.locator('[data-jp-id=draftQuality]').waitFor();
  for (const language of ['en','pt']) {
   await ui.locator('[data-jp-id=Language]').selectOption(language); await nativeReady(page);
   await ui.locator('[data-jp-id=draftQuality]').fill('77');
   const before = await page.evaluate(() => JSON.parse(sessionStorage.getItem('jellyp-native-v1')).draft);
   const settingsBefore = JSON.stringify(await api('JellyP/Settings'));
   let confirmation = '';
   page.once('dialog',async dialog => { confirmation = dialog.message(); await dialog.accept(); });
   await ui.locator('[data-action=copyDefaults]').click();
   await until(async () => (await ui.locator('#JpMessage').textContent()).includes(language === 'en' ? 'copied' : 'copiados'),'copy global defaults');
   const after = await page.evaluate(() => JSON.parse(sessionStorage.getItem('jellyp-native-v1')).draft);
   check(confirmation.includes(language === 'en' ? 'Global defaults' : 'Padrões globais') && after.Quality === baseline.Defaults.Quality,'explicit global-default copy ' + language + ' names its source and copies actual saved quality');
   check(after.Scope === before.Scope && JSON.stringify(after.ItemIds) === JSON.stringify(before.ItemIds) && JSON.stringify(after.LibraryIds) === JSON.stringify(before.LibraryIds) && after.PreserveOriginals === before.PreserveOriginals && after.Policies.every((policy,index) => policy.Enabled === before.Policies[index].Enabled),'global-default copy ' + language + ' preserves scope, exact item/library IDs, enabled types and original preservation');
    check(JSON.stringify(await api('JellyP/Settings')) === settingsBefore,'global-default copy ' + language + ' saves neither defaults/globals nor automation');
    await navigationCraft(page,'manual aside with both CTAs '+language);
  }
  for (const items of [[fixture.Id],[]]) {
   const automation = {...baseline.Automation,Enabled:false,DryRun:true,PreserveOriginals:true,ScopeMode:'legacy',ItemIds:items,LibraryIds:[]};
   await api('JellyP/Settings/Automation','PUT',{Automation:automation,AuthorizePermanentRemoval:false});
   const before = JSON.stringify(await api('JellyP/Settings'));
   await page.goto(target); await page.reload(); await ui.locator('#SaveAutomation').waitFor();
   for (const language of ['en','pt']) {
    await ui.locator('[data-jp-id=Language]').selectOption(language); await nativeReady(page);
    const text = await ui.locator('#JpMain').innerText();
    check(!/legacy|legad|project|proje[çc]|compatibility|compatibilidade|muta[çt]/i.test(text),'automation ' + language + ' contains human copy, no implementation jargon');
    if (items.length) {
     check(await ui.locator('[data-jp-id=autoScope] option[value=legacy]').textContent() === (language === 'en' ? 'Specific items' : 'Itens específicos') && await ui.locator('[data-jp-id=autoScope] option[value=items]').count() === 0,'saved item scope ' + language + ' has one understandable option, underlying value preserved');
     check((await ui.locator('#autoSelected').textContent()).includes('JellyP Canary (2026)'),'saved item scope resolves real saved name without migration');
    } else {
     check(await ui.locator('[data-jp-id=autoScope] option[value=legacy]').textContent() === (language === 'en' ? 'Choose where to automate' : 'Escolha onde automatizar'),'empty old scope ' + language + ' asks explicit choice without selecting all');
     await ui.locator('#SaveAutomation').click();
     check((await ui.locator('#JpMessage').textContent()).includes(language === 'en' ? 'Choose libraries' : 'Escolha bibliotecas'),'ambiguous empty automation scope ' + language + ' save blocked');
    }
   }
   check(JSON.stringify(await api('JellyP/Settings')) === before,'reading saved scope and blocked save never migrate or broaden server configuration');
  }
  await api('JellyP/Settings/Defaults','PUT',{Defaults:null,Globals:baseline.Globals});
  const before = JSON.stringify(await api('JellyP/Settings'));
  await page.goto(base + '/web/#/configurationpage?name=jellyp&jp=settings'); await page.reload(); await ui.locator('#SaveSettings').waitFor();
  for (const language of ['en','pt']) {
   await ui.locator('[data-jp-id=Language]').selectOption(language); await nativeReady(page);
   const text = await ui.locator('#JpMain').innerText();
   check(text.includes(language === 'en' ? 'Global defaults' : 'Padrões globais') && !/manual defaults|padrões manuais|legacy|legad|project|proje[çc]|CompletedUtc/i.test(text),'settings ' + language + ' global defaults and completion-age copy, no internal terms');
   check(text.includes(language === 'en' ? 'including automation' : 'inclusive automação') && text.includes(language === 'en' ? 'do not overwrite' : 'não os sobrescrevem'),'settings ' + language + ' distinguishes quality defaults from global storage effects on automation');
  }
  check(JSON.stringify(await api('JellyP/Settings')) === before,'opening defaults warning does not save the existing settings');
 } finally {
  await api('JellyP/Settings/Defaults','PUT',{Defaults:baseline.DefaultsSource === 'legacy' ? null : baseline.Defaults,Globals:baseline.Globals});
  await api('JellyP/Settings/Automation','PUT',{Automation:baseline.Automation,AuthorizePermanentRemoval:false});
  await page.goto(target); await page.reload(); await ui.locator('#SaveAutomation').waitFor(); await ui.locator('[data-jp-id=Language]').selectOption('en');
 }
}
async function main() {
 const info = await api('System/Info');
 check(fs.realpathSync(info.ProgramDataPath) === path.join(root,'data'),'marker + real ProgramDataPath verified before any mutation');
 check(info.Version === '12.1.0','official Jellyfin 12.1 host');
 previewGuards();
 const settings = await api('JellyP/Settings');
 check(!!settings.Automation && !!settings.Defaults && !!settings.Globals,'runtime DTO PascalCase, not documentation camelCase');
 if (!process.env.JELLYP_UI_SOURCE) {
  const response = await fetch(base + '/web/ConfigurationPage?name=jellyp',{headers});
  const embedded = await response.text();
  check(response.ok && embedded === fs.readFileSync(path.join(__dirname,'../Jellyfin.Plugin.JellyP/Web/config.html'),'utf8'),'installed DLL serves exact current embedded page; no source replacement');
  for (const name of ['jellyp-wordmark-dark.png','jellyp-wordmark-light.png','jellyp-symbol.png']) {
   const image = await fetch(base + '/web/ConfigurationPage?name=' + name,{headers});
   check(image.ok && image.headers.get('content-type')?.startsWith('image/png') && Buffer.from(await image.arrayBuffer()).equals(fs.readFileSync(path.join(__dirname,'../Jellyfin.Plugin.JellyP/Web/branding',name))),name + ' host ConfigurationPage serves exact embedded PNG with image/png');
  }
 }
 const unauth = await fetch(base + '/JellyP/Settings');
 check(unauth.status === 401,'real unauthenticated API returns 401');
 const browser = await chromium.launch({headless:true});
 activeBrowser = browser;
 if (args.includes('--journey')) await prototypeReferences(browser);
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
  const ui = page.locator('#JellyPConfigPage:not(.hide)').last();
  await ui.locator('[data-jp-id=draftQuality]').waitFor();
  await setHostTheme(page,'dark',page.url());
 await ui.locator('[data-jp-id=Language]').selectOption('en');
 await page.waitForFunction(() => [...document.querySelectorAll('#JellyPConfigPage:not(.hide)')].at(-1)?.querySelector('#JpTitle')?.textContent === 'Simulate');
 check(await ui.locator('[data-jp-id=draftQuality]').evaluate(node => node.classList.contains('emby-input')),'real native emby-input enhancement');
 check(await ui.locator('[data-jp-id=draftPrimaryEnabled]').getAttribute('data-embycheckbox') === 'true','real native dynamic emby-checkbox enhancement');
 check(await ui.locator('.checkboxOutline').count() >= 11,'host checkbox outlines, not fake controls');
 check(await ui.locator('.selectArrowContainer').count() >= 3,'host select arrows loaded');
 check(await ui.locator('[data-jp-id=draftQuality]').evaluate(node => document.querySelectorAll('label[for="' + node.id + '"]').length === 1),'one native label per input');
 check(await ui.locator('[data-type]').count() === 11,'all eleven backend image categories represented');
  check(await ui.locator('.jp-steps [aria-current=step] .jp-step-number').textContent() === '1' && await ui.locator('.jp-steps [data-stage="1"]').isDisabled() && await ui.locator('.jp-steps [data-stage="2"]').isDisabled(),'numbered current step and distinct disabled future stages');
  await navigationCraft(page,'manual draft');
  await branding(page,'dark','initial desktop');
  await realSticky(page,'manual-draft','aside'); await realSticky(page,'manual-draft','aside','mobile');
 await page.screenshot({path:path.join(screenshots,'01-native-selection-en-dark-desktop.png'),fullPage:true});
 await ui.locator('[data-type=Primary] [data-action=disclose]').focus();
 await page.keyboard.press('Enter');
 check(await ui.locator('[data-jp-id=draftPrimaryEditor]').isVisible(),'keyboard disclosure opens real per-type editor');
 await ui.locator('[data-type=Primary] [data-action=disclose]').click();
 await ui.locator('#JpNav nav a').filter({hasText:'Automation'}).click();
 await ui.locator('#SaveAutomation').waitFor();
  check(page.url().includes('configurationpage?name=jellyp') && page.url().includes('jp=automation'),'internal navigation uses genuine host configurationpage router');
  await navigationCraft(page,'automation'); await realSticky(page,'automation','aside'); await realSticky(page,'automation','aside','mobile');
  const settingsLink=ui.getByRole('link',{name:'Change in Settings',exact:true});
  await settingsLink.hover();
  check(await settingsLink.evaluate(node=>getComputedStyle(node).backgroundColor!=='rgba(0, 0, 0, 0)' && !node.classList.contains('raised') && node.getAttribute('is')==='emby-linkbutton'),'secondary settings detour is a genuine native link with restrained hover feedback');
  await settingsLink.focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab');
  check(await settingsLink.evaluate(node=>node===document.activeElement && node.matches(':focus-visible') && parseFloat(getComputedStyle(node).outlineWidth)>=2),'secondary navigation has a visible keyboard focus ring');
  await page.mouse.move(0,0);
 await ui.locator('[data-jp-id=autoQuality]').fill('84');
 await ui.locator('#JpNav nav a').filter({hasText:'Settings',exact:true}).click();
 await ui.locator('#SaveSettings').waitFor();
  await navigationCraft(page,'settings'); await realSticky(page,'settings','aside');
  await page.goBack(); await ui.locator('[data-jp-id=autoQuality]').waitFor();
 check(await ui.locator('[data-jp-id=autoQuality]').inputValue() === '84','browser Back preserves unsaved automation draft');
 await page.screenshot({path:path.join(screenshots,'02-native-automation-en-dark-desktop.png'),fullPage:true});
 if (process.env.JELLYP_AXE_MODULE) {
  const AxeBuilder = require(process.env.JELLYP_AXE_MODULE).default;
  const results = await new AxeBuilder({page}).include('#JellyPConfigPage:not(.hide)').withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
  fs.writeFileSync(path.join(screenshots,'axe-automation.json'),JSON.stringify(results.violations,null,2));
  check(results.violations.length === 0,'axe native automation WCAG A/AA');
 }
   if (args.includes('--journey')) {
    await journey(page,settings); await copyScenarios(page);
    for (const area of ['simulate','automation','settings']) {
     await page.goto(base+'/web/#/configurationpage?name=jellyp&jp='+area);await ui.locator('.jp-aside').waitFor();await nativeReady(page);
     await captureMatrix(page,'aside-'+area,async(theme,language,device)=>{await navigationCraft(page,area+' aside matrix '+theme+' '+language+' '+device);});
    }
    await page.goto(base+'/web/#/configurationpage?name=jellyp&jp=automation');await ui.locator('#SaveAutomation').waitFor();await nativeReady(page);
   }
 await ui.locator('[data-jp-id=Language]').selectOption('pt');
  await page.waitForFunction(() => [...document.querySelectorAll('#JellyPConfigPage:not(.hide)')].at(-1)?.querySelector('#JpTitle')?.textContent === 'Automação');
 await page.screenshot({path:path.join(screenshots,'03-native-automation-pt-dark-desktop.png'),fullPage:true});
 await page.setViewportSize({width:390,height:844});
  await page.screenshot({path:path.join(screenshots,'04-native-automation-pt-dark-mobile.png'),fullPage:true});
  await branding(page,'dark','mobile');
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
  await branding(page,'light','mobile');
  await page.screenshot({path:path.join(screenshots,'05-native-automation-pt-light-mobile.png'),fullPage:true});
 await audit('automation-pt-light-mobile');
 await page.setViewportSize({width:1600,height:1100});
 await nativeReady(page);
  await branding(page,'light','desktop');
  await page.screenshot({path:path.join(screenshots,'06-native-automation-pt-light-desktop.png'),fullPage:true});
 await audit('automation-pt-light-desktop');
 for (const area of ['simulate','settings','history']) {
  await ui.locator('#JpNav nav a[href*="jp=' + area + '"]').click();
  await until(async () => (await ui.locator('#JpNav nav a[aria-current=page]').getAttribute('href')).includes('jp=' + area) && await ui.locator('#JpTitle').isVisible(),'light ' + area);
   await audit(area + '-pt-light-desktop');
   await navigationCraft(page,area+' light desktop');
  await nativeReady(page);
  await page.screenshot({path:path.join(screenshots,'07-native-' + area + '-pt-light-desktop.png'),fullPage:true});
 }
 await hostTheme('dark');
 await page.setViewportSize({width:768,height:1024});
 await page.screenshot({path:path.join(screenshots,'08-native-automation-pt-dark-tablet.png'),fullPage:true});
  check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),'tablet no horizontal overflow');
  await navigationCraft(page,'automation tablet');
  if (!process.env.JELLYP_UI_SOURCE) {
   const plugin = (await api('Plugins')).find(plugin => plugin.Id.replaceAll('-','') === '0928e33489714205a455f42a76527f9e');
    check(plugin.HasImage === true,'native installed plugin advertises its packaged catalog composition');
   const installedImage = await fetch(base + '/Plugins/' + plugin.Id + '/' + plugin.Version + '/Image');
    check(installedImage.ok && installedImage.headers.get('content-type')?.startsWith('image/png') && Buffer.from(await installedImage.arrayBuffer()).equals(fs.readFileSync(path.join(__dirname,'../assets/branding/jellyp-catalog.png'))),'native anonymous plugin Image endpoint serves the approved opaque catalog composition via meta.json imagePath');
   for (const theme of ['light','dark']) {
    await hostTheme(theme); await nativeReady(page);
    await page.goto(base + '/web/#/dashboard/plugins');
    const cardImage = page.locator('a[href*="/dashboard/plugins/' + plugin.Id + '"] .MuiCardMedia-root');
    await cardImage.waitFor();
    for (const [device,viewport] of [['desktop',{width:1600,height:1100}],['wide-tablet',{width:950,height:1024}],['tablet',{width:768,height:1024}],['mobile',{width:390,height:844}]]) {
     await page.setViewportSize(viewport); await cardImage.scrollIntoViewIfNeeded();
     const geometry = await cardImage.evaluate(async node => {
      const style = getComputedStyle(node), rect = node.getBoundingClientRect(), source = style.backgroundImage.match(/^url\(["']?(.*?)["']?\)$/)[1];
      const image = new Image(); image.src = source; await image.decode();
      const canvas = document.createElement('canvas'); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
      const context = canvas.getContext('2d'); context.drawImage(image,0,0);
      const pixels = context.getImageData(0,0,canvas.width,canvas.height).data;
      let left = canvas.width, top = canvas.height, right = 0, bottom = 0;
       // Bound the foreground against the opaque navy background, not alpha.
       for (let y = 0; y < canvas.height; y++) for (let x = 0; x < canvas.width; x++) { const i = (y * canvas.width + x) * 4; if (Math.max(Math.abs(pixels[i] - pixels[0]),Math.abs(pixels[i+1] - pixels[1]),Math.abs(pixels[i+2] - pixels[2])) > 40) { left = Math.min(left,x); top = Math.min(top,y); right = Math.max(right,x + 1); bottom = Math.max(bottom,y + 1); } }
      const scale = Math.max(rect.width / canvas.width,rect.height / canvas.height), offsetX = (rect.width - canvas.width * scale) / 2, offsetY = (rect.height - canvas.height * scale) / 2;
      return {source:new URL(source).pathname,naturalWidth:canvas.width,naturalHeight:canvas.height,backgroundSize:style.backgroundSize,backgroundPosition:style.backgroundPosition,rect:rect.toJSON(),alphaBounds:{left,top,right,bottom},painted:{left:offsetX + left * scale,top:offsetY + top * scale,right:offsetX + right * scale,bottom:offsetY + bottom * scale}};
     });
      check(geometry.naturalWidth === 960 && geometry.naturalHeight === 540 && geometry.backgroundSize === 'cover' && geometry.backgroundPosition === '50% 50%' && geometry.painted.left > 1 && geometry.painted.top > 1 && geometry.painted.right < geometry.rect.width - 1 && geometry.painted.bottom < geometry.rect.height - 1,'real installed plugin card ' + theme + ' ' + device + ' keeps the approved symbol and wordmark inside the native cover surface');
     fs.writeFileSync(path.join(screenshots,'installed-card-' + theme + '-' + device + '.json'),JSON.stringify(geometry,null,2));
     await page.screenshot({path:path.join(screenshots,'installed-card-' + theme + '-' + device + '.png')});
     await cardImage.locator('..').screenshot({path:path.join(screenshots,'installed-card-' + theme + '-' + device + '-detail.png')});
    }
    await page.setViewportSize({width:1600,height:1100});
    await page.goto(base + '/web/#/dashboard/plugins/' + plugin.Id + '?name=JellyP');
    const detailImage = page.locator('img[src*="/Plugins/"]');
    await detailImage.waitFor(); await detailImage.evaluate(image => image.decode());
    check(await detailImage.evaluate(image => { const r = image.getBoundingClientRect(), box = image.parentElement.getBoundingClientRect(); return image.naturalWidth === 960 && image.naturalHeight === 540 && Math.abs(r.width / r.height - 16 / 9) < .01 && r.left >= box.left - 1 && r.right <= box.right + 1 && r.top >= box.top - 1 && r.bottom <= box.bottom + 1; }),'native installed-plugin detail ' + theme + ' displays the complete approved catalog composition');
    await page.screenshot({path:path.join(screenshots,'installed-plugin-detail-' + theme + '-desktop.png')});
    await page.setViewportSize({width:390,height:844});
    check(!await detailImage.isVisible(),'native installed-plugin detail ' + theme + ' preserves host mobile layout, which intentionally hides its large image');
    await page.screenshot({path:path.join(screenshots,'installed-plugin-detail-' + theme + '-mobile.png')});
   }
  }
 check(errors.length === 0,'zero browser page errors');
 check(outside.every(origin => origin === 'https://www.gstatic.com'),'all outbound attempts blocked; only official host Cast sender attempted gstatic, no JellyP external dependency');
  fs.writeFileSync(path.join(screenshots,'native-summary.json'),JSON.stringify({checks,errors,outside,failedLocalRequests},null,2));
 await browser.close();
  console.log(checks.length + ' checks passed (real native host + separately labelled renderer guard unit)');
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
 const ui = page.locator('#JellyPConfigPage:not(.hide)').last();
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
  await captureMatrix(page,'result',async (theme,language) => {
   await navigationCraft(page,'results '+theme+' '+language);
  const result = await ui.locator('.jp-success').first().evaluate(node => ({border:getComputedStyle(node).borderLeftColor,bg:getComputedStyle(node).backgroundColor,title:getComputedStyle(node.querySelector('h2')).color}));
  check(result.border === result.title && result.bg !== 'rgba(0, 0, 0, 0)',theme + ' ' + language + ' success banner has semantic title, border and surface');
  check(await ui.locator('.jp-steps [aria-current=step] .jp-step-number').evaluate(node => getComputedStyle(node).borderRadius === '50%' && getComputedStyle(node).backgroundColor !== 'rgba(0, 0, 0, 0)'),theme + ' ' + language + ' current stage has filled numbered circle');
 });
  const image = ui.locator('[data-image-key] .jp-preview img').first(); await image.scrollIntoViewIfNeeded(); await until(async () => image.evaluate(node => node.naturalWidth > 0),'real preview decoded');
  await navigationCraft(page,'simulation results'); await realSticky(page,'simulation-results','bar'); await realSticky(page,'simulation-results','bar','mobile');
 check(await image.evaluate(node => { const row = node.closest('[data-image-key]'), key = row.dataset.imageKey, url = new URL(node.src); return url.pathname.includes('/Items/' + key.split(':')[0] + '/Images/' + node.dataset.slotType + '/' + Number(key.split(':')[2])); }),'decoded thumbnail requests exact server ItemId/type/index slot');
 const thumb = ui.locator('.jp-preview img[data-slot-type=Thumb]');
 if (await thumb.count()) { await thumb.first().scrollIntoViewIfNeeded(); await until(async () => thumb.first().evaluate(node => node.naturalWidth > 0),'local Thumb decoded'); check(await thumb.first().evaluate(node => new URL(node.src).pathname.includes('/Images/Thumb/0')),'real local Thumb never falls back to Primary'); }
  await checked(field('SelectPage'),true);
  check(await ui.locator('[data-plan-key]:checked').count() === 10,'native page checkbox selects only visible proposals');
  check((await ui.locator('[data-selection-count=plan]').textContent())==='10 selected in total · 10 of 10 on this page' && (await field('SelectPage').locator('..').innerText()).includes('(10)') && (await ui.locator('[data-action=selectAll]').innerText()).includes('(14)'),'simulation explicit page/global labels and real total/page subtotals agree');
  await ui.locator('[data-action=selectAll]').click();
 await until(async () => (await ui.locator('#JpSelectedCount').textContent()).startsWith('14 '),'select all proposals');
  check((await api('JellyP/Plan/' + initial.PlanId + '/Keys?kind=selectable')).Keys.length === 14,'all-results selection scope remains server-owned and cross-page');
  await ui.locator('[data-list=images][data-offset="10"]').click();await until(async()=>await ui.locator('#JpRangeimages').textContent()==='11–14 of 14','global selection second page');
  check(await ui.locator('[data-plan-key]:checked').count()===4 && await field('SelectPage').isChecked() && (await ui.locator('[data-selection-count=plan]').textContent())==='14 selected in total · 4 of 4 on this page','global selection marks every real second-page key and reports 14 total versus 4 here');
  await ui.locator('[data-action=clearSelection]').click();
  check(await ui.locator('[data-plan-key]:checked').count() === 0 && !(await field('SelectPage').isChecked()),'clear selection clears page checkbox and exact cross-page subset');
  check(await ui.locator('[data-action=clearSelection]').isDisabled() && (await ui.locator('[data-selection-count=plan]').textContent()).startsWith('0 selected in total · 0 of 4'),'cleared global selection has zero total/page subtotal and disabled clear');
  await ui.locator('[data-list=images][data-offset="0"]').click();await until(async()=>await ui.locator('#JpRangeimages').textContent()==='1–10 of 14','return first page');
 const allKeys = await api('JellyP/Plan/' + initial.PlanId + '/Keys?kind=selectable');
 const first = ui.locator('[data-plan-key]').first(), firstKey = await first.getAttribute('data-plan-key');
 await checked(first,true);
 check(await field('SelectPage').evaluate(node => node.indeterminate && node.getAttribute('aria-checked') === 'mixed'),'native page checkbox exposes partial selection as indeterminate');
 check(await field('SelectPage').evaluate(node => { const outline=node.parentElement.querySelector('.checkboxOutline');return !!outline && getComputedStyle(outline,'::after').content === '""' && getComputedStyle(outline,'::after').backgroundColor !== 'rgba(0, 0, 0, 0)'; }),'indeterminate state is visibly drawn on the real host-generated checkbox outline');
 check(await first.evaluate(node => node.closest('[data-image-key]').dataset.selected === 'true'),'selected row receives scoped active accent without rebuilding checkbox');
 await until(async () => (await ui.locator('#JpSelectedBill').textContent()).includes('Only selected images · 1'),'bill 1');
 check(await first.evaluate(node => node === document.activeElement || node.parentElement.contains(document.activeElement)),'selection bill updates preserve focused checkbox node');
 await ui.locator('[data-list=images][data-offset="10"]').click();
 await until(async () => await ui.locator('#JpRangeimages').textContent() === '11–14 of 14','server page 2');
 const second = ui.locator('[data-plan-key]').first(), secondKey = await second.getAttribute('data-plan-key'); await checked(second,true);
  await until(async () => (await ui.locator('#JpSelectedBill').textContent()).includes('Only selected images · 2'),'crosspage bill');
  check((await ui.locator('[data-selection-count=plan]').textContent())==='2 selected in total · 1 of 4 on this page' && await field('SelectPage').evaluate(n=>n.indeterminate) && await ui.locator('[data-action=clearSelection]').isEnabled(),'cross-page subset has distinct global/page counters, mixed checkbox and enabled clear');
 const review = await api('JellyP/Plan/' + initial.PlanId + '/Review','POST',{Keys:[firstKey,secondKey],Offset:0,Limit:10});
 check(review.TotalSelected === 2 && review.Billing.BackupBytesBefore === 0 && review.Billing.BackupBytesAfter > 0 && review.Billing.DeltaBytes < 0,'server cross-page subset billing counts active + new retained originals correctly');
 await page.screenshot({path:path.join(screenshots,'10-native-analysis-subset-crosspage.png'),fullPage:true});
  await ui.locator('.jp-steps [data-stage="0"]').focus(); await page.keyboard.press('Enter'); await field('draftQuality').waitFor();
  await ui.locator('.jp-steps [data-stage="1"]').focus(); await page.keyboard.press('Space'); await ui.locator('#JpSelectedCount').waitFor();
 check((await ui.locator('#JpSelectedCount').textContent()).startsWith('2 '),'earlier real stage buttons preserve selection until an edit');
 await ui.locator('#Review').click(); await ui.locator('#Apply').waitFor();
  check(await ui.locator('#Apply').isDisabled(),'application locked until explicit review acknowledgement');
  await navigationCraft(page,'apply review'); await realSticky(page,'apply-review','aside'); await realSticky(page,'apply-review','aside','mobile');
 check(await ui.locator('#Apply').evaluate(node => node.closest('aside')?.contains(document.querySelector('#JellyPConfigPage:not(.hide) [data-jp-id=ReviewAck]'))) && await ui.locator('.jp-review aside .jp-bill').count() === 1,'Apply, acknowledgement and selection ledger belong to right-hand review aside');
  await captureMatrix(page,'review',async (theme,language,device) => {
   await navigationCraft(page,'review '+theme+' '+language+' '+device);
  check(await ui.locator('#Apply').evaluate(node => !!node.closest('aside')),theme + ' ' + language + ' ' + device + ' authorization stays in aside');
  const boxes = await ui.locator('.jp-review').evaluate(node => [...node.children].map(child => { const r=child.getBoundingClientRect();return {x:r.x,y:r.y,bottom:r.bottom}; }));
  check(device === 'desktop' ? boxes[1].x > boxes[0].x : boxes[1].y >= boxes[0].bottom - 1,theme + ' ' + language + ' ' + device + ' P2 left identities/right authorization, stacked on mobile');
  const balance = ui.locator('.jp-review aside .jp-balance[data-sign=extra]');
  check(await balance.evaluate(node => getComputedStyle(node).color === getComputedStyle(node.querySelector('strong')).color && node.textContent.includes('+')),theme + ' ' + language + ' signed amber ledger amount and text');
 });
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
 await until(async () => ui.locator('#JpExecutionBody .jp-preview img').first().evaluate(node => node.naturalWidth > 0),'current artwork preview in completed receipt');
  check(await ui.locator('#JpExecutionBody .jp-preview img').count() === 2,'completed receipt retains real current-slot visual identities');
  await captureMatrix(page,'completion',async (theme,language,device) => {
   check(await ui.locator('.restoreEntry').evaluate(node=>{const body=node.parentElement.querySelector('#JpExecutionBody'),r=node.getBoundingClientRect(),b=body.getBoundingClientRect();return !body.contains(node) && node.classList.contains('paperList') && r.top-b.bottom>=16 && node.querySelector('h2') && node.querySelectorAll('a[is="emby-linkbutton"]').length===1 && !body.querySelector('a[href*="jp=history"]');}),theme+' '+language+' '+device+' unique real restoration CTA in its own paper card, separated below completion/results');
   await navigationCraft(page,'completion '+theme+' '+language+' '+device);
  });
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
  await ui.locator('.jp-local-nav a[href*="sub=retire"]').focus();await page.keyboard.press('Enter');await ui.locator('.jp-local-nav a[href*="sub=retire"][aria-current=page]').waitFor();
  await page.goBack();await ui.locator('.jp-local-nav a:not([href*="sub=retire"])[aria-current=page]').waitFor();
  check((await ui.locator('#JpTitle').innerText())==='History and restoration','local maintenance navigation supports Enter and browser Back with current-page semantics');
 check(await ui.locator('[data-backup-id] .jp-preview img').count() > 0,'history uses inspected current local slots, not guessed original URLs');
 check(!await ui.locator('[data-action=reviewOneBackup]').first().evaluate(node => node.classList.contains('jp-danger-button')),'restoration remains a normal native action, not destructive red');
 const backup = (await api('JellyP/Backups?filter=restorable&limit=200')).Items.find(row => row.Key === firstKey);
 await ui.locator('[data-backup-id="' + backup.RecordId + '"] [data-action=reviewOneBackup]').click();
  await ui.locator('#JpDialog[open]').waitFor(); check((await ui.locator('#JpDialog').textContent()).includes(backup.SourceHash),'native restoration review displays actual stored checksum evidence');
  check(!(await ui.locator('.jp-dialog-row > .fieldDescription').first().innerText()).includes('image/') && await ui.locator('.jp-dialog-row details .jp-code').count() > 0,'maintenance review shows readable format; raw MIME, IDs and checksum remain available in disclosure');
 async function confirmBackup(kind) {
  await checked(field('BackupAck'),true);
  const response = page.waitForResponse(r => r.url().endsWith('/JellyP/Backups/' + kind) && r.request().method() === 'POST');
  await ui.locator('#ConfirmBackup').click(); const result = await response; assert.equal(result.status(),202); const receipt = await result.json();
  const detail = await until(async () => { const d = await api('JellyP/Executions/' + receipt.OperationId); return ['Queued','Running'].includes(d.Operation.State) ? null : d; },kind,60000);
  assert.equal(detail.Operation.Failed,0); assert.equal(detail.Operation.Invalidated,0);
  return detail;
 }
  const singleRestored = await confirmBackup('Restore');
  check(fileHash(regenRecord.Source.Path) === firstRecord.SourceHash,'native restoration writes exact original bytes, not a browser WebP representation');
  await ui.locator('#JpExecutionBody').waitFor();await nativeReady(page);
  async function assertReceipt(detail,label) {
   const id=detail.Operation.OperationId;
   await until(async()=>await ui.locator('#JpOperationTitle').getAttribute('data-operation-id')===id,label+' requested receipt');await nativeReady(page);
   const expected=detail.Items.slice(0,10).map(row=>({key:row.Key,status:row.Status}));
   check(JSON.stringify(await ui.locator('[data-result-key]').evaluateAll(nodes=>nodes.map(n=>({key:n.dataset.resultKey,status:n.dataset.resultStatus}))))===JSON.stringify(expected),label+' actual rendered identities and per-item statuses match the selected persisted receipt, not latest results');
   check((await ui.locator('#JpOperationTitle').innerText()).startsWith(detail.Operation.Kind==='Apply'?'Application':'Restoration') && (await ui.locator('#JpExecutionBody').innerText()).includes(id),label+' visible kind/date/operation context identifies requested receipt');
   check(await ui.locator('#JpExecutionBody').textContent().then(text=>detail.Items.slice(0,10).every(row=>text.includes(row.SourceHash || '—'))),label+' rendered stored hashes come from requested receipt');
  }
  for(const detail of [applied.detail,singleRestored]) {
   await navigate('history');await nativeReady(page);
   const id=detail.Operation.OperationId,receiptLink=ui.locator('a[href*="operation='+id+'"]');
   const requested=page.waitForResponse(r=>new URL(r.url()).pathname==='/JellyP/Executions/'+id);
   await receiptLink.click();const response=await requested;
   check((await response.json()).Operation.OperationId===id,'history link requests its own real persisted operation '+detail.Operation.Kind);
   await assertReceipt(detail,'history '+detail.Operation.Kind);
   await page.reload();await ui.locator('#JpExecutionBody').waitFor();await assertReceipt(detail,'reloaded history '+detail.Operation.Kind);
   const polled=page.waitForResponse(r=>new URL(r.url()).pathname==='/JellyP/Executions/'+id);
   await page.evaluate(()=>document.dispatchEvent(new Event('visibilitychange')));await polled;await assertReceipt(detail,'refreshed history '+detail.Operation.Kind);
   await page.screenshot({path:path.join(screenshots,'receipt-selected-'+detail.Operation.Kind.toLowerCase()+'.png'),fullPage:true});
  }
  await page.goBack();await ui.locator('#ReviewBackups').waitFor();await page.goForward();await ui.locator('#JpExecutionBody').waitFor();await assertReceipt(singleRestored,'browser Forward restoration');
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
  const volumeApplied = await apply(volumeKeys);
  await ui.locator('[data-list=operation][data-offset="10"]').click();await nativeReady(page);
  const tail=await api('JellyP/Executions/'+volumeApplied.receipt.OperationId+'?offset=10&limit=10');
  check(JSON.stringify(await ui.locator('[data-result-key]').evaluateAll(nodes=>nodes.map(n=>n.dataset.resultKey)))===JSON.stringify(tail.Items.map(row=>row.Key)),'persisted operation results page two shows that operation’s exact remaining identities');
  await page.reload();await ui.locator('#JpExecutionBody').waitFor();await nativeReady(page);
  check(JSON.stringify(await ui.locator('[data-result-key]').evaluateAll(nodes=>nodes.map(n=>n.dataset.resultKey)))===JSON.stringify(tail.Items.map(row=>row.Key)),'reload preserves selected operation and its results-page offset');
 await navigate('history'); await ui.locator('[data-backup]').first().waitFor();
  check(await ui.locator('[data-backup]').count() === 10,'history is paginated by image even one large operation');
  await navigationCraft(page,'restoration history'); await realSticky(page,'restoration-history','bar'); await realSticky(page,'restoration-history','bar','mobile');
  const eligibleTotal=(await api('JellyP/Backups?filter=restorable&limit=1')).Total;
  await checked(field('SelectBackupPage'),true);
  check((await ui.locator('[data-selection-count=backup]').textContent())==='10 selected in total · 10 of 10 on this page','restoration shares explicit native page checkbox and total/page counters');
  await ui.locator('[data-action=selectAllBackups]').click();
  await until(async()=>(await ui.locator('[data-selection-count=backup]').textContent()).startsWith(eligibleTotal+' selected in total'),'global eligible backup selection');
  check((await ui.locator('[data-selection-count=backup]').textContent())===eligibleTotal+' selected in total · 10 of 10 on this page' && (await ui.locator('[data-action=selectAllBackups]').innerText()).includes('('+eligibleTotal+')'),'restoration global count comes from all real eligible originals');
  await ui.locator('[data-list=backups][data-offset="10"]').click();await nativeReady(page);
  check(await ui.locator('[data-backup]:checked').count()===eligibleTotal-10 && await field('SelectBackupPage').isChecked(),'restoration global selection includes checked identities on second page');
  await ui.locator('[data-list=backups][data-offset="0"]').click();await nativeReady(page);
  await ui.locator('#ReviewBackups').click(); await ui.locator('#JpDialog[open]').waitFor();
  check(await ui.locator('.jp-dialog-row').count() === 10,'large restoration review mounts ten exact originals');
  await navigationCraft(page,'restoration dialog');
  check(await ui.locator('.jp-selection-bar').evaluate(node=>getComputedStyle(node).visibility==='hidden'),'list sticky selection bar is suppressed while native modal owns focus');
 await ui.locator('[data-list=dialog][data-offset="10"]').click();
 check(await ui.locator('.jp-dialog-row').count() >= 4,'all remaining restoration identities are reviewable without truncation');
 await page.keyboard.press('Escape');
 check(!(await ui.locator('#JpDialog').isVisible()),'Escape cancels native maintenance review without effects');
 await ui.locator('[data-action=clearBackups]').click();
 const restoreFirst = await ui.locator('[data-backup]').first().getAttribute('data-backup'); await checked(ui.locator('[data-backup]').first(),true);
 await ui.locator('[data-list=backups][data-offset="10"]').click();
 await until(async () => (await ui.locator('#JpRangebackups').textContent()).startsWith('11–'),'backup page 2 loaded');
  const restoreSecond = await ui.locator('[data-backup]').first().getAttribute('data-backup'); await checked(ui.locator('[data-backup]').first(),true);
  check((await ui.locator('[data-selection-count=backup]').textContent()).startsWith('2 selected in total · 1 of ') && await field('SelectBackupPage').evaluate(n=>n.indeterminate),'restoration cross-page subset and native mixed state agree with global/page counters');
 await ui.locator('#ReviewBackups').click(); await ui.locator('#JpDialog[open]').waitFor();
 await page.screenshot({path:path.join(screenshots,'13-native-batch-restoration-review.png'),fullPage:true});
 const restoredSubset = await confirmBackup('Restore');
 check(restoredSubset.Items.length === 2 && restoredSubset.Items.every(row => [restoreFirst,restoreSecond].includes(row.RecordId) && row.Status === 'Restored'),'crosspage restoration applies only the two reviewed record IDs');
 await navigate('history'); await ui.locator('a[href*="sub=retire"]').click();
 await until(async () => await ui.locator('#JpTitle').textContent() === 'Remove originals · permanent','permanent removal subpage');
 check(await ui.locator('[data-backup]:checked').count() === 0,'permanent removal has its own empty selection and subpage');
 const retireRow = (await api('JellyP/Backups?filter=retirable&limit=200')).Items[0];
 assert.ok(retireRow && fixtureIds.has(retireRow.ItemId) && sourceHashes.has(retireRow.SourceHash),'Refuse retirement except exact generated PNG originals');
  const retireTarget = page.url();
  // The separate destination preserves the current offset. Exercise stickiness
  // on its full first page, not the two-row tail where the bar correctly releases.
  if (await ui.locator('[data-list=backups][data-offset="0"]:enabled').count()) { await ui.locator('[data-list=backups][data-offset="0"]').click(); await until(async()=>await ui.locator('[data-backup]').count()===10,'retirement full first page'); }
   await navigationCraft(page,'permanent removal navigation'); await realSticky(page,'retirement','bar'); await realSticky(page,'retirement','bar','mobile');
   const retirementTotal=(await api('JellyP/Backups?filter=retirable&limit=1')).Total;
   await checked(field('SelectBackupPage'),true);
   check((await ui.locator('[data-selection-count=backup]').textContent())==='10 selected in total · 10 of 10 on this page','retirement uses the same explicit page selection and subtotal as simulation/restoration');
   await ui.locator('[data-action=selectAllBackups]').click();await until(async()=>(await ui.locator('[data-selection-count=backup]').textContent()).startsWith(retirementTotal+' selected in total'),'global retirement selection');
   await ui.locator('[data-list=backups][data-offset="10"]').click();await nativeReady(page);
   check(await field('SelectBackupPage').isChecked() && await ui.locator('[data-backup]:checked').count()===retirementTotal-10 && (await ui.locator('[data-selection-count=backup]').textContent()).includes('· '+(retirementTotal-10)+' of '+(retirementTotal-10)),'retirement global selection/count includes every real second-page eligible ID');
   await ui.locator('[data-action=clearBackups]').click();
   check(await ui.locator('[data-action=clearBackups]').isDisabled() && !(await field('SelectBackupPage').isChecked()),'retirement clear removes cross-page selection and disables only the empty clear action');
   await checked(ui.locator('[data-backup]').first(),true);await ui.locator('[data-list=backups][data-offset="0"]').click();await nativeReady(page);await checked(ui.locator('[data-backup]').first(),true);
   check((await ui.locator('[data-selection-count=backup]').textContent())==='2 selected in total · 1 of 10 on this page' && await field('SelectBackupPage').evaluate(n=>n.indeterminate),'retirement cross-page subset reports 2 globally, 1 here and native mixed state');
   await ui.locator('[data-action=clearBackups]').click();
 for (const theme of ['dark','light']) {
  await setHostTheme(page,theme,retireTarget);
  await ui.locator('[data-action=selectAllBackups]').click(); await nativeReady(page);
  await redButton(ui.locator('#ReviewBackups'),theme + ' enabled removal review');
  await redButton(ui.locator('[data-action=reviewOneBackup]').first(),theme + ' individual removal');
  await ui.locator('#ReviewBackups').click(); await ui.locator('#JpDialog[open]').waitFor();
  await checked(field('BackupAck'),true); await nativeReady(page);
  await redButton(ui.locator('#ConfirmBackup'),theme + ' enabled permanent confirmation');
  await page.screenshot({path:path.join(screenshots,'fidelity-removal-en-' + theme + '-desktop.png'),fullPage:true});
  await page.setViewportSize({width:390,height:844});
  await ui.locator('#ConfirmBackup').scrollIntoViewIfNeeded();
  check(await ui.locator('#ConfirmBackup').evaluate(node => {const r=node.getBoundingClientRect();return r.top >= 0 && r.bottom <= innerHeight;}),theme + ' mobile permanent confirmation reachable, not covered');
  await page.screenshot({path:path.join(screenshots,'fidelity-removal-en-' + theme + '-mobile.png'),fullPage:true});
  await auditFidelity(page,'removal-' + theme);
  await page.keyboard.press('Escape'); await page.setViewportSize({width:1600,height:1100});
  await ui.locator('[data-action=clearBackups]').click();
 }
 await setHostTheme(page,'dark',retireTarget);
 // The prior history page may be at offset 10; choose the explicit matching page.
 if (!await ui.locator('[data-backup="' + retireRow.RecordId + '"]').count()) await ui.locator('[data-list=backups][data-offset="0"]').click();
 await checked(ui.locator('[data-backup="' + retireRow.RecordId + '"]'),true); await ui.locator('#ReviewBackups').click(); await ui.locator('#JpDialog[open]').waitFor();
 check(await ui.locator('#ConfirmBackup').isDisabled(),'permanent removal requires its own explicit acknowledgement');
 await page.screenshot({path:path.join(screenshots,'14-native-permanent-retirement-review.png'),fullPage:true});
 const retired = await confirmBackup('RetireSelected');
  check(retired.Items.length === 1 && retired.Items[0].RecordId === retireRow.RecordId && retired.Items[0].Status === 'Retired','only one generated original permanently retired; active WebP remains');
  await ui.locator('#JpExecutionBody').waitFor(); await nativeReady(page);
  check(!(await ui.locator('.restoreEntry').innerText()).includes('Want to undo'),'retirement conclusion never offers to undo the irreversible deletion');
 await navigate('history'); await ui.locator('#ReviewBackups').waitFor();
 await ui.locator('[data-action=selectAllBackups]').click(); await ui.locator('#ReviewBackups').click(); await ui.locator('#JpDialog[open]').waitFor();
 await confirmBackup('Restore');
  check((await api('JellyP/Backups')).Total === 0,'all remaining generated originals restored; no active backups left');
  await until(async()=>await ui.locator('.restoreEntry').count()>0 && (await ui.locator('.restoreEntry').innerText()).includes('No stored originals available'),'restoration conclusion reflects real empty availability');
  check(await ui.locator('.restoreEntry a').count()===0 && await ui.locator('#JpExecutionBody a[href*="jp=history"]').count()===0,'restoration conclusion with no originals offers neither an unavailable restore CTA nor a duplicate in the results card');
  await page.screenshot({path:path.join(screenshots,'completion-restored-no-originals-desktop.png'),fullPage:true});
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
 await field('draftPreserveOriginals').selectOption('false');
 const removalPlan = await analyze();
 const removalKey = (await api('JellyP/Plan/' + removalPlan.PlanId + '/Keys?kind=selectable')).Keys[0];
 await checked(ui.locator('[data-plan-key="' + removalKey + '"]'),true); await ui.locator('#Review').click(); await ui.locator('#Apply').waitFor();
 await captureMatrix(page,'review-remove',async (theme,language,device) => {
  check(await field('PermanentAck').evaluate(node => !!node.closest('aside')) && await field('ReviewAck').evaluate(node => !!node.closest('aside')),theme + ' ' + language + ' ' + device + ' separate permanence and review consents stay next to Apply');
  await checked(field('ReviewAck'),true); await checked(field('PermanentAck'),true);
  check(await ui.locator('#Apply').isEnabled(),theme + ' ' + language + ' ' + device + ' explicit acknowledgements enable removal application');
  await redButton(ui.locator('#Apply'),theme + ' ' + language + ' ' + device + ' removal Apply');
  check(await ui.locator('.jp-review aside .jp-balance[data-sign=released]').evaluate(node => node.textContent.includes('−') && getComputedStyle(node).color === getComputedStyle(node.querySelector('strong')).color),theme + ' ' + language + ' green released ledger uses signed amount and wording');
 });
 // Review only: no Apply with removal of conversion originals is submitted.
 await ui.locator('[data-action=stage][data-stage="0"]').click(); await field('draftQuality').waitFor(); await field('draftPreserveOriginals').selectOption('true');
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
