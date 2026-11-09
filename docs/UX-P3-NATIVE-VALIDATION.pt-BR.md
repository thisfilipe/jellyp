# Validação nativa P3–P7 — implementação e evidência local

Host descartável Jellyfin 12.1, loopback `127.0.0.1:18096`. Não foram usadas
credenciais, bancos ou mídia de produção. Backend preservado pelo
escritor de frontend. Protótipo P2 e smoke legado não foram alterados nem
apresentados como validação desta página.

## Problema de fundos encontrado na integração — corrigido e verificado

**Fundos múltiplos: identidade/índice da análise diverge da posição real.**
Com 12 PNGs gerados e enviados pela API oficial ao mesmo filme descartável,
`Plan/Images` atribuiu hashes a índices diferentes de `Items/{id}/Images`.
Foi comparado o SHA-256 dos arquivos reais informados pelo host, somente após
verificar marcador e `System/Info.ProgramDataPath`, sem usar a representação
WebP enviada ao navegador. Resultado: índice 0 e 10 coincidiram; 1–9 e 11 não.
Exemplo sanitizado: hash do slot real Backdrop 1 corresponde ao índice 9 no
plano; hash do slot real 9 corresponde ao índice 8 no plano.

Aplicação nativa exata de Primary 0 + Backdrop 9: `CompletedWithErrors`,
`Changed=1`, `Failed=0`, `Invalidated=1`; o fundo foi recusado com
`SourceChanged`. Nenhuma substituta foi aplicada. A UI mostrou o resultado
individual corretamente. A hipótese responsável é a ordem de hidratação de
`ImageInfos` em `DetailedInventory` versus `GetImageInfo(type,index)` em
`GetCurrent`; não é conclusão de correção. **O backend não foi alterado nesta
execução de UI.** O escritor de backend posterior confirmou o diagnóstico e
aplicou a correção mínima: reidratação via `GetItemById` para itens com imagens
repetidas do mesmo tipo em `Inventory`, `DetailedInventory` e `InspectItems`,
sem ordenar por filename; planos antigos usam schema novo e são recusados.
A regressão nativa posterior (07/11/2026) verificou seis Backdrops: todos os hashes
do plano coincidem com os índices nativos; aplicar somente o slot 1 resulta em
`Changed=1`, `Invalidated=0`, com hashes dos demais slots intactos. A restauração
retorna todos aos bytes originais. A mesma build converteu 52 Backdrops num lote
sem invalidação ou falha. Evidência reproduzível: `scripts/smoke-native-planning.cjs`.

## Lacuna de retenção encontrada na integração — corrigida e verificada

Na primeira implementação, `Review.Retention` fornecia apenas amostra de até 50
identidades e `Truncated`, sem leitura completa. A UI bloqueava a aplicação, em
vez de autorizar exclusão de destinatários ocultos. Esse diagnóstico motivou a
paginação estável de todas as identidades da retenção revisada.

**Atualização backend (correção pós-validação):** `Review` agora aceita
`RetentionOffset`/`RetentionLimit`, retorna `Total`/`Offset`/`Limit`/`HasMore` e
um `Fingerprint` do conjunto completo; a UI passou a paginar a lista e envia
`AuthorizeRetentionFingerprint` no Apply. A aplicação persiste os IDs exatos
autorizados e só executa esses IDs, revalidando limites/identidades antes de
qualquer mutação. A conferência posterior no host real percorreu seis páginas
da API (52 IDs únicos e fingerprint estável), abriu a sexta página na UI nativa
(`51–52 of 52`) e confirmou a aplicação real com o fingerprint enviado pelo
cliente. Exatamente 52 originais gerados foram expirados e uma capa selecionada
foi convertida; os 52 WebP ativos permaneceram registrados. Nenhuma mídia de
produção foi usada. Para emular idade, o teste altera somente `CompletedUtc`
dos journals sintéticos identificados; não escreve no banco do Jellyfin.

## Conferência do integrador — 07/11/2026 (antes da correção de fidelidade)

- Build Release: zero erros/avisos; **181 testes .NET** e **9 testes de release**
  aprovados após as correções de integridade/concorrência/recuperação.
- **79 verificações nativas**, incluindo **7 axe**, repetidas na DLL corrigida
  realmente instalada, sem substituição de recursos/API; capturas em
  `/tmp/opencode/jellyp-native/ui-evidence/delivery/`, log
  `/tmp/opencode/jellyp-native/ui-delivery-smoke.log`.
- **16 verificações nativas adicionais** de fundos/retenção pelo script
  `smoke-native-planning.cjs`, incluindo navegação da UI até a sexta página.
  Captura `ui-evidence/planning-delivery/native-retention-page6.png`, resumo
  `planning-summary.json`, log `/tmp/opencode/jellyp-native/planning-delivery-smoke.log`.
- SHA-256 da DLL do build e da DLL instalada, iguais:
  `3a12fcd4e58a41389f7034878da2eb8217e4f92173c36732e9f0abc2a6dcf599`.
- Limpeza do teste restaura a capa, remove somente os Backdrops sintéticos
  adicionados, devolve padrões/limites e confirma automação inalterada. Journals,
  planos e operações da fixture são preservados como evidência local.

Comando do teste específico (autorizar apenas em fixture descartável):

```sh
JELLYP_PLAYWRIGHT_MODULE=/caminho/para/playwright \
node scripts/smoke-native-planning.cjs --root /tmp/opencode/jellyp-native \
  --screenshots /tmp/opencode/jellyp-native/ui-evidence/planning
```

Uma tentativa da jornada na build final foi interrompida por `Failed to fetch`
na UI. Não foi considerada aprovação. O host local foi encontrado desligado
posteriormente (log com shutdown normal, sem evidência conclusiva da causa da
falha inicial). A fixture foi reiniciada, o único original gerado pendente foi
restaurado com verificação de pertencimento aos hashes sintéticos, e a jornada
inteira passou sem mudança no código do produto. O smoke agora registra
`failedLocalRequests` para tornar falhas de transporte diagnosticáveis; não foi
introduzido sleep de inicialização nem alegada correção de uma causa não provada.

As duas primeiras tentativas do novo harness específico identificaram pressupostos do
próprio teste (filtrar a categoria avaliada e usar `data-jp-id` para controles
nativos com IDs de view). Foram corrigidos sem relaxar hashes, alcance ou
autorização. A execução final completa acima passou; falhas anteriores não
foram contadas como aprovação.

## Implementação entregue

### Correção de fidelidade ao P2 — 07/11/2026

Pré-condição conferida antes desta correção: commit `e4207bf` contém a entrega
anterior completa (24 arquivos); ele permanece como HEAD. O escopo autorizado é
UI, smoke nativo e documentação, somente na fixture marcada. Nenhum backend ou
arquivo do protótipo foi alterado nesta correção; não houve novo commit/push.

