# Contrato de produto e estado — entrega da fase P1

Data do registro: **17 de outubro de 2026**. Baseline do código: **0.3.0.1**.
Documento de entrada: [PLAN.md](PLAN.md). Especificação de referência:
[UX-REFACTOR-PLAN.pt-BR.md](UX-REFACTOR-PLAN.pt-BR.md).

**Status.** Este documento é a entrega da fase **P1** (definir o contrato de
produto e de estado), autorizada explicitamente pelo mantenedor em 17/10/2026.
Ele registra mapeamento, decisões de produto, jornadas, estados e semântica de
métricas a partir do código real. Ele **não** autoriza P2–P8, não descreve
implementação e não substitui a especificação: onde houver divergência, a
especificação permanece a referência e a divergência é um defeito deste
documento.

Método: cada afirmação sobre o comportamento **atual** foi conferida no código
0.3.0.1 com referência de arquivo/linha. O que é **proposto** está marcado como
proposto e ainda depende de P2 (validação de fluxo) e P3 (contratos e
persistência). Nomes de classe/endpoint existentes são citados apenas como
evidência; P3 define os contratos finais.

Convenções: **Atual** = observado no código 0.3.0.1; **Decidido (P1)** = decisão
de produto vinculante para P2–P4, ainda sem implementação; **Adiado** = pertence
a P2/P3; **Evidência** = arquivo:linha.

---

## 1. Inventário atual mapeado para as cinco áreas

Tabela campo-a-campo do formulário compartilhado atual
(`Jellyfin.Plugin.JellyP/Web/config.html`) e da configuração persistida
(`Configuration/PluginConfiguration.cs`), com a área de destino no desenho de
cinco ambientes (especificação §6) e o alcance real de cada item hoje.

Alcance: a coluna "Hoje" indica o efeito e o que persiste; a coluna de destino
indica onde a responsabilidade vive no desenho proposto.

