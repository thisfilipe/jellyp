# Plano detalhado de refatoração da UX do JellyP

Data do registro: **17 de outubro de 2026**. Baseline do código: **0.3.0.1**.
Alvo atual: **Jellyfin 12.1 / .NET 10**.

## 1. Status, autoridade e leitura deste documento

**Reconciliação de 08/11/2026:** a implementação principal foi commitada até
`30401c5`, incluindo os refinamentos de UX e branding posteriores à primeira
integração. P3/P4 estão entregues como contratos e implementação; P5/P6 estão
implementadas e exercitadas, com aceite técnico final ainda parcial em P7.
P8 está em preparação, sem release/produção. O acompanhamento atual e a matriz
conservadora V01–V15 estão no [checklist de fechamento](ALPHA-CLOSURE.pt-BR.md).
Os relatos e contagens datados abaixo são históricos; não representam testes
executados depois do reboot nem uma única suíte final. A fixture em `/tmp` foi
perdida; resta recriá-la para os gates de host pendentes.

**Este é um plano de produto, arquitetura e validação. A integração nativa já foi
implementada na árvore de trabalho e exercitada em Jellyfin descartável; não foi
publicada nem implantada em produção.** Contratos e limites da evidência atual:
[integração implementada](UX-P3-INTEGRATION.pt-BR.md) e
[validação nativa](UX-P3-NATIVE-VALIDATION.pt-BR.md).

O mantenedor pediu explicitamente: avaliar e planejar a interface inteira, sem
modificar a implementação. Depois, pediu que a avaliação fosse documentada em
detalhe para permitir continuidade por um agente sem acesso à conversa original.
Esta última instrução autoriza a escrita da documentação, não o início da
refatoração, de uma implantação ou de uma nova versão.

Uma instrução posterior pode autorizar uma etapa específica. Até lá, as tarefas
P1–P8 abaixo são propostas pendentes, não ordens de execução. Não interpretar uma
caixa desmarcada como autorização para trabalhar nela automaticamente.

> **Registro de execução (17/10/2026):** o mantenedor autorizou a fase P1. A
> entrega está em [UX-P1-CONTRACT.pt-BR.md](UX-P1-CONTRACT.pt-BR.md). As fases
> seguintes dependem de autorização específica.
>
> **Atualização P2 (05/11/2026):** P2 foi autorizada para artefatos e exercício;
> a revisão visual/UX do protótipo, seus testes locais e documentação também
> foram autorizados. Protótipo revisado e verificado em navegador; **exercício
> sem contexto e validação de navegação/compreensão D2 pendentes**. P3–P8, runtime, backend e produção não
> foram autorizados. Evidência e limites: [desenho P2](UX-P2-DESIGN.pt-BR.md).

> **Decisão de continuidade (06/11/2026, substitui o bloqueio acima):** a direção
> de P2 foi aceita pelo mantenedor para implementação nativa após testes e
> correções iterativas. O exercício com pessoa sem contexto fica **adiado e não
> bloqueia o avanço**; não foi executado nem será declarado aprovado. A integração
> real será implementada e validada por jornadas completas, reaproveitando o
> backend publicado e adaptando apenas lacunas demonstradas. Esta instrução
> autoriza atualizar o plano, não iniciar runtime, implantar ou publicar.

> **Autorização posterior e execução (06–07/11/2026):** o mantenedor autorizou
> seguir com a implementação usando componentes reais do Jellyfin e adaptar o
> backend às necessidades da UX, além de concluir correções e testes em ambiente
> descartável. Essa instrução substitui o bloqueio anterior de runtime, mas não
> autoriza produção, instalação remota ou release. A árvore contém página nativa,
> APIs aditivas, planos/operações duráveis e manutenção seletiva. Conferência final:
> 181 testes .NET, 9 de release, 79 verificações nativas (7 axe) e 16 verificações
> específicas de fundos/retenção aprovadas. Casos ainda não exercitados no host
> constam da validação nativa; isso não marca P3–P7 inteiras como concluídas.

Este documento é a especificação de referência da proposta. O [PLAN.md](PLAN.md)
é seu ponto de entrada em inglês. A [arquitetura atual](ARCHITECTURE.md) descreve
o que existe no código; não deve ser confundida com a arquitetura pretendida aqui.

Convenções utilizadas:

- **Atual:** comportamento observado no código 0.3.0.1 ou identificado na avaliação.
- **Observado:** resultado de teste ou dificuldade relatada pelo mantenedor.
- **Proposto:** comportamento desejado, ainda dependente de implementação e validação.
- **Decisão pendente:** escolha que precisa ser resolvida antes da etapa indicada.
- **Critério de aceite:** evidência necessária para considerar uma entrega concluída.

Os exemplos são sintéticos. Não copiar para este documento títulos da biblioteca
de produção, IDs reais, caminhos privados, credenciais, imagens, bancos ou logs.

## 2. Produto e contexto que um novo agente precisa conhecer

JellyP é um plugin de administração do Jellyfin para reduzir o armazenamento de
imagens internas de metadata por meio de WebP. O usuário escolhe bibliotecas ou
itens, tipos de imagem, qualidade, preservação de originais e outros limites.

O plugin possui três operações importantes:

1. **Conversão inicial:** transformar JPEG/PNG elegível em WebP.
2. **Regeneração a partir do original:** atualizar uma conversão anterior quando
   os ajustes mudam, usando o original guardado para evitar perdas sucessivas.
   A implementação atual chama isso de sincronização de políticas.
3. **Restauração:** recolocar os bytes originais e sua referência ativa no Jellyfin.

Há também administração de originais guardados e tarefas agendadas nativas.
Preservar o original permite restaurar e regenerar depois, mas inicialmente ocupa
mais espaço. Excluir o original é permanente depois da verificação; isso precisa
ser compreendido antes de autorizar a exclusão.

O Jellyfin organiza o acervo por bibliotecas. No escopo de biblioteca entram seus
descendentes e a própria imagem de biblioteca, quando elegível. Pessoas creditadas
também podem entrar, mas suas entidades e fotos são globais: uma foto pode aparecer
em outras bibliotecas. Seleção por ID individual significa exatamente aquele item,
não todos os episódios de uma série. Um item pode ter várias imagens de um tipo.

A proteção contra arquivos compartilhados considera referências do servidor
inteiro. Uma análise de poucos itens pode, portanto, precisar de uma leitura
global demorada. Isso não significa autorização para alterar todo o servidor.

O objetivo da refatoração é tornar esses recursos operáveis sem ensinar a divisão
interna de classes, tarefas, cursores e endpoints ao usuário.

## 3. Evidências e limites da validação existente

### 3.1 O que motivou a avaliação

O mantenedor, mesmo conhecendo a finalidade do plugin, precisou de instruções
externas repetidas para descobrir como simular, aplicar e sincronizar. Relatou
perda de contexto ao clicar em um botão inferior e encontrar o andamento em
outra parte da página. Considerou a interface inadequada para um usuário novo.