Comparação tela a tela com o P2 aprovado:

- **Simular / seleção:** mantém as três seções numeradas, resumo lateral,
  status-expansor único por tipo e cópia explícita de **Padrões globais**.
  Etapas usam círculos numerados, atual preenchida em azul/ciano, anteriores
  alcançáveis e futuras desabilitadas, com `aria-current="step"`.
- **Resultado:** recupera superfície e título de sucesso, próximo passo em
  destaque, rótulos de ação com símbolo/texto e acentos distintos. Prévia é a
  imagem **atual**, não uma renderização inventada da saída proposta. A toolbar
  pertence à lista: checkbox nativo de página (inclusive indeterminado), todas
  as propostas e limpar; paginador separado. Conta continua **depois da lista**
  e usa exclusivamente a seleção exata enviada ao servidor.
- **Revisão / Executar:** identidades e proteções ficam à esquerda;
  **Antes de autorizar**, consequências, conta, consentimentos e Apply voltam
  ao aside direito aderente. No celular ele vem depois das identidades/proteções,
  em fluxo normal. A lista completa paginada de expiração permanece na seção
  principal; resumo e consentimento separado estão junto de Apply. Expiração
  não entra no saldo da seleção; fingerprint e paginação do backend não mudam.
- **Histórico / resultados:** prévias de slots locais confirmados pela inspeção
  real, nomes/ano/tipo legíveis, formatos PNG/JPEG/WebP na narrativa; IDs/hashes
  e enum original ficam em detalhes. Remover tem destino e seleção próprios,
  com botões vermelhos **preenchidos**, texto branco e confirmação separada.
  Restauração continua sem estilo destrutivo.
- **Automação / Configurações:** elimina jargão de migração da microcopy.
  `ScopeMode=legacy` com IDs aparece uma única vez como **Itens específicos**,
  resolvendo os nomes já salvos; sem IDs pede escolha explícita antes de salvar,
  sem reinterpretar o comportamento das tarefas existentes nem selecionar tudo.
  **Padrões globais** são o ponto de partida dos testes; a qualidade/alcance da
  automação continuam próprios. Armazenamento/retenção são globais e afetam
  também automação. Idade conta desde a conclusão da conversão/regeneração.
  Relatório é rotulado como o último disponível, sem inventar sua origem.

Implementação continua usando o shell e os `emby-*` aprimorados pelo Jellyfin,
não clones. Acentos semânticos estão restritos ao plugin. O tema base é herdado;
a paleta legível de acentos é escolhida pela superfície renderizada do host.
Previews usam `ApiClient.getImageUrl(ItemId,{type,index,...})` somente depois de
confirmar o **slot local exato** por estado do plano ou inspeção. Não há fallback
de Thumb para Primary, URL de filesystem, materialização de referência remota,
nem persistência/log de URLs autenticadas. Falha de carregamento volta ao ícone
rotulado, sem imagem quebrada.

O smoke foi ampliado sem remover as 79 verificações anteriores. A primeira
rodada ampliada de desenvolvimento passou **222 verificações**, com substituição
somente do recurso HTML para iteração; isso **não é evidência da DLL final**.
Encontrou e corrigiu contraste insuficiente do acento claro e uma espera do
harness que podia anteceder a troca real de idioma. A UI agora expõe `aria-busy`
durante a renderização e o teste aguarda esse estado e o aprimoramento nativo.
Capturas de referência do P2 são identificadas `p2-reference-*`; elas nunca são
contadas como resultados do host. Evidência final instalada é registrada abaixo.

- `Jellyfin.Plugin.JellyP/Web/config.html`: substituição real da página final,
  mantendo `data-role="page"`, `type-interior`, `pluginConfigurationPage`,
  `data-require`, `ApiClient` e `Dashboard`. CSS/JS inline embarcados no recurso
  existente: nenhum problema de autenticação de assets externos, novo framework,
  endpoint, alteração em `Plugin.cs` ou mudança no `.csproj`.
- Cinco áreas dentro do shell verdadeiro, sem copiar topbar/sidebar do Jellyfin.
   Coluna JellyP alinhada ao conteúdo, navegação horizontal em tablet e select
   nativo no celular. Formulários e aside de salvar separados; superfícies/campos
   herdam o tema do host, com variantes semânticas de ação restritas ao plugin,
   não um sistema escuro próprio.
- Componentes `emby-input/button/linkbutton/select/checkbox` reais, `paperList`,
  `fieldDescription`; rótulos gerados pelo host sem duplicação. IDs dos controles
  e destinos dos expansores são exclusivos por view em cache. Foco visível,
  teclado, consentimentos separados, diálogo nativo e movimento reduzido.
- Rascunho manual, defaults/globais, automação, seleção, revisão e recibo têm
  propriedade separada. `sessionStorage` guarda apenas DTOs de ajustes, IDs,
  chaves e offsets, nunca paths ou autorização. Plano/execução vêm do servidor.
  Navegar preserva rascunhos/subsets; editar invalida o snapshot local; salvar
  defaults invalida a revisão; recibo consumido não vira autorização nova.
- Análise síncrona real com estado de espera sem percentual inventado e abort
  HTTP explícito. Busca de catálogo por nome/ano/tipo e inspeção real. Onze tipos
  e editor completo por tipo, com status-expansor único. Cópia global explícita
  só para rascunho. Nenhum `Run`/`Sync` é usado para aplicar seleção.
- Listas do servidor de dez imagens, seleção zero inicial e cross-page por
  chaves opacas. Conta **depois** da lista, calculada por `Review` para a seleção
  inteira; antes/depois de ativo + original, sinal e verbo de aumento/liberação.
  Revisões grandes, resultados individuais e manutenção também paginam, sem
  diminuir o conjunto autorizado à página visível. Caps 1000/200 explícitos.
- Apply/Restore/RetireSelected reais, recibos persistidos, polling e cancelamento
  identificado. Histórico por imagem, restauração individual/lote, recuperação
  incerta separada e subpágina de remoção permanente com seleção independente.
  Revisão de manutenção reconsulta toda a elegibilidade necessária antes do
  consentimento; o servidor continua responsável pela revalidação e escrita.
- Automação independente, modo diagnóstico explicado sem promessa de fila de
  aprovação, gravação explícita sem execução, estado salvo versus rascunho,
  disparos reais via `ScheduledTasks` e relatório real via `Tasks.LastRun`.
- JSON nativo PascalCase, GUID N32 e `Type` textual comprovados e corrigidos no
  contrato P3. Mensagens en/pt-BR; sem dados ou efeitos sintéticos no produto.

## Registro da primeira integração — recursos realmente instalados

