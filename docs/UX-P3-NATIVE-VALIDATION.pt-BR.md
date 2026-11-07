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

## Conferência posterior do integrador — 07/11/2026 (build final)

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

- `Jellyfin.Plugin.JellyP/Web/config.html`: substituição real da página final,
  mantendo `data-role="page"`, `type-interior`, `pluginConfigurationPage`,
  `data-require`, `ApiClient` e `Dashboard`. CSS/JS inline embarcados no recurso
  existente: nenhum problema de autenticação de assets externos, novo framework,
  endpoint, alteração em `Plugin.cs` ou mudança no `.csproj`.
- Cinco áreas dentro do shell verdadeiro, sem copiar topbar/sidebar do Jellyfin.
  Coluna JellyP alinhada ao conteúdo, navegação horizontal em tablet e select
  nativo no celular. Formulários e aside de salvar separados; superfícies e cores
  dos controles vêm do tema do host, não de um sistema escuro próprio.
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

## Verificação final — recursos realmente instalados

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

- Marcador e `System/Info.ProgramDataPath` conferidos antes de qualquer mutação.
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