Três episódios esclarecem o problema:

- Um item selecionado não tinha imagens próprias registradas. A análise demorou
  e terminou sem tornar a causa suficientemente compreensível. O mantenedor
  confirmou depois ter confundido esse título com outro. Esse episódio não
  comprovou um defeito de hidratação de imagens.
- Uma conversão seguida de restauração foi questionada porque o navegador ainda
  recebia `image/webp`. A verificação do arquivo e da referência persistida
  confirmou o JPEG original com hash idêntico. O Jellyfin pode entregar uma
  representação WebP de uma fonte JPEG/PNG; o MIME da resposta não prova o formato
  armazenado. A UI não fornecia essa distinção nem a evidência da restauração.
- Após converter uma capa com qualidade 85 e guardar o original, o usuário mudou
  o ajuste individual para 80 e acionou a simulação comum. O relatório retornou
  zero conversões e indicou que deveria usar a sincronização. O seletor de modo
  estava em uma seção, e o botão de sincronização em outra, sob administração
  de originais. O caminho de sincronização não foi exercitado por esse clique.

### 3.2 Baseline funcional não equivale a aprovação da UX

A versão 0.3.0.0 possui registros de 116 testes .NET, além dos exercícios isolados.
A 0.3.0.1 registra 124 testes .NET, nove testes de publicação e exercícios de
navegador. A 0.3.0.1 acrescentou inspeção antecipada por item, explicações de
disponibilidade e correção do aviso de fila. Não resolveu a organização global.

Houve evidência controlada de conversão e restauração exata, além de validações
descartáveis de sincronização, escopo e agendamento. Isso não autoriza afirmar
que todos os cenários, plataformas ou operações de produção passaram. Consulte
[VALIDATION.md](VALIDATION.md) para os limites e a origem desses registros.

Um teste que encontra seletores, clica em botões e recebe HTTP de sucesso não
demonstra que alguém compreendeu o fluxo. A nova validação precisa verificar
integridade e usabilidade separadamente.

## 4. Diagnóstico da interface atual

| ID | Problema atual | Consequência | Prioridade |
|---|---|---|---|
| UX-01 | Uma página longa mistura seleção, ajustes, execução, automação, histórico e exclusão. | A ordem de trabalho precisa ser descoberta pelo usuário. | Alta |
| UX-02 | “Simular seleção” só procura conversões iniciais. | Mudar a qualidade de um WebP parece não funcionar. | Alta |
| UX-03 | Sincronização depende do modo escolhido em outra seção e fica junto de restauração/exclusão. | A ação não corresponde ao lugar nem ao controle visível. | Alta |
| UX-04 | Status, mensagens e resultado ficam distantes do botão e há vários pontos de feedback. | O usuário perde a relação entre clique e efeito. | Alta |
| UX-05 | Executar manualmente salva o formulário compartilhado. | Escopo, políticas e armazenamento usados pela automação podem mudar durante um teste. | Alta |
| UX-06 | Simulação e aplicação usam cursores independentes. | Não há garantia de aplicar o conjunto exato que acabou de ser revisado. | Alta |
| UX-07 | O relatório principal é sobrescrito por operações diferentes. | O usuário precisa inferir qual operação gerou os números. | Alta |
| UX-08 | Exclusão de originais tem alcance mais amplo que a seleção de itens na tela. | O alcance da exclusão pode ser interpretado incorretamente. | Alta |
| UX-09 | Métricas da execução e totais de armazenamento aparecem juntas. | Uma simulação pode parecer ter criado backups ou aumentado o uso global. | Média |
| UX-10 | Estados como ociosa, sem agendamento e somente simular não são claramente separados. | O estado real da automação fica ambíguo. | Média |
| UX-11 | Muitos ajustes globais, individuais e avançados competem pela atenção. | A tarefa básica exige interpretar opções raramente necessárias. | Média |
| UX-12 | Faltam formato/dimensões armazenados e evidências acessíveis de restauração. | O usuário tenta validar usando a representação entregue ao navegador. | Alta |
| UX-13 | Identidade do item, quantidade de imagens e disponibilidade podem exigir investigação externa. | Títulos semelhantes e itens sem imagens geram expectativas incorretas. | Média |

As prioridades orientam a ordem do projeto. “Alta” não significa que toda operação
afetada já causou dano: é uma avaliação do impacto da ambiguidade e da necessidade
de corrigir o contrato de interação.

## 5. Objetivos, limites e regras que permanecem

### 5.1 Objetivos de produto

- Uma pessoa nova consegue simular, entender o resultado, aplicar e restaurar
  sem instruções fornecidas fora do plugin.
- A navegação corresponde a tarefas completas, com estado e resultados próprios.
- Escolher bibliotecas é o caminho de alcance amplo; itens e imagens individuais
  permitem testes controlados. Lotes são limites de trabalho, não a organização
  principal do produto.
- A ação e seu andamento permanecem associados visual e semanticamente.
- A aplicação manual corresponde a uma seleção revisada e identificável.
- Testes manuais, padrões salvos e automação têm efeitos e botões de gravação distintos.
- O usuário entende quando pode restaurar, quando está usando mais espaço e
  quando uma exclusão encerrará a possibilidade de restauração.

### 5.2 Fora do escopo da refatoração

- Novo scheduler externo, watcher de arquivos ou interceptação de downloads.
- Baixar imagens ausentes para tornar uma simulação aparentemente produtiva.
- Converter órfãos, avatares, caches, capítulos ou imagens ao lado da mídia sem
  um projeto e autorização próprios. As exclusões atuais permanecem.
- Recompressão destrutiva de WebP sem original como alternativa silenciosa.
- Troca de versão de Jellyfin/.NET/codec ou adoção automática de um framework de frontend.
- Limpeza de disco de produção, retomada de workers, reinício, instalação ou release.
- Prometer que o plugin eliminou a possibilidade de uploads manuais concorrentes.

### 5.3 Invariantes de integridade

Preservar hashes, journals, cópias de recuperação, referências persistidas,
checagens de destino, alpha exato, dimensões verificadas, limites de memória,
filtros de qualidade/economia, orçamento de originais e reserva de espaço.

A contagem de referências continua global quando existe candidato real. Não
restringi-la às bibliotecas selecionadas para acelerar a UI. A aplicação deve
revalidar as referências atuais; uma simulação anterior não é trava de concorrência.

Utilizar os serviços suportados do Jellyfin. Não escrever diretamente em seu
banco, renomear arquivos referenciados, enfraquecer filtros para aumentar o número
de sucessos nem remover journals para desbloquear uma execução.

Cancelamento ocorre durante esperas e entre transações. Uma transação de imagem
em andamento precisa chegar a um estado seguro. A interface deve comunicar essa
distinção, sem prometer interrupção instantânea.

## 6. Organização proposta em cinco ambientes

