# P2 — Especificação de desenho e protótipo

Data: **17 de outubro de 2026**. Fase: **P2 autorizada** (artefatos + exercício).
Contrato de referência: [UX-P1-CONTRACT.pt-BR.md](UX-P1-CONTRACT.pt-BR.md).
Especificação: [UX-REFACTOR-PLAN.pt-BR.md](UX-REFACTOR-PLAN.pt-BR.md).

**Status.** Artefatos de desenho concluídos; **exercício com pessoa sem contexto
pendente**. Nada aqui é implementação: o protótipo é estático, com dados
sintéticos, e não fala com o Jellyfin. Textos e telas permanecem propostas até a
revisão pós-exercício (checklist P2 da especificação §11).

## 1. Protótipo

Arquivo único: [ux-prototype/jellyp-p2.html](ux-prototype/jellyp-p2.html).
Abrir diretamente no navegador (sem servidor, sem login). Faixa "PROTÓTIPO
SINTÉTICO" sempre visível; nenhuma ação é real.

- **Bancada de estados** (barra tracejada, fora do produto): permite ao
  facilitador colocar a tela em cada estado do contrato sem depender de dados
  reais — vazio, indisponível, conflito, espera, cancelamento, sucesso parcial,
  recuperação necessária.
- **Seletor de estrutura (D1)**: alterna a mesma interface entre *Páginas* e
  *Abas*, sem mudar o conteúdo. É o insumo central do exercício.
- **Idioma**: protótipo só em pt-BR (a implementação final terá a alternância
  existente en↔pt-BR); anotado para P5.
- **Dados sintéticos**: bibliotecas "Filmes" e "Séries"; itens "Viagem
  Sintética (2024)", "Cidade Exemplo (2021)", "Série Demonstração"; pessoa
  "Pessoa Exemplo"; números redondos e claramente fictícios.

## 2. D1 — alternativas comparadas no exercício

Evidência do host (assemblies fixados do Jellyfin 12.1,
`MediaBrowser.Model.Plugins.PluginPageInfo` e `IHasWebPages`):

- `GetPages()` devolve `IEnumerable<PluginPageInfo>`: **multi-página nativa**.
- `PluginPageInfo` oferece `Name`, `DisplayName`, `EmbeddedResourcePath`,
  `EnableInMainMenu`, `MenuSection`, `MenuIcon`. `MenuSection` é string livre
  interpretada pelo jellyfin-web; não há enum nem primitiva de abas no servidor.

| | **D1-A: páginas internas** | **D1-B: abas ARIA em uma página** |
|---|---|---|
| No host | 5 entradas em `GetPages()`; URLs distintas por área | 1 página; abas no HTML (padrão W3C APG, citado na especificação §14) |
| Voltar/avançar | Histórico do navegador nativo por URL | Exige sincronizar hash manualmente para não perder histórico |
| Link direto para uma área | Nativo | Só se o hash for mantido |
| Rascunho ao trocar de área | Precisa de retenção explícita (sessão/servidor) | Natural: um único contexto de página |
| Menu do Jellyfin | Cada página pode aparecer no menu (`EnableInMainMenu`/`MenuSection`/`MenuIcon`) — exposição maior, decisão do produto | Uma entrada só |
| Mobile | Navegação por menu/URL do host | Abas roláveis/empilhadas; tudo à vista |

O protótipo implementa as duas com **hash sincronizado nas duas** (para o
exercício comparar compreensão, não a limitação de uma implementação ingênua);
a diferença técnica real (histórico nativo vs. manual) está registrada acima
para P3/P5.

**O que o exercício precisa decidir:** em qual estrutura uma pessoa sem
contexto (a) localiza as cinco áreas, (b) volta para onde estava, (c) mantém o
rascunho e (d) usa razoavelmente em viewport estreito.

## 3. D2 — representação da análise única

Direção da especificação §7.3: uma simulação manual avalia os ajustes desejados
e propõe, por imagem, converter, regenerar do original, manter ou descartar.

No protótipo (área Simular → resultado):

