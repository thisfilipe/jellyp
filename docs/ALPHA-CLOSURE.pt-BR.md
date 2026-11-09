# Fechamento técnico e preparação da alpha

**Conferência final do harness em 08/11:** após revisão independente, corrigidos
cleanup do startup e checagens ausentes do upgrade isolado. **32 testes de release**
e **33 checks de upgrade/restauração reais** passaram em nova raiz marcada, com
credenciais novas e processo encerrado ao final. A evidência detalhada está em
[validação de upgrade](UPGRADE-VALIDATION.pt-BR.md). Isso não repete os testes de
UI/planning anteriores e não cria uma identidade de release.

**Gates V08/V10/V15 em 08/11/2026:** uma rodada determinística in-process corrigiu
o cancelamento com snapshot obsoleto, a consumação precoce do plano e a ordem de
persistência do marcador de mutação, além de registrar as provas de espera por
metadata, refresh não relacionado, orçamento por configuração e recuperação
conservadora. A suíte .NET passou com **204 testes** e a suíte de release com
**32**. Nenhum processo Jellyfin foi iniciado nesta rodada; a camada exata de
cada prova e o que continua não exercitado estão na seção 6. V08/V10/V15 estão
fechados **somente nesse gate determinístico**, não no scheduler/SIGKILL reais do
host.

Atualizado em **08/11/2026**. Código base reconciliado até **`f0ee074`**; esta
rodada mantém as correções e testes V08/V10/V15 na árvore de trabalho, sem commit.
Este é o acompanhamento atual de entrega; o plano detalhado conserva também a
história da proposta. Não transforma testes históricos em uma nova execução.

## 1. O que está entregue

- Contratos aditivos e backend: análise unificada, revisão/revalidação da seleção
  exata, operações persistidas, restauração e remoção seletivas, retenção e
  configuração independente da automação. Ver [contratos](UX-P3-INTEGRATION.pt-BR.md).
- Cinco áreas nativas e jornadas de simulação, aplicação, regeneração, histórico,
  restauração, remoção e configuração; seleção entre páginas e cálculo do conjunto.
- Correções posteriores: histórico abre a operação solicitada; seleção global e
  página têm contagens explícitas; painéis aderentes sem scroll interno; hierarquia,
  navegação e ações refinadas com feedback do mantenedor.
- Atalho do plugin no menu; versão obtida da API do host; logos por tema e a
  composição aprovada com fundo azul-marinho nos cartões e no pacote.
- Código e assets commitados; nenhum deploy em produção ou publicação nova.

## 2. Evidência disponível e seu alcance

| Conjunto histórico | Resultado registrado | Alcance |
|---|---|---|
| Refinamento de seleção/aside/histórico | 743 checks (742 host + 1 unitário), 23 axe; 20 planning | Jornada nativa na fixture anterior à integração de branding |
| Branding | 186 .NET, 16 release, 122 checks nativos (7 axe) | Recursos, cabeçalhos, endpoint e geometria dos cartões; não repetiu jornadas mutantes |
| Correção final da imagem | 5 testes .NET de branding, 17 release, 122 checks nativos | Arte opaca correta; captura real do cartão conferida |
| Upgrade V13/V11 em fixture recriada (08/11/2026) | 33 checks do harness endurecido, 121 UI sem jornada, 819 UI com jornada, 20 planning, 186 .NET (rodada anterior), 30 release | Host 12.1 recriado com credenciais novas; XML/journal/backup/triggers preservados; journal antigo restaurado byte-exact pela API atual; candidato local ainda `0.3.0.1` |

Os números não se somam e não significam uma execução única sobre a revisão
final. Axe não é certificação WCAG nem substitui leitor de tela. Resultados
históricos e limitações estão em [validação nativa](UX-P3-NATIVE-VALIDATION.pt-BR.md)
e [branding](../assets/branding/README.md). A rodada de upgrade desta retomada,
com comandos e hashes, está em [validação de upgrade](UPGRADE-VALIDATION.pt-BR.md).

