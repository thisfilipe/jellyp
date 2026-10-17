# Como usar o JellyP

Abra **Painel → Plugins → JellyP → Configurações** e escolha **Português (Brasil)**.
A versão aparece ao lado de JellyP. A atualização preserva seus ajustes anteriores;
confira o modo e a seleção de itens antes de executar.

## Primeiro lote controlado

1. Em **1. Selecione as imagens**, escolha **Somente os itens selecionados**,
   busque pelo nome e adicione um resultado. Também pode colar o link dos detalhes
   do item no Jellyfin ou seu ID. A seleção é por item: marcar uma série não inclui
   automaticamente seus episódios. Uma seleção restrita vazia impede a execução.
2. Marque os tipos de imagem. **Capas principais** corresponde a Primary; **Fundos**, a Backdrop.
   **Pessoas** vale para imagens de pessoas independentemente do tipo da imagem.
   Os demais tipos estão em **Mais tipos de imagem**. Só tipos marcados contam
   para o limite do lote.
3. Em **2. Escolha a qualidade e o armazenamento**, comece com qualidade 85,
   redução de dimensões desmarcada, originais guardados e lote de **1 imagem**.
   Ajustes por tipo são opcionais; sem eles, vale a configuração global.
4. Em **3. Simule ou aplique**, deixe **Simular** e clique em **Simular próximo lote**.
   O botão salva os ajustes exibidos antes de enfileirar. **Salvar ajustes**
   apenas salva. A simulação não altera as imagens.
5. Confira o andamento, a redução estimada e os motivos para ignorar imagens.
   Mesmo num lote pequeno, a conferência de arquivos compartilhados percorre
   toda a biblioteca. Em bibliotecas grandes, essa etapa pode levar minutos.
6. Pause envios de imagens e atualizações de metadata. Escolha **Aplicar**, confirme
   a janela e clique em **Converter próximo lote**. Confira o item no Jellyfin.
   Com original guardado, **Restaurar original** permite desfazer essa conversão.
7. Repita o botão para continuar. Ao terminar, volte a **Simular** e salve.
   Tarefas agendadas usam o modo salvo; esta tela não cria agendamentos.

## Uso emergencial para liberar espaço

Guardar originais inicialmente ocupa **mais espaço**. Abra **Precisa liberar espaço
agora? Comece por aqui.** e use o atalho: ele prepara uma simulação de 25 imagens
sem guardar originais. Mantém seu escopo, tipos e qualidade: revise-os. Não salva
nem executa sozinho. Comece com poucos itens selecionados, simule, confira e aplique.

Escolher **Remover originais após verificar** é permanente: o original só é apagado
após verificar o WebP e a referência persistida no Jellyfin, mas depois não haverá
restauração nem regeneração a partir dele. Uma cópia temporária ainda é necessária
até concluir essa verificação. A aplicação exige ciência da permanência e confirmação.

Depois de conferir o primeiro lote, escolha explicitamente **Biblioteca inteira**
se quiser ampliar e repita lotes de 25. Aumente gradualmente após inspecionar.
Ao terminar, volte a **Simular** e salve.

Mantenha a reserva padrão de **2 GiB**. Se o disco já estiver cheio, libere primeiro
espaço conhecido e alheio ao metadata para o Jellyfin funcionar e a conversão caber.
Reduzir a reserva não cria espaço. Não apague a pasta de metadata nem o banco.
Se ocorrer erro, pare e confira os detalhes e os registros de recuperação.

Se já houver originais guardados, **Remover originais guardados verificados…** pode
liberar espaço sem converter outra vez. Exige Aplicar e manutenção; verifica as
imagens convertidas e mantém cópias com recuperação pendente ou verificação incerta.
A exclusão também é permanente.

## Entendendo os controles

- **Qualidade** regula a compressão com perdas. 100 ainda tem perdas; **Sem perdas**
  exige pixels idênticos. A transparência é verificada exatamente nos dois modos.
  Redução de dimensões é opcional e nunca amplia imagens pequenas.
- **Semelhança mínima (SSIM)** é um filtro independente, padrão 0,96. Uma qualidade
  menor pode ser rejeitada por esse filtro. Não o reduza sem conferir a imagem.
  Formatos e perfis não compatíveis também são ignorados.
- **Redução das imagens no último lote** inclui estimativas da simulação.
  **Originais guardados** mede as cópias retidas e de recuperação.
  **Espaço líquido recuperado** desconta essas cópias da redução verificada das
  conversões ativas. Valor negativo aparece como espaço extra usado. Não é `df`.
- **Parar após a imagem atual** conclui a transação em curso. O próximo lote retoma.
  Mudar itens, tipos marcados ou ajustes efetivos de qualidade reinicia o passe
  daquele modo. Simulação e aplicação têm posições independentes.

## Mudar a qualidade depois

Isso depende dos originais guardados. Altere os ajustes globais ou por tipo e use
**Sincronizar mudanças de qualidade**, primeiro simulando e depois aplicando
sob manutenção. Ele usa o original para evitar perdas sucessivas. Sem original,
a imagem é ignorada. Desmarcar um tipo não desfaz conversões anteriores: restaure
explicitamente. Tipos recém-marcados usam o botão normal de próximo lote.

Veja também os [limites e exclusões](../README.md#supported-scope-and-limits) e a
[recuperação](RECOVERY.md).
