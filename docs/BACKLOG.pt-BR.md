# Pendências futuras do JellyP

## Prioridades — feedback de uso real da 0.4.0.0

Relatos do mantenedor em servidor com biblioteca muito grande. São observações
de uso real, **ainda sem reprodução controlada, perfil de desempenho ou causa
raiz comprovada**. Registrar não equivale a autorizar investigação em produção.
Os quatro pontos abaixo têm prioridade sobre refinamentos menores de conveniência.

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