| Ambiente | Pergunta do usuário | Conteúdo e ações principais |
|---|---|---|
| **Simular** | “O que acontecerá com estas imagens usando estes ajustes?” | Bibliotecas/itens, tipos, ajustes efetivos, preservação, limite de análise e resultado por imagem. |
| **Executar** | “O que exatamente autorizo alterar agora?” | Simulação escolhida, imagens autorizadas, resumo final, aplicação, andamento e resultado da operação. |
| **Automação** | “Onde, quando e com quais ajustes o plugin trabalha sozinho?” | Escopo próprio, políticas salvas, modo, agendamentos nativos e atividade relacionada. |
| **Histórico e restauração** | “O que aconteceu e o que posso desfazer?” | Operações e transações por imagem, evidências, originais disponíveis, restauração e exclusão explicitamente selecionada. |
| **Configurações** | “Quais são meus padrões e limites?” | Padrões globais/por tipo, armazenamento, limites e preferências avançadas, com alcance de cada configuração indicado. |

**Adendo P2 (05/11/2026):** páginas internas são a direção recomendada/inicial;
abas permanecem somente como comparação opcional na bancada. Não exigir uma
escolha D1 imediata. Não há decisão de implementar um novo roteador; a integração
nativa, voltar/avançar e celular ainda devem ser validados no host.

Cada área precisa preservar seu contexto. Navegar não pode aplicar ajustes,
descartar silenciosamente um rascunho, cancelar um trabalho ou ocultar como voltar
a uma execução ativa. Uma indicação global discreta pode levar ao trabalho ativo;
o detalhe do progresso permanece na página da própria operação.

## 7. Fluxo manual proposto

### 7.1 Selecionar o alcance e os tipos

1. Oferecer bibliotecas ou itens específicos como escolhas explícitas.
2. Seleção vazia nunca significa todo o servidor. “Todas as bibliotecas e metadata
   global” permanece uma decisão explícita, com seu alcance explicado.
3. Identificar itens por nome, ano e tipo. IDs ficam disponíveis nos detalhes;
   não devem ser a informação principal necessária para escolher um filme.
4. Mostrar imagens e tipos próprios disponíveis, incluindo múltiplos fundos.
   Informar ausência, referência remota, arquivo ausente ou exclusão conhecida.
5. Explicar o alcance global das fotos de pessoas perto de sua seleção.
6. A inspeção não deve baixar imagens remotas nem alterar metadata para montar
   uma prévia. Thumbnails não podem materializar referências remotas silenciosamente.

Antes da análise, a UI deve conseguir expressar algo como: “1 filme selecionado;
até 1 capa será avaliada; os outros tipos estão fora desta seleção”.

### 7.2 Definir os ajustes desejados

O rascunho começa com padrões salvos, mas editar o rascunho não altera esses
padrões nem a automação. A tela deve indicar essa origem e permitir voltar aos
padrões conscientemente.

Para cada tipo, apresentar “Usa os padrões” ou “Personalizado” com um resumo dos
valores efetivos: codificação, qualidade quando aplicável, dimensões e SSIM.
O editor individual deixa claro se substitui uma política completa ou campos
específicos. Não implementar herança por campo apenas no texto se o modelo não
conseguir representá-la. Esta decisão pertence a P1/P3.

Manter preservação de originais próxima da decisão de execução, com o impacto
estimado no espaço. Limites avançados continuam disponíveis com explicação local.
Qualidade do encoder não deve ser apresentada como porcentagem de fidelidade.
Qualidade 100 com perdas não equivale a codificação sem perdas.

### 7.3 Simular a intenção, sem exigir descobrir a sincronização

**Recomendação principal:** uma simulação manual deve avaliar os ajustes desejados
para a seleção e identificar a ação necessária por imagem. O usuário não precisa
escolher um botão técnico diferente porque a imagem já foi convertida.

| Situação | Ação/resultado a apresentar |
|---|---|
| JPEG/PNG elegível e aprovado pelos filtros | Converter para WebP. |
| Conversão do JellyP com política diferente e original íntegro | Gerar novamente a partir do original. |
| Conversão do JellyP já com os ajustes desejados | Manter: já utiliza estes ajustes. |
| Original necessário, mas indisponível ou inválido | Manter e explicar por que não pode regenerar. |
| WebP preexistente sem histórico/original do JellyP | Manter; não inventar sua qualidade anterior nem recomprimir silenciosamente. |
| Economia ou semelhança abaixo do limite | Manter e apresentar o filtro que rejeitou. |
| Imagem ausente, remota, compartilhada ou excluída | Apresentar o motivo e sua relação com o alcance escolhido. |

Exemplo sintético: uma capa convertida anteriormente usando qualidade 85 tem
original guardado. O usuário solicita qualidade 80. O resultado deve indicar
“Gerar novamente a partir do original; qualidade proposta: 80”, sem instruir o
usuário a procurar outro botão em outra seção.

Os caminhos de conversão e regeneração continuam tecnicamente diferentes. A
proposta é coordená-los na análise manual e explicar seus resultados, preservando
o caminho seguro de cada um. Não basta disparar as duas tarefas atuais em
sequência e mostrar apenas o último relatório.

### 7.4 Revisar o resultado

O resultado fica junto da ação que o iniciou e contém:

- Identidade da simulação, seleção utilizada e ajustes efetivos congelados.
- Quantidades analisadas, propostas para alteração, mantidas, descartadas e com falha.
- Linhas por imagem com item, tipo/posição, ação proposta e motivo.
- Formato, dimensões e tamanho de origem; saída proposta e economia estimada.
- Disponibilidade do original e condição de restauração após a ação proposta.
- Seleção explícita das imagens que seguirão para execução.
- Totais separados para redução estimada das imagens, originais a guardar e
  impacto líquido estimado. Os totais existentes do plugin aparecem identificados
  separadamente, não somados ao resultado desta simulação.

Uma amostra de 20 resultados não pode ser apresentada como lista completa de
autorização de uma operação maior. P3 deve definir paginação e limites, preservando
identidade e seleção de todas as imagens autorizadas no servidor.

Com zero propostas, a tela explica o motivo. “Nada precisa mudar”, “não há imagens
dos tipos escolhidos” e “não foi possível analisar” são resultados diferentes.
Uma falha não deve ser representada como uma lista vazia de sucesso.

Editar escopo ou ajustes invalida o resultado anterior para aplicação. A UI
mantém o relatório para consulta, mas exige uma nova análise dos ajustes alterados.

### 7.5 Executar a seleção revisada

O ambiente Executar recebe uma simulação e a seleção explícita de imagens.
Sua tela é de revisão/aplicação, não um segundo formulário com configurações
independentes capazes de divergir da análise.

Antes do envio, mostrar alcance, número de imagens, ações, ajustes e preservação
dos originais. O botão pode dizer “Aplicar às 10 imagens selecionadas”. Ao remover
originais após a conversão, a consequência deve estar explícita nessa revisão.