A fixture antiga em `/tmp` não sobreviveu ao reboot; a rodada de 08/11/2026
recriou uma nova fixture descartável. Capturas finais, resumos filtrados e
instruções manuais da rodada antiga foram preservados em um arquivo privado fora
do repositório. Não há backup executável, credenciais, banco ou instalação de
host antigos preservados. Não publicar esse arquivo como evidência de release
sem revisão de privacidade.

## 3. Matriz V01–V15: aceite conservador

“Parcial” significa que há implementação/testes, mas falta uma parcela do cenário
integral; não significa que a função esteja quebrada. Evidência backend não é
apresentada como observação de falha física no browser.

| Cenário | Situação | Fechamento necessário |
|---|---|---|
| V01 — sem imagem/tipo ausente | Exercitado nas jornadas nativas | Repetir smoke final na fixture recriada |
| V02 — converter e guardar original | Exercitado com seleção real | Repetir jornada final e validar bytes/recibo |
| V03 — alterar qualidade e regenerar | Exercitado; backend cobre políticas | Repetir caso positivo e descarte esperado separadamente |
| V04 — política já aplicada | Exercitado nas jornadas | Repetir regressão final |
| V05 — dimensões e restauração | Evidência de regeneração/restauração; combinação integral a conferir | Associar cenário específico a teste/resultado, sem inferir da soma de testes |
| V06 — alpha/pixels em logo | Regras backend cobertas; aceite nativo integral a consolidar | Provar lossless/lossy e descarte com original protegido |
| V07 — seleção obsoleta/subconjunto | Backend coberto; subset/paginação exercitados | Consolidar alteração entre revisão/aplicação e ausência de substituição |
| V08 — desconexão/fila/cancelamento | Parcial: cancelamento por `operationId` em fila, espera de metadata, antes da primeira mutação e contra snapshot finalizado coberto deterministicamente; desconexão/navegação/foco não reexecutados | Repetir a jornada de navegador/desconexão na fixture final; não inferir foco estável do teste in-process |
| V09 — manual independente da automação | Exercitado com configuração preservada | Repetir regressão final |
| V10 — scheduler e metadata | Parcial: espera por metadata relacionada, refresh não relacionado e serialização nativa cobertos no produto/adaptador real; disparo pelo scheduler Jellyfin não reexecutado | Disparar as tarefas pelo scheduler real na fixture final; o seam `EnsureIdle` já está provado |
| V11 — restauração e journals novos/antigos | Exercitado no host 12.1 em 08/11/2026: journal antigo restaurado pela API atual com SHA256 exato | Fechado para o gate local; repetir com identidade candidata final |
| V12 — remoção seletiva | Exercitado, inclusive 52 originais e fingerprint | Repetir conjunto exato e proteção de incertos na rodada final |
| V13 — upgrade da baseline | Exercitado no host 12.1 em 08/11/2026: baseline `0.3.0.1` → build local, escopo/journal/triggers/modo seguro preservados | Gate local fechado; falta upgrade com nova identidade candidata e catálogo público |
| V14 — teclado/idioma/mobile | Chromium e axe exercitados | Regressão final; leitor de tela/dispositivos físicos não validados |
| V15 — espaço/persistência/interrupção | Parcial: reserva por configuração, falha de persistência, marcador pré/pós-publicação e reinício simulado entre salvar/restaurar cobertos; SIGKILL/processo real não executado | Repetir interrupção com processo real da fixture depois de observar o journal e o PID exato |

Ver critérios integrais na seção 12 do [plano](UX-REFACTOR-PLAN.pt-BR.md).
Ainda faltam também duas bibliotecas reais sintéticas, aplicação sem guardar
originais e regeneração protegida sob retenção não-zero no host. Os caps de
5000/1000/200 não foram submetidos a stress na fixture anterior.

## 4. Ordem dos gates para fechar P7

1. **Revalidar checkout:** restore travado, build sem avisos, suíte .NET, release,
   sintaxe, auditoria e pacote local em saída separada. Registrar revisão exata.
2. **Recriar fixture:** Jellyfin/web 12.1, loopback, marcador, credenciais novas,
   dados sintéticos, providers remotos desabilitados; verificar processo/bind
   antes do wizard e o `ProgramDataPath` autenticado após o login, antes de
   biblioteca/mídia/plugin. Não restaurar nem copiar dados de produção.
