# Pendências futuras do JellyP

## Prioridades — feedback de uso real da 0.4.0.0

Relatos do mantenedor em servidor com biblioteca muito grande. São observações
de uso real, **ainda sem reprodução controlada, perfil de desempenho ou causa
raiz comprovada**. Registrar não equivale a autorizar investigação em produção.
Os quatro pontos abaixo têm prioridade sobre refinamentos menores de conveniência.

**Status da candidata 0.4.0.1:** a busca no tema escuro recebeu superfície
transparente herdada do host; o template padrão de políticas foi confirmado sem
override de Primary (overrides salvos continuam preservados); escopos grandes
passaram a ser processados em partes determinísticas com continuação explícita,
limite manual de 1.000 candidatas por parte e automação separada em 10.000.
A listagem de backups foi separada em preliminar (`CanReview`, sem promessa) e
verificação explícita por IDs exatos (`/Backups/Review`), com filtros
verificados mantidos. A validação nativa/browser desta candidata foi executada
(33 upgrade, 820 UI, 48 browser resilience; gates frescos independentes já
aprovados). Ainda pendentes: perfil de desempenho ponta a ponta em fixture real
representativa e as opções futuras de lote/orçamento sem limite (`0`).

### 1. Desempenho da jornada inteira e ausência de progresso — prioridade alta

**Esclarecimento posterior do mantenedor:** não se limita à simulação ou à conta.
Todo o percurso, desde a etapa 1 até a aplicação efetiva, é muito lento em um
servidor com **centenas de milhares de imagens**, mesmo selecionando um único
item. A demora atinge ler, localizar, analisar, revisar/calcular e aplicar. É um
problema de viabilidade do uso nessa escala, não apenas um refinamento visual.

- [ ] Investigar a demora excessiva mesmo ao simular um único item específico em
  biblioteca grande. O usuário relata um primeiro cálculo muito demorado sem
  progresso/status visível e, ao chegar aos resultados, outro cálculo demorado
  para apresentar a conta de armazenamento e permitir avançar.
- [ ] Medir de ponta a ponta e separadamente seleção/busca/inspeção, inventário,
  leitura e localização de arquivos, verificação de referências, conversão de
  teste, criação do plano, conta/revisão da seleção, revalidação, preparação da
  execução, gravação e persistência. Distinguir tempo em espera por metadata de
  custo computacional/I/O do próprio plugin. Registrar tempo até o primeiro
  resultado e até a primeira mutação, além do tempo total.
- [ ] Identificar trabalho proporcional à biblioteca inteira que se repete entre
  requisições, etapas ou imagens do mesmo lote. Avaliar consultas direcionadas,
  reaproveitamento por operação e índices/cache com invalidação verificável,
  conforme o perfil demonstrar. Não presumir que conversão WebP é o gargalo nem
  que basta executar tudo em background para tornar o custo aceitável.
- [ ] Exibir feedback contínuo e acessível desde o início: fase real, atividade,
  contagem/progresso quando conhecido, erros e cancelamento. Não inventar
  porcentagem quando o total ainda for desconhecido nem deixar a interface
  aparentemente parada durante o cálculo da conta.
- [ ] Evitar trabalho redundante entre análise, resultados e conta quando puder
  ser reutilizado com validade explícita. Preservar a conta após a lista, seleção
  exata entre páginas e revalidação antes de aplicar.
- [ ] Estender a otimização à preparação/revalidação e à aplicação em si. A
  experiência não está resolvida se a análise acelerar, mas Aplicar continuar
  repetindo varreduras demoradas. Definir metas e comparar antes/depois somente
  após medir uma baseline representativa.
- [ ] Validar com fixture sintética representativa de biblioteca grande e medir
  análise **e aplicação** de um item e de subconjuntos pequenos, variando o total
  da biblioteca até a ordem de centenas de milhares de imagens. Os testes
  anteriores com 14 imagens de
  jornada e 52 Backdrops não comprovam desempenho nesse cenário. Otimização não
  pode reduzir as checagens globais necessárias contra referências compartilhadas.

