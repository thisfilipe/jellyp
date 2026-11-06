# P2 — Desenho, protótipo e revisão técnica

Registro inicial: **17/10/2026**. Revisão visual/UX: **05/11/2026**.
Referências: [contrato P1](UX-P1-CONTRACT.pt-BR.md) ·
[especificação](UX-REFACTOR-PLAN.pt-BR.md) · [plano](PLAN.md).

**Status:** protótipo revisado a partir de feedback real com contexto, renderizado
e exercitado automaticamente. O ensaio relatado não equivale a validação sem contexto.
**Decisão de 06/11/2026:** direção de produto aceita pelo mantenedor para
implementação nativa. A validação com pessoa sem contexto fica **adiada, sem
bloquear o avanço**; não está aprovada. Este estudo não valida componentes reais
do Jellyfin nem a interação com o backend. Isso será implementado e validado por
jornadas integradas em P3–P7, preservando serviços funcionais existentes.
A atualização do plano autoriza documentação, não runtime, backend, produção,
instalação ou release. Não confundir a implementação deste estudo com P4–P6.

O exercício continua disponível quando houver oportunidade. Não é necessário
prolongar o protótipo sintético aguardando participantes: suas correções seguem
como referência de produto, e novas evidências do host real podem revisar o desenho.
Feedback independente/assíncrono pode continuar durante o alpha, sem dispensar
validação técnica de integridade, migração e recuperação antes da entrega.

### Retorno após os testes de paginação e automação

O ensaio com contexto relatou sucesso na seleção com saldo dinâmico, seleção
entre páginas, restauração e exclusão seletivas. Também identificou que colocar
a calculadora antes dos itens prejudicava a sequência de trabalho: o saldo foi
reposicionado **depois da lista**, mantendo atualização pela seleção. O smoke
verifica tanto a ordem no DOM quanto a posição renderizada.

A análise agendada é diagnóstico antes de habilitar alterações automáticas,
não um plano aguardando aprovação. A microcopy agora explica que mudar o modo
não aplica o relatório anterior: a próxima tarefa reanalisa as imagens.
Isso não adiciona um fluxo de aprovação agendada nem conclui a validação sem contexto.

Conferência desta correção: **311 verificações, zero falhas**, incluindo 20 axe;
sintaxe e `git diff --check` aprovados. Capturas regeneradas em
`/tmp/opencode/jellyp-p2-bill-after-list/`; tela de simulação inspecionada.

## 1. Abrir e experimentar

Abrir [ux-prototype/jellyp-p2.html](ux-prototype/jellyp-p2.html) diretamente no
navegador, sem servidor ou login. Manter na mesma pasta seus dois arquivos
auxiliares: `jellyp-p2.css` e `jellyp-p2.js`. Não é mais um HTML autocontido.
Não há bibliotecas JS, fontes remotas, API, armazenamento persistente ou credenciais.
Recarregar reinicia as fixtures; navegar entre áreas preserva o estado da sessão.

A faixa fixa **PROTÓTIPO SINTÉTICO** distingue o estudo do host. O cabeçalho
“Jellyfin / Administração / Plugins” é contexto visual, sem controles falsos.
As pequenas ilustrações geométricas “P2” são CSS próprio, não capas de mídia.

### Jornada principal

1. **Simular:** selecionar bibliotecas, itens ou explicitamente tudo; marcar tipos,
   editar qualidade e escolher preservação. Um único botão discreto por tipo é
   também o status: **Usando ajustes deste teste** / **Usando ajustes próprios**,
   com borda, chevron para baixo/cima e `aria-expanded`/`aria-controls`. Não há
   label de status repetido nem botão adicional “Editar ajustes deste tipo”.
   “Usar padrões globais” copia explicitamente os valores definidos em
   Configurações, após confirmação, sem salvar ou alterar automação, alcance,
   tipos ou preservação. As ações secundárias do resumo são alinhadas à esquerda.
2. **Simular → resultado:** a análise substitui o editor na mesma área. Snapshot
   identificado, ações por imagem e motivos ficam juntos, sem procurar abaixo de
   um formulário extenso. O foco e o anúncio chegam a “Simulação concluída —
   nenhuma imagem foi alterada”. A narrativa explica ações, preservação,
    restauração, espaço usado e próximo passo antes dos detalhes de IDs/SSIM.
   A análise é local e síncrona, sem atraso artificial ou fase de servidor fictícia.
   Qualidade 80 propõe regenerar a capa de Viagem a partir
   do original; a miniatura pode ser convertida; WebP externo é mantido e um filtro
   rejeita outra imagem. Não existe botão manual separado de sincronização.