3. **Migração primeiro:** instalar baseline em fixture e criar configuração,
   disparos e journals sintéticos; atualizar com o pacote candidato. Comparar
   configuração/horários e provar recuperação/restauração de dados antigos.
4. **Integridade negativa:** reproduções determinísticas para V08/V10/V15 e
   casos adicionais acima. Se necessário criar um harness isolado de testes, sem
   endpoint público de injeção de falhas nem sleeps para mascarar readiness.
5. **Rodada integrada final:** `smoke-native-ui.cjs --journey`, planning, temas,
   imagens, seleção entre páginas, histórico por operação e segurança dos dados.
   Guardar resultado e capturas correspondentes à DLL realmente instalada.

**Execução de 08/11/2026:** os passos 1–3 e 5 foram executados na fixture
recriada. O gate de migração V13 e a restauração V11 foram aprovados com
journal, backup e triggers reais; a DLL instalada era idêntica ao build local
(`CA0CDBC7…`). UI sem jornada (121), jornada (819) e planning (20) também
passaram. Detalhes e hashes em
[validação de upgrade](UPGRADE-VALIDATION.pt-BR.md). O passo 4 (V08/V10/V15) foi
executado depois, no nível determinístico in-process descrito na seção 6; não
houve novo processo de host, disparo pelo scheduler real nem SIGKILL nesta
retomada. A camada restante está explicitada naquela seção.

Interrupções são somente do processo verificado da fixture. Falta de espaço ou
falha de persistência deve ser simulada isoladamente, nunca enchendo disco do
computador nem alterando permissões/serviços do sistema ou produção.

## 5. Alpha / P8

- [ ] Fechar gates técnicos acima ou documentar restrição de escopo explícita;
  não dispensar integridade/migração por chamar o pacote de alpha.
- [ ] Atualizar guias de uso, recuperação e notas de migração para o fluxo atual.
- [ ] Preparar changelog e limitações com referência aos testes da revisão candidata.
- [ ] Definir nova versão de quatro partes e a identificação de alpha compatível
  com Jellyfin; não reutilizar `0.3.0.1` nem alterar seus assets publicados.
- [ ] Validar pacote e instalação/upgrade do candidato na fixture.
- [ ] Definir feedback por tarefa e caminho de rollback compatível com journals;
  não prometer downgrade seguro sem prova. Rollback precisa de snapshot da fixture
  anterior à atualização e restauração controlada, não só trocar a DLL.
- [ ] Somente com autorização específica: commit/push, tag, publicação e teste do
  catálogo público. Produção permanece uma decisão separada.

### Limitações não bloqueantes de uma alpha declarada

Avaliação por pessoa sem contexto está adiada por decisão do mantenedor. Firefox,
Safari, aparelhos físicos e leitor de tela não foram validados. Essas limitações
devem acompanhar a alpha; ausência de reclamações não equivale a aceite.

### Rascunho de notas para a candidata (não publicado)

**Mudanças:** interface nativa organizada por tarefa; análise e aplicação de um
conjunto revisado; cálculo de armazenamento do conjunto; relatórios por operação;
restauração em lote e exclusão separada; automação independente dos testes manuais;
branding e acesso direto pelo menu do Jellyfin.

**Atualização:** manter GUID, nomes das tarefas e disparos existentes. Carregar
configuração antiga não deve salvá-la, ampliar alcance nem habilitar conversões.
Padrões globais são ponto de partida dos testes; automação conserva ajustes
próprios. Limites globais de armazenamento continuam afetando as operações.
Journals antigos devem continuar utilizáveis sem inventar ajustes históricos que
não tenham sido registrados. Esses compromissos exigem a prova de upgrade acima.

**Coleta de feedback:** pedir tarefa, resultado esperado, resultado observado,
versões JellyP/Jellyfin, tema e navegador. Solicitar capturas anonimizadas; nunca
credenciais, imagens de produção, biblioteca completa, banco ou journals privados.
Em erro de recuperação, não recomendar apagar registros ou repetir uma operação
mutante indiscriminadamente; seguir [recuperação](RECOVERY.md).