### 1.1 Lentidão ao abrir histórico e loading genérico indefinido — prioridade alta

Novo relato de uso real na 0.4.0.0: ao navegar entre áreas, principalmente para
**Histórico e restauração**, o conteúdo fica vazio por muito tempo, mostrando
apenas “Loading JellyP...” e o spinner do host. O menu indica o destino, mas a
área principal não oferece status útil, fase, erro ou previsão. A captura fornecida
documenta esse estado; não prova sozinha travamento permanente nem sua causa.

- [ ] Incluir abertura do histórico/restauração e navegação entre áreas no perfil
  de desempenho ponta a ponta, não apenas análise/aplicação. Medir requisições,
  tempo de servidor, leitura de operações/journals, verificação dos backups,
  resolução de nomes/imagens, paginação e renderização. Identificar trabalho
  global ou sequencial desnecessário antes de propor a correção.
- [ ] Tornar visível o primeiro conteúdo útil sem depender de todos os cálculos
  ou seções da página. Avaliar carregamento progressivo e estados por seção,
  preservando conteúdo já disponível e indicando o que ainda está sendo obtido.
- [ ] Substituir a espera genérica indefinida por estados acessíveis e localizados:
  carregando, demorando além do esperado, erro, vazio e conteúdo disponível.
  Oferecer recuperação/repetição e navegação segura sem duplicar requisições
  mutantes, cancelar operações implicitamente ou ressuscitar seleções consumidas.
- [ ] Tratar falha, timeout, requisição obsoleta e mudança de rota para não deixar
  spinner preso nem permitir que resposta antiga sobrescreva a página atual.
  Definir timeouts a partir do contrato e das medições, não mascarar a lentidão
  apenas encerrando a espera no cliente.
- [ ] Testar respostas lentas/falhas controladas e muitos registros em fixture
  sintética; verificar progresso anunciado, foco, idioma, ambos os temas e se o
  histórico abre com custo proporcional ao conteúdo necessário à página.

Registrar junto da prioridade 1: melhorar o loading não substitui otimizar o
backend, e acelerar o backend não dispensa feedback durante operações longas.
Não anexar a captura de produção ou logs privados ao repositório sem sanitização.

### 2. Busca de itens específicos no tema escuro — bug visual

- [ ] Corrigir o fundo branco que aparece na área/lista de seleção de itens
  específicos ao digitar na busca da simulação, deixando títulos sem contraste.
  A hipótese relatada é aplicação indevida de estilo do tema claro; causa não
  confirmada. Conferir a superfície real fornecida pelo host e o CSS do plugin.
- [ ] Reproduzir durante digitação/resultados/seleção, incluindo foco e hover,
  e validar contraste nos temas claro e escuro sem alterar estilos globais do host.

### 3. Origem dos ajustes de capas — padrão inesperado

- [ ] Investigar por que o primeiro tipo da simulação (capas principais) aparece
  sempre em “Usar ajustes deste tipo”, em vez de partir dos ajustes do teste.
  O usuário descreve o comportamento como hardcoded; ainda não foi distinguido
  entre padrão do código, configuração salva, projeção antiga ou rascunho da sessão.
- [ ] Fazer o estado inicial seguir os ajustes do teste conforme a UX esperada,
  sem apagar overrides que a pessoa tenha escolhido/salvo explicitamente.
  Cobrir teste novo, recarga, padrões globais e configuração preexistente.

### 4. Limites de lote inconsistentes e insuficientes

#### Bloqueante de execução em massa: escopo rejeitado antes de limitar o lote

Novo relato do mantenedor: pedido de lote de 1.000 falha com erro descrito como
`PlanTooLarge`; reduzir o lote para 100 continua falhando. Isso impede uso em
massa em bibliotecas grandes, não apenas reduz sua velocidade.