Ambiente: Jellyfin server/web oficiais **12.1**, Chromium/Playwright **1.64.1**,
axe-playwright **4.14.0**, .NET **10**. Browser em modo de movimento reduzido;
a estabilidade também aguarda animações finitas por evento, não sleeps de layout.

1. `dotnet build JellyP.slnx -c Release --no-restore`: **0 erros, 0 avisos**.
2. `dotnet test JellyP.slnx -c Release --no-build --no-restore`: **148 aprovados,
   0 falhas, 0 ignorados** na árvore compartilhada, após a última alteração de UI.
3. `python3 -m unittest discover -s tests/release -v`: **9 aprovados**.
4. Package smoke com `scripts/package.py` em diretório de saída dentro do root
   descartável: ZIP determinístico, CRC/membros/metadata/identidade e auditoria
   de paths locais aprovados. Não publicou nem substituiu release remota.
5. `node --check scripts/smoke-native-ui.cjs`, compilação do JS inline por
   `new Function`, `git diff --check`: aprovados.
6. `python3 scripts/audit-publication.py --working-tree`: **75 arquivos de texto
   aprovados**; não dispensa revisão manual.
7. **79 verificações nativas, zero falhas**, incluindo **7 auditorias axe**
   WCAG 2 A/AA, 2.1 AA e 2.2 AA sem violações nas regiões auditadas.

Comando da última rodada:

```sh
JELLYP_PLAYWRIGHT_MODULE=/tmp/opencode/jellyp-native/browser/node_modules/playwright \
JELLYP_AXE_MODULE=/tmp/opencode/jellyp-native/browser/node_modules/@axe-core/playwright \
node scripts/smoke-native-ui.cjs --root /tmp/opencode/jellyp-native \
  --screenshots /tmp/opencode/jellyp-native/ui-evidence/final --journey
```

**Sem `JELLYP_UI_SOURCE` na rodada final.** O smoke compara byte a byte o HTML
servido pela DLL instalada com o arquivo atual antes da jornada. Não intercepta
respostas de API, não injeta dados em DTOs e não simula conversão por JS.
O único routing de rede final é uma barreira que aborta tráfego externo; todas as
respostas localhost vêm do host. O bundle oficial tentou carregar o Cast sender
de `www.gstatic.com`; essa tentativa foi bloqueada. JellyP não depende dele.

### Jornada realmente exercitada

- Com o host já inicializado, marcador e `System/Info.ProgramDataPath` conferidos
  antes das mutações da jornada.
  Quatorze PNGs gerados em treze filmes descartáveis, sendo capa + miniatura no
  Canary. MP4s extras são cópias do vídeo sintético já existente dentro do root.
- Análise real, SSIM/bytes reais, nenhuma seleção inicial, dez linhas, página 2,
  subset entre páginas, conta do servidor com originais, foco do checkbox
  preservado e etapas anteriores sem invalidar por mera navegação.
- Desvio a Configurações com foco em Armazenamento e retorno à revisão exata.
  Consentimento, botão Apply nativo com alvo ≥44 px, double-click emitindo um
  único POST, aplicação das duas chaves exatas e hashes reais dos WebP em disco
  iguais ao journal. Reload consultou o recibo concluído do servidor.
- Qualidade 85→80: proposta `Regenerate`, hash da fonte original preservado,
  novo hash de WebP verificado em disco. Restauração individual retornou bytes
  idênticos ao SHA-256 do PNG original, não à representação WebP do navegador.
- Aplicação de volume real; revisão e histórico paginados por imagem; consulta
  de todas as identidades numa revisão grande; Escape sem efeito; restauração de
  duas transações entre páginas; remoção permanente de **um** original com hash
  conhecido da fixture, sem remover WebP ativo; restauração dos demais originais.
- Snapshot da automação e IDs/chaves/disparos nativos inalterados pelo manual.
  Defaults salvos sem salvar automação. Posteriormente, save explícito da própria
  automação testou bibliotecas selecionadas, vazio recusado, todas explícitas,
  defaults independentes e ausência de operação iniciada. O smoke restaurou o
  snapshot anterior da automação e dos defaults/globais ao terminar.
- Qualidade inválida bloqueada pelo input nativo sem HTTP; credential inválida
  enviada ao servidor verdadeiro produziu 401 e mensagem de erro real na UI
  (getter restaurado imediatamente, sem imprimir token); cancelamento HTTP da
  análise preservou o relatório anterior, sem inventar novo resultado.
- Salvar novos defaults revogou a revisão e exigiu reanálise; somente visitar não.
- en/pt-BR; Dark e Light escolhidos e salvos na **preferência real de exibição do
  host**, devolvendo Dark ao terminar; desktop 1600×1100, mobile 390×844 e tablet
  768×1024, sem overflow horizontal. Aside móvel não fica aderente nem cobre campos.

## Evidência visual inspecionada

Diretório local: `/tmp/opencode/jellyp-native/ui-evidence/final/`.
Log: `/tmp/opencode/jellyp-native/ui-final-smoke.log`.
Resumo machine-readable: `native-summary.json`; sete arquivos `axe-*.json`.
Capturas são do host verdadeiro, não do protótipo:

- `01-native-selection-en-dark-desktop.png`;
- `02-native-automation-en-dark-desktop.png`,
  `03-native-automation-pt-dark-desktop.png`,
  `04-native-automation-pt-dark-mobile.png`;
- `05-native-automation-pt-light-mobile.png`,
  `06-native-automation-pt-light-desktop.png`;
- `07-native-{simulate,settings,history}-pt-light-desktop.png`;
- `08-native-automation-pt-dark-tablet.png`;
- `10-native-analysis-subset-crosspage.png`,
  `11-native-review-exact-subset.png`,
  `12-native-completion-reloaded.png`;
- `13-native-batch-restoration-review.png`,
  `14-native-permanent-retirement-review.png`.

Seleção, conta/revisão, remoção, tema claro mobile/desktop e tablet foram
inspecionados visualmente. As iterações corrigiram rótulos duplicados, vínculo ao
primeiro view oculto, IDs nativos, destinos ARIA em cache e capturas durante a
transição dos controles. A captura final aguarda aprimoramento real e estabilização.

## Limitações e handoff — não declarar aceite universal

- Fundos múltiplos e retenção paginada foram verificados na conferência posterior
  acima; não dependem de correção de índices no cliente. O escritor de UI não
  reescreveu APIs/coordinator/config/Tasks; as correções foram feitas no backend.
- A API de backups expõe fingerprint, não os ajustes históricos completos de
  `PolicySnapshot`; a UI não inventa a qualidade antiga. Nomes são resolvidos pelo
  catálogo real; dimensões/formato antigos nulos continuam desconhecidos.