### Situação nesta retomada

Reconciliação documental realizada e gates locais reexecutados em 08/11/2026,
com fonte em `30401c511b70ff24577b6af7ec1edcacf8bf9dc5`:

- Restore com `--locked-mode` aprovado, usando o cache existente.
- Build Release: zero avisos e zero erros; **186 testes .NET** aprovados.
- **30 testes de release** nesta rodada de hardening, sintaxe Python e dos quatro scripts CJS aprovados.
- `git diff --check` e auditorias do index/working tree aprovados; auditoria
  automatizada não substitui revisão manual.
- Pacote criado e validado em saída descartável separada, sem substituir
  `artifacts/`: quatro membros, 950.493 bytes, SHA256
  `56975d308f763171056b8cd281d589cc3db1432d332857d4ccfa92a1bc3de7df`.

**Bloqueio de identidade de release:** o pacote ainda informa `0.3.0.1`, mas o
código está 17 commits após a tag dessa versão e os bytes diferem do artefato
antigo (`9eb58165…`). O ZIP local é apenas um teste de empacotamento, não uma
candidata publicável. É necessário definir nova versão de quatro partes, alinhar
projeto, `build.yaml` e changelog e repetir as validações sobre a candidata.
Nenhuma versão foi alterada nesta retomada.

### Rodada de host em 08/11/2026

A fixture descartável foi recriada com os pacotes oficiais Jellyfin 12.1
(server `b57edb75…`, web `618e9b9c…`), apenas em `127.0.0.1:18096`, com
processo/cwd e bind loopback efetivo verificados antes do wizard e credenciais
novas. `ProgramDataPath` foi conferido após o login, antes da biblioteca, mídia
e mutações de plugin. O baseline local `0.3.0.1` foi instalado e gerou,
por API real, uma configuração legada de escopo exato, um trigger de intervalo
na tarefa de otimização, um trigger diário na sincronização, um journal e um
backup. O build local foi instalado como candidato de teste (DLL
`ca0cdbc7…`, idêntica à DLL do pacote). XML, journal, backup e saída WebP
permaneceram byte a byte; escopo, modo seguro e triggers não mudaram; o journal
antigo foi restaurado pela API atual com SHA256 exato. O harness reproduzível
está em [scripts/smoke-upgrade.py](../scripts/smoke-upgrade.py) e o detalhamento
em [validação de upgrade](UPGRADE-VALIDATION.pt-BR.md).

Nesta rodada de hardening passaram **33** checks do harness endurecido em raiz
nova e **30** testes de release (17 anteriores + 13 novos). Em fixtures
separadas, a rodada funcional registrou **121** checks de UI nativa sem jornada,
**819** com jornada e **20** de planning. Os **186** testes .NET já haviam
passado antes do hardening; o produto não mudou e a suíte geral não foi repetida
por instrução. O build Release teve zero avisos e zero erros.

Ambiente: .NET SDK 10.0.114, Python 3.12.3, Node 24.21.0, Linux x64,
Chromium 1248, Playwright 1.64.1 e `@axe-core/playwright` 4.14.0. Logs e
capturas sintéticas ficaram em `/tmp/opencode/` e não estão no Git. Não houve
instalação de pacote no SO, serviço systemd, acesso ao alias de produção ou
teste de catálogo público.

O host foi encerrado ao final; não há host nativo ativo nesta documentação.
UI/planning/axe, upgrade e as bibliotecas sintéticas foram reexecutados; V11 e
V13 estão fechados apenas para o gate local. V08/V10/V15 estão fechados somente
no gate determinístico in-process da seção 6; a camada de scheduler/SIGKILL real
do host permanece pendente. A validação de instalação final deve usar a nova
identidade candidata, sem sobrescrever a versão publicada. Este checklist não
autoriza publicação ou produção.

## 6. Rodada determinística V08/V10/V15 (08/11/2026)