1. Cada linha mostra item, tipo/posição, estado atual ("WebP com ajustes
   anteriores não registrados"), **ação proposta** com o texto principal
   ("Gerar novamente a partir do original", "Converter para WebP", "Manter —
   já utiliza estes ajustes", "Manter — redução abaixo do mínimo") e detalhe do
   motivo.
2. Cabeçalho separa as quantidades: analisadas · propostas · mantidas ·
   descartadas · falhas; e os totais da simulação (redução estimada, originais
   a guardar, impacto líquido estimado) ficam **separados** dos totais do
   plugin.
3. Seleção explícita por imagem (checkbox por linha + "selecionar todas as
   propostas") para Executar.
4. Sem instrução sobre botões técnicos: não existe "sincronizar" na área
   manual; regeneração é uma ação proposta.

**O que o exercício precisa validar:** uma pessoa entende "gerar novamente a
partir do original; qualidade 80" sem explicação prévia (tarefa 1) e autoriza
um subconjunto exato (tarefa 2).

## 4. Áreas, telas e estados no protótipo

| Área | Telas implementadas | Estados de P1 alcançáveis na bancada |
|---|---|---|
| **Simular** | escopo (bibliotecas/itens/tudo) · tipos + editor por tipo + resumo efetivo · preservação e limite · resultado por imagem | vazio (sem bibliotecas; sem tipos; item sem imagens), indisponível, dados parciais |
| **Executar** | revisão ("Aplicar às N imagens", consequência da remoção) · andamento (inventário → espera por metadata → processando) · resultado | conflito (outra operação), espera, cancelamento, sucesso parcial, recuperação necessária |
| **Automação** | escopo/modo próprios · gravação explícita · tarefas nativas (estado/disparos) | ociosa ≠ desativada, sem horário, tempo de execução |
| **Histórico e originais** | lista por operação · detalhe por imagem (formato/dimensões/tamanho armazenados + nota sobre WebP de resposta) · restauração · remoção seletiva | recuperação necessária, original restaurado, original removido, incerto preservado |
| **Configurações** | padrões globais e por tipo com alcance indicado · limites, reserva, quota, retenção (globais) · rascunho alterado + gravação explícita | rascunho alterado, valores efetivos |

Regras do contrato respeitadas no desenho: rascunho não grava; automação não é
alterada pelo manual; execução revalida e não substitui imagens descartadas;
métricas por execução vs. totais separadas; remoção de originais é seletiva
(a opção "todos os elegíveis" aparece como escolha explícita e destacada).

## 5. Acessibilidade e responsividade embutidas

- Estrutura *Abas*: `role=tablist/tab/tabpanel`, setas ←/→, `aria-selected`,
  foco no painel após troca (padrão W3C APG).
- Estrutura *Páginas*: navegação com `aria-current`, foco no título da área.
- Foco visível em todos os controles; link "pular para o conteúdo".
- Alvos de toque ≥ 2.5em em viewport estreita; nenhum conteúdo essencial
  depende de hover; layout em coluna abaixo de 45em.
- Mensagens de progresso/resultado em regiões `aria-live` (a implementação
  real segue a referência W3C da especificação §14).

## 6. Limites declarados

- Não é a implementação, não valida runtime, não mede desempenho.
- Simplificações declaradas: a análise sintética depende da qualidade efetiva
  por tipo e do escopo; SSIM, dimensões e limites não alteram os números
  exibidos. O editor por tipo substitui a política completa (D3) e a economia
  permanece global.
- Não substitui a validação em Jellyfin descartável (P5/P7): routing real do
  host para D1-A e integração de abas com o theme do host só se confirmam lá.
- Os textos do protótipo são candidatos; após o exercício serão revisados antes
  de virarem contrato (P2 → P3).

## 7. Registro do exercício

Roteiro e template: [UX-P2-EXERCISE.pt-BR.md](UX-P2-EXERCISE.pt-BR.md).
O registro preenchido é **privado** (inclui observações sobre uma pessoa) e vai
para `.local/`, nunca para os documentos públicos.