- Cancelamento da **requisição HTTP de análise** foi exercitado. O cancelamento
  identificado de execução está ligado ao endpoint real, mas não foi reproduzida
  uma espera determinística por metadata ou interrupção entre transações no browser:
  não há endpoint de injeção de espera da fixture; nenhuma espera foi encenada.
- Expiração elegível foi exercitada depois com 52 originais e consentimento real
  na UI. Regeneração protegida sob retenção não-zero, aplicar manual sem guardar
  originais, falha física e recuperação incerta ainda não foram forçadas no
  browser. A suíte backend cobre regras relacionadas, mas não substitui futura
  aceitação nativa desses cenários.
- A fixture tem uma biblioteca; o editor aceita arrays de bibliotecas, mas
  seleção de **duas bibliotecas reais** ainda não foi exercitada. Volume real foi
  14 imagens na jornada e 52 Backdrops no teste específico, não stress nos caps
  5000/1000/200.
- Chromium somente; sem certificação WCAG, leitor de tela, Safari/Firefox ou
  aparelhos físicos. Validação com pessoa sem contexto continua adiada.
- `smoke-ui.cjs`, `smoke-local.py` e smoke de catálogo legado não foram alterados
  ou apresentados como passando contra os novos seletores. O novo smoke é próprio;
  seus resultados não são os 311 checks do protótipo P2.
- Nesta validação não houve commit/push, SSH de produção, instalação no SO,
  publicação, mudança de versão,
  nova dependência ou alteração de auto-review. A única DLL substituída e os únicos
  processos reiniciados foram os da fixture marcada, após conferir comando/root.

### Estado da fixture ao entregar

Conferência read-only final: `Automation.Enabled=false`, `DryRun=true`,
`PreserveOriginals=true`; nenhuma operação ativa, zero backups ativos e zero
disparos nas duas tarefas JellyP. Defaults manuais explícitos com `BatchSize=20`
permanecem da preparação de volume (a automação conserva seu limite próprio de 1).
A biblioteca contém os filmes/PNGs gerados; um original sintético foi aposentado
permanentemente por rodada, como parte do teste, nunca um original de produção.
O host local segue executando a DLL validada para inspeção pelo mantenedor.

## Verificação final da fidelidade — 07/11/2026

Esta seção substitui a evidência visual da primeira integração como referência
da UI atual, sem reclassificar o protótipo como host ou dar aceite de produção.

- Build Release final: **zero erros/avisos**. Nesta correção foram reexecutados
  **181 testes .NET** e **9 de release**, todos aprovados. C# e protótipo P2
  permaneceram intactos em relação a `e4207bf`.
- `smoke-native-ui.cjs --journey`: **248 verificações, zero falhas**, incluindo
  **15 axe**. São 247 verificações ligadas ao host real (incluindo axe) e **uma
  verificação unitária explicitamente identificada** das guardas do renderer de
  preview; ela não injeta respostas de API nem se apresenta como cenário remoto
  real. As 79 verificações anteriores foram preservadas.
- `smoke-native-planning.cjs`: **16 verificações nativas aprovadas** novamente
  com a mesma DLL final: índices reais de Backdrop, subset e restauração exatos,
  seis páginas/52 IDs de retenção, fingerprint enviado pela UI, 52 originais
  expirados e WebP ativos preservados. A autorização separada está no aside;
  todas as identidades permanecem acessíveis na coluna principal.
- **Sem `JELLYP_UI_SOURCE` nas rodadas finais.** O teste compara o HTML servido
  pela DLL byte a byte com `Web/config.html`. Nenhuma resposta de API é mockada;
  apenas tráfego externo é bloqueado. Zero erros de página. O resumo registra
  três requisições locais abortadas (`UserViews`, `SyncPlay/List` e o cancelamento
  intencional de `Plan/Analyze`), não as omite nem alega zero abortos.
- SHA-256 do build e da DLL instalada, iguais:
  `f6c32e70fee3acd39bcee7a41d252c5bf0d145bb9dd963f665ad2b52e877bb73`.
- Sintaxe do JS embarcado e do smoke, `git diff --check` e auditoria de
  publicação (**76 arquivos de texto**) aprovados. Nada foi publicado.

Evidência reproduzível:

```sh
JELLYP_PLAYWRIGHT_MODULE=/tmp/opencode/jellyp-native/browser/node_modules/playwright \
JELLYP_AXE_MODULE=/tmp/opencode/jellyp-native/browser/node_modules/@axe-core/playwright \
node scripts/smoke-native-ui.cjs --root /tmp/opencode/jellyp-native \
  --screenshots /tmp/opencode/jellyp-native/ui-evidence/fidelity --journey
JELLYP_PLAYWRIGHT_MODULE=/tmp/opencode/jellyp-native/browser/node_modules/playwright \
node scripts/smoke-native-planning.cjs --root /tmp/opencode/jellyp-native \
  --screenshots /tmp/opencode/jellyp-native/ui-evidence/fidelity/planning
```

Logs: `fidelity-final.log` e `fidelity-planning.log`, dentro da raiz descartável.
Resumo: `ui-evidence/fidelity/native-summary.json`; resultados axe separados.

### Inspeção renderizada, não apenas seletores

O diretório `ui-evidence/fidelity/` contém referências `p2-reference-*` das seis
telas do estudo (seleção, resultado, revisão, automação, histórico, configurações),
em desktop/celular, separadas das capturas **nativas**. Resultado, revisão e
revisão sem preservar originais têm matriz `fidelity-{result,review,review-remove}`
**en/pt × Dark/Light × desktop/celular**, com página inteira e `*-viewport.png`.
As capturas `01`–`14` cobrem seleção, demais áreas, conclusão e manutenção real;
`fidelity-removal-*` mostra confirmação destrutiva habilitada nos dois temas.
`planning/native-retention-page6.png` registra a sexta página da retenção real.

Foram inspecionados P2 versus nativo: seleção/resumo, resultado com identidade,
toolbar e conta após a lista, revisão/aside, conclusão, automação, configurações,
histórico/remoção; também o topo móvel e os consentimentos/Apply em Dark e Light.
`*-aside-crop.png` são recortes das capturas finais, não novos estados simulados.
Capturas de página inteira podem registrar elementos fixos do shell na posição
de rolagem; os arquivos `*-viewport.png` mostram a janela real no topo.

A inspeção encontrou sobreposição dos consentimentos longos no aside: o host
12.1 fixa a altura de `.emby-checkbox-label`. A correção restrita ao plugin
mantém o checkbox/outline gerados pelo host, permite altura automática e alvo
mínimo de 44 px. A rodada final verifica geometricamente que cada texto cabe no
próprio rótulo nos dois idiomas, temas e breakpoints. O estado indeterminado
também é desenhado no outline nativo existente, pois o CSS do host só cobria
marcado/desmarcado. Capturas finais foram reinspecionadas sem sobreposição.