3. **Escolher:** nenhuma proposta vem marcada. Checkbox por identidade de imagem;
   “Selecionar propostas desta página” e “de todos os resultados” são ações
   explícitas e diferentes. Sem seleção, revisão bloqueada.
4. **Executar → revisão:** lista apenas as imagens selecionadas, com slots, ajustes,
   ações e impacto do conjunto. Consentimento é obrigatório e funcional. Remover
   originais e expiração global exigem consentimentos separados, não herdados
   da automação. Não há segundo formulário capaz de divergir da análise.
5. **Acompanhar:** inventário sem percentual inventado → processamento → resultado.
   Execução tem identidade, origem e contagens próprias. O indicador global permite
   voltar à operação; navegar não a cancela. A conclusão da encenação é automática.
6. **Histórico e restauração:** a aplicação cria registros da própria seleção.
   O bloco principal da conclusão oferece **Nova simulação**. Somente a seção
   abaixo, **Quer desfazer uma conversão?**, oferece **Ir para restauração**, com
   filtro “Somente restauráveis”. Não há atalho duplicado. Restaurar revisa a
   transação ativa e exibe formato/dimensões/bytes; após confirmar, abre a evidência
   sintética. Transações anteriores do mesmo slot não oferecem restauração.
   **Em lote:** marcar subconjunto ou todos os restauráveis → revisar lista exata
   de transações/formato/dimensões/bytes → confirmar → resultado individual.
   Seleção inicial zero; incertos, removidos e inativos ficam fora. Revalidação
   rejeita registros alterados sem substituição. Recuperação permanece separada;
   restauração/remover ficam bloqueados durante aplicação. Análises, revisões e
   seleções obsoletas são invalidadas. É extensão proposta de produto no
   [adendo P1](UX-P1-CONTRACT.pt-BR.md#8-adendo-de-produto-p2--feedback-com-contexto-05112026),
   não suporte de lote já implementado no backend.
7. **Remover originais:** subpágina própria pelo link local de Histórico, sem
   misturar controles ou seleção de restauração. Marcar → revisar lista/quantidade/bytes → autorizar →
   confirmar. “Todos os elegíveis do plugin” só seleciona, não remove. Incertos
   não são selecionáveis. Escape/voltar no diálogo não altera dados.

Editar alcance, tipos, preservação ou política invalida a análise para aplicação,
mas mantém o relatório consultável. Salvar novos limites/padrões também invalida
a revisão conservadoramente. Fontes/identidades sintéticas são rechecadas antes de
aplicar; uma imagem inválida não é substituída. Isso demonstra o contrato de UX,
**não implementa integridade, idempotência ou autorização de servidor**.

As etapas são botões reais. **Voltar** fica logo abaixo delas, no mesmo lugar no
resultado e na revisão. Resultado → seleção preserva snapshot, valores e subset
até uma edição; revisão → resultado preserva o subset. Alterar o subset revoga a
revisão anterior, sem tornar a análise obsoleta. Etapas futuras exigem snapshot e
seleção válidos. Navegar, inclusive pelo histórico do navegador, não recupera
autorizações consumidas nem apaga a execução. **Nova simulação** inicia nova
intenção sem apagar a última operação; ao admitir outra, o relatório anterior é
arquivado em memória. O histórico continua listando as transações, não é um novo
visualizador completo de relatórios arquivados.

Na revisão, as proteções globais são visíveis, não ficam em `details`: mínimo
livre protegido, teto **total** de originais e tempo para guardar. O botão real
**Alterar em Configurações** avisa, **antes de navegar**, que salvar invalida a
análise/seleção. Abre a seção Armazenamento com foco no título e permite **Voltar
à revisão**. Só navegar ou editar sem salvar preserva a revisão; salvar exige
simular e selecionar de novo, mesmo após voltar/avançar. Apenas Configurações
edita esses valores; nem o teste nem a automação os alteram implicitamente.

## 2. Hierarquia e vocabulário visual

- Tema escuro consistente, superfícies `#1c1c1c` sobre `#101010`, bordas discretas,
  raio de 4 px. Ciano `#00a4dc` para foco/seleção, não gráficos decorativos.
- Ação primária ciano mais escuro (`#007da8`) com texto branco; o ciano original
  não dava contraste suficiente para texto pequeno, detectado no audit automatizado.
- Tipografia de sistema (`Roboto`, `Noto Sans`, Arial): nenhum arquivo de fonte
  copiado. Corpo 15 px no desktop / 14 px no celular; títulos 24–28 px, textos
  auxiliares 12–13 px. Espaçamento principal 12/16/20/24 px.
- Formulário com três seções numeradas e resumo lateral, não grade de cartões
  equivalentes. Resultado usa linhas de imagens, rótulos de ação e detalhes
  progressivos. IDs são secundários, títulos/ano/tipo/posição são principais.
- O armazenamento usa uma **fatura leve**, não uma tabela de métricas: antes →
  depois, cada lado decomposto em imagem ativa e cópia original guardada. O saldo
  destacado usa sinal **+ / −**, âmbar para aumento, verde para liberação e um
  verbo independente da cor. Não chama a cópia original de “novos originais”.
  Simulação e barra mostram somente a seleção, revisão somente selecionadas e
  conclusão somente feitas, com verbo no passado. Zero selecionadas significa
  zero antes/depois e “Sem alterações selecionadas”. O potencial de todas as
  propostas é comparação secundária rotulada, não uma segunda fatura.
- `totals()` soma `antes = imagem ativa + original já guardado` e
  `depois = WebP ativo + original mantido`. Original já guardado só entra no antes
  de regeneração. Guardar reutiliza essa cópia; remover zera o original no depois.
  Saldo interno é `antes − depois`; sinal visual representa a mudança de uso.
  Expiração global de **outros** originais é mostrada e autorizada separadamente,
  nunca entra silenciosamente no saldo das imagens escolhidas.

Exemplos em KiB de fixtures (não resultados reais de compressão):

| Conjunto | Antes | Depois, guardar | Saldo ao guardar | Depois, remover | Saldo ao remover |
|---|---:|---:|---|---:|---|
| Capa restaurada, JPEG 486 → WebP 125 | 486 + 0 = **486** | 125 + 486 = **611** | usa **125 a mais** | 125 + 0 = **125** | libera **361**, irreversível |
| Regenerar WebP 141 → 125, original 486 existente | 141 + 486 = **627** | 125 + 486 = **611** | libera **16** | 125 + 0 = **125** | libera **502**, irreversível |
| Propostas iniciais: regeneração + miniatura 220 → 84 | 361 + 486 = **847** | 209 + 706 = **915** | usa **68 a mais** | 209 + 0 = **209** | libera **638**, irreversível |

Zero, subconjuntos e resultados parciais usam a mesma função; não incluem falhas,
ignoradas ou imagens não iniciadas na conta realizada. Não representam totais do
plugin, arquivos temporários, logs, caches ou overhead do sistema de arquivos.

## 3. Navegação — páginas internas recomendadas

A direção inicial/recomendada é **páginas internas**, conforme a revisão de
produto com contexto. Não se exige uma escolha D1 imediata para continuar. A
**bancada**, recolhida no rodapé, permite comparar opcionalmente as mesmas áreas:

| | Páginas internas (inicial) | Abas |
|---|---|---|
| Desktop | Navegação lateral, área ativa com `aria-current` | Barra `tablist`, indicador e foco móvel |
| Até 980 px | Navegação horizontal | Abas horizontais |
| Até 700 px | Seletor nativo rotulado com todas as cinco áreas | Abas roláveis; teclado/foco fazem a área entrar em vista |
| Estado | Mesmo rascunho em memória | Mesmo rascunho em memória |
| Histórico neste estudo | Hash `#/p2/paginas/área` | Hash `#/p2/abas/área` |

Ambas usam hash real para voltar/avançar e links diretos dentro do estudo.
Etapas usam `/simular/selecao` e `/simular/resultado`; exclusão tem subpágina
`/historico/remover-originais`; o desvio contextual usa
`/configuracoes/armazenamento`. Esses caminhos não são rotas do Jellyfin real.
O desvio da Automação acrescenta `/automacao` a esse endereço, conservando a
origem de retorno também ao voltar/avançar, sem sobrescrever o contexto manual.
Não há URL fictícia `configurationpage` nem alegação de que cinco páginas reais
do plugin foram registradas. A referência de P1 sobre `GetPages()` e
`PluginPageInfo` permanece válida como direção; routing e retenção de rascunho
entre páginas reais precisam ser projetados em P3 e validados em P5.

Isso registra uma direção de produto, não uma validação por pessoa sem contexto
ou prova de integração nativa. Abas não são requisito do exercício básico. D2
continua sendo a análise única; entendimento sem contexto de “gerar novamente a
partir do original” precisa de observação, não de um teste que encontra texto.

## 4. Cobertura de áreas e estados P1

| Área | Comportamento demonstrável | Exceções |
|---|---|---|
| Simular | bibliotecas/itens/tudo; busca local; seis tipos representativos; editor completo por tipo, qualidade/sem perdas/dimensão/SSIM; preservação e limite; análise congelada; escolha explícita | vazio, nenhum tipo, item sem imagens, inspeção indisponível, relatório obsoleto, WebP sem original, filtro, referência remota/compartilhada |
| Executar | conjunto exato; consentimentos; expiração global prevista; progresso/resultado contextual; navegação durante operação | conflito, recuperação bloqueante, espera, cancelamento, fonte invalidada, resultado parcial |
| Automação | uma ou mais bibliotecas ou todas explicitamente; modo/qualidade/tipos/limite/preservação próprios; edição e salvo separados; tarefas distintas explicadas; autorização destrutiva própria | vazio bloqueado, biblioteca removida, alterações não salvas, ociosa ≠ desativada, sem horário, tarefas indisponíveis; horários só no Jellyfin, indisponíveis aqui |
| Histórico e restauração | operações agrupadas; filtro; seleção restaurável; revisão individual/lote; resultado individual; remoção separada e confirmada | anterior inativa, restaurado, removido, incerto preservado, recuperação; revisão obsoleta, bloqueio em execução |
| Configurações | edição separada do salvo; política completa por tipo; mínimo livre/limite total de originais/retenção explicados; filtros avançados progressivos | valores inválidos, não salvo, retenção desde conclusão com previsão em Executar; consequência irreversível visível |

### Bancada do facilitador

Está **abaixo** do produto, não no topo; abrir para preparar cenários e recolher
durante as tarefas. Cada cenário abre a área correspondente. “Outra operação” e
“Recuperação” bloqueiam aplicação sem apagar a revisão. “Fonte alterada” invalida
uma das identidades autorizadas durante a encenação, sem incluir substituta.

“Espera por metadata” pausa a operação ativa ou cria uma operação de exemplo se
não houver trabalho ativo. “Próxima aplicação aguardará metadata” permite preparar
a espera **sem criar operação**, preservando a futura seleção do participante.
Voltar a “Jornada normal” durante espera retoma a encenação. Cancelar espera não
inventa imagens convertidas; cancelar processamento termina a transação sintética
atual antes de parar. O resultado parcial é uma **operação de exemplo distinta**,
com quatro imagens e resultados separados, não números inventados para a seleção
do participante. Recomeçar limpa toda a sessão sintética.

## 5. Responsividade, teclado e verificação

Desktop usa duas colunas; até 700 px, formulário/revisão em coluna, métricas da
revisão empilhadas e resumo inicial sem repetir todos os campos. Barra de seleção
permanece junto ao limite inferior da janela. Diálogo nativo fornece modal,
contenção de foco e Escape. Controles rotulados, foco visível, `aria-current`,
`aria-selected`, link de salto e região de anúncio; abas suportam ←/→/Home/End.
Digitação numérica e atualizações de progresso preservam os controles em foco;
digitar e clicar em simular funciona sem precisar clicar duas vezes. A encenação
não usa movimento essencial e respeita `prefers-reduced-motion`.

Comando reproduzível (módulos instalados fora do projeto):

```sh
JELLYP_PLAYWRIGHT_MODULE=/caminho/para/playwright \
JELLYP_AXE_MODULE=/caminho/para/@axe-core/playwright \
node scripts/smoke-prototype.cjs --screenshots /tmp/opencode/jellyp-p2-round2
```

O axe é opcional: sem `JELLYP_AXE_MODULE`, apenas as verificações do fluxo são
executadas. Para auditar `file://`, o teste troca temporariamente o `<link>` pelo
**mesmo CSS local inline**, evitando o XHR de CSS que o axe tenta fazer e o
Chromium bloqueia. O produto não faz esse XHR. HTTP é bloqueado e contabilizado.

**Resultado em 05/11/2026 (iteração 2 do feedback do mantenedor):** Playwright 1.64.1 e
axe-playwright 4.14.0: **241 verificações, zero falhas**, incluindo
**15 auditorias axe** (tags WCAG 2 A/AA, 2.1 AA e 2.2 AA); zero erros de
página/console e zero requisições HTTP/API/CDN. A rodada sem axe passou em
**226 verificações**, também sem erros ou HTTP. `node --check` dos dois JS e
`git diff --check` passaram. As **189 verificações anteriores (14 axe)** foram
preservadas (seletores/textos obsoletos atualizados ao novo controle, sem remover
contratos), incluindo as nove regressões corretivas: proteção de fundo
compartilhado na restauração, retenção que preserva o original exigido pela
regeneração selecionada, invalidação de análise/revisão após purga, restauração e
execução, validação da política efetiva, pluralização, foco após confirmações e
cancelamento, IDs obsoletos de remoção, status salvo da automação e dimensão na
recuperação. Isso não certifica conformidade WCAG nem substitui leitor de tela ou
usabilidade. A ampliação cobre o item Viagem com somente capa; descoberta e
teclado do editor; ajustes gerais versus próprios; cópia global versus automação;
narrativas de converter/recriar e consequências; restauração de subconjunto/todos,
lista exata, recuperação separada, revisão e seleções obsoletas, shared/dimensões
e bloqueio durante execução; automação vazia/múltipla/todas e biblioteca removida;
semântica de armazenamento e corte estrito da idade; confirmação móvel visível e
foco inicial no título sem esconder a introdução. Corridas são injeções controladas
nas fixtures, não prova de concorrência/integridade do backend.

A segunda iteração acrescenta **52 verificações (51 de fluxo/conta e 1 axe)**:
status-expansor único, Enter/Space e foco entre etapas, voltar/avançar nativo,
subset/revogação da revisão, navegação versus edição/invalidação, ida/volta a
Configurações sem salvar e após salvar, nove contas exatas de armazenamento,
faturas de conversão e regeneração guardar/remover, total versus seleção,
conclusão parcial/zero e verbo passado, operação ativa/concluída preservada,
nova intenção e arquivo em memória, proteção da operação ativa até contra troca
pela bancada, relatório concluído preservado ao salvar limites, zero sem remoção
fictícia, atalho único de restauração e ausência de
sobreposição da barra móvel sobre a narrativa.

Capturas desta rodada em `/tmp/opencode/jellyp-p2-round2/`: seleção, simulação,
revisão, conclusão, restauração, remoção, retenção, espera, abas, celular e tablet;
mais editor expandido, Viagem só capa, revisão/resultado do lote, automação
múltipla, armazenamento e confirmação móvel; faturas guardar/remover, desvio
contextual de Configurações e conclusão com saldo. Logs: `smoke-axe.log` e
`smoke-no-axe.log` no mesmo diretório.
Arquivos `*-viewport.png` mostram a janela real; os demais são página inteira. A
suíte verificou overflow em 1440 px, 360×800, 360×640, 768×1024 e 1280×900; nesta
revisão foram inspecionados visualmente editor/status, narrativa da capa,
revisão/resultado de restauração, automação e armazenamento em desktop. Na segunda
iteração foram inspecionados novamente seleção/status, narrativa, revisão,
faturas e conclusão no desktop, e narrativa sem sobreposição, regras/fatura,
Configurações e conclusão no celular. O modal móvel mantém as ações visíveis,
conteúdo rolável e foco inicial no título. As
capturas são evidência local regenerável, não registro de sessão humana nem
arquivos de produção.

## 6. Referência do Jellyfin e limites honestos

Inspeção do código atual `Jellyfin.Plugin.JellyP/Web/config.html` confirmou
`data-require="emby-input,emby-button,emby-linkbutton,emby-select,emby-checkbox"`,
`paperList`, `raised` e o acento `#00a4dc`. Nenhum desses arquivos de runtime foi
alterado. Não foram encontrados assets públicos de `jellyfin-web` nas localizações
locais verificadas (`.local` do projeto, `/usr/share`, `/opt`, `/srv` e busca por
diretórios de instalação/tooling no home). Nenhum banco, log ou credencial foi lido.

Referência pública consultada em 05/11/2026, branch `master` (não prova da versão
12.1 instalada):

- [emby-button.scss](https://github.com/jellyfin/jellyfin-web/blob/master/src/elements/emby-button/emby-button.scss): `inline-flex`, padding `0.9em 1em`, raio `0.2em`, peso 600.
- [emby-input.scss](https://github.com/jellyfin/jellyfin-web/blob/master/src/elements/emby-input/emby-input.scss): campo de largura total, fonte herdada e tamanho pelo menos 16 px para evitar zoom em iOS.
- [dark/theme.scss](https://github.com/jellyfin/jellyfin-web/blob/master/src/themes/dark/theme.scss): paleta ciano/azul de cartões e referência à superfície `32 32 32`.

Os arquivos foram lidos; não foram importados, compilados ou distribuídos neste
estudo. **É aproximação visual standalone**, não controles customizados `emby-*`,
não reprodução pixel-perfect, não prova de integração com tema/roteador do host.
Sem bundle local de host pronto, não foi montado Jellyfin descartável. Nenhum
serviço foi iniciado/reiniciado ou configuração de instalação alterada. A integração
real continua sendo aceite de P5/P7.

Outros limites:

- Dados, dimensões, tamanhos e evidência são inventados. O codec, hashes, SSIM,
  referências globais, espaço livre e journals **não são executados**. Qualidade,
  política, seleção, escopo e limite orientam o fluxo; os bytes de saída e os
  cenários de filtro são fixtures, não cálculo de qualidade/compressão.
- O editor demonstra substituição completa (D3), não herança por campo. Automação
  exercita os principais campos próprios; o editor avançado completo de políticas
  da automação e todos os tipos do runtime não são exaustivamente reproduzidos.
- Retenção é sintética por idade da fixture; a previsão e consentimento são proposta
  de UX, não contrato de elegibilidade/concorrência de expiração (P3). O código
  atual usa `CompletedUtc` e corte estrito `<`, não a criação/mtime do backup;
  otimização aplica expiração no início do lote, mas sincronização atual não.
  A evidência exata e a distinção atual/proposto estão no adendo P1.
- Sem durabilidade, paginação de servidor, migração, permissões reais, reload resiliente,
  reinício, desempenho ou validação de backend. Só Chromium automatizado; Firefox,
  Safari, leitor de tela e aparelhos físicos não foram validados.
- [Exercício sem contexto](UX-P2-EXERCISE.pt-BR.md), validação de navegação no host
  e compreensão D2 continuam pendentes. Resultados pessoais vão para `.local/`,
  nunca para documentos públicos.

## 7. Registro sanitizado do feedback de produto

Foi relatado um teste básico com contexto usando **Viagem Sintética, somente
capa**, com simulação, aplicação e restauração. O relato indica que a jornada
básica pôde ser percorrida, mas **não mede sucesso sem ajuda**, não tem sessão
sem contexto registrada e não encerra P2. Não se publicam identidade, citações
pessoais ou dados de bibliotecas reais.

Fricções observadas: editor por tipo parecia status; ações secundárias desalinhadas
e origem dos padrões ambígua; resultado exigia interpretar dados sem narrativa;
restauração difícil de descobrir e ausência de lote; tarefas/escopo/modo da
automação pouco claros; reserva, limite e retenção incompreensíveis. A revisão
respondeu inicialmente com separação de status/ação, cópia global explícita, narrativa e foco
no resultado, área de restauração nomeada e lote revisado, escopo múltiplo/todas
salvo e explicações de armazenamento fundamentadas no código. Páginas internas
permanecem recomendadas; abas são comparação opcional, não nova obrigação.

O registro é observação de produto, **não transcrição ou evidência de aprovação
humana do novo desenho**. As alterações ainda precisam ser exercitadas por pessoa
sem contexto; testes automatizados não substituem essa etapa.

### Segunda iteração — feedback humano do mantenedor (05/11/2026)

O mantenedor voltou à **mesma jornada Viagem, somente capa**. Não foi um ensaio
com pessoa sem contexto. Indicou redundância entre status e botão por tipo,
ausência de voltar/etapas clicáveis, dificuldade com a conta de originais,
limites globais escondidos e sem caminho de edição, e restauração duplicada na
conclusão. A iteração 2 implementa os cinco ajustes descritos acima; não registra
aprovação humana do resultado novo.

O mínimo livre protege o disco; o limite é o total de originais guardados, não
gasto de uma execução. O tempo usa idade desde a conclusão da conversão ou
regeneração (`CompletedUtc`). A exclusão só ocorre ao iniciar uma **otimização
aplicada** e só afeta originais elegíveis, nunca vídeos/mídia ou WebP ativo, nem
necessariamente no dia X. Guardar na operação não garante retenção indefinida.
A proteção do original necessário à regeneração e a previsão da análise única
continuam **proposta sintética**; não afirmam expiração no caminho de
sincronização atual. Evidência de runtime permanece no adendo P1, sem alterações.

Capturas adicionais `26`–`33` em `/tmp/opencode/jellyp-p2-round2/` cobrem ida e
retorno de Configurações, fatura de regeneração, conversão guardar/remover,
conclusão e celular. A inspeção renderizada desktop/mobile também identificou e
limitou a barra aderente à seção dos resultados: ela não deve cobrir a narrativa
inicial no celular. Limites de host, compressão real, leitor de tela e usabilidade
sem contexto permanecem os mesmos.

## 8. Terceira iteração — listas, saldo e automação (05/11/2026)

### Observação de produto, sem dados pessoais

O mantenedor relatou sucesso em seleção múltipla/restauração em lote e navegação.
O espaço ficou compreensível, mas a fatura não acompanhava checkboxes. Automação
continuou confusa, com layout destoante; controles de restauração estavam longe
dos registros, e remoção permanente precisava de destino próprio. Este relato é
contextualizado: **não foi criada nem aprovada uma sessão sem contexto**.

### Decisões propostas e implementadas somente no estudo

- **Histórico:** filtro, selecionar página/todos e revisão passam a pertencer à
  seção Operações. A barra compacta de contagem/revisão fica junto dos controles
  e acompanha a rolagem da lista; não há CTA depois de centenas de registros.
- **Exclusão:** subpágina local “Remover originais · permanente”, com hash direto,
  voltar/avançar nativos e retorno visível “Operações e restauração”. Mantém cinco
  áreas principais. Não exibe checkboxes/CTA de restauração; seleção separada,
  danger no link/revisão/confirmação e texto permanente, sem depender só da cor.
  Revisão contém quantidade, todos os destinatários e bytes; revalida snapshot
  completo por registro e apresenta resultado individual. Escape não altera dados;
  execução ativa bloqueia seleção e mutações nos dois destinos.
- **Paginação:** dez imagens por página, sem seletor de tamanho desnecessário.
  Histórico é paginado por imagem antes de agrupar as operações da página; uma
  operação de 27 imagens nunca monta 27 linhas de uma vez. Cabeçalho do grupo
  distingue total de imagens e quantidade nesta página. Simulação e exclusão
  também limitam dez linhas. Anterior/próxima têm nomes específicos, intervalo e
  total; após navegar, foco vai ao intervalo. Filtros reiniciam página, novas
  análises/revisões reiniciam seus detalhes e mutações fazem clamp de páginas.
  A seleção usa IDs estáveis em memória, preservada entre páginas e com total
  explícito. Selecionar página soma apenas elegíveis visíveis; “todos” explicita
  resultados ou plugin. Revisão/aplicação não inferem autorização pela página.
  Revisões grandes de aplicação/restauração/exclusão e resultados individuais
  também paginam; para listas auxiliares de até dez detalhes não há controles
  inativos redundantes. Todos os destinatários permanecem consultáveis.
- **Volume:** botão na bancada do rodapé acrescenta 27 imagens e 40 registros
  determinísticos, incluindo grupo de 27 imagens e outras 13 operações. Na análise
  padrão com qualidade 80: 31 avaliadas e 29 propostas. Não altera a fixture inicial;
  repetir não duplica, Recomeçar/reload limpa. Não usa APIs/dados privados.
- **Saldo:** a fatura principal usa somente IDs selecionados; zero é explicitamente
  “Sem alterações selecionadas”, antes/depois zero. Checkbox atualiza só fatura,
  contador e habilitação, mantendo o controle e a lista montados, foco e viewport.
  A região de anúncio informa contagem e saldo. O potencial de todas as propostas
  fica secundário, com alcance rotulado; expiração global continua separada.
- **Automação:** grade principal + aside de salvar/resumo/estado salvo, aderente no
  desktop. Tarefas compactas e consulta de relatório ficam na mesma coluna do
  formulário. Em celular, aside volta ao fluxo logo após a configuração e antes
  das tarefas/relatório, sem painel alto aderente encobrindo campos em 360×640.
  O modo é “Apenas analisar e gerar relatório”, não uma fila de aprovação. Lê bytes,
  estima e grava relatório/estado, sem alterar imagens nem guardar originais; não
  promete zero CPU/arquivos. Aplicar mantém preservação e autorização próprias.
  Salvar não dispara tarefa; disparos continuam no Jellyfin. A consulta abre um
  exemplo sintético explicitamente identificado, sem execução fictícia e sem
  inventar histórico completo: atualmente último relatório + até 20 amostras.
- **Proteções na Automação:** resumo curto com origem global e consequência da
  retenção, botão real para Armazenamento e retorno “Voltar à Automação”. Contexto
  de retorno distinto do manual; navegar/editar não salva, não perde o rascunho nem
  a revisão manual. Salvar globais invalida a revisão, exige conferir limites e
  nunca salva automação ou amplia seu alcance silenciosamente.

Implementação em HTML/CSS/JS por compatibilidade com o frontend standalone
existente e com Playwright; não há sistema novo que justifique migração para Rust.
Os contratos de paginação/concorrência/relatórios reais continuam em P3. Nada de
runtime, backend, deploy, serviços, commit/push ou configuração de revisão mudou.

### Verificação da iteração 3

Na árvore baseada em `65b411e`, Playwright **1.64.1** e axe-playwright **4.14.0**:

- **311 verificações, zero falhas**, incluindo **20 auditorias axe** com as mesmas
  tags WCAG 2 A/AA, 2.1 AA e 2.2 AA. **291 verificações sem axe**, zero falhas.
- As **241 anteriores, incluindo 15 axe**, foram preservadas. Seletores passaram
  a navegar explicitamente à subpágina de exclusão; as expectativas da fatura
  passaram do total fixo à seleção, conforme o feedback. Não se removeu contrato
  de integridade/isolamento para obter aprovação. São **70 verificações adicionais**
  (65 de fluxo/contas/foco e cinco axe).
- Cobertura nova: dez linhas por lista e por operação longa; percurso das 17
  operações do cenário de volume; seleção/revisão/aplicação entre páginas; todos
  os 40 destinatários de uma revisão consultáveis; restore/purge de subconjunto e
  todos com resultado/rejeição individual; página versus plugin, reset/clamp/filtro;
  saldo principal 0/1/subset/todas, foco/nó/viewport estáveis desktop/mobile; modo
  análise e relatório sem aprovação; aside aderente por rolagem real; retorno
  Auto/manual e histórico sem vazamento; revalidação após reduzir limite global;
  exclusão isolada/danger/Escape e controles móveis não cobertos pela barra.
- `node --check` em `jellyp-p2.js` e `smoke-prototype.cjs`, e `git diff --check`:
  aprovados. Zero erros de página/console e zero requisições HTTP/API/CDN.

Comando igual ao §5, usando módulos externos em
`/tmp/opencode/jellyp-browser/node_modules/{playwright,@axe-core/playwright}` e
`--screenshots /tmp/opencode/jellyp-p2-round3`. Logs finais nesse diretório:
`smoke-axe-final.log` e `smoke-no-axe-final.log`; logs intermediários não são o
resultado final. Capturas `01`–`42` e `47` são geradas pelo smoke; `43`–`46`, `48`
e `49` são inspeções complementares de viewport via Playwright local.

Renderização desktop/mobile **inspecionada**, não apenas compilada: operação
longa/toolbar (`43`/`44`), exclusão própria (`45`/`46`), diálogo grande (`37`/`41`),
Automação e sua ordem móvel (`38`/`39`), aside durante rolagem (`48`), saldo
selecionado (`42`/`49`) e foco móvel abaixo da barra (`47`). A inspeção motivou
barra de revisão junto ao cabeçalho, diálogos com corpo rolável/ações visíveis,
aside móvel antes das tarefas e correção do singular na exclusão.

Isso não certifica WCAG, leitor de tela ou usabilidade. Continua sendo estudo
cliente em memória, não paginação/concorrência de servidor, codec, integridade ou
scheduler real. Firefox/Safari, aparelhos físicos, integração no host e exercício
sem contexto permanecem não validados. P3–P8 não foram iniciadas.
