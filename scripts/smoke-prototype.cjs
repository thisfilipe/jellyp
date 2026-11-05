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
  const purgeArea = async () => { if(!page.url().endsWith('/historico/remover-originais')){await nav('Histórico e restauração');await page.locator('.local-nav .danger-link').click();}await page.locator('#purgeTitle').waitFor(); };
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
    await action('goRestore').click();
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
    await purgeArea();await page.locator('[data-purge="h1"]').check();
    await action('purgeReview').click();
    check('remoção lista só um original e os bytes exatos', (await page.getByRole('dialog').innerText()).includes('486 KiB') && await page.locator('dialog li').count() === 1);
    check('diálogo de remoção usa singular', (await page.getByRole('dialog').getByRole('heading').innerText()).includes('1 original permanentemente'));
    check('remoção permanente exige autorização própria', await page.locator('[data-confirm="purge"]').isDisabled());
    await page.locator('#dialogAck').check();
    await shot('06-desktop-remocao');
    await axe('diálogo de remoção');
    await page.locator('[data-confirm="purge"]').click();
    check('remove só h1; mantém h2 e incerto h4', await page.locator('[data-purge="h1"]').count() === 0 && await page.locator('[data-purge="h2"]').count() === 1 && (await page.locator('[data-record="h4"]').innerText()).includes('incerto'));
    check('foco após remoção vai ao resultado da área própria', await page.evaluate(() => document.activeElement.id === 'purgeResult'));
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
    await purgeArea(); await page.locator('[data-purge="h1"]').check();
    await nav('Executar'); await action('apply').click(); await wait('Aplicação concluída');
    check('expiração de um original registra singular', (await text()).includes('1 original removido') && !(await text()).includes('1 originais removidos'));
    await nav('Histórico e restauração');
    const expiredOriginal=(await page.locator('[data-record="h1"]').innerText()).includes('Original removido');await purgeArea();
    check('seleção de remoção fica obsoleta após expiração', await action('purgeReview').isDisabled() && (await action('purgeReview').innerText()).includes('(0)') && expiredOriginal);

    // Retention must keep originals required by the selected regeneration.
    await fresh();await nav('Configurações');
    await number('[data-cfg="retention"]', 30);await action('saveCfg').click();
    await nav('Simular');await simulate80();await review(['img-v-capa']);
    check('retenção protege original necessário à regeneração', (await text()).includes('Proteção desta operação') && (await text()).includes('original necessário à regeneração selecionada') && !(await text()).includes('Expiração global'));
    check('original protegido não exige consentimento contraditório', await page.locator('[data-ack="expiryAck"]').count() === 0);
    await page.locator('[data-ack="ack"]').check();
    check('original protegido dispensa autorização de expiração', await action('apply').isEnabled());
    await purgeArea(); await page.locator('[data-purge="h1"]').check();
    await nav('Executar'); await action('apply').click(); await wait('Aplicação concluída');
    check('regeneração usa o original protegido', (await text()).includes('1 aplicada') && (await text()).includes('Viagem Sintética') && !(await text()).includes('fonte alterada'));
    await nav('Histórico e restauração');
    check('original protegido não é removido pela retenção', !(await page.locator('[data-record="h1"]').innerText()).includes('Original removido'));
    await purgeArea();check('seleção de remoção fica obsoleta após regeneração', await action('purgeReview').isDisabled() && (await action('purgeReview').innerText()).includes('(0)'));

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
    await purgeArea(); await page.locator('[data-purge="h1"]').check(); await action('purgeReview').click();
    await page.locator('#dialogAck').check(); await page.locator('[data-confirm="purge"]').click();
    await nav('Executar');
    check('purga com revisão aberta impede aplicação antiga', (await text()).includes('Nenhuma seleção aguardando aplicação') && await action('apply').count() === 0);
    await nav('Simular');
    check('relatório antigo permanece consultável após purga', (await text()).includes('desatualizada') && await page.locator('.result-list [data-image-id]').count() > 0 && await action('review').isDisabled());

    // A selected original that stops being eligible must not leave a dead purge button.
    await fresh(); await purgeArea(); await page.locator('[data-purge="h1"]').check();await nav('Histórico e restauração');
    await page.locator('[data-record="h1"]').getByRole('button', { name: 'Restaurar original', exact: true }).click();
    await page.locator('[data-confirm="restore"]').click();
    await purgeArea();check('seleção h1 obsoleta após restauração não deixa botão morto', await action('purgeReview').isDisabled() && (await action('purgeReview').innerText()).includes('(0)'));

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
    check('status é o único expansor com painel controlado, sem label duplicado', (await coverEditor.innerText()).includes('Usando ajustes deste teste') && await coverEditor.getAttribute('aria-expanded')==='false' && await coverEditor.getAttribute('aria-controls')==='type-editor-manual-Primary' && !(await page.locator('#type-editor-manual-Primary').isVisible()) && await page.locator('.type-origin').count()===0 && !(await text()).includes('Editar ajustes deste tipo'));
    await coverEditor.focus();await page.keyboard.press('Enter');
    check('editor abre por teclado e evidencia a expansão', await coverEditor.getAttribute('aria-expanded')==='true' && await page.locator('#type-editor-manual-Primary').isVisible() && await page.locator('[data-custom="Primary"]').isVisible());
    await page.locator('[data-custom="Primary"]').check();
    await number('[data-policy="quality"][data-id="Primary"]',80);
    check('estado próprio do tipo não é confundido com ajustes do teste', (await coverEditor.innerText()).includes('Usando ajustes próprios') && await page.locator('#manual-quality').inputValue()==='85');
    check('resumo lateral distingue gerais 85 de capa com ajustes próprios 80', (await page.locator('.summary-panel').innerText()).includes('AJUSTES GERAIS DESTE TESTE') && (await page.locator('.summary-panel').innerText()).includes('Capa principal usa ajustes próprios:') && (await page.locator('.summary-panel').innerText()).includes('Qualidade 80'));
    await shot('17-desktop-editor-tipo');await axe('editor expandido');
    await action('analyse').click();
    check('Viagem só capa: exatamente uma proposta e nenhuma seleção', await page.locator('.result-list [data-image-id]').count()===1 && await page.locator('[data-row]:checked').count()===0 && await action('review').isDisabled());
    const narrative=await page.locator('.analysis-summary').innerText();
    check('narrativa anuncia simulação sem mutação, recriação e próximo passo', narrative.includes('Simulação concluída — nenhuma imagem foi alterada') && narrative.includes('capa principal de Viagem Sintética (2024) será recriada a partir do original guardado') && narrative.includes('Próximo passo:') && narrative.includes('selecione as imagens') && narrative.includes('não muda o disco'));
    check('narrativa explica preservação/restauração e delega saldo à seleção sem IDs/SSIM', narrative.includes('originais serão preservados') && narrative.includes('Histórico e restauração') && narrative.includes('saldo abaixo acompanha somente as imagens que você selecionar') && !narrative.includes('img-v-') && !narrative.includes('SSIM'));
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
    const allRestored=await page.locator('[aria-label="Resultado da restauração"] li').count()===2 && await page.locator('[data-restore]').count()===0 && await action('restoreBatch').isDisabled();await purgeArea();
    check('todos produz resultado individual e desativa seleções obsoletas', allRestored && await action('purgeReview').isDisabled());await nav('Histórico e restauração');
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
    const restoreBlocked=await action('restoreAll').isDisabled() && await action('restoreBatch').isDisabled() && await page.locator('[data-restore]:enabled,[data-act="restore"]:enabled').count()===0;await purgeArea();
    check('execução ativa bloqueia seleção/restauração/remoção em lote e individual', restoreBlocked && await action('purgeAll').isDisabled() && await page.locator('[data-purge]:enabled').count()===0);

    await fresh();await nav('Histórico e restauração');await page.locator('[data-restore="h1"]').check();await purgeArea();await page.locator('[data-purge="h1"]').check();await action('purgeReview').click();await page.locator('#dialogAck').check();await page.locator('[data-confirm="purge"]').click();await nav('Histórico e restauração');
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
    check('retenção explica origem da idade, momento e consequência sem ocultar essencial', (await text()).includes('desde a conclusão da conversão ou regeneração (CompletedUtc)') && (await text()).includes('no início de uma otimização aplicada') && (await text()).includes('nunca mídia, vídeos ou o WebP ativo') && (await text()).includes('irreversível') && (await text()).includes('meia-noite'));
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
    check('barra de seleção móvel não encobre a narrativa inicial', await page.evaluate(()=>{const a=document.querySelector('.analysis-summary').getBoundingClientRect(),b=document.querySelector('.selection-bar').getBoundingClientRect();return b.top>=a.bottom;}));
    await nav('Histórico e restauração');await action('restoreAll').click();await action('restoreBatch').click();
    await noOverflow('360×640 páginas: confirmação de restauração em lote sem overflow');
    check('lote móvel mantém confirmação e foco dentro do modal', await page.getByRole('dialog').evaluate(n=>n.contains(document.activeElement)&&n.getBoundingClientRect().right<=innerWidth));
    check('diálogo abre no título sem rolar sua introdução para fora', await page.getByRole('dialog').evaluate(n=>n.scrollTop===0&&document.activeElement.id==='dialogTitle'));
    check('ação de confirmar lote permanece visível na janela móvel', await page.locator('[data-confirm="restoreBatch"]').evaluate(n=>{const a=n.getBoundingClientRect(),b=n.closest('dialog').getBoundingClientRect();return a.top>=b.top&&a.bottom<=b.bottom&&a.bottom<=innerHeight;}));
    await shot('25-mobile-restauracao-lote');await axe('restauração em lote mobile');await page.keyboard.press('Escape');
    await page.setViewportSize({width:1440,height:1000});

    // Round 2: stage navigation is real history, not an authorization or a reset.
    await fresh();
    const stage = i => page.locator('[data-act="step"][data-value="'+i+'"]');
    check('etapas futuras bloqueadas sem snapshot ou seleção', await stage(1).isDisabled() && await stage(2).isDisabled());
    await simulate80();await page.locator('[data-row="img-v-mini"]').check();
    const analysisId=await page.evaluate(()=>S.analysis.id);
    await action('backSelection').focus();await page.keyboard.press('Enter');
    check('Voltar explícito por teclado foca seleção e preserva snapshot/subset', await page.evaluate(id=>document.activeElement.id==='simTitle'&&S.analysis.id===id&&!S.analysis.stale&&S.selection.join()==='img-v-mini',analysisId));
    await stage(1).focus();await page.keyboard.press('Space');
    check('expansão de etapa por Space retorna resultado com foco e checkbox', await page.evaluate(()=>document.activeElement.id==='resultadoAnalise') && await page.locator('[data-row="img-v-mini"]').isChecked());
    await page.goBack();await page.locator('#manual-quality').waitFor();
    check('history voltar entre etapas preserva valores e foco', await page.locator('#manual-quality').inputValue()==='80' && await page.evaluate(()=>document.activeElement.id==='simTitle'));
    await page.goForward();await page.locator('#resultadoAnalise').waitFor();
    check('history avançar não refaz análise nem perde subset', await page.evaluate(id=>S.analysis.id===id&&document.activeElement.id==='resultadoAnalise',analysisId) && await page.locator('[data-row="img-v-mini"]').isChecked());
    await stage(2).click();await wait('Revisão final');await page.locator('[data-ack="ack"]').check();
    await action('backResults').click();
    check('revisão Voltar mantém subset sem apagar relatório', await page.locator('[data-row="img-v-mini"]').isChecked() && await page.evaluate(id=>S.analysis.id===id&&document.activeElement.id==='resultadoAnalise',analysisId));
    await page.locator('[data-row="img-v-mini"]').uncheck();
    check('alterar subset revoga revisão anterior e bloqueia próxima etapa vazia', await stage(2).isDisabled() && await page.evaluate(()=>S.review===null));
    await action('backSelection').click();await number('#manual-quality',79);
    await stage(1).click();
    check('navegar não ressuscita snapshot invalidado por edição', (await text()).includes('desatualizada') && await stage(2).isDisabled() && await page.locator('[data-row]:checked').count()===0);

    // Settings detour: navigation is safe; explicit saving revokes old authority.
    await fresh();await simulate80();await review(['img-v-mini']);await page.locator('[data-ack="ack"]').check();
    check('regras essenciais visíveis na revisão, nunca escondidas em details', await page.locator('.storage-rules').isVisible() && await page.locator('.storage-rules').evaluate(n=>!n.closest('details')) && (await text()).includes('Salvar mudanças invalida esta análise') && (await text()).includes('Limite total de originais guardados') && (await text()).includes('Sem expiração automática'));
    await action('storageSettings').click();await page.locator('#storageSettingsTitle').waitFor();
    check('Alterar em Configurações navega seção específica com foco', page.url().endsWith('/configuracoes/armazenamento') && await page.evaluate(()=>document.activeElement.id==='storageSettingsTitle'));
    await shot('26-desktop-config-ida');
    await action('returnReview').first().click();await wait('Revisão final');
    check('ida/volta sem editar mantém subset, análise e consentimento', await action('apply').isEnabled() && await page.locator('.review-list [data-image-id="img-v-mini"]').count()===1 && await page.evaluate(()=>!S.analysis.stale&&document.activeElement.id==='execTitle'));
    await action('storageSettings').click();await page.locator('#storageSettingsTitle').waitFor();await number('[data-cfg="reserve"]',3);
    await action('returnReview').first().click();await wait('Revisão final');
    check('editar global sem salvar também preserva revisão e valor salvo', await action('apply').isEnabled() && await page.evaluate(()=>S.defaults.reserve===2&&S.cfg.reserve===3));
    await action('storageSettings').click();await page.locator('#storageSettingsTitle').waitFor();await action('saveCfg').click();
    check('salvar global revoga seleção/autorização e avisa reanálise', await page.evaluate(()=>S.analysis.stale&&S.review===null&&S.selection.length===0&&S.defaults.reserve===3) && (await text()).includes('Voltar não recupera a seleção'));
    await action('returnReview').first().click();await wait('Nenhuma seleção aguardando aplicação');
    check('retorno após salvar nunca ressuscita aplicação antiga', await action('apply').count()===0);
    await page.goBack();await page.locator('#storageSettingsTitle').waitFor();await page.goForward();await wait('Nenhuma seleção aguardando aplicação');
    check('history após salvar continua sem autoridade obsoleta', await action('apply').count()===0 && await page.evaluate(()=>S.selection.length===0&&S.review===null));
    await nav('Simular');await action('editDraft').first().click();await action('analyse').click();
    check('reanálise após salvar exige seleção explícita nova', await action('review').isDisabled() && await page.locator('[data-row]:checked').count()===0 && await page.evaluate(()=>!S.analysis.stale));

    // Exact byte accounting: active artwork plus the one retained original.
    const mathCases=[
      ['conversão guardar', ['convert'], 'guardar', [486,611,-125,486,0,125,486]],
      ['conversão remover', ['convert'], 'remover', [486,125,361,486,0,125,0]],
      ['regeneração guardar', ['regenerate'], 'guardar', [627,611,16,141,486,125,486]],
      ['regeneração remover', ['regenerate'], 'remover', [627,125,502,141,486,125,0]],
      ['mistura guardar', ['convert','regenerate'], 'guardar', [1113,1222,-109,627,486,250,972]],
      ['mistura remover', ['convert','regenerate'], 'remover', [1113,250,863,627,486,250,0]],
      ['zero guardar', [], 'guardar', [0,0,0,0,0,0,0]],
      ['zero remover', [], 'remover', [0,0,0,0,0,0,0]],
      ['não proposta ignorada', ['keep'], 'guardar', [0,0,0,0,0,0,0]]
    ];
    for(const [name,actions,storage,expected] of mathCases){const actual=await page.evaluate(({actions,storage})=>{const rows=actions.map(action=>({action,proposed:action!=='keep',source:action==='regenerate'?141:486,output:125,original:486}));const t=totals(rows,storage);return [t.before,t.after,t.net,t.activeBefore,t.storedBefore,t.activeAfter,t.storedAfter];},{actions,storage});check('conta exata '+name, JSON.stringify(actual)===JSON.stringify(expected));}

    await fresh();await simulate80();
    check('saldo principal vazio não fatura propostas não selecionadas', (await page.locator('.storage-bill').innerText()).includes('Sem alterações selecionadas') && await page.locator('.bill-comparison strong').allTextContents().then(xs=>xs.join('|')==='0 KiB|0 KiB') && (await page.locator('.selection-balance').innerText()).includes('Sem alteração'));
    await page.locator('[data-row="img-v-capa"]').check();
    check('saldo principal acompanha a seleção, não todas as propostas', (await page.locator('.selection-balance').innerText()).includes('Liberaria 16 KiB') && (await page.locator('.storage-bill .bill-balance').innerText()).includes('−16 KiB'));
    await action('review').click();await wait('Revisão final');
    check('fatura regeneração mantém original sem cobrar duas vezes', (await page.locator('.storage-bill').innerText()).includes('627 KiB') && (await page.locator('.storage-bill').innerText()).includes('611 KiB') && (await page.locator('.storage-bill .bill-balance').innerText()).includes('−16 KiB') && (await page.locator('.storage-bill').innerText()).includes('Somente as selecionadas'));
    await shot('27-desktop-fatura-regeneracao');
    await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aplicação concluída');
    check('conclusão saldo só feitas usa passado e não promete originais a guardar', (await page.locator('.storage-bill').innerText()).includes('Somente as imagens aplicadas') && (await page.locator('.bill-balance').innerText()).includes('Liberou 16 KiB') && !(await page.locator('.storage-bill').innerText()).includes('a guardar') && !(await page.locator('.storage-bill').innerText()).includes('Liberaria'));
    check('conclusão tem único atalho contextual restauração fora do bloco principal', await action('goRestore').count()===1 && await page.locator('.restore-entry [data-act="goRestore"]').count()===1 && !(await text()).includes('Ver histórico e restaurar') && await action('newRun').count()===1);
    await shot('28-desktop-conclusao-saldo');
    const completedId=await page.evaluate(()=>S.exec.id);
    await page.goBack();await page.locator('#resultadoAnalise').waitFor();
    check('voltar após aplicar mantém relatório consumido e execução concluída', await action('review').isDisabled() && await page.evaluate(id=>S.exec.id===id&&S.exec.phase==='done'&&S.review===null&&S.selection.length===0,completedId));
    await stage(0).click();await stage(1).click();await nav('Executar');await wait('Aplicação concluída');
    check('stepper não apaga conclusão nem reautoriza snapshot consumido', await action('newRun').isVisible() && await page.evaluate(id=>S.exec.id===id&&S.analysis.stale,completedId));
    await nav('Configurações');await number('[data-cfg="reserve"]',3);await action('saveCfg').click();await nav('Executar');
    check('salvar limites não apaga o relatório de operação concluída', await page.evaluate(id=>S.exec.id===id&&S.exec.phase==='done',completedId) && (await text()).includes('Liberou 16 KiB'));
    await action('newRun').click();
    check('Nova simulação inicia intenção nova mas conserva operação anterior', await page.locator('#manual-quality').isVisible() && await page.evaluate(id=>S.analysis===null&&S.review===null&&S.selection.length===0&&S.exec.id===id,completedId) && await stage(1).isDisabled());
    await nav('Executar');check('relatório concluído continua acessível depois de Nova simulação', (await text()).includes('Aplicação concluída') && await page.evaluate(id=>S.exec.id===id,completedId));

    // Conversion of the restored cover: the original is a copy, not a second source.
    await fresh();await nav('Histórico e restauração');await page.locator('[data-record="h1"] [data-act="restore"]').click();await page.locator('[data-confirm="restore"]').click();
    await nav('Simular');await page.locator('[data-draft="scope"]').selectOption('itens');await page.locator('[data-type="Thumb"]').uncheck();await simulate80();await page.locator('[data-row="img-v-capa"]').check();
    check('capa restaurada converter guardar: 486 antes, 611 depois e +125 a mais', (await page.locator('.storage-bill').innerText()).includes('486 KiB') && (await page.locator('.storage-bill').innerText()).includes('611 KiB') && (await page.locator('.bill-balance').innerText()).includes('+125 KiB') && (await page.locator('.bill-balance').innerText()).includes('Usaria 125 KiB a mais'));
    await shot('29-desktop-fatura-conversao-guardar');
    await action('editDraft').click();await page.locator('[data-draft="storage"][value="remover"]').check();await action('analyse').click();await page.locator('[data-row="img-v-capa"]').check();
    check('capa converter remover: 125 depois, libera 361 e irreversibilidade visível', (await page.locator('.bill-balance').innerText()).includes('−361 KiB') && (await page.locator('.storage-bill').innerText()).includes('Remoção irreversível'));
    await shot('30-desktop-fatura-conversao-remover');
    await review(['img-v-capa']);await page.locator('[data-ack="ack"]').check();await page.locator('[data-ack="permanent"]').check();await action('apply').click();await wait('Aplicação concluída');
    check('conclusão conversão remove original e libera saldo realizado correto', (await page.locator('.bill-balance').innerText()).includes('Liberou 361 KiB') && await page.evaluate(()=>S.images.find(r=>r.id==='img-v-capa').original===0));
    await fresh();await scenario('partial');
    check('conclusão parcial saldo não soma falha/ignorada', await page.locator('.bill-comparison strong').allTextContents().then(xs=>xs.join('|')==='847 KiB|915 KiB') && (await page.locator('.bill-balance').innerText()).includes('Usou 68 KiB'));
    await fresh();await page.locator('[data-draft="scope"]').selectOption('itens');await page.locator('[data-type="Thumb"]').uncheck();await page.locator('[data-draft="storage"][value="remover"]').check();await simulate80();await review(['img-v-capa']);
    check('regeneração remover inclui retirada do original existente no saldo', await page.locator('.bill-comparison strong').allTextContents().then(xs=>xs.join('|')==='627 KiB|125 KiB') && (await page.locator('.bill-balance').innerText()).includes('Liberaria 502 KiB'));
    await page.locator('[data-ack="ack"]').check();await page.locator('[data-ack="permanent"]').check();await action('apply').click();await wait('Aplicação concluída');
    check('regeneração remover confirma saldo realizado e perda do original', (await page.locator('.bill-balance').innerText()).includes('Liberou 502 KiB') && await page.evaluate(()=>S.images.find(r=>r.id==='img-v-capa').original===0));
    const priorId=await page.evaluate(()=>S.exec.id);await action('newRun').click();await page.locator('[data-type="Thumb"]').check();await action('analyse').click();await review(['img-v-mini']);await page.locator('[data-ack="ack"]').check();await page.locator('[data-ack="permanent"]').check();await action('apply').click();await wait('Aplicação concluída');
    check('nova operação conserva relatório anterior arquivado em memória', await page.evaluate(id=>S.exec.id!==id&&S.operations.some(e=>e.id===id&&e.phase==='done'&&e.results.length===1),priorId));
    await fresh();await scenario('waitNext');await simulate80();await review();await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aguardando metadata');
    const runningId=await page.evaluate(()=>S.exec.id);await nav('Simular');await stage(0).click();await stage(1).click();
    check('navegar etapas durante execução não apaga operação nem autoriza reaplicar', await page.evaluate(id=>S.exec.id===id&&S.exec.phase==='waiting'&&S.review===null&&S.analysis.stale, runningId) && await stage(2).isDisabled());
    await scenario('partial');
    check('bancada não substitui execução ativa por exemplo parcial', await page.evaluate(id=>S.exec.id===id&&S.exec.phase==='waiting'&&S.scenario==='waitNext',runningId));
    await nav('Executar');await action('cancel').click();await wait('Operação interrompida');
    check('conclusão zero sem alteração, sem promessa futura nem remoção fictícia', (await page.locator('.bill-balance').innerText()).includes('Sem alteração no espaço usado') && await page.locator('.bill-comparison strong').allTextContents().then(xs=>xs.join('|')==='0 KiB|0 KiB') && (await page.locator('.storage-bill').innerText()).includes('nenhum original deste conjunto foi removido') && !(await page.locator('.storage-bill').innerText()).includes('Remoção irreversível'));

    await fresh();await page.setViewportSize({width:360,height:640});await page.locator('[data-draft="scope"]').selectOption('itens');await page.locator('[data-type="Thumb"]').uncheck();await simulate80();await review(['img-v-capa']);
    await noOverflow('360×640: stepper e fatura/regras globais sem overflow');await shot('31-mobile-fatura-regras');await axe('fatura e regras mobile');
    await action('storageSettings').click();await page.locator('#storageSettingsTitle').waitFor();
    check('config móvel foco no armazenamento e retorno real disponível', await page.evaluate(()=>document.activeElement.id==='storageSettingsTitle') && await action('returnReview').last().isVisible());
    await noOverflow('360×640: configurações contextualizadas sem overflow');await shot('32-mobile-config-retorno');
    await action('returnReview').last().click();await wait('Revisão final');await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aplicação concluída');
    await noOverflow('360×640: conclusão saldo e atalho único sem overflow');await shot('33-mobile-conclusao-saldo');
    await action('goRestore').click();await wait('Restaurar imagens');
    check('único atalho móvel conduz restauração filtrada', await page.locator('#historyFilter').inputValue()==='restorable');
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
    await purgeArea();await action('purgeAll').click();await action('purgeReview').click();await noOverflow('360 px: diálogo sem overflow');
    check('diálogo cabe e prende foco no modal nativo', await page.getByRole('dialog').evaluate(n=>{const r=n.getBoundingClientRect();return r.x>=0&&r.right<=innerWidth&&n.contains(document.activeElement);}));
    await shot('14-mobile-remocao');await page.keyboard.press('Escape');
    await page.setViewportSize({width:768,height:1024});await nav('Simular');await noOverflow('768 px: layout intermediário sem overflow');await shot('15-tablet');
    await page.setViewportSize({width:360,height:640});await bench();await page.locator('#shell').selectOption('paginas');await page.locator('#bench').evaluate(n=>{n.open=false;});
    check('páginas mobile expõe as cinco áreas no seletor', await page.locator('#areaMenu option').count()===5 && await page.locator('#areaMenu').isVisible());
    for(const name of ['Executar','Automação','Histórico e restauração','Configurações','Simular']){await nav(name);await noOverflow('360×640 páginas: '+name);}
    await shot('16-mobile-paginas');await axe('páginas mobile');
    await page.setViewportSize({width:1280,height:900});await nav('Configurações');await noOverflow('1280 px: configurações sem overflow');
    check('redução de movimento é respeitada', await page.evaluate(()=>matchMedia('(prefers-reduced-motion: reduce)').matches));

    // Round 3: bounded lists are views, never the authorization boundary.
    const nextPage=key=>page.locator('[data-act="page"][data-list="'+key+'"][aria-label^="Próxima"]');
    const prevPage=key=>page.locator('[data-act="page"][data-list="'+key+'"][aria-label^="Página anterior"]');
    const volume=async()=>{await bench();await action('volume').click();await page.locator('#bench').evaluate(n=>{n.open=false;});};
    const billValues=()=>page.locator('#selectedBill .bill-comparison strong').allTextContents();
    await fresh();await volume();await simulate80();
    check('volume determinístico: 31 avaliadas, 29 propostas e só dez linhas montadas', await page.evaluate(()=>S.analysis.rows.length===31&&S.analysis.rows.filter(r=>r.proposed).length===29) && await page.locator('.result-list [data-image-id]').count()===10 && (await page.locator('#page-proposals').innerText()).includes('1–10 de 31'));
    check('volume zero selecionadas: saldo principal zero e revisão bloqueada', (await billValues()).join('|')==='0 KiB|0 KiB' && (await page.locator('#selectedBill').innerText()).includes('Sem alterações selecionadas') && await action('review').isDisabled());
    const firstChoice=page.locator('[data-row="img-v-capa"]');await firstChoice.scrollIntoViewIfNeeded();await firstChoice.focus();
    await page.evaluate(()=>{window.checkboxBefore=document.querySelector('[data-row="img-v-capa"]');window.scrollBefore=scrollY;window.rowTopBefore=window.checkboxBefore.getBoundingClientRect().top;});
    await firstChoice.check();
    check('checkbox atualiza saldo sem remontar controle, perder foco ou mover linha/viewport', await page.evaluate(()=>window.checkboxBefore===document.activeElement&&Math.abs(scrollY-window.scrollBefore)<2&&Math.abs(window.checkboxBefore.getBoundingClientRect().top-window.rowTopBefore)<2));
    check('volume uma selecionada: principal 627 → 611, anúncio acessível contém saldo', (await billValues()).join('|')==='627 KiB|611 KiB' && (await page.locator('#announcer').innerText()).includes('1 imagem selecionada. Liberaria 16 KiB'));
    await nextPage('proposals').click();
    check('paginador específico transfere foco ao intervalo e preserva seleção invisível', await page.evaluate(()=>document.activeElement.id==='page-proposals'&&S.selection.join()==='img-v-capa') && (await billValues()).join('|')==='627 KiB|611 KiB');
    await page.locator('[data-row="img-volume-7"]').check();
    check('volume subconjunto entre páginas: principal soma só dois IDs', (await billValues()).join('|')==='834 KiB|895 KiB' && (await page.locator('.selection-bar').innerText()).includes('2 imagens selecionadas') && (await page.locator('#announcer').innerText()).includes('Usaria 61 KiB'));
    await shot('34-volume-simulacao-selecao');await axe('volume seleção entre páginas');
    await action('review').click();await wait('Revisão final');
    check('revisão preserva as identidades de páginas diferentes', await page.locator('.review-list [data-image-id]').evaluateAll(ns=>ns.map(n=>n.dataset.imageId).join('|')==='img-v-capa|img-volume-7') && (await page.locator('.bill-balance').innerText()).includes('Usaria 61 KiB'));
    await page.locator('[data-ack="ack"]').check();await action('apply').click();await wait('Aplicação concluída');
    check('aplicação entre páginas tem resultados individuais só do subconjunto', await page.locator('.execution-result li').count()===2 && await page.evaluate(()=>S.exec.results.every(r=>r.result==='done')&&S.exec.results.map(r=>r.id).join('|')==='img-v-capa|img-volume-7'));

    await fresh();await volume();await simulate80();await action('selectPage').click();
    check('selecionar página é explícito e não inclui páginas ocultas', await page.evaluate(()=>S.selection.length===8) && (await action('allRows').innerText()).includes('todos os resultados (29)'));
    await action('allRows').click();
    check('selecionar todos os resultados inclui 29 IDs e saldo de todos', await page.evaluate(()=>S.selection.length===29) && (await billValues()).join('|')==='6,47 MiB|8,75 MiB');
    await action('review').click();await wait('Revisão final');
    const reviewed=[];do{reviewed.push(...await page.locator('.review-list [data-image-id]').evaluateAll(ns=>ns.map(n=>n.dataset.imageId)));if(await nextPage('review').isDisabled())break;await nextPage('review').click();}while(true);
    check('revisão grande pagina detalhes sem truncar destinatários ou autorização', new Set(reviewed).size===29 && await page.evaluate(()=>S.review.rows.length===29) && await page.locator('.review-list [data-image-id]').count()===9);
    await page.locator('[data-ack="ack"]').check();await prevPage('review').click();
    check('paginar revisão não descarta consentimento do conjunto exato', await page.locator('[data-ack="ack"]').isChecked() && await action('apply').isEnabled());
    await shot('35-volume-revisao');

    await fresh();await volume();await nav('Histórico e restauração');
    check('toolbar pertence à lista Operações, sem seção anterior de seleção', await page.locator('.operations-list #historyFilter').count()===1 && await page.locator('.operations-list [data-act="restoreBatch"]').count()===1 && await page.locator('.restore-panel').count()===0);
    check('restauração não contém controles de exclusão, mas oferece link direto danger', await page.locator('[data-purge],[data-act="purgeReview"]').count()===0 && await page.locator('.local-nav .danger-link').getAttribute('href').then(x=>x.endsWith('/historico/remover-originais')));
    check('histórico limita dez imagens mesmo com múltiplas operações', await page.locator('.history-record').count()===10 && (await page.locator('#page-history').innerText()).includes('de 44'));
    await page.locator('[data-act="selectPage"][data-list="history"]').click();
    check('selecionar página do histórico ignora incerto/removido e não marca grupo inteiro', await page.evaluate(()=>restoreIds().length===8));
    await page.locator('[data-act="clearSelection"][data-list="history"]').click();
    await page.locator('[data-restore="h1"]').check();await nextPage('history').click();
    check('operação de 27 imagens também é dividida em páginas', await page.locator('.history-record').count()===10 && await page.locator('.history-group').count()===1 && (await page.locator('.history-group header .badge').innerText()).includes('27 imagens · 10 nesta página'));
    await page.locator('[data-restore="hv-7"]').check();await action('restoreBatch').click();
    check('restauração entre páginas revisa somente h1 e hv-7, com bytes/dimensões', await page.locator('.restore-review li').count()===2 && await page.getByRole('dialog').innerText().then(t=>t.includes('Transação ativa h1')&&t.includes('Transação ativa hv-7')&&t.includes('307 KiB')&&t.includes('1280 × 720')));
    await page.evaluate(()=>{S.history.find(h=>h.id==='hv-7').active=false;});await page.locator('[data-confirm="restoreBatch"]').click();
    check('lote entre páginas revalida por registro: sucesso e rejeição sem substituta', await page.locator('[aria-label="Resultado da restauração"] li').count()===2 && await page.evaluate(()=>S.restoreResults.length===2&&S.restoreResults[0].done&&!S.restoreResults[1].done&&S.history.find(h=>h.id==='hv-8').status==='available'));
    await page.locator('#historyFilter').selectOption('restorable');
    check('filtro reinicia página, exclui restaurados/inativos e preserva proteção', await page.evaluate(()=>S.pages.history===0) && await page.locator('[data-record="h1"],[data-record="hv-7"]').count()===0);
    await action('restoreAll').click();await action('restoreBatch').click();
    check('revisão de todos restauráveis anuncia alcance total e monta só dez detalhes', await page.getByRole('dialog').innerText().then(t=>t.includes('40 imagens')) && await page.locator('.restore-review li').count()===10 && await page.evaluate(()=>dialogData.rows.length===40));
    const restoreReviewIds=[];do{restoreReviewIds.push(...await page.locator('.restore-review small').allTextContents());if(await nextPage('dialog').isDisabled())break;await nextPage('dialog').click();}while(true);
    check('todos os 40 destinatários da restauração são consultáveis no diálogo', new Set(restoreReviewIds).size===40);
    await page.keyboard.press('Escape');await nextPage('history').click();await page.locator('#historyFilter').selectOption('all');
    check('alterar filtro retorna à primeira página sem perder seleção crosspage', await page.evaluate(()=>S.pages.history===0&&restoreIds().length===40));
    await shot('36-volume-historico');

    await purgeArea();
    check('exclusão é subpágina danger própria e seleção independente', page.url().endsWith('/historico/remover-originais') && await page.locator('[data-restore],[data-act="restoreBatch"]').count()===0 && await action('purgeReview').isDisabled() && await page.evaluate(()=>restoreIds().length===40));
    await page.goBack();await page.locator('#historyTitle').waitFor();await page.goForward();await page.locator('#purgeTitle').waitFor();
    check('voltar/avançar do navegador mantém subpágina e rascunhos independentes', await page.evaluate(()=>S.historyPage==='purge'&&restoreIds().length===40&&purgeIds().length===0));
    check('exclusão também limita dez linhas e conta total', await page.locator('.purge-row').count()===10 && (await page.locator('#page-purge').innerText()).includes('de 41'));
    await page.locator('[data-act="selectPage"][data-list="purge"]').click();
    check('selecionar página da exclusão só marca nove elegíveis visíveis, não o incerto', await page.evaluate(()=>purgeIds().length===9&&!purgeIds().includes('h4')));
    await page.locator('[data-act="clearSelection"][data-list="purge"]').click();
    await page.locator('[data-purge="h2"]').check();await nextPage('purge').click();
    const purgeSecond=await page.locator('[data-purge]').first().getAttribute('data-purge');await page.locator('[data-purge="'+purgeSecond+'"]').check();await action('purgeReview').click();
    check('remoção crosspage revisa lista exata/bytes e confirmação danger própria', await page.locator('.purge-review li').count()===2 && await page.getByRole('dialog').innerText().then(t=>t.includes('Transação h2')&&t.includes('Transação '+purgeSecond)) && await action('purgeReview').getAttribute('class')==='danger' && await page.locator('[data-confirm="purge"]').getAttribute('class')==='danger' && await page.locator('[data-confirm="purge"]').isDisabled());
    await page.keyboard.press('Escape');
    check('Escape preserva subset de exclusão sem remover nem tocar na restauração', await page.evaluate(()=>purgeIds().length===2&&restoreIds().length===40&&S.history.find(h=>h.id==='h2').status==='available'));
    await action('purgeReview').click();await page.locator('#dialogAck').check();await page.evaluate(id=>{S.history.find(h=>h.id===id).original++;},purgeSecond);await page.locator('[data-confirm="purge"]').click();
    check('purga revalida snapshot completo e informa resultado individual por ID', await page.evaluate(id=>S.purgeResults.length===2&&S.purgeResults.find(h=>h.id==='h2').done&&!S.purgeResults.find(h=>h.id===id).done&&S.history.find(h=>h.id===id).status==='available',purgeSecond));
    await action('purgeAll').click();await action('purgeReview').click();
    check('remover todos exige escolha explícita e diálogo paginado do conjunto completo', await page.locator('.purge-review li').count()===10 && await page.evaluate(()=>dialogData.rows.length===39) && await nextPage('dialog').isEnabled());
    await shot('37-volume-exclusao-dialogo');await axe('exclusão grande paginada');
    await nextPage('dialog').click();await page.locator('#dialogAck').check();await page.locator('[data-confirm="purge"]').click();
    check('confirmação em outra página não limita remoção à página visível', await page.evaluate(()=>S.purgeResults.length===39&&S.purgeResults.every(h=>h.done)&&eligible().length===0) && await action('purgeReview').isDisabled());
    check('mutação faz clamp da página de originais e mantém incerto preservado', await page.evaluate(()=>S.pages.purge===0&&S.history.find(h=>h.id==='h4').status==='uncertain') && await page.locator('.purge-row').count()===1 && (await page.locator('#page-purge').innerText()).includes('1–1 de 1'));
    await page.locator('#purgeFilter').selectOption('eligible');
    check('filtro de exclusão vazio tem intervalo 0 e botão próxima bloqueado', (await page.locator('#page-purge').innerText()).includes('0–0 de 0') && await nextPage('purge').isDisabled());
    await nav('Histórico e restauração');
    check('restauração também limpa seleção obsoleta após purga total', await action('restoreBatch').isDisabled() && await page.locator('[data-restore]').count()===0);

    // Auto: concrete reporting semantics and a contextual settings detour.
    await fresh();await simulate80();await review(['img-v-mini']);await page.locator('[data-ack="ack"]').check();await nav('Automação');await number('[data-auto="quality"]',79);await page.locator('[data-auto-lib="series"]').check();
    check('modo análise explicita relatório, ausência de alteração, originais e aprovação', await page.locator('[data-auto="mode"] option:checked').textContent().then(t=>t==='Apenas analisar e gerar relatório') && await page.locator('.mode-explanation').innerText().then(t=>t.includes('sem alterar imagens nem guardar originais')&&t.includes('não cria fila para aprovação')));
    check('tarefas em modo salvo análise descrevem avaliação, não mutação já autorizada', await page.locator('.task-row .help').allTextContents().then(xs=>xs.length===2&&xs.every(t=>t.includes('sem alterá-las'))));
    check('Automação: tarefas na coluna principal e salvar no aside aderente desktop', await page.locator('.auto-grid>div .task-row').count()===2 && await page.locator('.auto-grid>aside.summary-panel [data-act="saveAuto"]').count()===1 && await page.locator('.auto-grid>aside').evaluate(n=>getComputedStyle(n).position==='sticky'));
    await action('storageSettings').click();await page.locator('#storageSettingsTitle').waitFor();
    check('desvio Auto foca armazenamento e oferece retorno sem copy de revisão manual', await page.evaluate(()=>document.activeElement.id==='storageSettingsTitle'&&S.cfgReturn==='auto') && (await action('returnReview').first().innerText()).includes('Voltar à Automação'));
    await number('[data-cfg="reserve"]',4);await action('returnReview').first().click();await page.locator('#autoTitle').waitFor();
    check('Auto ida/volta sem salvar preserva rascunho e revisão manual separadamente', await page.evaluate(()=>S.auto.quality===79&&S.auto.libs.join()==='filmes,series'&&S.auto.dirty&&S.autoSaved.quality===85&&S.defaults.reserve===2&&S.cfg.reserve===4&&S.review.ack&&!S.analysis.stale));
    await action('storageSettings').click();await page.locator('#storageSettingsTitle').waitFor();await action('saveCfg').click();await action('returnReview').first().click();await page.locator('#autoTitle').waitFor();
    check('salvar global invalida revisão manual, não salva/perde draft Auto nem amplia alcance', await page.evaluate(()=>S.defaults.reserve===4&&S.review===null&&S.analysis.stale&&S.auto.quality===79&&S.auto.dirty&&S.autoSaved.quality===85&&S.auto.scope==='bibliotecas'&&S.auto.libs.length===2));
    await action('autoReport').click();
    check('relatório acessível é exemplo honesto, não execução nem seleção aprovada', await page.getByRole('dialog').innerText().then(t=>t.includes('nenhuma tarefa foi executada')&&t.includes('0 imagens alteradas')&&t.includes('Sem fila para aprovação')&&t.includes('até 20 amostras')));
    await page.locator('[data-confirm="report"]').click();
    check('fechar relatório não anuncia alteração confirmada nem aprova uma tarefa', (await page.locator('#announcer').innerText()).includes('Nenhuma tarefa foi executada ou aprovada') && await action('autoReport').evaluate(n=>n===document.activeElement));
    await shot('38-desktop-automacao');await axe('automação relatório e proteções');
    await page.locator('.task-row').last().scrollIntoViewIfNeeded();
    check('aside desktop acompanha rolagem real e mantém salvar dentro da janela', await action('saveAuto').evaluate(n=>{const r=n.getBoundingClientRect();return r.top>=54&&r.bottom<=innerHeight;}) && await page.locator('.auto-grid>aside').evaluate(n=>Math.abs(n.getBoundingClientRect().top-54)<2));
    await page.setViewportSize({width:360,height:640});await noOverflow('Auto mobile: coluna, save e tarefas sem overflow');
    check('Auto mobile desativa aside sticky alto, mantém salvar acessível sem cobrir formulário', await page.locator('.auto-grid>aside').evaluate(n=>getComputedStyle(n).position==='static') && await action('saveAuto').isVisible());
    await shot('39-mobile-automacao');await axe('automação mobile');
    await purgeArea();await noOverflow('exclusão mobile própria sem overflow');await shot('40-mobile-exclusao');
    await fresh();await volume();await nav('Histórico e restauração');await action('restoreAll').click();await action('restoreBatch').click();
    check('diálogo grande mobile mantém título/ações e paginação sem 200 linhas', await page.locator('.restore-review li').count()===10 && await page.locator('[data-confirm="restoreBatch"]').evaluate(n=>{const r=n.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight;}) && await nextPage('dialog').isVisible());
    await nextPage('dialog').click();await noOverflow('diálogo paginado mobile sem overflow');await shot('41-mobile-volume-dialogo');await axe('diálogo volume mobile');await page.keyboard.press('Escape');
    await page.locator('#historyFilter').selectOption('restorable');await nextPage('history').click();await action('restoreAll').click();await action('restoreBatch').click();await page.locator('[data-confirm="restoreBatch"]').click();
    check('restaurar todos desde página posterior reinicia filtro/página e pagina resultados completos', await page.evaluate(()=>S.historyFilter==='all'&&S.pages.history===0&&S.restoreResults.length===42&&S.restoreResults.every(h=>h.done)) && await page.locator('[aria-label="Resultado da restauração"] li').count()===10);
    await fresh();await volume();await nav('Simular');await volume();
    check('fixture de volume não duplica ao preparar de novo', await page.evaluate(()=>S.images.length===36&&S.history.length===44));
    await bench();await action('reset').click();
    check('Recomeçar limpa volume e restaura fixture pequena e seleção vazia', await page.evaluate(()=>S.images.length===9&&S.history.length===4&&S.selection.length===0&&!S.volume));
    await simulate80();const mobileChoice=page.locator('[data-row="img-v-capa"]');await mobileChoice.scrollIntoViewIfNeeded();await mobileChoice.focus();
    await page.evaluate(()=>{window.mobileNode=document.activeElement;window.mobileScroll=scrollY;window.mobileTop=window.mobileNode.getBoundingClientRect().top;});await mobileChoice.check();
    check('checkbox móvel também preserva nó, foco, viewport e posição da linha', await page.evaluate(()=>document.activeElement===window.mobileNode&&Math.abs(scrollY-window.mobileScroll)<2&&Math.abs(window.mobileNode.getBoundingClientRect().top-window.mobileTop)<2));
    await page.locator('#selectedBill').scrollIntoViewIfNeeded();
    if(shots)await page.screenshot({path:path.join(shots,'42-mobile-saldo-selecionado-viewport.png')});
    await fresh();await simulate80();await review(['img-v-mini']);await page.locator('[data-ack="ack"]').check();await nav('Automação');await number('[data-auto="quality"]',78);
    check('resumo de Auto reage à edição numérica sem depender de outro checkbox', (await page.locator('.auto-grid>aside>.help').first().innerText()).includes('qualidade 78'));
    await action('storageSettings').click();await page.locator('#storageSettingsTitle').waitFor();await action('returnReview').first().click();await page.locator('#autoTitle').waitFor();await nav('Executar');await action('storageSettings').click();await page.locator('#storageSettingsTitle').waitFor();
    check('desvio manual após Auto não herda destino de retorno da automação', await page.evaluate(()=>S.cfgReturn==='review') && (await action('returnReview').first().innerText()).includes('Voltar à revisão'));
    await page.evaluate(()=>{location.hash='#/p2/paginas/configuracoes/armazenamento/automacao';});await wait('Voltar à Automação');
    check('contexto de retorno Auto é restaurado pelo link específico, não por estado global antigo', await page.evaluate(()=>S.cfgReturn==='auto'&&document.activeElement.id==='storageSettingsTitle'));
    await page.goBack();await wait('Voltar à revisão');
    check('history devolve contexto manual anterior sem vazamento de retorno Auto', await page.evaluate(()=>S.cfgReturn==='review'&&S.review.ack&&S.auto.quality===78));
    await action('returnReview').first().click();await wait('Revisão final');
    check('ida/volta Auto e depois manual preserva revisão sem salvar Auto', await action('apply').isEnabled() && await page.evaluate(()=>S.auto.quality===78&&S.autoSaved.quality===85&&S.auto.dirty));
    await nav('Automação');await action('storageSettings').click();await page.locator('#storageSettingsTitle').waitFor();await number('[data-cfg="batch"]',100);await action('saveCfg').click();await action('returnReview').first().click();await page.locator('#autoTitle').waitFor();
    check('reduzir limite global revalida draft Auto sem truncar lote ou salvar silenciosamente', await action('saveAuto').isDisabled() && await page.locator('[data-auto="batch"]').inputValue()==='250' && await page.evaluate(()=>S.autoSaved.batch===250&&S.defaults.batch===100&&S.auto.quality===78));
    await page.goto(url+'#/p2/paginas/historico/remover-originais');await page.locator('#purgeTitle').waitFor();
    check('link direto de exclusão mantém cinco ambientes e inicia seleção própria zero', await page.locator('#shellnav a').count()===5 && await action('purgeReview').isDisabled() && await page.locator('[data-restore]').count()===0);
    await page.locator('[data-purge="h1"]').check();await page.locator('[data-purge="h2"]').evaluate(n=>{n.scrollIntoView({block:'start'});n.focus({preventScroll:true});});
    check('exclusão móvel pluraliza uma seleção e mantém foco abaixo da barra aderente', (await page.locator('.purge-panel .batch-bar strong').innerText())==='1 original selecionado' && await page.locator('[data-purge="h2"]').evaluate(n=>n===document.activeElement&&n.getBoundingClientRect().top>=document.querySelector('.batch-bar').getBoundingClientRect().bottom));
    await nav('Histórico e restauração');await page.locator('[data-restore="h2"]').evaluate(n=>{n.scrollIntoView({block:'start'});n.focus({preventScroll:true});});
    check('restauração móvel mantém controle focado abaixo da barra aderente', await page.locator('[data-restore="h2"]').evaluate(n=>n===document.activeElement&&n.getBoundingClientRect().top>=document.querySelector('.batch-bar').getBoundingClientRect().bottom));
    if(shots)await page.screenshot({path:path.join(shots,'47-mobile-foco-lista-viewport.png')});
    await page.setViewportSize({width:1440,height:1000});
    await fresh();check('logo continua na navigation-column sem faixa vazia acima do main', await page.locator('.navigation-column .product-heading').count()===1 && await page.evaluate(()=>Math.abs(document.querySelector('.product-heading').getBoundingClientRect().top-document.querySelector('main').getBoundingClientRect().top)<2));
    await volume();await nav('Histórico e restauração');const seenOperations=new Set();let bounded=true;
    do{for(const op of await page.locator('.history-group header .operation-id').allTextContents())seenOperations.add(op);bounded=bounded&&await page.locator('.history-record').count()<=10&&await page.locator('.history-group').count()<=10;if(await nextPage('history').isDisabled())break;await nextPage('history').click();}while(true);
    check('paginação percorre também operações distintas, não apenas o grupo longo', bounded&&seenOperations.size===17&&(await page.locator('#page-history').innerText()).includes('41–44 de 44'));
    await prevPage('history').click();const lateRestore=page.locator('[data-act="restore"]').last(),lateId=await lateRestore.getAttribute('data-id');await lateRestore.click();await page.locator('[data-confirm="restore"]').click();
    check('restaurar individual de página posterior mostra e foca a própria evidência', await page.locator('#evidence-'+lateId).getAttribute('open')!==null && await page.locator('#evidence-'+lateId+' summary').evaluate(n=>n===document.activeElement));
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