Na aplicação, o backend deve:

1. Resolver no servidor as identidades autorizadas, sem aceitar caminhos do cliente.
2. Verificar a identidade e validade da simulação e de seus ajustes.
3. Revalidar fontes, originais, referências compartilhadas, destinos, espaço e conflitos.
4. Aplicar somente às imagens autorizadas que continuarem válidas.
5. Informar imagens alteradas externamente, invalidadas ou não iniciadas.
6. Nunca selecionar outras imagens para substituir as que deixaram de ser válidas.

Uma simulação não dispensa checagens de integridade no momento da escrita. O
objetivo do vínculo é preservar a intenção e a seleção; não prometer ausência de
concorrência nem aplicar um resultado antigo sem verificar novamente.

### 7.6 Andamento, conclusão e saída da página

Progresso, cancelamento e resultado pertencem à operação identificada. Exibir
fase, contagens com unidades claras e motivo da espera. Diferenciar, por exemplo,
“verificando referências compartilhadas do servidor” de “avaliando 10 capas”.
Uma leitura global não deve parecer uma conversão global.

Não usar porcentagem inventada quando o total ainda não é conhecido. Não somar
itens inventariados e imagens processadas como se fossem a mesma unidade. Uma
espera normal por metadata não é uma falha e deve indicar que continuará sozinha.

O usuário pode sair e voltar para trabalhos que foram aceitos para execução em
segundo plano. P3 deve definir a durabilidade desses trabalhos; não assumir que a
fila atual em memória já fornece esse comportamento após reinício do servidor.
Interrupção do processo deve produzir estado recuperável e verdadeiro, sem
prometer retomada automática de toda a fila se isso não existir.

Atualizar progresso não deve reconstruir controles em foco, perder cliques,
apagar edições ou deslocar o usuário repetidamente. Não depender de um salto
automático de rolagem para compensar ações e resultados distantes.

## 8. Demais ambientes

### 8.1 Automação

- Possui configuração própria de bibliotecas, tipos, ajustes e armazenamento.
- A gravação é explícita: “Salvar configuração da automação”. Uma execução manual
  não altera essa configuração como efeito colateral.
- Mostra separadamente: escopo configurado, modo simulação/aplicação, horários
  configurados no Jellyfin e estado da execução.
- Ociosa não significa desativada; sem horário não significa aplicar está proibido.
- Mantém o scheduler nativo e seus limites de duração/cancelamento. Não criar
  outro processo nem um segundo mecanismo de cron.
- Preserva chaves das tarefas e disparos existentes. Uma migração precisa explicar
  como as tarefas atuais de otimização e sincronização se relacionam com a nova UI.
- Não inventar horário exato da próxima execução se a API nativa não o fornece.
- Defaults, rascunho e automação devem ter origem/precedência visíveis. Copiar um
  conjunto de ajustes entre eles, se oferecido, é uma ação explícita.

### 8.2 Histórico, restauração e originais

Organizar por operação, com detalhes por imagem. Mostrar operação solicitada,
modo, alcance, datas, resultado e origem manual/agendada. IDs e detalhes técnicos
ficam acessíveis sem dominar a apresentação.

Para cada imagem, diferenciar: conversão ativa com original disponível, original
restaurado, original removido e recuperação necessária. Uma restauração deve
apresentar formato/dimensões/tamanho armazenados e a verificação de integridade.
Explicar ali que o Jellyfin ainda pode entregar WebP ao navegador.

A restauração usa a transação ativa correta, não uma entrada antiga só por ter o
mesmo nome. O histórico de uma sincronização pode incluir restauração intermediária
e nova conversão; a apresentação deve explicar a operação completa ao usuário.