**Evidência no código publicado:** `AnalyzePlan` solicita `DetailedInventory`
com `maximumEntries: MaximumPlanImages` (5.000) antes do laço que limita as
avaliações por `BatchSize`. O adapter real lança `PlanScopeTooLarge` quando o
inventário do escopo excede esse teto. O limite conta posições de imagem do
escopo inventariado, não só propostas aprovadas ou candidatas avaliadas. Assim,
reduzir o lote não reduz o inventário necessário ao plano atual. O nome exato do
erro HTTP observado pelo usuário ainda não foi coletado; o código encontrado
é `PlanScopeTooLarge`, não um identificador literal `PlanTooLarge`.

- [ ] Corrigir o contrato de planejamento para processar um escopo grande por
  partes limitadas sem exigir que ele inteiro caiba num plano. Não orientar o
  usuário a reduzir o lote como solução quando esse parâmetro não limita o escopo.
- [ ] Projetar continuação determinística/progresso entre partes, preservando
  seleção exata revisada, identidade dos slots, alterações concorrentes e
  proteção global contra referências compartilhadas. Não apenas remover o teto
  e carregar centenas de milhares de registros/bytes em memória.
- [ ] Reproduzir com escopo superior a 5.000 posições e lotes de 100 e 1.000;
  provar que cada lote pode ser analisado, revisto e aplicado e que a continuação
  não fica repetindo só as primeiras imagens ou substituindo autorizações.
- [ ] Distinguir na interface limites de escopo/plano, candidatas por lote e
  seleção para aplicação; um erro de escopo não deve parecer rejeição de um lote
  que a própria interface permite. Medir a solução na escala da prioridade 1.

- [ ] Resolver a discrepância percebida: o teste manual permite até **1.000**,
  enquanto os ajustes globais aceitam **10.000**. O mantenedor considera ambos
  baixos para seu uso. Não apenas aumentar o atributo `max` da interface: conferir
  validação de API, plano, seleção, memória e execução incremental.
- [ ] Tornar explícita a relação entre o teto salvo e o limite efetivo de um teste,
  sem oferecer nos padrões um valor que o fluxo manual não consiga usar como tal.
- [ ] Tratar em conjunto com a opção de lote sem limite solicitada abaixo,
  preservando limites de segurança bem justificados e evitando prometer suporte
  a volumes ainda não medidos. Compatibilidade de valores existentes é obrigatória.

## Limites opcionais — feedback após a publicação 0.4.0.0

O mantenedor solicitou registrar duas melhorias, sem implementá-las nesta versão:

- [ ] Permitir processamento sem um limite configurado de imagens por lote,
  com uma opção explícita de “Sem limite” (considerar `0` como representação).
  Distinguir essa preferência dos limites técnicos de planos, seleção e operações;
  avaliar paginação/processamento incremental para não carregar tudo em memória.
- [ ] Permitir guardar originais sem um orçamento máximo configurado de tamanho,
  com uma opção explícita de “Sem limite” (considerar `0` como representação).
  Manter a reserva mínima de espaço livre, a proteção de recuperação e a retenção
  por idade como regras independentes. “Sem limite” não significa espaço infinito.

### Compatibilidade e apresentação

- Não reinterpretar valores existentes silenciosamente; definir e testar os
  contratos de configuração/API, UI e automação antes de alterar a semântica.
- Mostrar claramente a unidade, o alcance e o significado de zero em cada campo.
- O limite de imagens não deve ser confundido com paralelismo ou tamanho da página.
- Registrar que, em 0.4.0.0, zero é inválido para lote e orçamento de originais.
  Só a retenção em dias aceita zero como ausência de expiração por idade.
- Valores altos não eliminam os tetos atuais: até 1.000 candidatas no lote manual
  e até 10.000 no lote da automação. O orçamento de originais também protege a
  cópia temporária necessária à conversão, mesmo sem retenção posterior.

Essas pendências não autorizam mudanças de código, nova release ou alterações em
produção. O comportamento publicado de 0.4.0.0 permanece inalterado.
