# Como usar o JellyP

Abra **Painel → Plugins → JellyP → Configurações** e escolha Português (Brasil)
ou English. Após atualizar, reinicie o Jellyfin, recarregue a página e confira a
versão exibida no cabeçalho.

A página tem cinco áreas: **Simular**, **Executar**, **Automação**,
**Histórico e restauração** e **Configurações**. O trabalho manual é independente
da automação agendada.

## Escopo e políticas de imagem

Escolha **Bibliotecas selecionadas** e marque as bibliotecas que o JellyP pode
processar. Pastas, temporadas, episódios e versões próprias entram
automaticamente. Uma seleção vazia ou excluída impede a execução; nunca significa
todas as bibliotecas. Configurações globais antigas continuam preservadas para as
tarefas existentes, mas a página pede que você escolha bibliotecas antes de salvar
um escopo global legado.

Para um teste preciso, escolha **Itens específicos**, busque pelo nome ou cole o
link/ID dos detalhes. Os IDs selecionam exatamente esses itens: escolher uma série
não inclui seus episódios. **Todas as bibliotecas e metadata global** inclui
explicitamente o servidor inteiro. Pessoas são entidades globais do Jellyfin; o
escopo de biblioteca inclui as pessoas creditadas nos itens selecionados e suas
fotos também podem aparecer em outras bibliotecas. Arquivos compartilhados
continuam protegidos em todas as bibliotecas.

Marque qualquer um dos 11 tipos de imagem. Padrões globais ou ajustes por tipo
definem qualidade 0–100, modo com/sem perdas, redução opcional de dimensões e
SSIM mínimo. Os padrões são qualidade 85, SSIM 0,96, sem redução, originais
guardados e pelo menos 5% e 4 KiB de economia. Qualidade 100 ainda tem perdas;
**Sem perdas** exige pixels idênticos. A transparência é verificada exatamente. O
filtro SSIM é independente da qualidade do codificador.

## Simule e selecione imagens exatas

Em **Simular**, escolha escopo, tipos de imagem, qualidade, originais e o limite
avançado de imagens, depois clique em **Simular as imagens**. O servidor lê os
bytes reais, confere referências compartilhadas e o progresso do inventário, e
retorna propostas. Nada é alterado nessa etapa.

O resultado mostra contagens avaliadas e propostas, motivos para ignorar imagens,
a conta de armazenamento da seleção exata e propostas com nomes de itens em todas
as páginas de resultado. Imagens já em WebP, compartilhadas, remotas, ausentes ou
não suportadas são explicadas em vez de ignoradas silenciosamente. Se nenhum item tiver candidata local utilizável, o
JellyP evita o inventário global; candidatas reais ainda passam pela checagem de
referências compartilhadas.

Selecione as propostas desejadas em todas as páginas de resultado e escolha
**Revisar seleção**. Somente essas identidades exatas podem seguir. Mudar escopo,
políticas ou padrões invalida o plano e exige nova simulação. O plano também
expira; confira a validade antes de aplicar.

A análise manual é processada em partes determinísticas ordenadas por chave. O
padrão manual salvo e cada parte avaliam no máximo 1.000 candidatas; isso não é um
limite da biblioteca inteira. Quando **Continuar para a próxima parte** aparece,
ela alcança as próximas posições sem repetir a primeira parte. A continuação é
idempotente por parte pai e é recusada se expiração, padrões salvos, escopo ou
políticas mudaram. Uma parte consumida ainda pode continuar, e a parte anterior
permanece disponível para leitura. A automação mantém o limite separado de 10.000
candidatas.

## Execute e acompanhe os recibos

Em **Executar**, revise os ajustes congelados, a seleção exata e a conta de
armazenamento, depois confirme. O JellyP revalida cada imagem selecionada antes da
mutação. A operação é persistida no servidor: navegar, recarregar ou reconectar
não a cancela nem cria um segundo disparo.

Progresso, esperas e resultados individuais ficam disponíveis em **Histórico e
restauração**. **Parar** atua na operação exata e cancela em fronteiras de
transação ou durante esperas de metadata; o trabalho não gravado fica para uma
execução posterior. A conversão aguarda automaticamente uma varredura completa ou
uma atualização do item, de seus pais ou de sua biblioteca. Metadata e tarefas
agendadas sem relação não precisam ser desligadas. Evite envios manuais de
imagens durante a conversão porque o Jellyfin não oferece uma trava compartilhada
para plugins.