Outras verificações novas: círculos/estados reais das etapas e Enter/Space;
propriedade de Apply/conta/consentimentos pelo aside; empilhamento móvel; RGB e
contraste ≥4,5 do vermelho preenchido com texto branco **habilitado**; restauração
sem danger; prévias decodificadas com `naturalWidth > 0` e requisição do slot
exato, inclusive Thumb sem fallback; seleção página/todas/limpar/indeterminado,
subset cross-page e preservação do foco; saldo âmbar/verde com número, sinal e
verbo; cópia real de Padrões globais em en/pt preservando alcance/tipos/originais
sem qualquer gravação; leitura de escopo antigo com nomes reais sem migração e
salvar ambíguo bloqueado; ausência de jargão na microcopy principal.

**Atualização 09/11/2026:** os limites acima são históricos desta seção. A
rodada integrada final fechou no host real, em Chromium, a espera determinística
por metadata com navegação/volta/reload/offline/reconexão sem cancelamento
implícito, a seleção de duas bibliotecas reais, a aplicação sem guardar originais
e a regeneração protegida sob retenção não-zero. Os números e as camadas exatas
estão em [ALPHA-CLOSURE.pt-BR.md](ALPHA-CLOSURE.pt-BR.md) seção 8; a ressalva de
que axe não é certificação WCAG e que Firefox/Safari/leitor de tela/dispositivos
físicos continuam não validados permanece. Os caps 5000/1000/200 continuam sem
stress e não bloqueiam a alpha declarada.

### Limites e estado final

Não foi forçado um slot remoto/ausente no host nesta correção: a barreira desses
estados é testada separadamente na função real de renderização, sem API mockada.
Não se declara teste real da falha de carregamento de imagem, leitor de tela,
Safari/Firefox, aparelho físico, compreensão sem contexto ou certificação WCAG.
Revisão sem guardar originais foi exercitada visualmente/por consentimentos, mas
não submetida a Apply; a remoção real desta jornada é a aposentadoria seletiva
do original sintético identificado. Demais limites da integração continuam acima.

Conferência read-only após as duas suítes finais: `Enabled=false`, `DryRun=true`,
`PreserveOriginals=true`, nenhuma operação ativa/pendente, zero backups ativos e
zero disparos nas duas tarefas JellyP. Socket somente `127.0.0.1:18096`.
Somente processos/DLL da fixture marcada foram reiniciados/substituídos.
`e4207bf` continua como HEAD; quatro arquivos de UI/teste/documentação modificados,
sem novo commit, push, SSH, produção, serviço persistente ou auto-review alterado.

## Refinamento do feedback de navegação, aderência e conclusão — 07/11/2026

Esta seção é a evidência mais recente. As alterações de fidelidade anteriores
foram preservadas. O novo escopo foi autorizado explicitamente para a UI e a
fixture descartável, incluindo o card separado de restauração. Não houve mudança
no backend, protótipo P2, contratos, produção, configuração de auto-review,
commit ou push. `UX-P3-INTEGRATION.pt-BR.md`, já modificado antes desta rodada,
foi preservado sem nova edição nesta rodada.

### Causa real e correção do sticky

Inspeção dos ancestrais no Jellyfin **12.1 real** encontrou:

- Quem rola é `document.scrollingElement` (**HTML**), não o formulário, a página
  absoluta do plugin, nem um contêiner com altura própria dentro do JellyP.
- O Jellyfin aprimora o **wrapper do próprio plugin**,
  `#JellyPConfigPage > [data-role=content]`, com `overflow:hidden`. Esse ancestral
  não rolava, mas interceptava o referencial de `position:sticky`. Por isso o
  aside anterior declarava sticky e, mesmo assim, saía da janela junto do form.
- O cabeçalho fixo visível do dashboard é `header.MuiAppBar-root` (48 px nesta
  fixture). `.skinHeader` existe, mas está oculto nesse shell. Medir somente ele
  produz offset zero e deixa o topo do menu/aside atrás do cabeçalho verdadeiro.

A correção é **restrita ao wrapper do plugin**: `overflow:clip visible` corta
somente no eixo horizontal sem criar outro scroll container. Nenhum CSS global,
body, altura/scroll do shell ou transformação do Jellyfin foi alterado. Menu e
asides usam sticky CSS real; não há fallback fixed, listener de scroll ou sleeps
de inicialização. Um `ResizeObserver` lê o cabeçalho visível, com folga de 16 px,
e é desconectado em `viewhide`/`viewdestroy`, assim como o listener de resize.

Asides desktop têm altura máxima da janela disponível e rolagem interna nativa;
consentimentos e Apply são alcançáveis sem esconder o restante do painel. No
celular permanecem em fluxo normal, sem altura máxima ou overflow interno. As
barras de seleção ficam aderentes ao **fundo da própria região de lista**, com
superfície `paperList`, safe area móvel e margem de foco. Não persistem em outra
área nem sob diálogo modal. Ao chegar ao fim da região, o sticky libera
normalmente: não foi substituído por uma barra global fixa.

### Hierarquia e componentes preservados

- Um helper de navegação gera `a is="emby-linkbutton"` com o mesmo acento
  discreto nas cinco áreas, inclusive **Alterar em Configurações**. Ações
  secundárias benignas continuam botões sem `raised`, com hover, active,
  foco visível e disabled coerentes. Primárias e destrutivas reais mantêm sua
  hierarquia; restauração não recebe vermelho destrutivo.
- **Remover originais** na navegação local agora é destino de página com ícone,
  texto vermelho moderado, underline ativo e `aria-current="page"`; não é um
  botão vermelho preenchido, nem um falso ARIA tab. Enter e Back foram testados.
  Revisão, confirmação e remoção individual continuam vermelhas preenchidas,
  com texto branco e contraste medido ≥4,5.
- Paginador único compartilhado pelas sete listas: anterior/intervalo-total/
  próxima próximos entre si, ícones Material reais, rótulos, alvos ≥44 px e
  disabled legível, sem raised/blur. IDs, offsets, dez linhas e seleção exata
  entre páginas não mudaram. A retenção real também foi verificada em desktop
  e celular, inclusive o disabled da última página.
- A seleção da simulação é um bloco próximo do cabeçalho da lista: checkbox
  nativo de página, selecionar todas com quantidade explícita, limpar e uma
  explicação curta do alcance. Não há três CTAs cinza equivalentes. Zero inicial,
  indeterminado do host, subset cross-page e conta depois da lista permanecem.
- Primárias dos asides ocupam a largura disponível. As duas ações do resumo e
  os atalhos equivalentes nos outros asides compartilham o centro do painel,
  sem centralizar indiscriminadamente links fora dele. O menu recupera ícones
  decorativos e separador entre Simular/Executar e as três áreas de gestão. O
  select móvel continua com exatamente cinco destinos, sem ícones nos labels;
  o separador não interfere na navegação horizontal de tablet. **Última operação**
  usa ícone separado e alinhamento central, sem seta órfã.
