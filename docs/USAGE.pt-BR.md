# Como usar o JellyP

Abra **Painel → Plugins → JellyP → Configurações** e escolha Português (Brasil).
Após atualizar, reinicie o Jellyfin, recarregue a página e confira a versão no topo.

## Comece pelas bibliotecas

Em **1. Selecione bibliotecas ou itens**, escolha **Bibliotecas selecionadas** e
marque onde o plugin deve atuar. Pastas, temporadas e episódios entram automaticamente.
Uma seleção vazia ou excluída impede a execução; nunca significa todas as bibliotecas.
Tarefas existentes preservam o escopo antigo após atualizar; para salvar um escopo
antigo global na nova tela, selecione as bibliotecas desejadas.

Para testar algo específico, escolha **Somente os itens selecionados**, busque pelo
nome ou cole o link dos detalhes/ID. Isso seleciona exatamente esses itens: um ID
de série não inclui os episódios. **Todas as bibliotecas e metadata global** inclui explicitamente
todas as bibliotecas e metadata global.

Ao selecionar itens, a tela verifica as imagens próprias cadastradas e mostra o ano,
tipo e final do ID para distinguir versões do mesmo título. Ela avisa se não houver
imagens ou tipos marcados, se forem remotas, já WebP, ausentes ou excluídas.
Pré-candidatas locais ainda precisam passar pelas verificações da simulação.
O JellyP não baixa capas ausentes. Use **Atualizar andamento** para refazer essa
verificação depois de alterar imagens fora do plugin. Uma seleção sem candidatas
locais termina sem inventário global; havendo candidatas, a checagem global continua.

Pessoas são entidades globais do Jellyfin. No escopo de biblioteca entram as pessoas
creditadas nos itens selecionados; suas fotos também podem aparecer em outras
bibliotecas. Arquivos de imagem compartilhados por entidades distintas continuam
protegidos, mesmo se uma das referências estiver fora da seleção.

Marque os tipos de imagem. Use ajustes globais ou individuais. Comece com qualidade
85, sem reduzir dimensões. 100 ainda tem perdas; Sem perdas exige pixels idênticos.
A semelhança mínima (SSIM), padrão 0,96, é um filtro separado: baixar qualidade não
o desativa. Não reduza o filtro sem conferir qualidade. A transparência é verificada
exatamente.

## Simule e veja o resultado

Em **3. Teste ou execute agora**, escolha **Simular** e clique em **Simular seleção**.
O botão salva escopo, qualidade e a seleção separada de automação, depois solicita
uma simulação identificada. Não muda o modo salvo para as tarefas agendadas.

A tela mostra progresso do inventário, quantas imagens seriam convertidas, redução
estimada, motivos para ignorar imagens e até 20 exemplos com nomes. O inventário
confere todas as referências, mesmo numa seleção pequena. Se resultar em zero,
a tela explica imagens já em WebP, compartilhadas ou excluídas. Se nenhum item
corresponder, reveja a seleção ou os IDs.

O **Limite de imagens por execução**, nos ajustes avançados, começa em 250. Ele
limita trabalho por execução; o escopo continua sendo a biblioteca escolhida.
Execuções posteriores retomam o passe. Simulação e aplicação têm posições próprias.
Mudar escopo ou ajustes efetivos reinicia o passe daquele modo.

## Aplique agora ou deixe agendado

Para uma conversão manual, escolha **Aplicar** em Executar agora. Isso não habilita
conversões automáticas. O plugin aguarda sozinho varreduras e atualizações do item,
de seus pais ou de sua biblioteca. A espera é cancelável e continua até o conflito terminar ou a tarefa ser
cancelada; limites de duração configurados no agendador do Jellyfin continuam valendo. Não é preciso desligar tarefas agendadas sem relação com as imagens.
Evite envios manuais de imagens durante conversões: a API pública do Jellyfin não
oferece uma trava compartilhada para isso.

Em **4. Processamento agendado**, escolha independentemente **Somente simular** ou
**Aplicar conversões** e salve. Use **Abrir tarefas agendadas do Jellyfin** e configure
os disparos das tarefas de otimização e sincronização do JellyP. O plugin não cria
horários sozinho e preserva os agendamentos existentes. Os próximos disparos retomam
o escopo e os ajustes salvos, até o limite configurado, sem confirmações manuais de
manutenção. Se a execução for cancelada, a imagem atual fica para uma nova execução.

## Liberação de espaço e restauração

Guardar originais inicialmente ocupa **mais espaço**. Para um teste reversível,
guarde os originais, use limite de 1 imagem e confira o item. **Restaurar original**
desfaz essa transação sem alterar a automação.

Para liberar espaço imediatamente, escolha **Remover originais após verificar**.
O atalho no guia de emergência prepara uma simulação não salva de 25 imagens com
essa opção, mantendo escopo, qualidade e automação. Revise tudo antes de executar.
Aplicar sem guardar originais exige ciência da permanência e confirmação. Uma
automação salva para aplicar com essa opção também apaga os originais permanentemente.

Cada fonte só é removida após verificar o WebP e a referência persistida. Sem o
original guardado, não haverá restauração nem regeneração a partir dele. Uma cópia
temporária ainda é necessária durante a verificação. **Remover originais guardados
verificados…** libera cópias anteriores sem converter novamente e mantém as incertas
ou com recuperação pendente.

Mantenha a reserva padrão de 2 GiB. Se o disco já estiver cheio, libere primeiro
espaço conhecido e alheio ao metadata. Reduzir a reserva não cria espaço. Não apague
metadata nem o banco. O espaço líquido liberado desconta os originais guardados da
redução verificada das conversões ativas; não é uma medida exata de `df`.

Para mudar qualidade/dimensões de imagens já convertidas, guarde os originais,
altere os ajustes e use **Sincronizar mudanças de qualidade**, primeiro simulando.
Isso evita perdas sucessivas. Desmarcar um tipo não desfaz suas conversões; restaure
explicitamente. Tipos recém-marcados usam a otimização comum.

Veja os [limites e exclusões](../README.md#supported-scope-and-limits) e a
[recuperação](RECOVERY.md).