Esta rodada foi executada **sem iniciar processo Jellyfin**, sem alterar
`artifacts/`, sem endpoint público de injeção de falhas e sem encher disco. Ela
usa classes reais do produto e o sistema de arquivos real em raízes temporárias
descartáveis; adaptadores falsos substituem somente as bordas de host/IO que
precisam de uma barreira determinística. A suíte completa passou com **204
testes .NET**, build Release sem avisos/erros, **32 testes de release**, sintaxe
Python/CJS e auditoria de publicação. UI/planning/axe não foram repetidos porque
o produto alterado é backend; os números históricos 121/819/20 não foram
reproduzidos nesta rodada.

**Camadas exatas**

- **Produto real:** `OptimizationCoordinator`, `OptimizeArtworkTask`,
  `SynchronizePoliciesTask`, `JellyfinArtworkStore.EnsureIdle`, journal,
  `DurableFiles`, conversão WebP e recuperação.
- **Bordas falsas determinísticas:** `IArtworkStore` de teste com barreiras de
  `EnsureIdle`/`SaveAsync` e um `ILibraryManager`/`IProviderManager` de teste para
  fila/progresso de refresh e scan. Não são respostas HTTP mockadas.
- **Seams de teste:** hooks privados acionados por reflexão nos testes simulam
  finalização concorrente e falha antes/depois da publicação do marcador; não há
  endpoint público de injeção de falhas nem comportamento de fault na release.
- **Não exercitado:** despacho pelo scheduler Jellyfin, SIGKILL de processo real,
  disco fisicamente cheio, permissões de SO e navegador/desconexão/foco.

**V08 — cancelamento por identidade**

- `CancelDuringApplyValidationLeavesPlanReadyAndNeverMutates`: cancelamento
  depois da validação e antes da mutação mantém o plano `Ready`, zero saves e
  nenhum journal. Antes da correção o plano ficava `Consumed` sem mutação.
- `CancelDuringFirstApplyMetadataWaitLeavesPlanReadyAndNeverMutates`: o
  cancelamento na primeira espera de metadata não marca `MutationStarted`, não
  grava imagem e devolve o plano a `Ready`.
- `CancelDuringFirstRetentionWaitLeavesPlanReadyAndNoRemoval`: o cancelamento na
  primeira espera de retenção não remove backup, não marca mutação e mantém o
  plano `Ready`.
- `CancelBatchRestoreDuringFirstMetadataWaitLeavesBackupAndNoRestore`: o
  cancelamento na primeira espera de restore em lote deixa o item `NotStarted`,
  preserva o WebP e o original guardado e encerra a operação como `Cancelled`.
- `CancelAfterMetadataWaitDoesNotStartTheNextTransaction`: com a primeira imagem
  já confirmada, o cancelamento durante a espera da segunda não inicia a segunda;
  a operação fica `Cancelled`, a primeira `Changed`, a segunda `NotStarted` e o
  plano permanece `Consumed`.
- `FinalizedOperationIsNotRevertedByCancellationSnapshot`: uma finalização
  concorrente que grava `Completed` entre a leitura e a persistência do
  cancelamento não pode ser revertida para o snapshot `Running`; o terminal,
  `MutationStarted` e o progresso são preservados e o pedido retorna `false`.
- `CancelExecutionReportsTerminalStateWhenFinalizationWinsTheRace`: se a
  finalização vence depois da pré-checagem, o endpoint relê o estado atual e
  retorna `409 OperationTerminal`, sem cancelar a tarefa nativa.
- `ConcurrentCancellationRequestsPreserveProgress`: dois pedidos concorrentes
  preservam `Running`, `CancelRequested`, `MutationStarted` e o progresso já
  persistido.
- `CancellationAfterLegacyMetadataWaitDoesNotStartTransaction`: a mesma barreira
  no lote legado nativo não grava a imagem e preserva o original.
- `CancelExecutionTargetsOnlyTheExactQueuedOperationAndReleasesItsPlan`: o
  endpoint de cancelamento usa o `operationId` exato; operação em fila vira
  `Cancelled`, o plano volta a `Ready`, a tarefa nativa é cancelada e um ID
  desconhecido retorna 404 sem cancelar outra operação.