- `.restoreEntry` é uma **paperList própria, irmã de `#JpExecutionBody`**, com
  espaço real entre cards, título h2 e CTA secundário contornado visível. Não há
  atalho de restauração dentro do card de resultados: ali só Nova simulação.
  Disponibilidade vem de `Backups?filter=restorable&limit=1`, não de estado
  inventado. Apply oferece acesso aos originais verificados disponíveis; Restore
  e Retire falam de **outros** originais quando existem, sem prometer desfazer
  remoção permanente. Após restaurar o último original, não existe CTA
  indisponível/duplicado. A consulta não amplia seleção nem autoriza mutações.

### Provas de rolagem real e inspeção visual

Diretório: `/tmp/opencode/jellyp-native/ui-evidence/navigation-sticky/final/`.
Os pares `sticky-*-before.png` / `sticky-*-after.png` são **capturas da janela**,
não screenshots longos que reposicionam elementos fixos. Cada par tem JSON com
scroll owner ativo, `scrollTop`, viewport e retângulos de conteúdo normal,
menu, aside/barra e cabeçalho. Exemplos da rodada final:

| Caso | Scroll HTML | Conteúdo normal: top | Elemento aderente |
|---|---:|---:|---|
| Simular desktop | 180 → 400 | 14,03 → −205,97 | menu/aside top 64 → 64 |
| Automação desktop | 180 → 400 | −37,41 → −257,41 | menu/aside top 64 → 64 |
| Revisão desktop | 218 → 438 | 55,59 → −164,41 | menu/aside top 64 → 64; fundo 884 |
| Resultado desktop | 809 → 1029 | 220,78 → 0,78 | barra: fundo 888 → 888 |
| Resultado mobile | 1350 → 1570 | 283,80 → 63,80 | barra: fundo 844 → 844 |
| Restauração mobile | 677 → 897 | 399,52 → 179,52 | barra: fundo 844 → 844 |
| Remoção mobile | 863 → 1083 | 285,86 → 65,86 | barra: fundo 844 → 844 |

Desktop determinístico 1600×900, mobile 390×844; cabeçalho real termina em 48 px.
Também há pares de Configurações desktop e asides mobile: estes últimos se
deslocam **220 px** junto do conteúdo, comprovando fluxo normal. O teste foca o
checkbox nativo da última linha e mede seu **outline gerado pelo host**, acima da
barra e abaixo do cabeçalho. `sticky-apply-review-desktop-actions.png` comprova
consentimento e Apply alcançados pela rolagem interna do aside alto.

Foram inspecionados os pares de revisão/automação desktop, resultado mobile,
menu/cabeçalho, remoção mobile, História light e a matriz de conclusão dark/light
desktop/mobile. `fidelity-completion-{en,pt}-{dark,light}-{desktop,mobile}.png`
mostra o card separado. `completion-restored-no-originals-desktop.png` mostra a
conclusão real sem CTA quando não resta original. As referências `p2-reference-*`
permanecem identificadas como protótipo, não host. O harness agora aguarda o
desaparecimento do aviso real `.toastContainer .toast` do Jellyfin antes das
capturas, sem removê-lo por CSS ou sleep arbitrário.

### Resultado final instalado e limites

- Build Release: **0 erros, 0 avisos**; **181 testes .NET** e **9 de release**
  aprovados nesta rodada. Sintaxe dos dois smokes e do JS inline, `diff --check`
  e auditoria de publicação (**76 arquivos de texto**) aprovados.
- `smoke-native-ui.cjs --journey`: **428 verificações, zero falhas**, incluindo
  **17 axe** sem violações nas regiões auditadas. São **427 verificações ligadas
  ao host** e **uma guarda unitária** do renderer de previews, explicitamente
  separada. As regressões anteriores foram mantidas; não houve API mockada.
- `smoke-native-planning.cjs`: **18 verificações** aprovadas: as 16 anteriores
  mais duas de paginação nativa da retenção desktop/mobile. Fundos, hashes,
  fingerprint e expiração exata de 52 originais continuam aprovados.
- **Sem `JELLYP_UI_SOURCE` nas rodadas finais.** O smoke compara byte a byte o
  HTML realmente servido pela DLL com `Web/config.html`. Build e DLL instalada
  têm SHA-256 igual:
  `9c607a39b2c7109a388f8e26206c3718fe385f8fb6088f3b20cfbfee873b9e27`.
- Logs na raiz descartável: `navigation-final.log`, `navigation-planning.log`,
  `navigation-build.log`, `navigation-dotnet-tests.log`,
  `navigation-release-tests.log`. Resumos e 17 relatórios axe ficam no diretório
  `final/`; o resumo específico de retenção fica em `final/planning/`.
- Zero erros JS. Três abortos locais registrados, não ocultados: `UserViews`,
  `SyncPlay/List` e o cancelamento intencional de `Plan/Analyze`. A única tentativa
  externa é o Cast sender oficial bloqueado; o plugin não depende dele.

Na iteração de desenvolvimento, medições identificaram testes que atravessavam
o fim correto da região sticky (revisão curta e última página de duas linhas),
e um foco já ativo reaproveitado após mudar viewport. O harness passou a usar o
intervalo aderente real, a primeira página completa e uma transição real de foco;
não se alterou o produto para fixar barras fora de seus limites. Uma rodada
interrompida após conversão deixou 12 originais sintéticos: foram conferidos
contra os hashes dos PNGs gerados e restaurados pela API real antes de recomeçar.
Falhas de iteração não são contadas como aprovações finais.

Continuam os limites anteriores: Chromium em desktop emulado, não aparelhos
físicos, Safari/Firefox, leitor de tela ou certificação WCAG. Os retângulos de
scroll foram medidos no tema Dark; os temas Dark/Light têm matriz renderizada,
overflow, componentes, contraste e axe, não uma certificação de todos os shells
ou temas personalizados. Não houve nova direção visual ou autorização de produção.

Conferência read-only após as duas suítes finais: `Enabled=false`, `DryRun=true`,
`PreserveOriginals=true`, zero operações ativas/pendentes, zero recuperação
pendente, zero backups ativos e zero disparos nas duas tarefas JellyP. O HTML
servido continua idêntico ao fonte; somente a fixture marcada foi reiniciada.

## Seleção, asides e recibos específicos — feedback posterior a `6623bec`

Esta rodada substitui a decisão anterior de rolagem interna dos asides. O
protótipo aprovado é referência somente de leitura; backend, contratos e shell
do Jellyfin permanecem intactos. Não houve commit/push, SSH, systemctl, produção,
configuração persistente do SO, agentes adicionais ou alteração de auto-review.