**Extensão de produto solicitada na revisão P2 (05/11/2026): restauração em lote.**
Oferecer acesso direto, filtro de restauráveis e seleção explícita de transações
ativas com original disponível, incluindo “todos os restauráveis” como ação de
seleção, nunca confirmação. Revisar lista exata e confirmar; revalidar cada
transação e apresentar resultado individual sem substituições. Incertos, removidos
e inativos não são restauráveis em lote; recuperação permanece separada. Bloquear
durante execução e invalidar análise/revisão e seleções obsoletas após mutação.
Isso é **proposta demonstrada só no protótipo**, não API/backend implementado.
Na iteração 3, os controles pertencem à lista Operações e a exclusão é uma
subpágina local independente dentro do Histórico. Paginação cliente por imagem
(inclusive grupos longos) conserva seleção e destinatários completos nas revisões;
o saldo principal acompanha essa seleção. Automação explica análise/relatório,
sem fila de aprovação, e tem desvio próprio às proteções globais com retorno ao
rascunho. Ver desenho P2 §8 e adendo P1 §9; paginação/relatórios reais seguem em P3.
P3 define autorização, concorrência, persistência e compatibilidade; ver o adendo
do [contrato P1](UX-P1-CONTRACT.pt-BR.md#8-adendo-de-produto-p2--feedback-com-contexto-05112026).

Excluir originais é um fluxo próprio:

1. Escolher explicitamente quais originais serão removidos.
2. Verificar sua elegibilidade para remoção com as regras existentes.
3. Mostrar quantidade, alcance e bytes que poderão ser liberados.
4. Confirmar a consequência sobre restauração e regeneração futura.
5. Executar somente sobre essa seleção e manter os originais incertos.

“Todos os originais elegíveis do plugin”, se oferecido, é uma escolha explícita.
Não pode ser inferida da ausência de filtro ou de uma seleção de filmes em outra
página. Não reutilizar o endpoint atual de exclusão global como se fosse seletivo.

### 8.3 Configurações e informação progressiva

Manter padrões globais, políticas por tipo e limites com alcance identificado.
Mostrar os valores efetivos antes de pedir que o usuário edite detalhes.
Guardar alterações somente por ação explícita, com indicação de rascunho alterado.

Opções avançadas ficam em contexto, sem esconder decisões essenciais como manter
ou excluir originais. A ajuda deve responder à dúvida da tarefa atual; um guia
extenso não pode ser pré-requisito para compreender botões básicos.

## 9. Contratos de estado e mudanças técnicas necessárias

Os nomes abaixo descrevem responsabilidades. Não determinam classes, endpoints
ou formatos de armazenamento finais; estes serão definidos em P3.

| Estado/entidade | Responsabilidade | Regra de isolamento |
|---|---|---|
| Padrões salvos | Valores reutilizáveis e limites com alcance definido. | Só mudam em gravação explícita de configurações. |
| Configuração da automação | O que tarefas agendadas podem fazer e onde. | Não muda ao simular ou aplicar manualmente. |
| Rascunho manual | Seleção e ajustes em edição. | Não autoriza processamento nem altera configurações salvas. |
| Simulação identificada | Snapshot da intenção, candidatos e resultados por imagem. | Não altera imagens nem remove originais. |
| Seleção autorizada | Subconjunto exato da simulação para aplicar. | Não é substituído por um novo cursor global. |
| Execução | Solicitação, origem, estado, progresso e resultados próprios. | Não sobrescreve o relatório de outra operação. |
| Journal de imagem | Evidência durável de escrita, original e recuperação. | Preserva compatibilidade e regras de integridade existentes. |

P3 precisa definir pelo menos:

- Operação e modo explícitos em solicitações e relatórios; não inferi-los pelo
  nome de um arquivo de cursor ou pelo botão que uma página lembra ter acionado.
- Identidades estáveis, snapshots de política, hashes e regras de validade da análise.
- Identidade de slot com detecção de mudanças de índice/arquivo, especialmente
  para múltiplos fundos. Um índice numérico isolado não basta após alterações.
- Dados necessários para mostrar formato, dimensões, tamanho e ações propostas.
- Paginação, retenção, expiração e limites dos planos e relatórios. Persistir
  metadados de análise não deve exigir guardar todos os WebPs gerados na simulação.
- Admissão e recibo da operação, concorrência, cancelamento e estados terminais.
- Comportamento de reenvio/double click: não produzir duas aplicações da mesma
  autorização. Definir idempotência, não depender só de desabilitar o botão.
- Estado após recarregar a UI, fechar a conexão HTTP, reiniciar o servidor ou
  encontrar uma transação interrompida.
- Tratamento de sucesso parcial, rejeições de qualidade, fonte alterada e falhas.
- Autorização de administrador para inspeção, simulação e mutações. Nunca receber
  caminhos de arquivos do cliente nem expor segredos ou caminhos em resultados públicos.

Na baseline atual, `PolicyFingerprint` é um hash, não uma cópia legível dos ajustes
anteriores. Não inferir a qualidade 85 a partir desse hash, nem estimar qualidade
anterior como um fato ao ler WebP. Para novas operações, registrar os valores
efetivos necessários. Em registros antigos, apresentar “ajustes anteriores não
registrados” quando for o caso; formato original e hash disponíveis continuam úteis.

A análise unificada deve planejar conversões e regenerações sem perder verificações
globais e sem simplesmente juntar duas respostas de `Run`/`Sync`. A estratégia de
aplicação pode reutilizar os mecanismos atuais de conversão e restauração segura.

### 9.1 Mapa do código atual para um agente iniciar a leitura

| Arquivo/módulo | O que conferir antes de projetar alterações |
|---|---|
| [Web/config.html](../Jellyfin.Plugin.JellyP/Web/config.html) | `collect`, `save`, `run`, `renderStatus`, `renderRecords`, seleção e eventos. Formulário compartilhado e dependências entre seções. |
| [Api/JellyPController.cs](../Jellyfin.Plugin.JellyP/Api/JellyPController.cs) | `Selection`, `Run`, `Sync`, `Cancel`, `Rollback` e exclusão de originais; autorização e despacho nativo. |
| [Configuration/PluginConfiguration.cs](../Jellyfin.Plugin.JellyP/Configuration/PluginConfiguration.cs) | Padrões, `ScopeMode`, políticas e substituição completa por tipo; flags salvas da automação. |
| [Migration/OptimizationCoordinator.cs](../Jellyfin.Plugin.JellyP/Migration/OptimizationCoordinator.cs) | Snapshot da fila, cursores por modo/operação, seleção de candidatos, relatório principal, sincronização, rollback e retirement. |
| [Migration/Models.cs](../Jellyfin.Plugin.JellyP/Migration/Models.cs) | `ArtworkReference`, `MigrationRecord`, `RunReport`, recibos e contrato do store. |
| [Migration/ItemArtworkInspection.cs](../Jellyfin.Plugin.JellyP/Migration/ItemArtworkInspection.cs) | Inspeção preliminar por slot, sem garantia de conversão. |
| [Infrastructure/JellyfinArtworkStore.cs](../Jellyfin.Plugin.JellyP/Infrastructure/JellyfinArtworkStore.cs) | Leitura persistida, referências globais, escopo, serviços de imagem e conflitos. |
| [Tasks](../Jellyfin.Plugin.JellyP/Tasks) | Chaves, integração e serialização das tarefas nativas. |
| [Tests](../tests/Jellyfin.Plugin.JellyP.Tests) | Regressões de integridade, escopo, concorrência, sincronização e API. |
| [scripts/smoke-ui.cjs](../scripts/smoke-ui.cjs) | Fluxos reais de navegador; atualizar expectativas para intenção do usuário, não só localização de elementos. |

### 9.2 Compatibilidade e migração

- Preservar GUID do plugin e chaves nativas `JellyPOptimizeArtwork` e
  `JellyPSynchronizePolicies`, salvo decisão explícita com migração testada.
- Preservar disparos existentes; não criar horários ou habilitar conversões como
  efeito colateral da atualização.
- Ler e restaurar journals antigos. Não substituí-los para acomodar a nova apresentação.
- Migrar escopos sem ampliá-los. Considerar `legacy`, bibliotecas removidas e listas
  de itens vazias. Uma situação que exige revisão não deve virar “tudo” silenciosamente.
- Definir o que acontece com os endpoints atuais para evitar quebra silenciosa de
  integrações administrativas existentes.
- Configurações e relatórios antigos podem não conter dados dos novos contratos.
  Mostrar limitações honestamente e testar o upgrade a partir da baseline pública.

## 10. Decisões pendentes e momento de resolução

| ID | Decisão | Recomendação inicial / etapa limite |
|---|---|---|
| D1 | Páginas internas recomendadas/iniciais; abas só comparação opcional (adendo P2). | Validar navegação, voltar/avançar e celular; integração nativa no host em P5. Não exigir escolha binária imediata. |
| D2 | Tratamento manual de conversão e regeneração. | Análise única da intenção por padrão; validar entendimento em P2 e contrato em P3. |
| D3 | Herança de ajustes por tipo. | **Resolvida em nível de produto (P1)**: substituição completa por tipo, sem herança por campo; ver contrato §3.1. Representação/migração final em P3. |
| D4 | Alcance de reservas, quotas, retenção e padrões. | **Resolvida em nível de produto (P1)**: reserva/quota/retenção/economia globais; escopo e preservação por operação; ver contrato §3.2. Contrato técnico em P3. |
| D5 | Durabilidade, validade, paginação e retenção das simulações. | Planos identificados e limitados; revalidar antes de aplicar. Resolver em P3. |
| D6 | Relação entre tarefas nativas existentes e novo fluxo. | Preservar chaves/disparos e segurança; análise manual unificada não exige segundo scheduler. Resolver em P3. |
| D7 | Exclusão seletiva e sua migração de API. | Seleção explícita de originais; opção global separada se necessária. Resolver em P3. |
| D8 | Dados antigos e operações em andamento durante upgrade. | Sem inventar ajustes anteriores, perder recuperação ou reinterpretar fila. Resolver em P3. |

O plano não exige perguntar novamente ao usuário cada escolha técnica rotineira.
As decisões devem ser sustentadas pelo código, pelos protótipos e pelo escopo
autorizado. Mudanças de comportamento de produto além desta proposta precisam ser
explicitadas, não introduzidas silenciosamente durante a implementação.

## 11. Etapas, dependências e entregáveis

Cada etapa depende de instrução que autorize o trabalho correspondente. P1/P2 e a
integração incremental P3–P7 têm os registros de autorização acima. Os checklists
abaixo definem o aceite completo, não uma afirmação de que todos os cenários já
foram exercitados. P8/publicação e implantação em produção continuam pendentes.
Não inferir implantação a partir de autorização de implementação e testes locais.

### P0 — Registrar a baseline e os limites

- [x] Documentar contexto, falhas observadas, proposta e regras de continuidade.
- [x] Separar o comportamento atual do comportamento proposto.
- [x] Registrar que implementação, produção e release não estão autorizados por este plano.
- [ ] Quando houver retomada, reconferir versão, instruções aplicáveis e árvore de trabalho.

**Saída:** este documento e seus links de descoberta. O último item é uma
verificação de início da próxima tarefa, não motivo para alterar produção agora.

### P1 — Definir o contrato de produto e de estado

Dependência: P0 e autorização para esta etapa. Autorizada e entregue em 17/10/2026.

- [x] Mapear cada campo/botão atual para uma das cinco áreas e identificar seu alcance.
- [x] Resolver D3/D4 em nível de produto: herança, rascunho, padrões e automação.
- [x] Descrever jornadas de primeira conversão, mudança de qualidade, restauração,
  exclusão de originais e agendamento.
- [x] Definir estados vazios, dados indisponíveis, conflito, espera, cancelamento,
  sucesso parcial e recuperação necessária.
- [x] Definir texto e unidade das métricas por execução versus totais do plugin.
- [x] Registrar decisões e questões restantes com sua evidência.

**Entrega:** [UX-P1-CONTRACT.pt-BR.md](UX-P1-CONTRACT.pt-BR.md).

**Aceite:** cada ação tem alcance, efeito persistido, estados e resultado definidos.
Não há seletor distante que altere o significado de um botão de outra tarefa.
O contrato §7 registra como o aceite foi demonstrado; a validação com pessoa sem
contexto permanece evidência desejável, adiada sem bloquear a implementação.

### P2 — Desenhar e validar os fluxos antes de implementar o runtime

Dependência: P1. Artefatos de desenho/protótipo também precisam estar no escopo autorizado.
Autorizada em 17/10/2026 (artefatos + exercício). Artefatos entregues; exercício
sem contexto adiado pela decisão de 06/11/2026. **Direção de produto aceita pelo
mantenedor para implementação**; isso não equivale a validação independente ou no host.

Revisão autorizada em 05/11/2026: protótipo standalone refeito em torno da jornada
selecionar → simular → escolher subconjunto → revisar → acompanhar → restaurar.
Seleção inicial vazia, consentimentos funcionais, remoção com revisão separada,
isolamento de estado, invalidação de simulação e retenção global explícita.
Renderização desktop/mobile inspecionada; verificações de navegador/auditoria
aprovadas (contagens atuais no desenho P2), sem API real. A referência ao Jellyfin é
aproximada a partir de código público; **não houve integração real no host**.
Esse resultado técnico não fecha o aceite sem contexto ou a integração no host.

A iteração de feedback com contexto mantém páginas internas, separa status/ação
no editor por tipo, explicita cópia de padrões globais, narra a simulação e amplia
a restauração para lote revisado. Automação salva múltiplas bibliotecas ou todas
explicitamente e explica tarefas/modo/disparos nativos. Armazenamento distingue
mínimo livre e teto total de originais; a idade atual de retenção usa `CompletedUtc`,
com expiração elegível no início de otimização aplicada, não à meia-noite. O relato
do teste básico não substitui uma pessoa sem contexto. Registro sanitizado e
evidência de código: desenho P2 e adendo P1. Nenhuma dessas mudanças autoriza P3.

- [x] Criar wireframes das cinco áreas, incluindo celular e navegação por teclado.
- [x] Representar seleção, editor por tipo, lista de resultado e revisão de execução.
- [x] Representar todos os estados definidos em P1, não apenas o caminho de sucesso.
- [x] Revisar visual/fluxo do protótipo e exercitar os contratos sintéticos no navegador, sem tocar no runtime.
- [ ] Validar descoberta da navegação recomendada e compreensão D2 por objetivo, sem ensinar botões; comparação de abas é opcional.
- [ ] Registrar onde o participante hesitou, procurou feedback ou interpretou alcance errado.
- [ ] Revisar o fluxo antes de considerar a quantidade de telas e seus textos fechados.

**Artefatos:** [UX-P2-DESIGN.pt-BR.md](UX-P2-DESIGN.pt-BR.md) ·
[ux-prototype/jellyp-p2.html](ux-prototype/jellyp-p2.html) ·
[UX-P2-EXERCISE.pt-BR.md](UX-P2-EXERCISE.pt-BR.md) (roteiro + template de registro).
O desenho de P2 está encerrado como referência aceita para iniciar implementação,
sem congelar futuras correções de UX. Os itens de exercício externo permanecem
desmarcados como evidência adiada, não como impedimento para P3–P6.

**Aceite para avançar:** direção aceita pelo mantenedor após as jornadas
exercitadas e correções registradas; 311 verificações sintéticas aprovadas,
incluindo 20 axe. Integração nativa e compreensão por novos usuários ainda não
comprovadas. O exercício por objetivo continua disponível para quando houver
participantes; dúvidas recorrentes geram revisão, não um tutorial obrigatório maior.

### P3 — Mapear capacidades existentes e definir contratos por jornada

Dependências: P1 e direção de P2 aceita pelo mantenedor. Não depende do exercício
externo adiado nem de validação em runtime que só a implementação poderá fornecer.

P3 é uma definição técnica curta, incremental e vinculada à próxima jornada real,
não outra fase de protótipo standalone ou uma reescrita presumida do backend.
O backend publicado é a baseline funcional a preservar; o problema observado é
o fluxo confuso. Capacidades novas do desenho, contudo, não devem ser tratadas
como existentes sem conferir o código e os contratos de API.

- [ ] Mapear, para a próxima jornada nativa, serviços/endpoints existentes reutilizáveis,
  lacunas demonstradas e checagens necessárias; evitar reformular mecanismos funcionais.
- [ ] Definir responsabilidades do quadro da seção 9 e resolver D5–D8.
- [x] Definir formato das solicitações/resultados, identidades e snapshots necessários.
- [x] Definir validade e revalidação de uma seleção revisada, incluindo múltiplos fundos.
- [x] Projetar isolamento de rascunho/padrões/automação e compatibilidade de configuração.
- [x] Definir idempotência, fila, concorrência, cancelamento e comportamento após desconexão/reinício.
- [x] Definir retenção e limites para planos e relatórios sem inflar armazenamento.
- [x] Projetar leitura de journals antigos e ausência de valores de política legíveis.
- [ ] Produzir matriz de migração e riscos, preservando contratos de integridade.

**Aceite:** existe um desenho revisável de dados e estados que sustenta a UX sem
substituir seleção por cursor ou enfraquecer checagens. Não chamar contratos de
“validados em runtime” por terem sido descritos ou compilados. Resolver antes da
mutação as decisões críticas daquela jornada; refinar decisões das demais à medida
que forem implementadas. Aparência nativa, routing, foco e interação real com a API
só podem ser aceitos com implementação no Jellyfin, em P4/P5/P7 por jornada.

#### Execução incremental de P3–P7

Primeira fatia proposta: página real de plugin em **Jellyfin descartável marcado**,
com componentes nativos, seleção e análise conectadas aos serviços existentes;
completar a mesma jornada com seleção revisada exata, aplicação, andamento e
restauração. O teste de ponta a ponta usa arquivos sintéticos, não produção.

Definir o contrato necessário (P3), implementar a conexão/ajuste mínimo (P4/P5),
inspecionar e exercitar no host (P7), e corrigir com a evidência obtida. A aplicação
fica indisponível até existir suporte seguro no servidor; não conectar uma seleção
exata a um endpoint que usa outro cursor ou alcance apenas para fazer a UI funcionar.
Não é preciso concluir todo o backend antes de renderizar a primeira tela nativa,
nem esperar todas as telas para testar a primeira jornada integrada.

### P4 — Implementar o suporte de backend

Dependência: decisões P3 necessárias à jornada e autorização de implementação.
Reaproveitar o backend atual; mudanças P4 só para lacunas comprovadas, em conjunto
com a integração P5 e testes P7 daquela jornada.

- [x] Implementar análise identificada com ações por imagem e motivos explícitos.
- [x] Implementar a aplicação do conjunto exato autorizado com revalidação atual.
- [x] Separar estados salvos e relatórios por operação.
- [x] Reutilizar mecanismos seguros de conversão, regeneração e recuperação.
- [x] Adicionar testes significativos de isolamento, migração, concorrência e seleção obsoleta.
- [x] Preservar autorização administrativa e ausência de caminhos arbitrários no cliente.

**Aceite:** cenários V01–V13 abaixo passam na camada de serviço/API correspondente;
nenhum caminho escreve fora da seleção para compensar um descarte.

### P5 — Implementar navegação e fluxo manual

Dependências: direção aceita de P2, decisões P3 da jornada e autorização de
implementação. Iterar com P4; não esperar a conclusão de todo o backend. Controles,
tema e comportamento reais do Jellyfin são critérios desta implementação, não de P2.

- [x] Separar a página monolítica em responsabilidades de navegação, edição de
  rascunho, análise, execução e apresentação de resultados.
- [x] Utilizar componentes e estilo nativos do Jellyfin, evitando uma biblioteca
  visual paralela para os mesmos controles.
- [x] Implementar Simular e Executar com ações explícitas e progresso contextual.
- [x] Preservar rascunhos e contexto ao navegar, atualizar progresso ou mudar idioma.
- [x] Implementar seleção por imagem, informação efetiva por tipo e revisão final.
- [ ] Tratar teclado, foco, leitores de tela, contraste e telas estreitas.

**Aceite:** V01–V08 e V14 passam no navegador; uma execução não exige procurar
o status em outra área, e uma mudança de qualidade não exige descobrir um botão oculto.

### P6 — Implementar automação, histórico e administração de originais

Dependências: contratos e suporte necessários à respectiva jornada em P3/P4/P5.
Também se implementa e valida por fatias; não exige reescrever os serviços atuais.

- [x] Implementar configuração independente de automação e vínculo com o scheduler nativo.
- [x] Implementar histórico por operação com detalhes por imagem.
- [x] Apresentar evidências de restauração e dados do arquivo armazenado.
- [x] Implementar seleção/revisão explícita para exclusão de originais.
- [x] Exibir escopos de quotas, retenção e métricas sem misturar totais.
- [ ] Validar compatibilidade de configuração, journals e agendamentos da baseline.

**Aceite:** V09–V13 passam; restaurar permanece possível usando registros antigos,
e uma exclusão não alcança originais que o usuário não selecionou.

### P7 — Validar integridade, comportamento e usabilidade

Começa junto à primeira jornada implementada em P4/P5 e acompanha P6.
P4–P6 completas são dependência da consolidação final, não do início dos testes.

- [ ] Executar a matriz V01–V15, registrando versão, fixture, resultado e evidência.
- [ ] Executar suíte .NET e testes de publicação pertinentes, com dependências fixadas.
- [ ] Validar em Jellyfin descartável real, incluindo reinício e disparo nativo.
- [ ] Revisar teclado, foco estável, leitura de status e layout móvel.
- [ ] Repetir tarefas com pessoa sem contexto quando houver participantes
  (validação adiada, não bloqueia alpha; registrar como não executada se continuar indisponível).
- [ ] Coletar feedback assíncrono por tarefa quando disponível, sem classificá-lo
  como sessão observada ou supor que ausência de relatos comprova usabilidade.
- [ ] Separar testes aprovados, reprovados, não executados e limitações remanescentes.

**Aceite:** critérios de UX e integridade passam separadamente. Não marcar a etapa
concluída apenas pelo número de testes, por HTTP 200 ou por screenshots sem overflow.
Uma entrega alpha pode ter validação independente de usabilidade adiada e declarada;
isso **não dispensa** os testes de integridade, autorização, restauração, exclusão,
migração e integração real aplicáveis aos recursos entregues. Feedback de uso
continua após disponibilização, sem substituir esses critérios prévios.

### P8 — Documentar a versão final e preparar uma entrega

Dependência: P7.

- [ ] Atualizar guias, arquitetura implementada e notas de migração para o comportamento final.
- [ ] Reescrever títulos/descrições em torno da implementação entregue, mantendo
  decisões abandonadas somente quando explicarem uma limitação relevante.
- [ ] Auditar publicação, privacidade, arquivos novos e mudanças no pacote.
- [ ] Preparar versão e release somente se uma instrução posterior também autorizar essa entrega.
- [ ] Preparar inicialmente entrega alpha com limitações de usabilidade conhecidas,
  canais de feedback e testes técnicos registrados; não promover a estável por
  tempo de uso ou silêncio de usuários. Publicação/push exigem autorização própria.
- [ ] Validar instalação/upgrade em fixture e documentar recuperação/rollback da atualização.
- [ ] Considerar produção somente no escopo específico autorizado, sem retomar workers
  ou ampliar a biblioteca como efeito colateral da atualização da UI.

**Aceite:** a documentação corresponde ao software entregue e a migração foi
demonstrada. Passar P8 não equivale, por si só, a autorização de implantação.

## 12. Matriz de validação e cenários obrigatórios

Usar arquivos gerados e servidor descartável marcado. Exclusão permanente,
interrupção abrupta, falta de espaço e corrupção de estado são cenários de fixture,
não experiências a repetir na biblioteca de produção.

| ID | Cenário | Evidência esperada |
|---|---|---|
| V01 | Selecionar um item sem imagens ou sem o tipo marcado. | Explicação anterior à análise; zero bem explicado; nenhuma imagem baixada ou alterada. |
| V02 | Simular e aplicar uma capa JPEG/PNG com original guardado. | Resultado por imagem; aplicação da identidade revisada; hash/referência válidos; original disponível. |
| V03 | Trocar qualidade 85 para 80 em uma conversão com original. | A simulação única propõe regenerar do original ou explica um filtro; não exige outro botão. |
| V04 | Simular novamente sem mudar uma política já aplicada. | Resultado “já utiliza estes ajustes”, sem recompressão desnecessária. |
| V05 | Regenerar com redução de dimensões e depois restaurar. | Dimensões/tamanhos reais apresentados; restauração exata do original de origem. |
| V06 | Logo transparente, sem perdas e com perdas. | Verificação de alpha e pixels conforme o modo; descartes honestos; original preservado. |
| V07 | Revisar dez imagens, autorizar um subconjunto e alterar outra imagem antes de aplicar. | Só as autorizadas válidas são processadas; a alterada é identificada; nenhuma substituição automática. |
| V08 | Desconectar, navegar e cancelar durante fila, inventário e espera. | Contexto recuperável; identidade da operação preservada; cancelamento seguro e foco estável. |
| V09 | Teste manual com seleção/política diferente da automação. | Configuração e disparos da automação inalterados. |
| V10 | Agendamento nativo com scan/refresh relacionado e tarefa não relacionada. | Espera/cancelamento corretos; tarefa não relacionada não exige intervenção manual. |
| V11 | Restaurar depois de regenerar, usando histórico novo e antigo. | Transação ativa correta; bytes do original; referência persistida e evidência explícita. |
| V12 | Excluir um subconjunto de originais guardados. | Revisão do alcance/bytes; somente os selecionados elegíveis removidos; incertos preservados. |
| V13 | Upgrade da baseline com journals, escopo legado e disparos configurados. | Sem ampliação de escopo, perda de recuperação, novos horários ou conversões habilitadas. |
| V14 | Teclado, idioma e viewport estreito durante edição e progresso. | Navegação e foco previsíveis; mensagens acessíveis; rascunho intacto; ações essenciais visíveis. |
| V15 | Falta de espaço, erro de persistência e interrupção entre salvar/restaurar. | Reserva/orçamento respeitados, estado verdadeiro e recuperação conservadora intacta. |

A lista cobre caminhos positivos e negativos. Um descarte pelo SSIM esperado é
sucesso da regra de proteção, mas não substitui um caso positivo de regeneração.
Registrar os dois quando a funcionalidade exige ambos.

### 12.1 Exercício de usabilidade sem tutorial

Pedir a alguém sem contexto que realize, em fixture, estas tarefas:

1. “Descubra o que mudaria ao usar qualidade 80 nestas imagens.”
2. “Aplique somente a estas duas imagens e mantenha possibilidade de desfazer.”
3. “Encontre a operação enquanto ela espera por metadata e interrompa a espera.”
4. “Desfaça uma das alterações e confirme o formato armazenado.”
5. “Configure trabalho futuro em uma biblioteca sem alterar seu teste manual.”
6. “Libere somente o original guardado que você escolheu.”

Observar se a pessoa encontra a ação e o resultado, prevê o alcance, compreende
quais ajustes estão salvos e identifica se pode restaurar. Não ensinar previamente
“clique no botão de baixo com o modo escolhido em cima”. Esse tipo de instrução
é precisamente a dependência que a refatoração precisa eliminar.

Registrar intervenções do facilitador e revisar o fluxo quando forem necessárias
para tarefas básicas. Não reduzir a avaliação à preferência por cores ou ao número
de cliques; verificar compreensão e correspondência com o objetivo.

## 13. Regras de continuidade para outro agente

1. Ler [PLAN.md](PLAN.md), este documento e as instruções aplicáveis ao checkout.
2. Verificar o pedido mais recente: documentação não autoriza implementação.
3. Reconferir baseline e alterações de terceiros; os caminhos deste mapa podem evoluir.
4. Identificar a etapa autorizada e suas dependências. Não saltar prototipação e
   contratos para apenas redistribuir o formulário existente em abas.
5. Atualizar tarefas e decisões com evidência real; manter propostas distintas do que passou.
6. Não tomar uma hipótese de bug como diagnóstico: distinguir fonte armazenada,
   cache, representação HTTP, identidade de item e disponibilidade de original.
7. Não extrair credenciais de bancos ou logs. Usar somente mecanismos de acesso
   explicitamente previstos e autorizados para o trabalho.
8. Usar ambientes descartáveis para alterações de dados e falhas induzidas.
   Acesso limitado concedido em uma tarefa anterior não autoriza implantação,
   varreduras, conversões amplas ou retomada de serviços durante a refatoração.
9. Manter documentos públicos sanitizados; operações reais e segredos pertencem
   a registros privados, nunca a fixtures ou relatos públicos.
10. Ao entregar, informar o que foi implementado, como foi validado, o que permanece
    pendente e quais decisões ainda são propostas.

## 14. Referências e documentos relacionados

- [Arquitetura atual](ARCHITECTURE.md): integração, filas, cursores e limites existentes.
- [Validação atual](VALIDATION.md): testes e limites da evidência anterior.
- [Recuperação](RECOVERY.md): journals, originais e regras de restauração.
- [Contribuição](../CONTRIBUTING.md): integridade, comandos e publicação.
- [Uso atual em português](USAGE.pt-BR.md): fluxo lançado; será atualizado somente
  quando a refatoração tiver sido implementada e validada.
- [Heurísticas de usabilidade — Nielsen Norman Group](https://www.nngroup.com/articles/ten-usability-heuristics/):
  referência para visibilidade do estado, linguagem, reconhecimento e prevenção de erros.
- [Mensagens de status — W3C](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html):
  acessibilidade das mensagens de progresso/resultado, sem depender de deslocar o foco.
- [Padrão de abas — W3C APG](https://www.w3.org/WAI/ARIA/apg/patterns/tabs/):
  navegação por teclado e semântica quando abas forem a solução escolhida.

As referências orientam o projeto; não provam conformidade ou usabilidade da
implementação atual. A avaliação de problemas e a proposta de fluxo são específicas
do JellyP e precisam ser verificadas pelos critérios deste documento.
