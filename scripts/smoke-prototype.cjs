/* Synthetic P2 contracts; file:// only, no server or credentials.
 * JELLYP_PLAYWRIGHT_MODULE=/path/to/playwright node scripts/smoke-prototype.cjs
 * Optional --screenshots /tmp/opencode/jellyp-p2. Never points at a live host.
 */
'use strict';
const { chromium } = require(process.env.JELLYP_PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const args = process.argv.slice(2);
const shots = args.includes('--screenshots') ? args[args.indexOf('--screenshots') + 1] : null;
const url = pathToFileURL(path.resolve(__dirname, '../docs/ux-prototype/jellyp-p2.html')).href;

(async () => {
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, reducedMotion: 'reduce' });
  const page = await context.newPage();
  const AxeBuilder = process.env.JELLYP_AXE_MODULE ? require(process.env.JELLYP_AXE_MODULE).default : null;
  const results = [], errors = [], remote = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  // Fail closed: even an accidental endpoint or CDN cannot be contacted by this test.
  await page.route(/^https?:/, route => { remote.push(route.request().url()); return route.abort(); });
  const check = (name, ok) => { results.push({ name, ok: !!ok }); console.log((ok ? 'OK   ' : 'FAIL ') + name); };
  const text = () => page.locator('main').innerText();
  const wait = t => page.waitForFunction(t => document.querySelector('main').innerText.includes(t), t);
  const action = a => page.locator('[data-act="' + a + '"]');
  const bench = async () => { await page.locator('#bench').evaluate(n => { n.open = true; }); };
  const scenario = async value => { await bench(); await page.locator('#scenario').selectOption(value); };
  const nav = async name => {
    const menu = page.locator('#areaMenu');
    if (await menu.count() && await menu.isVisible()) {
      await menu.selectOption({label:name});
      await page.getByRole('heading', { name, exact: true, level: 1 }).waitFor();
      return;
    }
    const tabs = page.getByRole('tab', { name, exact: true });
    if (await tabs.count()) await tabs.click(); else await page.locator('#shellnav').getByRole('link', { name, exact: true }).click();
    await page.getByRole('heading', { name, exact: true, level: 1 }).waitFor();
  };
  const fresh = async () => { await page.goto(url); await page.getByRole('heading', { name: 'Simular', exact: true }).waitFor(); };
  const number = async (selector, value) => { await page.locator(selector).fill(String(value)); await page.locator(selector).press('Tab'); };
  const simulate80 = async () => { await number('#manual-quality', 80); await action('analyse').click(); await page.locator('#resultadoAnalise').waitFor(); };
  const review = async (ids = ['img-v-capa', 'img-v-mini']) => {
    for (const id of ids) await page.locator('[data-row="' + id + '"]').check();
    await action('review').click(); await wait('Revisão final');
  };
  const shot = async name => {
    if (!shots) return;
    fs.mkdirSync(shots, { recursive: true });
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: path.join(shots, name + '.png'), fullPage: true });
    await page.screenshot({ path: path.join(shots, name + '-viewport.png') });
  };
  const noOverflow = async name => check(name, await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
  const axe = async name => {
    if (!AxeBuilder) return;
    // axe tries to XHR external CSS under file://, which Chromium blocks. Inline the
    // exact same local CSS only during the audit; the product itself needs no XHR.
    const css = fs.readFileSync(path.resolve(__dirname, '../docs/ux-prototype/jellyp-p2.css'), 'utf8');
    await page.evaluate(css => {
      const link = document.querySelector('link[rel="stylesheet"]');
      window.auditLink = link;
      const style = document.createElement('style'); style.id = 'axe-local-css'; style.textContent = css;
      link.replaceWith(style);
    }, css);
    let report;
    try {
      report = await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
    } finally {
      await page.evaluate(() => { document.getElementById('axe-local-css').replaceWith(window.auditLink); delete window.auditLink; });
    }
    check('axe: '+name, report.violations.length === 0);
    if(report.violations.length)console.error(JSON.stringify(report.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)}))));
  };
  try {
    await fresh();
    check('entrada local + cinco áreas', await page.locator('#shellnav a').count() === 5);
    check('bancada recolhida, fora do fluxo principal', !(await page.locator('#bench').getAttribute('open')));
    await shot('01-desktop-selecao');
    await axe('seleção desktop');
    await page.locator('[data-lib="filmes"]').uncheck();
    check('vazio nunca vira tudo: simular bloqueado', await action('analyse').isDisabled());
    await page.locator('[data-lib="filmes"]').check();
    await page.locator('[data-type="Primary"]').uncheck();
    await page.locator('[data-type="Thumb"]').uncheck();
    check('nenhum tipo: bloqueado e explicado', await action('analyse').isDisabled() && (await text()).includes('Marque pelo menos'));
    await page.locator('[data-type="Primary"]').check();
    await page.locator('[data-type="Thumb"]').check();
    await page.locator('#manual-quality').fill('');
    check('campo obrigatório vazio bloqueia sem converter vazio em zero', await action('analyse').isDisabled());
    await page.locator('#manual-quality').fill('80');
    await action('analyse').click();
    check('editar número e clicar funciona na primeira tentativa', await page.locator('#resultadoAnalise').count() === 1);
    check('análise única: converter + regenerar + manter + filtro', (await text()).includes('Gerar novamente a partir do original') && (await text()).includes('Converter para WebP') && (await text()).includes('WebP sem original') && (await text()).includes('Economia de 3%'));
    check('escopo real de Filmes: 4 imagens, nenhuma da série', await page.locator('.result-list [data-image-id]').count() === 4 && !(await text()).includes('Série Demonstração'));
    check('nenhuma proposta pré-autorizada', await page.locator('[data-row]:checked').count() === 0 && await action('review').isDisabled());
    check('foco no resultado', await page.evaluate(() => document.activeElement.id === 'resultadoAnalise'));
    check('contabilidade coerente: guardar usa 68 KiB a mais', (await text()).includes('68 KiB') && (await text()).includes('a mais em uso'));
    await shot('02-desktop-simulacao');
    await axe('resultado desktop');

    // Stale results remain consultable, but cannot authorize an operation.
    await page.locator('[data-row="img-v-capa"]').check();
    check('anúncio pluraliza uma imagem selecionada', (await page.locator('#announcer').innerText()).includes('1 imagem selecionada'));
    await action('editDraft').click();
    await page.locator('[data-lib="series"]').check();
    await action('showResults').click();
    check('mudar biblioteca invalida sem apagar relatório', (await text()).includes('desatualizada') && await action('review').isDisabled() && await page.locator('.result-list [data-image-id]').count() === 4);
    await action('editDraft').first().click();
    await page.locator('[data-lib="series"]').uncheck();
    await action('analyse').click();
    await review(['img-v-mini']);
    check('revisão enumera só a identidade escolhida', await page.locator('.review-list [data-image-id]').count() === 1 && await page.locator('.review-list [data-image-id="img-v-mini"]').count() === 1);
    check('revisão e aplicação de uma imagem usam singular', (await text()).includes('Revisão final · 1 imagem') && (await text()).includes('Aplicar à 1 imagem') && !(await text()).includes('1 imagens'));
    check('confirmação é obrigatória, não decorativa', await action('apply').isDisabled());
    await page.locator('[data-ack="ack"]').check();
    check('confirmar habilita a aplicação', await action('apply').isEnabled());
    await shot('03-desktop-revisao');
    await axe('revisão desktop');
    const applyBox = await action('apply').boundingBox();
    await action('apply').click();
    await wait('Verificando referências');
    check('inventário não inventa porcentagem', !(await page.locator('progress').getAttribute('value')));
    check('andamento recebe foco', await page.evaluate(() => document.activeElement.id === 'andamento'));
    // The second physical click must not admit another operation. It can legitimately
    // move focus to the page background, so focus is asserted before this click.
    await page.mouse.click(applyBox.x + applyBox.width / 2, applyBox.y + applyBox.height / 2);
    await nav('Automação');
    check('automação manualmente intocada durante execução', await page.locator('[data-auto="quality"]').inputValue() === '85' && await page.locator('[data-auto="mode"]').inputValue() === 'preview');
    await page.locator('#activity a').click();
    await wait('Aplicação concluída');
    check('aplica exatamente uma; sem admissão duplicada', await page.locator('.execution-result li').count() === 1 && (await text()).includes('Miniatura'));
    check('contador de uma aplicação usa singular', (await text()).includes('1 aplicada') && !(await text()).includes('1 aplicadas'));
    await shot('04-desktop-conclusao');
    await action('go').filter({ hasText: 'Ver histórico' }).click();
    await wait('Operações');
    const newly = page.locator('.history-record').filter({ has: page.locator('[data-id^="exec-"]') }).first();
    const newGroup = page.locator('.history-group').filter({ has: page.locator('[data-record^="exec-"]') }).first();
    check('histórico pluraliza um registro', (await newGroup.locator('header .badge').innerText()).trim() === '1 imagem');
    await newly.getByRole('button', { name: 'Restaurar original', exact: true }).click();
    check('restauração revisa formato, dimensão e bytes', (await page.getByRole('dialog').innerText()).includes('JPEG · 1280 × 720 · 220 KiB'));
    await page.keyboard.press('Escape');
    check('Escape cancela restauração sem mutação', await newly.getByRole('button', { name: 'Restaurar original', exact: true }).count() === 1);
    await newly.getByRole('button', { name: 'Restaurar original', exact: true }).click();
    await page.locator('[data-confirm="restore"]').click();
    check('restauração chega ao estado correto', (await text()).includes('Original restaurado no cenário'));
    await page.locator('.history-record details').first().evaluate(n => { n.open = true; });
    check('distingue bytes armazenados da representação WebP', (await text()).includes('Armazenado após restaurar') && (await text()).includes('HTTP não comprova'));
    await shot('05-desktop-restauracao');

    // Retire one, never an implicit plugin-wide delete.
    await page.locator('[data-purge="h1"]').check();
    await action('purgeReview').click();
    check('remoção lista só um original e os bytes exatos', (await page.getByRole('dialog').innerText()).includes('486 KiB') && await page.locator('dialog li').count() === 1);
    check('diálogo de remoção usa singular', (await page.getByRole('dialog').getByRole('heading').innerText()).includes('1 original permanentemente'));
    check('remoção permanente exige autorização própria', await page.locator('[data-confirm="purge"]').isDisabled());
    await page.locator('#dialogAck').check();
    await shot('06-desktop-remocao');
    await axe('diálogo de remoção');
    await page.locator('[data-confirm="purge"]').click();
    check('remove só h1; mantém h2 e incerto h4', await page.locator('[data-purge="h1"]').count() === 0 && await page.locator('[data-purge="h2"]').count() === 1 && (await page.locator('[data-record="h4"]').innerText()).includes('incerto'));
    check('foco após remoção vai ao título do histórico', await page.evaluate(() => document.activeElement.id === 'historyTitle'));
    await action('purgeAll').click();
    check('selecionar todos não remove imediatamente', await page.locator('[data-purge]:checked').count() === 1 && (await page.locator('[data-record="h2"]').innerText()).includes('Original disponível'));
    check('anúncio de um original selecionado usa singular', (await page.locator('#announcer').innerText()).includes('1 original selecionado') && (await page.locator('#announcer').innerText()).includes('Nada foi removido'));
    await nav('Simular');await action('editDraft').first().click();await action('analyse').click();
    check('original removido não permite regeneração silenciosa', (await page.locator('[data-image-id="img-v-capa"]').innerText()).includes('Original indisponível') && await page.locator('[data-row="img-v-capa"]').count()===0);

    // Isolation includes unsaved settings, saved settings and automation authorization.
    await nav('Simular'); await action('editDraft').click();
    await page.locator('[data-draft="storage"][value="remover"]').check();
    await nav('Automação');
    check('preservação manual não vaza para automação', await page.locator('[data-auto="storage"][value="guardar"]').isChecked());
    await axe('automação');
    await page.locator('[data-auto-lib="filmes"]').uncheck();
    await page.locator('[data-auto-lib="series"]').check();
    await page.locator('[data-auto="mode"]').selectOption('apply');
    await page.locator('[data-auto="storage"][value="remover"]').check();
    await action('saveAuto').click();
    check('automação destrutiva requer confirmação separada', await page.locator('[data-confirm="auto"]').isDisabled());
    await page.locator('#dialogAck').check(); await page.locator('[data-confirm="auto"]').click();
    check('automação gravada explicitamente', (await text()).includes('Automação salva nesta sessão'));
    check('foco após autorizar automação vai ao título', await page.evaluate(() => document.activeElement.id === 'autoTitle'));
    await nav('Configurações');
    await axe('configurações');
    await number('[data-cfg="quality"]', 90);
    await nav('Simular');
    check('padrão não salvo não altera manual', await page.locator('#manual-quality').inputValue() === '80');
    await nav('Configurações'); await action('saveCfg').click();
    await nav('Simular');
    check('padrão salvo também não sobrescreve manual', await page.locator('#manual-quality').inputValue() === '80');
    await nav('Automação');
    check('padrão salvo não sobrescreve automação', await page.locator('[data-auto="quality"]').inputValue() === '85' && await page.locator('[data-auto-lib="series"]').isChecked() && !(await page.locator('[data-auto-lib="filmes"]').isChecked()));

    // Complete-policy override, validation and stale snapshots.
    await fresh();
    await page.locator('[data-act="editType"][data-id="Primary"]').click();
    await page.locator('[data-custom="Primary"]').check();
    await number('[data-policy="quality"][data-id="Primary"]', 80);
    await number('#manual-quality', 90);
    await action('analyse').click();
    check('personalização completa não herda qualidade do rascunho', (await page.locator('[data-image-id="img-v-capa"]').innerText()).includes('Qualidade proposta: 80'));
    await action('editDraft').click();
    await page.locator('#manualAdvanced').evaluate(n => { n.open = true; });
    await number('[data-draft="batch"]', 251);
    check('limite manual não ultrapassa global', await action('analyse').isDisabled());
    await number('[data-draft="batch"]', 1);
    await action('analyse').click();
    check('limite é aplicado ao conjunto sintético', await page.locator('.result-list [data-image-id]').count() === 1);

    // Effective policy validation: lossless ignores quality; disabled types are not effective.
    await fresh();
    await page.locator('#manualAdvanced').evaluate(n => { n.open = true; });
    await page.locator('[data-draft="lossless"]').check();
    await page.locator('#manual-quality').evaluate(n => { n.disabled = false; n.value = ''; n.dispatchEvent(new Event('input', { bubbles: true })); });
    check('qualidade vazia não bloqueia sem perdas', await action('analyse').isEnabled());
    await action('analyse').click();
    check('análise sem perdas não exige qualidade numérica', await page.locator('#resultadoAnalise').count() === 1 && (await text()).includes('Sem perdas'));

    await fresh();
    await page.locator('[data-act="editType"][data-id="Primary"]').click();
    await page.locator('[data-custom="Primary"]').check();
    await number('[data-policy="quality"][data-id="Primary"]', 999);
    check('personalizado inválido de tipo habilitado bloqueia', await action('analyse').isDisabled());
    await page.locator('[data-type="Primary"]').uncheck();
    check('personalizado de tipo desmarcado não bloqueia', await action('analyse').isEnabled());
    await action('analyse').click();
    check('política efetiva ignora tipo desmarcado', await page.locator('#resultadoAnalise').count() === 1 && await page.locator('[data-image-id="img-v-capa"]').count() === 0);

    // Expiry is global, visible and separately authorized (D4).
    await fresh();await nav('Configurações');
    await number('[data-cfg="retention"]', 30);await action('saveCfg').click();
    await nav('Simular');await simulate80();await review(['img-v-mini']);
    check('revisão prevê expiração fora da seleção', (await text()).includes('Expiração global') && (await text()).includes('486 KiB') && (await text()).includes('fora da seleção manual'));
    check('expiração de um original usa singular', (await text()).includes('1 original') && (await text()).includes('será removido') && !(await text()).includes('1 originais'));
    await page.locator('[data-ack="ack"]').check();
    check('retenção não é autorização silenciosa', await action('apply').isDisabled());
    await page.locator('[data-ack="expiryAck"]').check();
    check('retenção explicitamente autorizada habilita aplicação', await action('apply').isEnabled());
    await shot('07-desktop-retencao');
    await nav('Histórico e restauração'); await page.locator('[data-purge="h1"]').check();
    await nav('Executar'); await action('apply').click(); await wait('Aplicação concluída');
    check('expiração de um original registra singular', (await text()).includes('1 original removido') && !(await text()).includes('1 originais removidos'));
    await nav('Histórico e restauração');
    check('seleção de remoção fica obsoleta após expiração', await action('purgeReview').isDisabled() && (await action('purgeReview').innerText()).includes('(0)') && (await page.locator('[data-record="h1"]').innerText()).includes('Original removido'));

    // Retention must keep originals required by the selected regeneration.
    await fresh();await nav('Configurações');
    await number('[data-cfg="retention"]', 30);await action('saveCfg').click();
    await nav('Simular');await simulate80();await review(['img-v-capa']);
    check('retenção protege original necessário à regeneração', (await text()).includes('Proteção desta operação') && (await text()).includes('original necessário à regeneração selecionada') && !(await text()).includes('Expiração global'));
    check('original protegido não exige consentimento contraditório', await page.locator('[data-ack="expiryAck"]').count() === 0);
    await page.locator('[data-ack="ack"]').check();
    check('original protegido dispensa autorização de expiração', await action('apply').isEnabled());
    await nav('Histórico e restauração'); await page.locator('[data-purge="h1"]').check();
    await nav('Executar'); await action('apply').click(); await wait('Aplicação concluída');
    check('regeneração usa o original protegido', (await text()).includes('1 aplicada') && (await text()).includes('Viagem Sintética') && !(await text()).includes('fonte alterada'));
    await nav('Histórico e restauração');
    check('original protegido não é removido pela retenção', !(await page.locator('[data-record="h1"]').innerText()).includes('Original removido'));
    check('seleção de remoção fica obsoleta após regeneração', await action('purgeReview').isDisabled() && (await action('purgeReview').innerText()).includes('(0)'));

    // Permanent removal during conversion has its own gate.
    await fresh();await page.locator('[data-draft="storage"][value="remover"]').check();await simulate80();await review();
    await page.locator('[data-ack="ack"]').check();
    check('aplicar sem originais requer segundo consentimento', await action('apply').isDisabled());
    await page.locator('[data-ack="permanent"]').check();
    check('consentimento permanente está vinculado à revisão', await action('apply').isEnabled());

    // Conflict/recovery keep the reviewed identity rather than substitute/clear it.
    await scenario('conflict');
    check('conflito bloqueia e preserva revisão', await action('apply').isDisabled() && await page.locator('.review-list [data-image-id]').count() === 2);
    await scenario('recovery');
    check('recuperação bloqueia novas aplicações', await action('apply').isDisabled() && (await text()).includes('Recuperação necessária'));
    await nav('Histórico e restauração');await action('recover').click();await page.locator('[data-confirm="restore"]').click();
    check('recuperação tem revisão, não limpeza de journal', (await text()).includes('Original restaurado no cenário'));
    await nav('Executar');
    check('restauração de recuperação invalida revisão preservada', (await text()).includes('Nenhuma seleção aguardando aplicação') && await action('apply').count() === 0);
    await nav('Simular');await action('editDraft').first().click();await action('analyse').click();
    check('restauração de recuperação aplica dimensão 900 × 900', (await page.locator('[data-image-id="img-c-capa"]').innerText()).includes('900 × 900'));

    await fresh();await scenario('waitNext');await simulate80();await review();await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aguardando metadata');
    check('espera preserva as identidades da própria aplicação', (await text()).includes('exec-002') && (await text()).includes('2 não iniciadas'));
    await action('cancel').click();await wait('Operação interrompida');
    check('cancelar a própria seleção não aplica nenhuma imagem', (await text()).includes('0 aplicadas') && await page.locator('.execution-result li').count() === 0);

    await fresh();await simulate80();await review();await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aplicando às imagens selecionadas');await action('cancel').click();
    check('cancelamento foca o andamento, não o botão desabilitado', await page.evaluate(() => document.activeElement.id === 'andamento'));
    await wait('Operação interrompida');
    check('cancelamento conclui só a transação atual e preserva o restante', (await text()).includes('1 aplicada') && (await text()).includes('1 não iniciada') && await page.locator('.execution-result li').count()===1);
    await nav('Simular');
    check('cancelamento consome a autorização da análise', (await text()).includes('desatualizada') && await action('review').isDisabled());

    await fresh();await simulate80();await review(['img-v-mini']);await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aplicação concluída');
    await nav('Simular');
    check('aplicação consome a autorização da análise', (await text()).includes('desatualizada') && await action('review').isDisabled() && await page.locator('[data-row]:checked').count() === 0);

    // Waiting, unknown totals, cancellation, and partial results are separate states.
    await fresh();await scenario('waiting');await wait('Aguardando metadata');
    check('espera não inventa progresso ou falha', await page.locator('progress').getAttribute('value') === null);
    await shot('08-desktop-espera');
    await axe('espera');
    await action('cancel').click();await wait('Operação interrompida');
    check('cancelar espera não inventa conversões', await page.locator('.execution-result li').count() === 0 && (await text()).includes('2 não iniciadas'));
    await scenario('partial');await wait('Concluída com pendências');
    check('sucesso parcial separa aplicadas/ignoradas/falhas', (await text()).includes('2 aplicadas') && (await text()).includes('1 ignorada') && (await text()).includes('1 falha'));
    await scenario('tasks');
    check('tarefas indisponíveis não viram ociosas', (await text()).includes('Dados de tarefas indisponíveis') && !(await text()).includes('domingo às'));

    // A changed authorized source is never replaced by a different image.
    await fresh();await scenario('changed');await nav('Simular');await simulate80();await review(['img-v-mini']);await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Concluída com pendências');
    check('fonte alterada é invalidada, nunca substituída', await page.locator('.execution-result li').count() === 1 && (await text()).includes('fonte alterada') && (await text()).includes('0 aplicadas'));

    // Empty, unavailable and no-image item must not look like the same success.
    await fresh();await scenario('empty');
    check('sem bibliotecas é vazio explícito', await action('analyse').isDisabled() && (await text()).includes('Nenhuma biblioteca disponível'));
    await scenario('unavailable');await action('analyse').click();
    check('falha de inspeção não vira zero de sucesso', (await text()).includes('Não foi possível analisar') && await action('review').isDisabled());
    check('anunciador de falha não diz simulação concluída', (await page.locator('#announcer').innerText()).includes('Não foi possível analisar') && !(await page.locator('#announcer').innerText()).includes('Simulação concluída'));
    await fresh();await page.locator('[data-draft="scope"]').selectOption('itens');await page.locator('[data-item="viagem"]').uncheck();await page.locator('[data-item="sem"]').check();await action('analyse').click();
    check('item sem imagens possui explicação própria', (await text()).includes('Nenhuma imagem dos tipos escolhidos'));

    // Shared backdrops stay protected through restoration.
    await fresh();
    await page.locator('#moreTypes').evaluate(n => { n.open = true; });
    await page.locator('[data-type="Backdrop"]').check();
    await simulate80();
    check('fundo compartilhado é protegido antes de restaurar', (await page.locator('[data-image-id="img-c-fundo"]').innerText()).includes('Referência compartilhada') && await page.locator('[data-row="img-c-fundo"]').count() === 0);
    await nav('Histórico e restauração');
    await page.locator('[data-record="h2"]').getByRole('button', { name: 'Restaurar original', exact: true }).click();
    await page.locator('[data-confirm="restore"]').click();
    await nav('Simular'); await action('editDraft').first().click(); await action('analyse').click();
    check('restauração preserva proteção de fundo compartilhado', (await page.locator('[data-image-id="img-c-fundo"]').innerText()).includes('Referência compartilhada') && await page.locator('[data-row="img-c-fundo"]').count() === 0);

    // Changing originals invalidates an open review but keeps the report consultable.
    await fresh(); await simulate80(); await review(['img-v-capa']);
    await nav('Histórico e restauração'); await page.locator('[data-purge="h1"]').check(); await action('purgeReview').click();
    await page.locator('#dialogAck').check(); await page.locator('[data-confirm="purge"]').click();
    await nav('Executar');
    check('purga com revisão aberta impede aplicação antiga', (await text()).includes('Nenhuma seleção aguardando aplicação') && await action('apply').count() === 0);
    await nav('Simular');
    check('relatório antigo permanece consultável após purga', (await text()).includes('desatualizada') && await page.locator('.result-list [data-image-id]').count() > 0 && await action('review').isDisabled());

    // A selected original that stops being eligible must not leave a dead purge button.
    await fresh(); await nav('Histórico e restauração'); await page.locator('[data-purge="h1"]').check();
    await page.locator('[data-record="h1"]').getByRole('button', { name: 'Restaurar original', exact: true }).click();
    await page.locator('[data-confirm="restore"]').click();
    check('seleção h1 obsoleta após restauração não deixa botão morto', await action('purgeReview').isDisabled() && (await action('purgeReview').innerText()).includes('(0)'));

    // Automation status derives from what was saved, not from unrelated area messages.
    await fresh(); await nav('Automação');
    await page.locator('[data-auto="mode"]').selectOption('apply');
    await action('saveAuto').click();
    check('salvar automação devolve foco ao título', await page.evaluate(() => document.activeElement.id === 'autoTitle'));
    await nav('Simular'); await page.locator('#manual-quality').fill('81');
    await nav('Automação');
    check('status da automação deriva do modo salvo após edição manual', (await page.locator('#autoStatus').innerText()).includes('aplicação permitida') && !(await page.locator('#autoStatus').innerText()).includes('somente simular'));
    await nav('Configurações'); await number('[data-cfg="quality"]', 86); await action('saveCfg').click();
    await nav('Automação');
    check('status da automação não é sobrescrito por Configurações', (await page.locator('#autoStatus').innerText()).includes('aplicação permitida') && !(await page.locator('#autoStatus').innerText()).includes('somente simular'));

    // Defaults confirmation keeps focus in the rendered draft.
    await fresh(); await action('defaults').click(); await page.locator('[data-confirm="defaults"]').click();
    check('usar padrões devolve foco ao título de Simular', await page.evaluate(() => document.activeElement.id === 'simTitle'));

    // Maintainer feedback: the exact item / cover-only journey, not a library shortcut.
    await fresh();
    check('páginas internas continuam direção inicial, abas só na bancada', await page.locator('body').getAttribute('data-shell')==='paginas' && (await page.locator('#shell option[value="paginas"]').textContent()).includes('recomendado'));
    await page.locator('[data-draft="scope"]').selectOption('itens');
    await page.locator('[data-type="Thumb"]').uncheck();
    const coverEditor=page.locator('[data-act="editType"][data-id="Primary"]');
    check('editor separa status de ação explícita com painel controlado', (await coverEditor.innerText()).includes('Editar ajustes deste tipo') && await coverEditor.getAttribute('aria-expanded')==='false' && await coverEditor.getAttribute('aria-controls')==='type-editor-manual-Primary' && !(await page.locator('#type-editor-manual-Primary').isVisible()) && (await coverEditor.locator('..').innerText()).includes('Usa os ajustes deste teste'));
    await coverEditor.focus();await page.keyboard.press('Enter');
    check('editor abre por teclado e evidencia a expansão', await coverEditor.getAttribute('aria-expanded')==='true' && await page.locator('#type-editor-manual-Primary').isVisible() && await page.locator('[data-custom="Primary"]').isVisible());
    await page.locator('[data-custom="Primary"]').check();
    await number('[data-policy="quality"][data-id="Primary"]',80);
    check('estado próprio do tipo não é confundido com ajustes do teste', (await coverEditor.locator('..').innerText()).includes('Ajustes próprios deste tipo') && await page.locator('#manual-quality').inputValue()==='85');
    check('resumo lateral distingue gerais 85 de capa com ajustes próprios 80', (await page.locator('.summary-panel').innerText()).includes('AJUSTES GERAIS DESTE TESTE') && (await page.locator('.summary-panel').innerText()).includes('Capa principal usa ajustes próprios:') && (await page.locator('.summary-panel').innerText()).includes('Qualidade 80'));
    await shot('17-desktop-editor-tipo');await axe('editor expandido');
    await action('analyse').click();
    check('Viagem só capa: exatamente uma proposta e nenhuma seleção', await page.locator('.result-list [data-image-id]').count()===1 && await page.locator('[data-row]:checked').count()===0 && await action('review').isDisabled());
    const narrative=await page.locator('.analysis-summary').innerText();
    check('narrativa anuncia simulação sem mutação, recriação e próximo passo', narrative.includes('Simulação concluída — nenhuma imagem foi alterada') && narrative.includes('capa principal de Viagem Sintética (2024) será recriada a partir do original guardado') && narrative.includes('Próximo passo:') && narrative.includes('selecione as imagens') && narrative.includes('não muda o disco'));
    check('narrativa explica preservação, restauração e ocupação sem IDs/SSIM', narrative.includes('originais serão preservados') && narrative.includes('Histórico e restauração') && narrative.includes('liberaria ~16 KiB') && !narrative.includes('img-v-') && !narrative.includes('SSIM'));
    await page.locator('#analysisSettings').evaluate(n=>{n.open=true;});
    check('detalhes congelados distinguem ajustes gerais dos efetivos por tipo', (await page.locator('#analysisSettings').innerText()).includes('Gerais deste teste: Qualidade 85') && (await page.locator('#analysisSettings').innerText()).includes('Capa principal · ajustes próprios: Qualidade 80'));
    await page.locator('#analysisSettings').evaluate(n=>{n.open=false;});
    await shot('18-desktop-viagem-so-capa');
    await review(['img-v-capa']);await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aplicação concluída');
    await action('goRestore').click();await wait('Restaurar imagens');
    check('conclusão abre diretamente restauração filtrada e exclui transação anterior', await page.locator('#historyFilter').inputValue()==='restorable' && await page.locator('[data-record="h1"]').count()===0 && await page.locator('[data-restore^="exec-"]').count()===1);
    await page.locator('[data-restore^="exec-"]').check();await action('restoreBatch').click();
    check('revisão da capa lista a transação ativa exata', await page.locator('.restore-review li').count()===1 && (await page.getByRole('dialog').innerText()).includes('Capa principal') && (await page.getByRole('dialog').innerText()).includes('1600 × 2400') && !(await page.getByRole('dialog').innerText()).includes('Fundo · posição 1'));
    await shot('19-desktop-restauracao-revisao');await axe('revisão restauração em lote');
    await page.locator('[data-confirm="restoreBatch"]').click();
    check('capa aplicada pode ser restaurada em lote de uma, com resultado e foco', await page.locator('[aria-label="Resultado da restauração"] li').count()===1 && (await page.locator('[aria-label="Resultado da restauração"]').innerText()).includes('Restaurado') && await page.evaluate(()=>document.activeElement.id==='restoreResult'));

    // Explicit subset/all, recovery exclusion, stale review and active-operation guards.
    await fresh();await simulate80();await review(['img-v-capa']);await nav('Histórico e restauração');
    check('restauração começa sem seleção e só ativos disponíveis são selecionáveis', await page.locator('[data-restore]').count()===2 && await page.locator('[data-restore]:checked').count()===0 && await action('restoreBatch').isDisabled() && await page.locator('[data-record="h3"] [data-restore],[data-record="h4"] [data-restore]').count()===0);
    await page.locator('[data-restore="h1"]').check();await action('restoreBatch').click();
    await page.keyboard.press('Escape');
    check('Escape da revisão em lote preserva seleção e dados', await page.locator('[data-restore="h1"]').isChecked() && (await page.locator('[data-record="h1"]').innerText()).includes('Original disponível'));
    await action('restoreBatch').click();await page.locator('[data-confirm="restoreBatch"]').click();
    check('subconjunto restaura h1 sem restaurar/remover h2 ou incerto h4', (await page.locator('[data-record="h1"]').innerText()).includes('Original restaurado') && (await page.locator('[data-record="h2"]').innerText()).includes('Original disponível') && (await page.locator('[data-record="h4"]').innerText()).includes('incerto') && await action('restoreBatch').isDisabled());
    await nav('Executar');check('lote de restauração invalida revisão manual aberta', await action('apply').count()===0);
    await nav('Simular');check('lote de restauração invalida análise e zera seleção sem apagar relatório', (await text()).includes('desatualizada') && await page.locator('.result-list [data-image-id]').count()===4 && await page.locator('[data-row]:checked').count()===0 && await action('review').isDisabled());

    await fresh();await nav('Histórico e restauração');await action('restoreAll').click();
    check('todos restauráveis só seleciona os dois elegíveis, sem executar', await page.locator('[data-restore]:checked').count()===2 && (await page.locator('[data-record="h1"]').innerText()).includes('Original disponível') && (await page.locator('#announcer').innerText()).includes('Nada foi restaurado'));
    await action('restoreBatch').click();check('todos revisa lista exata, não inclui removidos/incertos', await page.locator('.restore-review li').count()===2 && !(await page.getByRole('dialog').innerText()).includes('900 × 900') && !(await page.getByRole('dialog').innerText()).includes('Miniatura'));
    await page.locator('[data-confirm="restoreBatch"]').click();
    check('todos produz resultado individual e desativa seleções obsoletas', await page.locator('[aria-label="Resultado da restauração"] li').count()===2 && await page.locator('[data-restore]').count()===0 && await action('restoreBatch').isDisabled() && await action('purgeReview').isDisabled());
    await shot('20-desktop-restauracao-lote-resultado');
    await nav('Simular');await page.locator('#moreTypes').evaluate(n=>{n.open=true;});await page.locator('[data-type="Backdrop"]').check();await simulate80();
    check('restauração em lote mantém proteção shared e dimensões do original', (await page.locator('[data-image-id="img-c-fundo"]').innerText()).includes('Referência compartilhada') && (await page.locator('[data-image-id="img-c-fundo"]').innerText()).includes('3840 × 2160') && await page.locator('[data-row="img-c-fundo"]').count()===0);

    await fresh();await nav('Histórico e restauração');await action('restoreAll').click();await action('restoreBatch').click();
    // Controlled fixture race: not a real endpoint or a claim about backend concurrency.
    await page.evaluate(()=>{S.history.find(h=>h.id==='h1').active=false;});
    await page.locator('[data-confirm="restoreBatch"]').click();
    check('revalidação em lote relata inativo individualmente e restaura só o válido', await page.locator('[aria-label="Resultado da restauração"] li').count()===2 && (await page.locator('[aria-label="Resultado da restauração"]').innerText()).includes('Não restaurado') && !(await page.locator('[data-record="h1"]').innerText()).includes('Original restaurado') && (await page.locator('[data-record="h2"]').innerText()).includes('Original restaurado'));
    await fresh();await nav('Histórico e restauração');await action('restoreAll').click();await action('restoreBatch').click();
    await page.evaluate(()=>{S.history.find(h=>h.id==='h1').status='removed';S.history.find(h=>h.id==='h2').status='uncertain';});
    await page.locator('[data-confirm="restoreBatch"]').click();
    check('removido/incerto após revisão não são restaurados nem substituídos', await page.locator('[aria-label="Resultado da restauração"] li strong').allTextContents().then(xs=>xs.length===2&&xs.every(t=>t.startsWith('Não restaurado'))) && await action('restoreBatch').isDisabled());
    await fresh();await scenario('recovery');await nav('Histórico e restauração');await action('restoreAll').click();await action('restoreBatch').click();
    check('lote não usa original incerto da recuperação especial', !(await page.getByRole('dialog').innerText()).includes('900 × 900'));
    await page.keyboard.press('Escape');await action('recover').click();await page.locator('[data-confirm="restore"]').click();
    check('recuperação individual permanece disponível separadamente do lote', (await page.locator('[data-record="h4"]').innerText()).includes('Original restaurado') && await action('recover').count()===0);
    await fresh();await scenario('waitNext');await simulate80();await review();await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aguardando metadata');await nav('Histórico e restauração');
    check('execução ativa bloqueia seleção/restauração/remoção em lote e individual', await action('restoreAll').isDisabled() && await action('restoreBatch').isDisabled() && await action('purgeAll').isDisabled() && await page.locator('[data-restore]:enabled,[data-purge]:enabled,[data-act="restore"]:enabled').count()===0);

    await fresh();await nav('Histórico e restauração');await page.locator('[data-restore="h1"]').check();await page.locator('[data-purge="h1"]').check();await action('purgeReview').click();await page.locator('#dialogAck').check();await page.locator('[data-confirm="purge"]').click();
    check('purga invalida seleção antiga de restauração sem deixar revisão morta', await action('restoreBatch').isDisabled() && (await action('restoreBatch').innerText()).includes('(0)') && await page.locator('[data-restore="h1"]').count()===0);
    await fresh();await simulate80();await review(['img-v-capa']);await nav('Histórico e restauração');await page.locator('[data-restore="h1"]').check();await nav('Executar');await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aplicação concluída');await nav('Histórico e restauração');
    check('nova aplicação invalida seleção da transação anterior para restaurar', await page.locator('[data-restore]:checked').count()===0 && await page.locator('[data-record="h1"] [data-restore]').count()===0 && await action('restoreBatch').isDisabled());

    await fresh();await page.locator('[data-draft="scope"]').selectOption('itens');await page.locator('[data-item="viagem"]').uncheck();await page.locator('[data-item="serie"]').check();await page.locator('[data-type="Thumb"]').uncheck();await page.locator('[data-draft="storage"][value="remover"]').check();await simulate80();
    check('narrativa de primeira conversão distingue converter de recriar e perda de restauração', (await page.locator('.analysis-summary').innerText()).includes('capa principal de Série Demonstração (2023) será convertida para WebP') && (await page.locator('.analysis-summary').innerText()).includes('não poderá restaurar nem recriar') && !(await page.locator('.analysis-summary').innerText()).includes('será recriada'));

    // Global copy is explicit and never saves/overwrites independent automation.
    await fresh();await simulate80();await action('editDraft').click();
    check('ações secundárias do resumo usam o mesmo alinhamento à esquerda', await page.locator('.summary-actions button').evaluateAll(ns=>ns.length===2&&ns.every(n=>getComputedStyle(n).textAlign==='left')&&Math.abs(ns[0].getBoundingClientRect().x-ns[1].getBoundingClientRect().x)<1));
    check('origem dos padrões e efeitos da cópia são explícitos', (await page.locator('.summary-panel').innerText()).includes('Definidos em Configurações; copia para este teste, não salva nem altera automação'));
    await nav('Automação');await number('[data-auto="quality"]',77);await action('saveAuto').click();
    await nav('Configurações');await number('[data-cfg="quality"]',91);await action('saveCfg').click();
    await nav('Simular');await page.locator('[data-draft="storage"][value="remover"]').check();await action('defaults').click();
    check('diálogo de padrões distingue Configurações, teste e automação', (await page.getByRole('dialog').innerText()).includes('Não salva nem altera automação'));
    await page.locator('[data-confirm="defaults"]').click();
    check('cópia global altera codificação, mas mantém alcance/tipos/preservação', await page.locator('#manual-quality').inputValue()==='91' && await page.locator('[data-draft="storage"][value="remover"]').isChecked() && await page.locator('[data-lib="filmes"]').isChecked() && await page.locator('[data-type="Thumb"]').isChecked());
    await nav('Automação');check('cópia global não altera automação salva', await page.locator('[data-auto="quality"]').inputValue()==='77');

    // Explicit multiple/all scope persists the set, never treating empty as all.
    await fresh();await nav('Automação');
    check('automação explica tarefas distintas, modo e disparo externo sem link falso', (await text()).includes('Otimizar novas imagens') && (await text()).includes('Atualizar conversões quando os ajustes mudam') && (await text()).includes('Salvar ajustes não dispara o trabalho') && (await text()).includes('configurados no Jellyfin e estão indisponíveis neste protótipo') && await page.locator('main a').count()===0);
    await page.locator('[data-auto-lib="filmes"]').uncheck();
    check('automação vazia bloqueia salvar, sem escopo implícito', await action('saveAuto').isDisabled() && (await text()).includes('Seleção vazia não significa tudo'));
    await page.locator('[data-auto-lib="filmes"]').check();await page.locator('[data-auto-lib="series"]').check();
    check('editar múltiplas bibliotecas não muda conjunto salvo antes de salvar', (await page.locator('#autoSavedScope').innerText())==='Filmes');
    await action('saveAuto').click();check('salvar múltiplas bibliotecas mantém conjunto correto', (await page.locator('#autoSavedScope').innerText())==='Filmes + Séries');
    await shot('21-desktop-automacao-multiplas');
    await page.locator('[data-auto-lib="filmes"]').uncheck();await nav('Simular');await nav('Automação');
    check('draft de automação e salvo são separados mesmo ao navegar', !(await page.locator('[data-auto-lib="filmes"]').isChecked()) && (await page.locator('#autoSavedScope').innerText())==='Filmes + Séries');
    await page.locator('[data-auto="scope"]').selectOption('tudo');await action('saveAuto').click();
    check('todas é opção explícita e resumo salvo correto', (await page.locator('#autoSavedScope').innerText())==='Todas as bibliotecas e metadata global');
    await page.locator('[data-auto="scope"]').selectOption('bibliotecas');await page.locator('[data-auto-lib="filmes"]').check();await action('saveAuto').click();await scenario('removedLibrary');
    check('biblioteca removida não é substituída nem vira tudo', (await text()).includes('Biblioteca salva indisponível') && (await page.locator('#autoSavedScope').innerText())==='Filmes + Séries' && await action('saveAuto').isDisabled());
    await page.locator('[data-auto-lib="series"]').uncheck();await action('saveAuto').click();
    check('remover explicitamente biblioteca indisponível permite salvar só Filmes', (await page.locator('#autoSavedScope').innerText())==='Filmes');

    // Storage meaning and the strict completion-age boundary, not a midnight job.
    await fresh();await nav('Configurações');
    check('armazenamento explica mínimo livre, limite total e ausência de mutação em simulação', (await text()).includes('Espaço livre mínimo a preservar') && (await text()).includes('Limite total de originais guardados') && (await text()).includes('não um gasto de processamento por execução') && (await text()).includes('Simular não muda o disco'));
    check('retenção explica origem da idade, momento e consequência sem ocultar essencial', (await text()).includes('desde a conclusão da conversão') && (await text()).includes('no início de um lote de aplicação') && (await text()).includes('nunca mídia, vídeos ou o WebP ativo') && (await text()).includes('irreversível') && (await text()).includes('meia-noite'));
    await shot('22-desktop-armazenamento');
    await number('[data-cfg="retention"]',40);await action('saveCfg').click();await nav('Simular');await simulate80();await review(['img-v-mini']);
    check('idade exatamente igual à retenção ainda não expira na fixture', await page.locator('[data-ack="expiryAck"]').count()===0);
    await nav('Configurações');await number('[data-cfg="retention"]',39);await action('saveCfg').click();await nav('Simular');await action('editDraft').first().click();await action('analyse').click();await review(['img-v-mini']);
    check('idade superior à retenção prevê expiração no início e não afeta WebP ativo', (await text()).includes('no início deste lote') && (await text()).includes('39 dias desde a conclusão da conversão') && (await text()).includes('nunca mídia, vídeos ou WebP ativo') && await page.locator('[data-ack="expiryAck"]').count()===1);
    await nav('Automação');check('automação mostra retenção global e perda irreversível antes de salvar', (await text()).includes('Retenção de originais: 39 dias') && (await text()).includes('não é um gasto permitido por execução') && (await text()).includes('impede restauração e recriação'));

    // Narrow pages (the recommended structure): editor, narrative and batch confirmation.
    await fresh();await page.setViewportSize({width:360,height:640});
    await page.locator('[data-draft="scope"]').selectOption('itens');await page.locator('[data-type="Thumb"]').uncheck();await coverEditor.click();
    await noOverflow('360×640 páginas: editor expandido sem overflow');await shot('23-mobile-editor-tipo');
    await simulate80();await noOverflow('360×640 páginas: narrativa só capa sem overflow');await shot('24-mobile-viagem-so-capa');
    await nav('Histórico e restauração');await action('restoreAll').click();await action('restoreBatch').click();
    await noOverflow('360×640 páginas: confirmação de restauração em lote sem overflow');
    check('lote móvel mantém confirmação e foco dentro do modal', await page.getByRole('dialog').evaluate(n=>n.contains(document.activeElement)&&n.getBoundingClientRect().right<=innerWidth));
    check('diálogo abre no título sem rolar sua introdução para fora', await page.getByRole('dialog').evaluate(n=>n.scrollTop===0&&document.activeElement.id==='dialogTitle'));
    check('ação de confirmar lote permanece visível na janela móvel', await page.locator('[data-confirm="restoreBatch"]').evaluate(n=>{const a=n.getBoundingClientRect(),b=n.closest('dialog').getBoundingClientRect();return a.top>=b.top&&a.bottom<=b.bottom&&a.bottom<=innerHeight;}));
    await shot('25-mobile-restauracao-lote');await axe('restauração em lote mobile');await page.keyboard.press('Escape');
    await page.setViewportSize({width:1440,height:1000});

    // Both D1 variants preserve draft and browser history. Tabs need real focus behavior.
    await fresh();await number('#manual-quality',80);await bench();await page.locator('#shell').selectOption('abas');
    await page.getByRole('tab', {name:'Simular',exact:true}).waitFor();
    check('abas: sem sidebar, sem URL fictícia do host', await page.locator('aside.sidebar').count() === 0 && !(await text()).includes('configurationpage'));
    await page.getByRole('tab',{name:'Simular',exact:true}).focus();await page.keyboard.press('ArrowRight');await wait('Nenhuma seleção aguardando');
    check('seta move seleção E foco para Executar', await page.getByRole('tab',{name:'Executar',exact:true}).evaluate(n=>n===document.activeElement&&n.getAttribute('aria-selected')==='true'));
    await page.keyboard.press('End');await page.getByRole('heading',{name:'Configurações',exact:true,level:1}).waitFor();
    check('End chega à última aba', await page.getByRole('tab',{name:'Configurações',exact:true}).evaluate(n=>n===document.activeElement));
    await page.goBack();await page.getByRole('heading',{name:'Executar',exact:true,level:1}).waitFor();
    check('voltar nativo retorna à área anterior', true);
    await page.goForward();await page.getByRole('heading',{name:'Configurações',exact:true,level:1}).waitFor();
    check('avançar nativo retorna à área seguinte', true);
    await nav('Simular');check('rascunho preservado entre áreas e estrutura', await page.locator('#manual-quality').inputValue()==='80');
    await page.locator('#bench').evaluate(n=>{n.open=false;});
    await shot('09-desktop-abas');

    // Actual mobile flow (not just 5 elements with non-zero width).
    await page.setViewportSize({width:360,height:800});
    await noOverflow('360 px: seleção sem overflow');await shot('10-mobile-selecao');
    await axe('seleção mobile e abas');
    for(const name of ['Executar','Automação','Histórico e restauração','Configurações','Simular']){await nav(name);await noOverflow('360 px: '+name);}
    await action('analyse').click();await noOverflow('360 px: resultado sem overflow');
    await shot('11-mobile-simulacao');
    await axe('resultado mobile');
    await review();await page.locator('[data-ack="ack"]').check();await noOverflow('360 px: revisão sem overflow');await shot('12-mobile-revisao');
    await action('apply').click();await wait('Aplicação concluída');await noOverflow('360 px: conclusão sem overflow');
    await nav('Histórico e restauração');await noOverflow('360 px: histórico atualizado sem overflow');await shot('13-mobile-historico');
    await axe('histórico mobile');
    await action('purgeAll').click();await action('purgeReview').click();await noOverflow('360 px: diálogo sem overflow');
    check('diálogo cabe e prende foco no modal nativo', await page.getByRole('dialog').evaluate(n=>{const r=n.getBoundingClientRect();return r.x>=0&&r.right<=innerWidth&&n.contains(document.activeElement);}));
    await shot('14-mobile-remocao');await page.keyboard.press('Escape');
    await page.setViewportSize({width:768,height:1024});await nav('Simular');await noOverflow('768 px: layout intermediário sem overflow');await shot('15-tablet');
    await page.setViewportSize({width:360,height:640});await bench();await page.locator('#shell').selectOption('paginas');await page.locator('#bench').evaluate(n=>{n.open=false;});
    check('páginas mobile expõe as cinco áreas no seletor', await page.locator('#areaMenu option').count()===5 && await page.locator('#areaMenu').isVisible());
    for(const name of ['Executar','Automação','Histórico e restauração','Configurações','Simular']){await nav(name);await noOverflow('360×640 páginas: '+name);}
    await shot('16-mobile-paginas');await axe('páginas mobile');
    await page.setViewportSize({width:1280,height:900});await nav('Configurações');await noOverflow('1280 px: configurações sem overflow');
    check('redução de movimento é respeitada', await page.evaluate(()=>matchMedia('(prefers-reduced-motion: reduce)').matches));
  } catch(e) {
    check('exceção: '+e.stack, false);
    if(shots)await shot('failure');
  } finally {
    check('nenhum erro de página/console', errors.length===0);
    check('zero requisições HTTP/API/CDN', remote.length===0);
    if(errors.length)console.error(errors);
    if(remote.length)console.error(remote);
    const failures=results.filter(r=>!r.ok).length;
    console.log(`RESULTADO: ${results.length} verificações, ${failures} falhas.`);
    await browser.close();process.exitCode=failures?1:0;
  }
})().catch(e=>{console.error(e);process.exitCode=1;});