### Diagnóstico e implementação

- **Aside alto:** a regressão vinha do `max-height` com `overflow:auto` acrescentado
  na rodada anterior, não de um segundo scroll owner imposto pelo host. Mantém-se
  o reparo restrito ao wrapper do plugin (`overflow:clip visible`). Menu e asides
  agora têm altura intrínseca completa e `top = min(header + 16px, viewport −
  altura do painel − 16px)`. Painéis curtos aderem abaixo do header; altos rolam
  com a página até a borda inferior, aderindo enquanto houver espaço na própria
  região. Ao voltar, o scroll continua pertencendo à página. `ResizeObserver`
  mede conteúdo real/cabeçalho e acompanha mudanças de idioma, largura e altura;
  antigos painéis são desobservados, e os listeners são desligados ao ocultar ou
  destruir a view. Não há handler de scroll, clone fixed ou alteração de body.
  Mobile continua em fluxo normal.
- **Seleção global:** não foi encontrado um motor que selecionasse somente a
  página. `Plan/{id}/Keys?kind=selectable` já retornava o conjunto global do
  snapshot; faltavam rótulos/subtotais inequívocos. Simulação e manutenção agora
  usam o mesmo componente: checkbox **Selecionar todos desta página (N)**,
  ação **Selecionar todos os itens (N)**, limpar discreto, contagem global e
  subtotal selecionado/elegível da página. A explicação distingue propostas do
  plano e originais elegíveis. O total elegível de manutenção vem da página com
  filtro explícito; ao exibir também protegidos, uma consulta `limit=1` com filtro
  de elegibilidade evita usar esse `Total` mais amplo. A ação global continua
  consultando as chaves reais; caps 1000/200,
  zero inicial, seleção entre páginas, revisão exata e fingerprint de retenção
  permanecem. Limpar vazio fica disabled; mixed/checked refletem só a página.
- **CTAs:** centralizar a caixa e `text-align` não centralizava os filhos do
  `inline-flex` nativo. Todos os botões/links dos asides recebem `display:flex`,
  `justify-content:center` e centralização dos spans de texto, inclusive ações
  dentro de wrappers. Parágrafos e labels de checkbox não foram centralizados.
- **Menu:** `.jp-link` aplicava ciano a todos os destinos. Menu e último recibo
  agora herdam foreground neutro do host; somente a área atual usa acento,
  indicador e superfície. Ícones herdam o estado; hover/foco e separador continuam.
- **Automação:** a seção de tarefas era irmã externa de `.jp-work`, por isso
  ocupava as duas colunas. Agora é uma área real do mesmo grid, abaixo do form e
  somente na coluna principal; o aside ocupa sua própria coluna. No celular a
  ordem é form → salvar → tarefas. Nomes, estado e agendamento são explícitos;
  chaves/GUIDs e triggers crus ficam em detalhes técnicos. Orientação de
  agendamento no Jellyfin e último relatório têm seções distintas. Nenhum
  agendamento foi criado, nem alcance ou origem de relatório inventados.
- **Histórico:** havia um defeito real no frontend. `readRoute()` lia o recibo
  correto, mas a hidratação posterior de um plano já consumido sobrescrevia
  `operationId` com a execução desse plano (e a operação ativa também podia
  substituir o destino explícito). O URL agora prevalece após a hidratação;
  trocar recibo limpa detalhe/offset antigos. Respostas atrasadas de outra
  identidade/página/view são descartadas. O contexto visível inclui tipo,
  data/ID, resultados daquele recibo e retorno ao Histórico. Paginar sinaliza
  `aria-busy`; reload conserva o offset do mesmo recibo. Não se usa `LastRun` nem
  uma cópia do último relatório para reconstruir operações antigas.

### Fixture e iterações — não confundir com aprovações finais

A primeira jornada recusou os nove originais ativos já presentes na fixture.
Foram identificados oito PNGs pelos hashes de `native-png/` e o poster da
biblioteca descartável `JellyP fixture`, com diretório de metadata dentro da raiz
verificada. Os nove foram restaurados pela API real (`Changed=9`, `Failed=0`).
Não se presumiu que o nono hash fosse um dos PNGs: ele pertencia ao poster da
biblioteca. Nas iterações, verificações novas também identificaram leituras do
harness antes da conclusão de paginação/seleção assíncrona. As esperas passaram
a usar `aria-busy`, identidade/intervalo e contador reais; nenhum DTO/API foi
mockado e nenhum critério de identidade foi relaxado. Originais sintéticos de
rodadas interrompidas foram conferidos por hash e restaurados antes de repetir.
Uma primeira rodada instalada chegou ao fim da jornada e foi reprovada pela
auditoria adicional da tela inicial: os números pequenos das três seções tinham
contraste 4,41:1 no Dark. Sua opacidade passou de 0,65 para 0,8; a regra axe não
foi excluída. Essa tentativa está separada em `installed-attempt/` e não conta
como aprovação final. A inspeção também corrigiu a precedência CSS do foreground
neutro de Limpar seleção; há uma verificação computed específica desse estado.
A verificação nova de hover também tentou acessar o menu por trás de um diálogo
nativo aberto. O host corretamente interceptou o ponteiro; o harness agora não
tenta interagir com esse fundo enquanto o modal possui o foco. Não usa `force`,
remoção de backdrop ou bypass de acessibilidade. Essa tentativa está separada em
`installed-modal-harness/`; não é uma falha de navegação do produto nem aprovação.

### Evidência final

- Build Release final: **0 erros, 0 avisos**; **181 testes .NET**, **9 testes de
  release**, sintaxe dos dois smokes/JS embarcado e `git diff --check` aprovados.
- UI completa: **743 verificações aprovadas**, sendo **742 ligadas ao host** e
  **uma guarda unitária** do renderer de preview já identificada separadamente.
  Inclui **23 auditorias axe** A/AA sem violações nas regiões auditadas.
- Planning completo: **20 verificações reais aprovadas**. Preserva índices/hashes
  de Backdrop, subset exato, restauração e expiração de 52 originais com o
  fingerprint autorizado. Acrescenta ausência de nested scroll e acesso ao Apply
  centralizado pela página após percorrer a retenção até `51–52 of 52`.
- **Sem `JELLYP_UI_SOURCE` e sem respostas de API mockadas** nas rodadas finais.
  O HTML da DLL foi comparado byte a byte com o fonte antes da jornada e novamente
  após as duas suítes. Nenhum erro JS; um aborto local registrado, o cancelamento
  intencional de `Plan/Analyze`. A tentativa externa do Cast sender oficial foi
  bloqueada, como nas rodadas anteriores.
