# P2 — Desenho, protótipo e revisão técnica

Registro inicial: **17/10/2026**. Revisão visual/UX: **05/11/2026**.
Referências: [contrato P1](UX-P1-CONTRACT.pt-BR.md) ·
[especificação](UX-REFACTOR-PLAN.pt-BR.md) · [plano](PLAN.md).

**Status:** protótipo revisado a partir de feedback real com contexto, renderizado
e exercitado automaticamente. O ensaio relatado não equivale a validação sem contexto.
**Validação com pessoa sem contexto continua pendente.** A autorização desta
revisão abrange protótipo/testes/documentação, não runtime, backend, produção,
instalação ou release. Não confundir a implementação deste estudo com P4–P6.

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
   editar qualidade e escolher preservação. O editor distingue o estado “Usa os
   ajustes deste teste” / “Ajustes próprios deste tipo” da ação **Editar ajustes
   deste tipo**, com borda, chevron, painel e `aria-expanded`/`aria-controls`.
   “Usar padrões globais” copia explicitamente os valores definidos em
   Configurações, após confirmação, sem salvar ou alterar automação, alcance,
   tipos ou preservação. As ações secundárias do resumo são alinhadas à esquerda.
2. **Simular → resultado:** a análise substitui o editor na mesma área. Snapshot
   identificado, ações por imagem e motivos ficam juntos, sem procurar abaixo de
   um formulário extenso. O foco e o anúncio chegam a “Simulação concluída —
   nenhuma imagem foi alterada”. A narrativa explica ações, preservação,
   restauração, impacto líquido e próximo passo antes dos detalhes de IDs/SSIM.
   A análise é local e síncrona, sem atraso artificial ou fase de servidor fictícia.
   Qualidade 80 propõe regenerar a capa de Viagem a partir
   do original; a miniatura pode ser convertida; WebP externo é mantido e um filtro
   rejeita outra imagem. Não existe botão manual separado de sincronização.
3. **Escolher:** nenhuma proposta vem marcada. Checkbox por identidade de imagem;
   “Selecionar propostas” é uma ação explícita. Sem seleção, revisão bloqueada.
4. **Executar → revisão:** lista apenas as imagens selecionadas, com slots, ajustes,
   ações e impacto do conjunto. Consentimento é obrigatório e funcional. Remover
   originais e expiração global exigem consentimentos separados, não herdados
   da automação. Não há segundo formulário capaz de divergir da análise.
5. **Acompanhar:** inventário sem percentual inventado → processamento → resultado.
   Execução tem identidade, origem e contagens próprias. O indicador global permite
   voltar à operação; navegar não a cancela. A conclusão da encenação é automática.
6. **Histórico e restauração:** a aplicação cria registros da própria seleção.
   O resultado oferece acesso direto à restauração com filtro “Somente
   restauráveis”. Restaurar revisa a
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
7. **Remover originais:** marcar → revisar lista/quantidade/bytes → autorizar →
   confirmar. “Todos os elegíveis do plugin” só seleciona, não remove. Incertos
   não são selecionáveis. Escape/voltar no diálogo não altera dados.

Editar alcance, tipos, preservação ou política invalida a análise para aplicação,
mas mantém o relatório consultável. Salvar novos limites/padrões também invalida
a revisão conservadoramente. Fontes/identidades sintéticas são rechecadas antes de
aplicar; uma imagem inválida não é substituída. Isso demonstra o contrato de UX,
**não implementa integridade, idempotência ou autorização de servidor**.

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
- Métricas da operação não se misturam aos totais do plugin. Na fixture inicial,
  qualidade 80 + guardar os dois originais propostos: redução de **152 KiB** nas
  imagens, **220 KiB** de novos originais e **68 KiB a mais em uso**; regeneração
  reutiliza o original existente. Não chamar redução de imagem de espaço livre.

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
node scripts/smoke-prototype.cjs --screenshots /tmp/opencode/jellyp-p2-feedback
```

O axe é opcional: sem `JELLYP_AXE_MODULE`, apenas as verificações do fluxo são
executadas. Para auditar `file://`, o teste troca temporariamente o `<link>` pelo
**mesmo CSS local inline**, evitando o XHR de CSS que o axe tenta fazer e o
Chromium bloqueia. O produto não faz esse XHR. HTTP é bloqueado e contabilizado.

**Resultado em 05/11/2026 (iteração do feedback):** Playwright 1.64.1 e
axe-playwright 4.14.0: **189 verificações, zero falhas**, incluindo
**14 auditorias axe** (tags WCAG 2 A/AA, 2.1 AA e 2.2 AA); zero erros de
página/console e zero requisições HTTP/API/CDN. A rodada sem axe passou em
**175 verificações**, também sem erros ou HTTP. `node --check` dos dois JS e
`git diff --check` passaram. As **133 verificações anteriores (11 axe)** foram
preservadas, incluindo as nove regressões corretivas: proteção de fundo
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

Capturas desta rodada em `/tmp/opencode/jellyp-p2-feedback/`: seleção, simulação,
revisão, conclusão, restauração, remoção, retenção, espera, abas, celular e tablet;
mais editor expandido, Viagem só capa, revisão/resultado do lote, automação
múltipla, armazenamento e confirmação móvel. Logs: `smoke-axe.log` e
`smoke-no-axe.log` no mesmo diretório.
Arquivos `*-viewport.png` mostram a janela real; os demais são página inteira. A
suíte verificou overflow em 1440 px, 360×800, 360×640, 768×1024 e 1280×900; nesta
revisão foram inspecionados visualmente editor/status, narrativa da capa,
revisão/resultado de restauração, automação e armazenamento em desktop, além da
narrativa e confirmação em celular. O modal móvel mantém as ações visíveis,
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
- Sem durabilidade, paginação, migração, permissões reais, reload resiliente,
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
responde com separação de status/ação, cópia global explícita, narrativa e foco
no resultado, área de restauração nomeada e lote revisado, escopo múltiplo/todas
salvo e explicações de armazenamento fundamentadas no código. Páginas internas
permanecem recomendadas; abas são comparação opcional, não nova obrigação.

O registro é observação de produto, **não transcrição ou evidência de aprovação
humana do novo desenho**. As alterações ainda precisam ser exercitadas por pessoa
sem contexto; testes automatizados não substituem essa etapa.