- Cobertura anterior preservada: `CancelBeforeNativeStartLeavesPlanReady...`,
  `CancelWhileWaitingPersistsEvenAgainstConcurrentProgressSaves`,
  `CancelQueuedRequestNeverFallsBackToApplyingSavedSettings`.

**V10 — scheduler e metadata**

- `NativeTaskDoesNotWaitForUnrelatedMetadataRefresh`: refresh de outro item não
  bloqueia a tarefa nativa; o alvo converte e o item não relacionado permanece
  PNG.
- `NativeTaskWaitsForRelatedMetadataAndResumes`: refresh do item alvo leva a
  tarefa a `WaitingForMetadata` com zero saves; ao liberar, ela retoma e conclui.
- `UnrelatedRefreshProgressDoesNotBlockButRelatedProgressDoes` e
  `UnrelatedRefreshDoesNotBlockButTargetLibraryAncestorAndScanDo`: o
  `EnsureIdle` real aceita refresh não relacionado e rejeita item, pai, biblioteca
  e scan relacionados.
- Cobertura anterior preservada: `NativeOptimizeAndSyncSerializeWithoutFailingEachOther`
  e `CancellingNativeWaiterLeavesActiveReportAndGateIntact` exercitam a
  serialização/cancelamento das duas tarefas nativas.

**V15 — orçamento, persistência e interrupção**

- `ConfiguredFreeSpaceReserveDeniesBeforeBackupOrSave`: uma reserva mínima
  configurada acima do espaço disponível nega antes de backup/save, sem encher o
  disco; original intacto, zero saves, nenhum journal/backup.
- `OrphanOriginalFilesCountAgainstBudget` (anterior): originais órfãos contam no
  teto de backup e não são apagados silenciosamente.
- `PrePublicationMarkerFailureReleasesPlanWithoutMutation`: falha antes da
  publicação do marcador de mutação deixa o plano `Ready`, `MutationStarted=false`
  e zero saves. O marcador é persistido em uma cópia antes de alterar o estado
  compartilhado em memória.
- `PostPublicationMarkerFailureKeepsPlanReservedConservatively`: quando a falha
  pode ter ocorrido depois da publicação do marcador, o registro durável é lido;
  se ele já indica mutação, o plano permanece `Reserved` e `MutationStarted=true`
  em vez de liberar a reserva com incerteza. Isso é conservador, não uma promessa
  de que nenhum byte mudou.
- `ApplyPersistenceFailureRestoresOriginalAndLeavesNoRecovery`: falha de
  persistência após o save restaura o original, deixa o journal `RolledBack`,
  zero recuperação pendente e nenhum backup restaurável.
- `RestartDuringRestoreRequiresExplicitRecoveryAndRemovesVerifiedOutput`: barreira
  de estado simula queda entre o save do original e o journal `RolledBack`; o
  novo coordenador bloqueia novas mutações, a recuperação explícita restaura,
  remove a saída verificada, apaga o backup e termina sem pendência.
- Cobertura anterior preservada: `RestartWithPreparedJournalRequiresExplicitRecovery`,
  `PartialUnknownOutputRetainsBackupAndBlocksRetry`,
  `FailedSaveBeforeReferenceMutationRemovesVerifiedOrphan` e
  `InterruptedRolledBackBackupDeletionIsFinalizedIdempotently`.

**Limitações e próximo passo**

- O kill real do processo da fixture e o disparo pelo scheduler Jellyfin não
  foram executados; a prova de reinício usa a construção de um novo coordenador
  sobre o journal real, não um SIGKILL. Para fechar a camada de host, repetir com
  processo exato: observar `WaitingForMetadata`/`MutationStarted` no journal,
  matar somente o PID verificado, reiniciar e conferir `Interrupted`, ausência de
  replay e recuperação explícita.
- V08 também inclui desconexão/navegação/foco no navegador; essa parcela não foi
  reexecutada nesta rodada.
- V10 inclui o disparo nativo pelo scheduler real; a lógica de espera e o
  adaptador `EnsureIdle` foram provados, mas a decisão do scheduler do host não.
- V15 inclui falta de espaço física e erro de SO; a rodada usou quotas
  configuradas e falhas de persistência injetadas, sem tocar no disco do sistema.