- SHA-256 do build e da DLL instalada, iguais:
  `f3e1e972947246c95ee467ed666f9e03a2856e1ae547d6795e9136a582d7001d`.

Comandos finais reproduzíveis, na raiz do repositório:

```sh
JELLYP_PLAYWRIGHT_MODULE=/tmp/opencode/jellyp-native/browser/node_modules/playwright \
JELLYP_AXE_MODULE=/tmp/opencode/jellyp-native/browser/node_modules/@axe-core/playwright \
node scripts/smoke-native-ui.cjs --root /tmp/opencode/jellyp-native \
  --screenshots /tmp/opencode/jellyp-native/ui-evidence/selection-aside-refinement/final --journey
JELLYP_PLAYWRIGHT_MODULE=/tmp/opencode/jellyp-native/browser/node_modules/playwright \
node scripts/smoke-native-planning.cjs --root /tmp/opencode/jellyp-native \
  --screenshots /tmp/opencode/jellyp-native/ui-evidence/selection-aside-refinement/final/planning
```

Logs na raiz descartável: `refinement-final-ui.log`,
`refinement-final-planning.log`, `refinement-build.log`,
`refinement-dotnet-tests.log`, `refinement-release-tests.log`.
Resumos: `final/native-summary.json`, `final/planning/planning-summary.json`.

### Regressões e inspeção renderizada

- Simulação: página 1 seleciona 10; global seleciona as 14 chaves reais; página 2
  mostra 14 no total e 4/4 locais. Limpar zera ambas as páginas; subset entre
  páginas mostra 2 no total e 1/4 locais, com mixed nativo e conta exata após a
  lista. Restauração e remoção repetem página → global → limpar → subset entre
  páginas sobre originais elegíveis reais. Nenhuma autorização foi inferida da
  página visível. O servidor continua recebendo os IDs/chaves exatos revisados.
- Histórico: duas operações novas persistidas, Apply com **duas imagens Changed**
  versus Restore com **uma imagem Restored**, foram abertas pelos próprios links
  dos recibos. O teste compara ID da API, chaves/status efetivamente renderizados
  e hashes armazenados; detecta tanto trocar IDs quanto exibir o mesmo resultado
  recente sob URLs diferentes. Repete após reload, atualização real por
  visibilitychange e Back/Forward. Uma aplicação longa também conserva sua
  segunda página de resultados após reload.
- Aside alto: em 1600×900, o painel de revisão conserva **1063,09 px** de altura;
  rolar HTML de 218 para 438 desloca seu topo de 55,59 para −164,41, sem scroll
  interno, enquanto o menu permanece em 64 px e o header termina em 48 px. Os
  testes alcançam a aderência inferior, voltam usando wheel sobre o painel e
  redimensionam para 640 px de altura; Apply continua acessível pela página.
- Foram inspecionados os screenshots finais reais de toolbar mobile Light,
  revisão/Apply desktop Dark e mobile Light, Simular/Automação/Configurações com
  CTAs centralizados, menu neutro/ativo, tarefas na coluna principal, os dois
  recibos distintos e retenção com consentimentos/Apply visíveis. Matrizes
  `fidelity-*` e `detail-*-viewport.png` cobrem **en/pt × Dark/Light ×
  desktop/mobile**; também há tablet 768×1024. Exemplos:
  `detail-result-pt-light-mobile-viewport.png`,
  `detail-review-pt-dark-desktop-viewport.png`,
  `fidelity-aside-automation-pt-light-desktop.png`,
  `detail-aside-settings-en-light-mobile-viewport.png`,
  `receipt-selected-apply.png`, `receipt-selected-restore.png` e
  `planning/native-retention-aside-page-scroll.png`. Os pares `sticky-*` e JSONs
  distinguem movimento da página, aderência e fluxo mobile. Referências P2 seguem
  separadas e identificadas, nunca contadas como host.

Limites: Chromium emulado, não aparelhos físicos, Safari/Firefox, leitor de tela
ou certificação WCAG. Reflow por resize/conteúdo/idioma foi exercitado; zoom pelo
comando do navegador não foi forçado separadamente. Não houve stress nos caps
1000/200 nem cenário novo de protegidos/incertos criado artificialmente. Os
demais limites anteriores permanecem; não se declara aceite de produção.

Conferência read-only final: `Enabled=false`, `DryRun=true`,
`PreserveOriginals=true`, **zero backups ativos**, **zero operação ativa**,
**zero recuperação pendente**; ambas as tarefas Idle e sem disparos. Socket
somente `127.0.0.1:18096`. A fixture continua executando a DLL validada; HEAD
continua `6623bec`, com alterações somente em UI, dois smokes e este documento.

## Gates de host V08/V10/V15 — 09/11/2026

Esta seção registra uma rodada **backend/host**, não uma nova jornada de UI. Em
uma raiz nova e marcada (`/tmp/opencode/jellyp-host-gates-proof5-20261109`), com
credenciais novas, Jellyfin 12.1 apenas em loopback e HEAD `e21328a`, passaram
**80 checks de host** no candidato cuja DLL tem SHA256
`3AE44BA2A35FAFC08EDF03D91D0BCF5E9FE0B3F981EC578089E0E5330FBC3D82`.

- V08: cancelamento pelo endpoint real e `operationId` exato antes da primeira
  mutação (plano `Ready`, zero journal) e entre transações depois de uma mutação
  real (plano `Consumed`, `Changed=1`, `NotStarted=1`, segundo item intacto).
- V10: `IntervalTrigger` real iniciou `OptimizeArtworkTask`; refresh relacionado
  produziu `WaitingForMetadata` e retomou; refresh não relacionado permaneceu
  `Active` durante a conversão do alvo e não bloqueou.
- V15: `MutationStarted` e journal `Prepared` observados; SIGSTOP/SIGKILL via
  pidfd somente do PID verificado por argv/cwd/uid/starttime/socket; reinício da
  mesma DLL marcou `Interrupted`, `PendingRecovery=1`, sem replay; recuperação
  explícita restaurou o PNG exato e deixou journal `RolledBack`. O caso observado
  foi **Prepared-before-save, sem WebP antes do kill**; a remoção de saída não
  foi alegada (`outputRemovalProven=false`).

O provedor de metadata que mantém o refresh aberto é um auxiliar de teste em
`tests/host-fixture/JellyP.TestSlowMetadata`, instalado somente na fixture; não
faz parte do produto e não é endpoint de falha. Navegador/desconexão/foco, disco
fisicamente cheio, erro de permissão de SO e falha de hardware não foram
exercitados. Os números históricos **121/819/20** de UI/planning não foram
reproduzidos nesta rodada. Detalhes e comandos estão na seção 7 do
[fechamento](ALPHA-CLOSURE.pt-BR.md) e em [VALIDAÇÃO](VALIDATION.md).