## Automação

Em **Automação**, configure escopo, políticas, originais e se as tarefas nativas
do JellyP podem analisar ou aplicar. Salve a automação independentemente dos
rascunhos manuais e dos Padrões globais. Salvar a automação não amplia o escopo
nem habilita conversões.

O JellyP expõe **Otimizar imagens da biblioteca** e **Sincronizar políticas de
imagens** em Painel → Tarefas Agendadas → JellyP. Configure os disparos lá; o
plugin não cria um agendamento automaticamente. Identidades das tarefas e
disparos existentes são preservados na atualização. Simular ou aplicar
manualmente não habilita nem desabilita o processamento agendado.

Quando uma política muda, a análise manual propõe regenerar a partir do original
guardado. Revise e aplique essas propostas exatas. A tarefa nativa de
sincronização também pode regenerar políticas inalteradas quando essa opção está
habilitada em Automação. Imagens cujo original foi aposentado aparecem como
**OriginalUnavailable**. Arte externa alterada não é sobrescrita. Desmarcar um
tipo não desfaz conversões existentes; use a restauração explicitamente.

## Histórico, restauração e remoção permanente

**Histórico e restauração** lista recibos persistidos de operações e seus
resultados individuais. A lista padrão é deliberadamente preliminar: mostra
originais guardados que podem ser selecionados para verificação (`CanReview`),
não uma afirmação de que todo ID é restaurável. Selecione IDs exatos, abra a
revisão e leia o resultado por ID. IDs inválidos, compartilhados, alterados,
incertos ou superados devem ser removidos explicitamente; nada é descartado ou
substituído silenciosamente. Os filtros explícitos verificados
`restorable`/`retirable` e a ação `Backups/Review` fazem a checagem
autoritativa.

A restauração exige originais guardados. A aposentadoria é permanente: sem o
original, não há restauração nem regeneração a partir dele. Cada ação é
revalidada e confirmada para as identidades exatas selecionadas. A aposentadoria
de originais não remove os WebP ativos. A fila e o apply revalidam os IDs exatos
novamente mesmo após uma revisão bem-sucedida.

Retenção zero significa remoção manual; caso contrário, originais elegíveis
expiram no início de um lote de aplicação. Siga a [recuperação](RECOVERY.md) para
pendências, desinstalação e pouco espaço.

## Configurações e contabilização de armazenamento

**Configurações** contém os Padrões globais para novos testes manuais, além das
proteções globais de armazenamento e filtros de economia. Essas proteções valem
para todas as operações, inclusive automação. Salvar padrões invalida a análise e
a revisão manuais; não salva nem amplia a automação.

Mantenha a reserva padrão de 2 GiB e o limite de 512 MiB para originais. Guardar
originais inicialmente ocupa mais espaço. O espaço líquido liberado desconta os
arquivos de backup reais da redução verificada das conversões ativas; não é uma
previsão exata de `df`. Journals, banco, caches de cliente, blocos do sistema de
arquivos e alterações posteriores de outros plugins afetam o total. Se o disco já
estiver cheio, libere espaço conhecido e alheio ao metadata antes de tentar
converter; reduzir a reserva não cria espaço. Não apague metadata nem o banco.

## Atualização e rollback

Faça backup dos dados e do banco do Jellyfin antes de atualizar. Instale o novo
pacote pelo catálogo ou manualmente, reinicie em uma janela ociosa e confira a
versão ativa. O GUID do plugin, as identidades das tarefas nativas, os disparos
configurados, o escopo/políticas salvos e os journals existentes são preservados.
Journals antigos continuam utilizáveis sem inventar ajustes históricos.

Um downgrade seguro de DLL não é prometido. Para reverter, use o backup anterior
à atualização e uma restauração controlada, não apenas a troca da DLL. Resolva
recuperações pendentes antes de desinstalar e mantenha o diretório de dados do
plugin até que seus originais não sejam mais necessários.

Veja [escopo e limites](../README.md#supported-scope-and-limits),
[recuperação](RECOVERY.md) e [validação](VALIDATION.md). A validação de navegador
cobriu somente Chromium; Firefox, Safari, leitores de tela e dispositivos físicos
continuam não validados.