| # | Elemento atual (id/evidência) | Hoje: efeito e alcance | Área de destino |
|---|---|---|---|
| 1 | `Scope` (libraries/items/all) — config.html:46 | Escolhe `ScopeMode`; salvo por `collect` a cada Save/Run/Sync (config.html:514) | **Simular** (rascunho: escopo da análise) + **Automação** (escopo próprio, hoje o mesmo campo) |
| 2 | `LibraryOptions` — config.html:48,408-421 | `IncludedLibraryIds`; idem acima | Simular + Automação (separados no destino) |
| 3 | `ItemSearch`/`SearchResults`/`ItemIds`/`AddItems`/`SelectedItems` — config.html:49-50,445-469 | `IncludedItemIds`; inspeção por item via `GET JellyP/Selection` (JellyPController.cs:17-24); os ids salvos também alimentam a automação (OptimizationCoordinator.cs:190,208-209) | Simular + Automação (hoje o mesmo campo; escopos separados no destino) |
| 4 | Aviso de escopo legado `LegacyScopeNotice` — config.html:44,481 | Explica escopo antigo preservado (`ScopeMode=legacy`) | Simular (explicação; comportamento preservado) |
| 5 | Tipos por categoria `JellyPPolicies` (Enabled) — config.html:51,373-392 | `ImagePolicies[].Enabled`; quais tipos são avaliados | Simular (seleção) + Configurações/Automação (padrão salvo) |
| 6 | Editor por tipo (`OverrideGlobal`, Quality, Lossless, ResizeImages, MaximumDimension, MinimumSsim) — config.html:384-388 | Substituição **completa** da política efetiva quando `OverrideGlobal` (`OptionsFor`, PluginConfiguration.cs:58-67) | Configurações (padrão por tipo) + Simular (rascunho pode divergir) |
| 7 | Resumo efetivo por tipo "Usa os ajustes globais / Ajustes individuais" — config.html:393-406 | Mostra codificação, qualidade e se haverá redução ("Pode reduzir dimensões"/"Dimensões originais"), **sem** número de dimensão nem SSIM (config.html:402-405) | Simular e Configurações; §7.2 exige valores efetivos completos → P2/P3 |
| 8 | `Encoding`, `Quality`, `ResizeImages`, `MaximumDimension` — config.html:54 | Defaults globais de codificação; salvos por `collect` | Configurações (padrões salvos); Simular exibe efetivos |
| 9 | `Storage` (Keep/Remove originals) — config.html:55,478 | `PreserveOriginals`; hoje um único valor global usado pelo manual **e** pela automação; `discard` aposenta o original logo após o commit verificado (OptimizationCoordinator.cs:529-532) | Simular (rascunho) e Executar (revisão final); Automação (config própria); Configurações indica o padrão |
| 10 | `PermanentAck` — config.html:59,477,512-513 | Autorização de remoção permanente da operação; **nunca** persiste (`populate` zera, config.html:501). Hoje também é exigido ao salvar quando a automação está em `apply`+`discard` (config.html:477,512), inclusive numa simulação manual; executado em automação, a remoção depende só da política salva (aposenta após commit — OptimizationCoordinator.cs:529-532) | Executar (confirmação da operação revisada, **sem** depender da automação); Automação ganha autorização própria e explícita na gravação (forma definida em P3) |
| 11 | `BatchSize` — config.html:55 | `BatchSize`; limite por execução, persiste junto com tudo | Configurações (limite global) + Simular/Executar (limite de teste do rascunho, se menor; ver D4) |
| 12 | `MinimumSsim` / `SavingsPercent` / `SavingsKiB` — config.html:56 | `MinimumSsim` é padrão global substituível por tipo junto da codificação (PluginConfiguration.cs:64); economia % e KiB são sempre globais | Configurações (padrões; economia sempre global — D3) |
| 13 | `ReserveGiB`, `QuotaMiB`, `BackupRetentionDays` — config.html:56 | Reserva, orçamento e retenção; persistem. `BackupRetentionDays>0` dispara **purga automática** de originais elegíveis no início de todo lote Optimize de aplicação, manual ou agendado (OptimizationCoordinator.cs:180-183,613-629) | Configurações (globais; nunca alterados por rascunho — D4). Executar deve **prever e mostrar** a purga na revisão; nunca silenciosa (§4.4) |
| 14 | `SyncUnchangedPolicies` — config.html:56,501 | "Sincronizar também políticas não mudadas"; persiste; entra no contexto do cursor no modo sync (OptimizationCoordinator.cs:372) | Automação (regeneração da sincronização nativa) |
| 15 | `Mode` (preview/apply) — config.html:58 | Não persiste: reseta para `preview` a cada carga (config.html:499); decide `Run` **e** `Sync` (dryRun e confirmação de remoção, config.html:581-585,606-607) | **Executar** (absorvido pela ação "Aplicar às N imagens"; elimina o seletor distante — UX-03) |
| 16 | `SelectionSummary` — config.html:58,483 | "N tipos · até N imagens por execução · escopo" | Simular (antes da análise; especificação §7.1) |
| 17 | `Save` — config.html:60,604 | Salva tudo (escopo, políticas, automação) | Deixa de existir como botão único; gravações explícitas por área (Simular não grava) |
| 18 | `Run` ("Simulate selection"/"Convert selection") — config.html:60,486,606 | **Salva o formulário inteiro** e então enfileira `POST JellyP/Run?dryRun=` (config.html:581-588) — UX-05 confirmado | Simular (analisar) e Executar (aplicar seleção revisada) |
| 19 | `Cancel` ("Stop after current image") — config.html:60,609 | `POST JellyP/Cancel`: cancela fila + tarefas nativas (JellyPController.cs:34-42) | Executar + Automação (por operação identificada) |
| 20 | `Refresh` — config.html:60,608 | Recarrega `GET JellyP/Status` | Global discreto + por operação |
| 21 | `DirtyState`/mensagens `JellyPMessage` — config.html:43,60,349,354 | Feedback em ponto único, distante da ação (UX-04) | Progresso/resultado por operação; aviso global só para trabalho ativo |
| 22 | `Automation` (Simulate only/Apply) — config.html:62,499 | `Enabled`+`DryRun` persistidos; é o modo da automação; remoção permanente em automação hoje é autorizada no momento da gravação pelo ack (ver #10) | Automação (inclui autorização explícita para remoção permanente; forma em P3) |
| 23 | `SaveAutomation` — config.html:66,605 | Salva o mesmo formulário inteiro | Automação (gravação explícita só da automação) |
| 24 | `NativeTasks` + link p/ tarefas — config.html:66,589-593 | Lista tarefas nativas da categoria JellyP, estado e nº de disparos | Automação (visibilidade) |
| 25 | Tarefas nativas `JellyPOptimizeArtwork`/`JellyPSynchronizePolicies` — Tasks/*.cs:9-21 | Chaves e disparos nativos; nenhum trigger padrão (`GetDefaultTriggers() => []`) | Automação (preservar chaves/disparos — invariante) |
| 26 | `Sync` ("Sync changed quality settings") — config.html:71,585,607 | Salva tudo, usa o `Mode` atual (dryRun/confirmação) e enfileira `POST JellyP/Sync` | **Simular/Executar** (regeneração proposta pela análise — especificação §7.3; o botão separado deixa de existir, sem preservar o acoplamento de `Mode`) |
| 27 | `PhaseText`, `RunProgress`, `RunSummary` — config.html:67,548-559 | Andamento/resultado do último lote, em seção distante do botão (UX-04) | Executar/Simular (contextual, por operação) |
| 28 | `InventoryExplanation`, `SkipReasons`, `TechnicalStatus` — config.html:67-70,554-565 | Explicações de inventário, motivos de descarte, detalhe técnico | Resultado da operação (contextual; técnico sob detalhes) |
| 29 | `SampleResults` (até 20) — config.html:532-538 | Amostra por imagem com motivo | Resultado da simulação (por imagem; paginação em P3 — especificação §7.4) |
| 30 | Métricas `WouldConvertValue`, `SavingsValue`, `BackupsValue`, `NetValue`, `AccountingHelp` — config.html:68-69,553-561 | Estimativa do lote, totais do plugin e ajuda contábil juntos | Métricas por operação vs totais separados (seção 6 deste documento) |
| 31 | `RecoveryNotice` — config.html:69,562-563 | `PendingRecovery>0`; em modo aplicar, `Run` fica desabilitado (config.html:487) | Histórico/Executar (bloqueio de nova aplicação preservado) |
| 32 | `JellyPRecords`/"Recent conversions" (últimas 100) — config.html:71,567-576 | `RecentRecords`: estado, bytes, restaurar (`Rollback`), identificadores | Histórico e originais |
| 33 | `Retire` ("Remove verified stored originals…") — config.html:71,610 | `POST JellyP/Backups/Retire?olderThanDays=0` → `PurgeAsync(agora, ..., authorizedManual:true)` (JellyPController.cs:89-102): alcance **global** do plugin, não da seleção — UX-08 confirmado | Histórico e originais (fluxo próprio com seleção explícita — especificação §8.2) |
| 34 | `QuickGuide`, `PrepareEmergency` — config.html:42,613 | Guia + atalho de rascunho (só muda storage/batch/modo na tela) | Simular (informação progressiva; atalho é rascunho, não grava) |
| 35 | `Language` — config.html:41,601 | Preferência local (localStorage), não persiste no servidor | Global (mantém) |
| 36 | `JellyPVersion` — config.html:41,597 | Versão do plugin | Global (mantém) |
| 37 | `MaintenanceWindowConfirmed` — PluginConfiguration.cs:17 | Campo persistido, **sempre gravado `false`** pela UI (config.html:514) e **nunca lido** pelo backend (sem consumo em `OptimizationCoordinator`/`JellyPController`); há texto órfão sobre "janela de manutenção" (config.html:151) | Configurações/P3: campo legado; decidir remoção ou migração sem alterar comportamento |

Evidência adicional do modo de persistência atual: `collect` monta o objeto
inteiro e `save` grava via `updatePluginConfiguration`
(config.html:505-519,520); `run` chama `save` antes de enfileirar
(config.html:581-588). Ou seja, **toda** execução manual/troca de política
persiste o formulário compartilhado — a origem de UX-05.

### 1.1 Confirmações de diagnóstico com evidência

- **UX-02** (simulação não vê WebP já convertido): motivos
  `PolicyUnchanged` e mensagem de inventário sobre WebP já elegível
  (config.html:318,522; InventorySummary.cs:7-15) — o caminho de regeneração é
  outro botão (`Sync`).
- **UX-05** (execução salva configuração): ver #18 acima.
- **UX-06** (cursores independentes): testes
  `PreviewAndApplyMaintainIndependentCursors` e
  `ChangingEffectivePolicyRestartsCursor`
  (tests/.../OptimizationCoordinatorTests.cs:535,594; ainda válidos na
  evidência desta fase).
- **UX-08** (alcance da exclusão maior que a seleção): ver #33 acima.
- **UX-12** (formato/dimensões armazenados): `MigrationRecord` guarda
  `OriginalMimeType`, `OriginalBytes`, `OutputBytes`, hashes, estado e datas
  (Models.cs:43-60); a UI atual mostra apenas bytes e estado
  (config.html:571-572). **Dimensões não são persistidas** — existem só
  transitoriamente em `WebpOptimizationResult` (WebpOptimization.cs:59-70). O
  formato original é detectado apenas como PNG ou assumido JPEG
  (OptimizationCoordinator.cs:721-722). P3 precisa registrar dimensões para
  cumprir a exigência de exibir formato/dimensões/tamanho armazenados
  (especificação §8.2).

---

## 2. Contrato de estado (aplicação da seção 9 da especificação)

A tabela abaixo fixa as entidades e as regras de isolamento em nível de
produto. Os nomes descrevem responsabilidade; representação é decisão de P3.

| Entidade (P1) | Corresponde hoje a | Regra de isolamento decidida em P1 |
|---|---|---|
| Padrões salvos | Campos globais + `ImagePolicies` (PluginConfiguration.cs:13-51) | Só mudam em gravação explícita na área Configurações. |
| Configuração da automação | `Enabled`, `DryRun`, padrões, escopo salvo (hoje os mesmos campos do manual) | Só muda em gravação explícita na área Automação. Simular/Executar não a alteram como efeito colateral (inverte UX-05). |
| Rascunho manual | Estado de tela (perde-se hoje; `populate` recria, config.html:493-504) | Vive na área Simular; não grava configuração; preservado ao navegar (durabilidade definida em P3); não autoriza processamento; começa dos padrões salvos. |
| Simulação identificada | Hoje: `RunReport` único em `last-run.json` + `Samples` (Models.cs:63-84) | Snapshot de intenção/candidatos/resultados; não altera imagens nem originais; cada operação tem relatório próprio (hoje Run, Sync, restauração e remoção sobrescrevem o mesmo `last-run.json` — UX-07; OptimizationCoordinator.cs:385-403,445,737). |
| Seleção autorizada | Hoje: não existe; quatro cursores `optimize/sync × preview/apply` + configuração salva definem o alvo (OptimizationCoordinator.cs:185; contexto = escopo + ids + políticas habilitadas, :362-373) | Subconjunto exato por identidade de imagem; revalidado no momento da aplicação; nunca substituído por outro conjunto. |
| Execução | Hoje: fila nativa + `RunReport` (Tasks/*.cs; Models.cs:63) | Solicitação com identidade, origem, estado e resultado próprios; o progresso pertence à execução. |
| Journal de imagem | `MigrationRecord` + journal durável (Models.cs:43-60; Infrastructure/DurableFiles.cs) | Preservado sem reinterpretação; compatibilidade de leitura garantida (invariante). |

Invariantes de integridade da especificação §5.3 permanecem vinculantes e não
são renegociados por este documento.

---

## 3. Decisões de produto

### 3.1 D3 — Herança de ajustes por tipo (decidida)

**Fatos.** O modelo atual só representa **substituição completa**: com
`OverrideGlobal`, `OptionsFor` substitui Quality, Lossless,
ResizeImages/MaximumDimension e MinimumSsim pelos do tipo; os filtros de
economia continuam globais (PluginConfiguration.cs:58-67). O resumo por tipo
mostra codificação, qualidade e se haverá redução, mas não o número de dimensão
nem o SSIM (config.html:402-405) — §7.2 exige os valores efetivos completos. Teste
`EffectiveFingerprintTracksOverrideButIgnoresUnusedGlobalSettings` fixa que a
política efetiva ignora configurações globais não usadas
(OptimizationCoordinatorTests.cs:663-672). O flag único `OverrideGlobal`
seleciona Quality, Lossless, dimensão e SSIM de uma vez: **não existe herança
por campo**. Quando a redução está desligada nos dois escopos, a dimensão cai no
limite técnico de 8192 (PluginConfiguration.cs:62-63).

**Decidido (P1).**

1. O editor individual permanece **substituição completa** ("Personalizado
   substitui a política do tipo"), nunca herança por campo. A UI deve dizer isso
   e mostrar os valores efetivos resultantes antes de editar.
2. Filtros de economia, reserva, quota e retenção são **sempre globais**; onde
   aparecerem, devem estar rotulados como tal, para não sugerir alcance por
   tipo.
3. Herança por campo fica explicitamente **fora deste redesenho**. Se for
   desejada no futuro, exige representação e migração próprias.
4. P3 deve definir o texto final e confirmar que nenhuma tela nova implique
   herança por campo.

**Justificativa.** É representável sem migração; atende ao critério de §7.2 da
especificação ("deixar claro se substitui a política completa"); a
funcionalidade de campo-por-campo não tem necessidade registrada no
diagnóstico.

### 3.2 D4 — Alcance de reservas, quotas, retenção e padrões (decidida)

**Fatos.** Hoje um único formulário persiste escopo, políticas, limites e modo
de automação juntos; executar salva tudo (config.html:514-519). Reserva, quota
e retenção não têm representação por operação. Retenção zero significa "guardar
até remoção explícita" (config.html:56).

**Decidido (P1).**

| Categoria | Onde se decide | Persiste? |
|---|---|---|
| Reserva de espaço livre, orçamento de originais, retenção de originais | Configurações, somente por gravação explícita | Sim (globais) |
| Filtros de economia (redução % e KiB) | Configurações; sempre globais | Sim |
| Padrões de codificação (Lossless/Quality/dimensão/SSIM por tipo) | Configurações; substituição completa por tipo (D3) | Sim |
| Escopo (bibliotecas/itens/tudo) | Simular (rascunho) **e** Automação (config própria) — valores independentes | Só o da Automação |
| Preservação de originais da operação (guardar/remover) | Executar/Simular (rascunho) **e** Automação (config própria) | Só o da Automação |
| Limite por execução | Global em Configurações; o rascunho pode propor um valor **menor** para teste, sem persistir | Só o global |
| Idioma | Global (local) | Preferência local |

Regra de ouro: **rascunho manual nunca altera** reserva, quota, retenção,
automação ou padrões; automação nunca herda o rascunho; a interface identifica
a origem de cada valor (padrão salvo, automação ou rascunho).

**Justificativa.** Cumpre o item explícito de D4 ("impedir que um rascunho
altere retenção global") e elimina UX-05.

### 3.3 Decisões adiadas com evidência

| Ponto | Estado | Onde fechar |
|---|---|---|
| Forma de navegação (páginas/abas), voltar/avançar, celular | Adiado (D1) | P2, com exercício real |
| Análise única de conversão+regeneração na simulação (D2) | Direção decidida na especificação §7.3; contrato técnico pendente | P3 |
| Durabilidade/validade/paginação das simulações (D5) | Pendente | P3 |
| Relação tarefas nativas × novo fluxo (D6) | Direção: preservar chaves/disparos (invariante); desenho pendente | P3 |
| Exclusão seletiva de originais e migração da API (D7) | Direção §8.2; contrato pendente | P3 |
| Purga automática por `BackupRetentionDays`: manter expiração global e como confirmá-la na revisão | Efeito definido (prevista na revisão, nunca silenciosa — §4.4); mecanismo e textos pendentes | P3 |
| Dados antigos e operações em andamento no upgrade (D8) | Pendente | P3 |

### 3.4 Admissão da execução manual (comportamento a preservar)

**Fatos.** `POST JellyP/Run?dryRun=` e `POST JellyP/Sync?dryRun=` criam um
recibo e enfileiram a tarefa nativa; só existe uma operação JellyP por vez
(`409`) (OptimizationCoordinator.cs:32-33,697-706; JellyPController.cs:48-69).
Uma requisição manual explícita com `dryRun=false` habilita a mutação **apenas
na cópia de configuração da requisição**; a automação persistida não muda
(OptimizationCoordinator.cs:35-39). Simulação (`dryRun=true`) não espera
metadata, não altera imagens nem configuração; grava apenas o relatório/estado
da própria simulação.

**Decidido (P1).** A área Executar emite uma requisição **autocontida** com a
simulação e a seleção autorizada, sem salvar o formulário compartilhado e sem
depender do modo salvo da automação. Autorização administrativa e requisição
explícita permanecem; simulação continua sem mutação e sem espera.

---

## 4. Jornadas de referência

Jornadas em nível de produto para as cinco áreas; cada uma será verificada no
exercício de P2 e convertida em contrato em P3. "Rascunho" e "revisão" são
estados de UI; nada aqui autoriza implementação.

### 4.1 Primeira conversão (espaço liberado)

1. **Simular**: escolher bibliotecas ou itens; marcar tipos; revisar ajustes
   efetivos; escolher no rascunho a preservação dos originais com o impacto de
   espaço à vista.
2. A simulação explica, por imagem, o que aconteceria e por quê (converter,
   manter, descartar por filtro, indisponível).
3. **Executar** recebe a simulação e a seleção explícita; revisão final mostra
   alcance, nº de imagens, ações e consequência da remoção de originais.
4. Aplicação revalida no servidor; andamento e resultado ficam na execução.
5. **Histórico** mostra a operação com estado por imagem e evidência de
   restauração possível.

### 4.2 Mudança de qualidade (85 → 80) em imagem já convertida

1. **Simular** com os novos ajustes: a análise propõe "gerar novamente a partir
   do original" para conversões do JellyP com original íntegro; "já utiliza
   estes ajustes" quando nada mudou; e explica filtros que rejeitem.
2. O botão separado de sincronização deixa de existir; a análise única propõe a
   regeneração (#26 é absorvido), eliminando UX-03 e a dependência de modo
   distante.
3. **Executar** aplica a seleção revisada; regeneração continua tecnicamente
   distinta, com o mesmo caminho seguro atual.

### 4.3 Restauração

1. **Histórico e originais** lista operações; cada imagem restaurável mostra
   estado (convertido com original, restaurado, removido, recuperação
   necessária).
2. Restaurar usa a transação ativa correta (não uma entrada antiga de mesmo
   nome) e apresenta formato/dimensões/tamanho guardados + verificação de
   integridade, explicando que o navegador pode continuar recebendo WebP
   (representação ≠ arquivo armazenado).
3. Recuperação pendente bloqueia nova aplicação até ser tratada (comportamento
   atual preservado, config.html:487).

### 4.4 Exclusão de originais

1. Fluxo próprio em **Histórico e originais**: escolher explicitamente os
   originais; o destino "todos os elegíveis do plugin" é uma escolha explícita,
   nunca inferida (corrige o alcance global do botão atual, #33).
2. Antes de confirmar: quantidade, alcance, bytes que serão liberados e a
   consequência permanente sobre restauração/regeneração.
3. Executa somente sobre a seleção; originais incertos permanecem.
4. A retenção global (`BackupRetentionDays>0`) continua sendo expiração
   automática de originais elegíveis no início de um lote de aplicação (fato
   atual, OptimizationCoordinator.cs:180-183,613-629); a revisão de Executar
   deve prever e mostrar essa purga com quantidade e bytes — nunca silenciosa.

### 4.5 Agendamento

1. **Automação** tem configuração própria (escopo, tipos, ajustes, limite,
   preservação); gravação explícita.
2. Estado separado: configurado, modo simulação/aplicação, disparos nativos e
   estado da execução (ociosa ≠ desativada; sem horário ≠ aplicar proibido).
3. Simular/Executar manuais nunca alteram a automação; disparos e chaves
   nativos são preservados.

---

## 5. Estados obrigatórios por área

Vocabulário único. P2 desenha cada estado; P3 define durabilidade dos
transitórios.

| Estado | Definição e onde aparece | Evidência atual |
|---|---|---|
| Vazio | Sem bibliotecas; seleção vazia nunca significa tudo; item sem imagens; nenhum tipo marcado; nenhum original guardado; nenhum agendamento | config.html:307-318,429-432,447,517; PluginConfiguration.cs:87-90 |
| Dados indisponíveis | Inspeção indisponível; tarefas indisponíveis; item removido; original indisponível/inválido | config.html:427-429,592; reasons `OriginalUnavailable`/`InvalidInput` (config.html:522) |
| Conflito | Outra operação JellyP em execução ou na fila; tarefa nativa não ociosa (`409 Conflict`); uploads manuais não bloqueáveis | OptimizationCoordinator.cs:32-33,697-706; JellyPController.cs:48-69; config.html:152,270 |
| Espera | Na pré-execução, apenas scan de biblioteca ativo bloqueia (`EnsureIdle(null)`, JellyfinArtworkStore.cs:63-65); por item, refresh de metadata relacionado (item + cadeia de pais) também bloqueia (JellyfinArtworkStore.cs:66-81); simulação/inspeção nunca esperam; fase `WaitingForMetadata`, retoma sozinha e é cancelável; inventário global de referências não é conversão global | OptimizationCoordinator.cs:168-183,220,676-695; config.html:250,313,548 |
| Cancelamento | Entre imagens e durante esperas; o token não é observado dentro da transação de imagem/restauração, então a transação corrente termina com segurança; o cursor é preservado e a retomada é um novo lote | OptimizationCoordinator.cs:99,217-219,450-611; JellyPController.cs:34-42; config.html:154,240,273 |
| Sucesso parcial | Convertidas, ignoradas e falhas sempre separadas; motivos agregados (`SkipReasons`) sem mascarar falha | Models.cs:63-84; config.html:251,316,564 |
| Recuperação necessária | N transações; restauração antes de nova aplicação | config.html:254,487,563; OptimizationCoordinator.cs:168-171 |

---

## 6. Semântica das métricas (por execução vs totais do plugin)

| Métrica | Unidade | Alcance | Rótulo proposto |
|---|---|---|---|
| Imagens analisadas | imagens | execução | "N avaliadas" |
| Propostas/seriam convertidas | imagens | simulação | "N imagens seriam convertidas" |
| Convertidas / ignoradas / falhas | imagens | execução | "N convertidas · N ignoradas · N falhas" |
| Redução estimada das imagens | bytes de imagem | simulação | "Redução estimada nas imagens (não é espaço livre)" |
| Originais a guardar / impacto líquido estimado | bytes | simulação | "Originais a guardar: X · Impacto líquido estimado: Y" — separados dos totais do plugin |
| Redução nas imagens do lote | bytes de imagem | execução | "Redução nas imagens deste lote" — **nunca** apresentar como espaço livre em disco |
| Originais guardados | bytes | **total do plugin** | "Originais guardados (total)" |
| Espaço líquido liberado | bytes; negativo vira "a mais em uso" | **total do plugin** | "Espaço líquido liberado (total)" — desconta originais guardados; só conta transações cujo arquivo de saída existe, cuja referência atual aponta para o caminho gravado e cujo original de origem não existe mais (sem recálculo de hash hoje — OptimizationCoordinator.cs:88-96); não inclui logs, caches ou custo do sistema de arquivos |
| Pendências de recuperação | transações | **total do plugin** | "N operações precisam de recuperação" |

Regras: totais do plugin nunca somam estimativas de uma simulação
(config.html:560-561 já separa; manter e rotular); progresso usa unidades
distintas para itens inventariados e imagens processadas (especificação §7.6);
nenhuma porcentagem quando o total é desconhecido; "N imagens" da simulação não
autoriza mais do que as imagens identificadas na própria simulação.

---

## 7. Como este documento satisfaz o aceite de P1

- Cada ação/campo atual tem área de destino, alcance e efeito persistido
  definidos (seção 1).
- Nenhum seletor distante sobrevive ao desenho: `Mode` (#15) é absorvido por
  Executar; `Sync` (#26) por Simular/Executar; `Automation` deixa de ser salvo
  por execução manual (#18).
- Decisões D3/D4 resolvidas em nível de produto (seção 3).
- Jornadas, estados e unidade das métricas definidos (seções 4–6).

**Pendências para P2/P3** (não são lacunas de P1): forma de navegação e
verificação com pessoa sem contexto (P2); contratos de dados, paginação,
idempotência, migração e endpoints (P3). Nada neste documento foi implementado
ou testado em runtime.

## 8. Adendo de produto P2 — feedback com contexto (05/11/2026)

Este adendo **estende a proposta**, não reescreve o inventário histórico de P1 e
não afirma suporte do backend. O escopo autorizado é protótipo sintético, smoke e
documentação. P3–P8 continuam sem autorização de implementação.

- **Navegação:** páginas internas são a direção recomendada e inicial. Abas ficam
  apenas como comparação opcional na bancada; uma escolha binária D1 não é prévia
  obrigatória ao uso. Integração nativa, voltar/avançar e celular ainda precisam de
  validação. Não há validação por pessoa sem contexto.
- **Nome da área:** a apresentação passa a **Histórico e restauração**, priorizando
  a descoberta de desfazer, sem confundir com remover originais.
- **Extensão solicitada: restauração em lote.** Seleção explícita por transação
  ativa com original disponível, sem seleção inicial. “Todos os restauráveis” só
  marca os elegíveis; revisar a lista exata e confirmar precedem qualquer efeito.
  Cada registro é revalidado e tem resultado individual; incerto, removido ou
  inativo não entra no lote e não é substituído. Recuperação continua um fluxo
  separado. Restaurações invalidam análise/revisão e seleções obsoletas; mutações
  concorrentes ficam bloqueadas durante execução. Contratos, autorização,
  concorrência, cancelamento, durabilidade e compatibilidade de journals são
  trabalho de P3, **não uma nova API implementada**.
- **Automação:** uma ou mais bibliotecas selecionadas, ou todas explicitamente;
  vazio nunca significa todas. Ajustes em edição e conjunto salvo são distintos.
  Biblioteca indisponível exige revisão explícita, não substituição silenciosa.
  Salvar não dispara tarefas. Otimizar novas imagens e atualizar conversões a
  partir dos originais continuam tarefas nativas distintas, com chaves/disparos
  preservados; não se propõe segundo scheduler nem unificação silenciosa.

### Conferência de armazenamento no código atual

Leitura, sem alterações, de `Migration/OptimizationCoordinator.cs`:

- `ConvertAsync` (linhas 461–468): `ActualBackupBytes() + source.Length` deve
  caber em `MaximumBackupBytes`. É teto **total de originais**, inclusive a cópia
  segura temporária antes de remover após verificar, não orçamento por execução.
  `EnsureSpace` soma `MinimumFreeSpaceBytes` aos bytes necessários; esse mínimo
  livre não pode ser consumido pelo plugin. `ActualBackupBytes` (648–650) mede os
  arquivos da pasta de originais.
- Início do lote (180–183): expiração atual ocorre em aplicação de **otimização**
  (`!DryRun && !synchronize`), não em simulação, não num disparo diário de retenção
  e não no caminho atual de sincronização. Não generalizar isso como backend já
  unificado para a futura análise única.
- `PurgeCoreAsync` (613–630): considera `Committed`, backup não removido e
  **`CompletedUtc < cutoffUtc`**, com verificações de referência/hash e ausência
  da origem. A idade é desde a conclusão da conversão/regeneração (506 e 596),
  **não** `CreatedUtc`, mtime do backup ou último acesso. Uma regeneração atualiza
  essa conclusão mesmo reutilizando o original. Igualdade exata ao corte não
  satisfaz `<`. As idades inteiras da fixture só ilustram essa regra.
- A retenção remove **originais guardados**, nunca mídia/vídeos ou WebP ativo;
  a perda de restauração/regeneração é irreversível. Não prometer expiração à
  meia-noite de X dias. Previsão, consentimentos, proteção do original necessário
  à regeneração selecionada e dados de elegibilidade reais do novo fluxo dependem
  do desenho P3; a bancada não executa hashes, espaço ou purga reais.

## 9. Adendo P2 — iteração 3 com feedback contextual (05/11/2026)

Proposta demonstrada no estudo; **nenhuma API, runtime ou persistência mudou**.

- **Exclusão tem finalidade e endereço próprios:** subpágina “Remover originais”
  em Histórico e restauração (`/historico/remover-originais` no hash do estudo),
  não uma sexta área principal nem uma seção abaixo da restauração. Link e ações
  dizem “permanente”, além do vermelho danger. Seleções são independentes.
- **Paginação não é autorização:** páginas de dez imagens, inclusive uma operação
  longa; seleção por identidade persiste entre páginas. “Selecionar página” e
  “todos os resultados/do plugin” têm alcance explícito. Revisões paginam detalhes,
  mas conservam todos os destinatários, bytes e revalidação individual do snapshot.
  Nenhuma seleção oculta é truncada ou substituída. Paginação cliente é só estudo;
  transporte, limites, autorização e durabilidade reais continuam em P3.
- **Saldo principal da simulação = seleção atual**, não todas as propostas. Zero
  selecionadas apresenta zero antes/depois e “Sem alterações selecionadas”.
  Totais potenciais de todas as propostas são comparação secundária explícita.
  Expiração de outros originais segue separada da conta autorizada.
- **Automação em análise não é aprovação:** “Apenas analisar e gerar relatório”
  lê bytes e otimiza para avaliar; não altera imagens, não guarda originais, não
  cria fila de aprovação nem aplicação futura por conta própria. O trabalho usa
  processamento e grava relatório/estado. Evidência atual, conferida por leitura:
  `OptimizationCoordinator.cs:256–288` (`WouldConvert`, estimativa e `ConvertAsync`
  condicionado a `!DryRun`), cursor próprio em 185 e relatório em 737. O backend
  atual conserva o último relatório e até 20 amostras, **não** um histórico completo
  de análises agendadas. O exemplo acessível no protótipo é rotulado sintético.
- **Desvio para proteções globais pela Automação:** foco em Armazenamento e retorno
  explícito à Automação; ir/editar/voltar não salva nem perde seu rascunho ou a
  revisão manual. Salvar Configurações muda só os globais, invalida revisão manual
  e exige conferir limites/validade da automação, sem ampliar alcance nem salvá-la.

O mantenedor relatou sucesso nos testes contextualizados de múltiplos itens/lote
e navegação; relatou fricção na automação e no saldo fixo. Não é uma sessão sem
contexto nem aprovação da nova iteração. O aceite humano P2 permanece pendente.
