/* Browser exercise for the P2 design prototype (docs/ux-prototype/jellyp-p2.html).
 * Static file; no Jellyfin server, no credentials, synthetic data only.
 * Requires Playwright (JELLYP_PLAYWRIGHT_MODULE may point to its installed module).
 * Optional --screenshots <directory> stores desktop/mobile captures.
 * Exit code is non-zero when any check fails.
 */
'use strict';
const { chromium } = require(process.env.JELLYP_PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs');
const path = require('node:path');
const arguments_ = process.argv.slice(2);
const argument = name => arguments_.includes(name) ? arguments_[arguments_.indexOf(name) + 1] : undefined;
const root = path.resolve(__dirname, '..');
const url = 'file://' + path.join(root, 'docs', 'ux-prototype', 'jellyp-p2.html');
const shots = argument('--screenshots');

(async () => {
    const results = [];
    const errors = [];
    const check = (name, condition, detail = '') =>
        results.push((condition ? 'OK   ' : 'FALHA ') + name + (detail ? ' — ' + detail : ''));
    const browser = await chromium.launch();
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    page.on('console', message => { if (message.type() === 'error') errors.push('console: ' + message.text()); });
    page.on('pageerror', error => errors.push('pageerror: ' + error.message));
    const text = async () => await page.locator('main').innerText();
    const nav = async name => {
        const tab = page.getByRole('tab', { name });
        if (await tab.count()) await tab.click(); else await page.getByRole('link', { name }).click();
        await waitH1(name);
    };
    const waitText = async text => await page.waitForFunction(t => (document.querySelector('main')?.innerText || '').includes(t), text);
    const waitH1 = async name => await page.waitForFunction(n => document.querySelector('main h1')?.textContent === n, name);
    const waitSel = async selector => await page.waitForSelector(selector);
    const shot = async name => {
        if (!shots) return;
        fs.mkdirSync(shots, { recursive: true });
        await page.screenshot({ path: path.join(shots, name), fullPage: false });
    };

    try {
        await page.goto(url);
        check('carrega na área Simular', (await page.locator('main h1').innerText()) === 'Simular');

        // D1 shells must be visibly distinct.
        await page.click('#shellAbas');
        await waitSel('#shellnav [role="tablist"]');
        check('Abas: barra de abas (tablist)', await page.evaluate(() => !!document.querySelector('#shellnav [role="tablist"]')));
        check('Abas: sem barra lateral de páginas', await page.evaluate(() => !document.querySelector('aside.sidebar')));
        check('Abas: sem breadcrumb/URL de página', !(await text()).includes('configurationpage'));
        await shot('prototype-abas.png');
        await page.click('#shellPaginas');
        await waitSel('#shellnav [aria-current="page"]');
        check('Páginas: barra lateral de páginas', await page.evaluate(() => !!document.querySelector('#shellnav.sidebar')));
        check('Páginas: link ativo com aria-current', await page.evaluate(() => !!document.querySelector('#shellnav [aria-current="page"]')));
        check('Páginas: breadcrumb + URL simulada', (await text()).includes('configurationpage?name=jellyp-simular'));
        await shot('prototype-paginas.png');

        // Navigation in both shells.
        for (const [shell, role] of [['paginas', 'link'], ['abas', 'tab']]) {
            await page.click(shell === 'paginas' ? '#shellPaginas' : '#shellAbas');
            await waitSel(shell === 'paginas' ? '#shellnav [aria-current="page"]' : '#shellnav [role="tablist"]');
            for (const name of ['Executar', 'Automação', 'Histórico e originais', 'Configurações', 'Simular']) {
                await page.getByRole(role, { name }).click();
                await waitH1(name);
                check(shell + ': navega para ' + name, true);
            }
        }

        // D2: changed quality proposes regeneration from the original.
        await page.locator('input[data-field="quality"]').fill('80');
        await page.locator('input[data-field="quality"]').press('Enter');
        await page.getByRole('button', { name: 'Analisar as imagens' }).click();
        await waitSel('#resultadoAnalise');
        let body = await text();
        check('análise: 3 avaliadas, 2 propostas', body.includes('3 avaliadas') && body.includes('2 propostas'));
        check('D2: propõe regenerar do original', body.includes('Gerar novamente a partir do original'));
        check('D2: qualidade proposta 80', body.includes('Qualidade proposta: 80'));
        check('foco vai para o resultado da análise', await page.evaluate(() => document.activeElement?.id === 'resultadoAnalise'));
        check('seleção inicial de propostas', (await page.locator('input[data-act="toggleRow"]:checked').count()) === 2);
        await shot('prototype-resultado.png');

        // Execute: review, progress, waiting, cancellation, partial result.
        await page.getByRole('button', { name: /Revisar e executar/ }).click();
        await waitH1('Executar');
        check('executar: revisão com alvo exato', (await text()).includes('Aplicar às 2 imagens selecionadas'));
        await page.getByRole('button', { name: /Aplicar às/ }).click();
        await waitSel('#andamento');
        check('executar: andamento', (await text()).includes('Processando'));
        check('foco vai para o andamento', await page.evaluate(() => document.activeElement?.id === 'andamento'));
        await page.locator('select[data-bench="exec"]').selectOption('waiting');
        await waitText('Aguardando varredura de metadata');
        body = await text();
        check('espera: fase de metadata na própria operação', body.includes('Aguardando varredura de metadata') && body.includes('Interromper a espera'));
        await page.getByRole('button', { name: 'Interromper a espera' }).click();
        await waitText('Parada solicitada');
        check('cancelamento: parada entre imagens', (await text()).includes('Parada solicitada'));
        await page.locator('select[data-bench="exec"]').selectOption('partial');
        await waitText('1 ignorada · 1 falha');
        check('sucesso parcial separado', (await text()).includes('1 ignorada · 1 falha'));
        await page.locator('select[data-bench="exec"]').selectOption('idle');

        // Bench opens the affected area.
        await nav('Simular');
        await page.locator('input[data-bench="recovery"]').check();
        await waitH1('Executar');
        check('bancada: recuperação abre Executar', (await page.locator('main h1').innerText()) === 'Executar');
        await page.locator('input[data-bench="recovery"]').uncheck();

        // History: restore one, purge exactly one chosen original.
        await nav('Histórico e originais');
        check('histórico: 2 originais restaráveis', (await page.getByRole('button', { name: 'Restaurar original' }).count()) === 2);
        await page.getByRole('button', { name: 'Restaurar original' }).first().click();
        await waitText('Restaurar') || await waitText('Original restaurado');
        check('restauração muda estado', (await text()).includes('Original restaurado'));
        await page.locator('input[data-purge="h5"]').check();
        await waitText('Remover selecionados (1)');
        check('purga: seleção explícita', (await text()).includes('Remover selecionados (1)'));
        await page.getByRole('button', { name: /Remover selecionados/ }).click();
        await waitText('Convertido · original removido');
        body = await text();
        check('purga: remove só o selecionado', body.includes('Convertido · original removido'));
        check('purga: incerto preservado', body.includes('Mantido: não foi possível verificar'));

        // Automation isolated from the manual draft.
        await nav('Automação');
        await page.locator('input[data-auto="modo"][value="aplicar conversões"]').check();
        await waitText('não salvas');
        check('automação: alteração própria não salva', (await text()).includes('não salvas'));
        await page.getByRole('button', { name: 'Salvar configuração da automação' }).click();
        await waitText('Configuração da automação salva');
        check('automação: gravação explícita', (await text()).includes('salva'));

        // Settings defaults isolated from the manual draft (D4).
        await nav('Configurações');
        await page.locator('input[data-cfg="quality"]').fill('90');
        await page.locator('input[data-cfg="quality"]').press('Enter');
        await waitText('não gravado');
        check('configurações: gravação pendente', (await text()).includes('não gravado'));
        await nav('Simular');
        check('isolamento D4: rascunho manual segue 80', (await text()).includes('Qualidade 80'));

        // Keyboard and viewport.
        await page.click('#shellAbas');
        await waitSel('#shellnav [role="tablist"]');
        await page.getByRole('tab', { name: 'Simular' }).focus();
        await page.keyboard.press('ArrowRight');
        await page.waitForFunction(() => document.querySelector('#shellnav [role="tab"][aria-selected="true"]')?.textContent === 'Executar');
        check('teclado: seta troca de aba', (await page.evaluate(() => document.querySelector('#shellnav [role="tab"][aria-selected="true"]')?.textContent)) === 'Executar');
        await page.setViewportSize({ width: 360, height: 640 });
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
        check('mobile: sem overflow horizontal', overflow <= 1, 'overflow=' + overflow + 'px');
        check('mobile: 5 áreas visíveis', await page.evaluate(() => {
            const items = [...document.querySelectorAll('#shellnav [role="tab"], #shellnav a')];
            return items.length === 5 && items.every(item => item.getBoundingClientRect().width > 0);
        }));
        await shot('prototype-mobile.png');
    } catch (error) {
        results.push('EXCEÇÃO NO TESTE: ' + error.message.split('\n')[0]);
    } finally {
        console.log(results.join('\n'));
        console.log('\nERROS DE CONSOLE/PÁGINA: ' + (errors.length ? '\n  ' + errors.join('\n  ') : 'nenhum'));
        const failures = results.filter(line => line.startsWith('FALHA') || line.startsWith('EXCEÇÃO')).length;
        console.log('RESULTADO: ' + results.length + ' passos, ' + failures + ' problemas');
        await browser.close();
        process.exit(failures ? 1 : 0);
    }
})();
