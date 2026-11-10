# Validação da candidata 0.4.0.1 — histórico NO-GO + correção final

**Estado atual: GO — candidata 0.4.0.1 pronta para release, sem publicação.**

## Encerramento da integração (10/11/2026)

A revisão independente final aprovou os bloqueantes de integridade e a separação
entre listagem preliminar e elegibilidade verificada de backups. Depois dela,
foram ajustados apenas textos da UI: tradução dos novos motivos de revisão e
título de originais guardados antes da verificação de remoção. Sintaxe JavaScript
e resolução desses motivos em inglês/português foram conferidas. Build Release
sem avisos/erros, **232 testes .NET** e **59 testes de release** foram repetidos
pelo integrador após esses ajustes.

Os **820 checks nativos**, **33 de upgrade** e **85 gates de host em fixture
limpa** citados abaixo pertencem às rodadas indicadas; a jornada nativa completa
não foi repetida para a última alteração exclusivamente textual. Os testes de
100k metadados usam adaptador com biblioteca falsa, não servidor de produção.
As limitações de memória/checkpoints, filtros verificados explícitos e escala
do benchmark permanecem declaradas; não se promete uma latência de produção.

O pacote de entrega é recompilado a partir do commit integrado e gerado em
`/tmp/opencode/jellyp-0.4.0.1-ready/`, com SHA256 em sidecar. **Os hashes das
tabelas seguintes identificam os pacotes históricos de validação**, não esse
pacote pós-commit: `SourceRevisionId` e os textos embutidos podem alterar os bytes.
Uma publicação futura deve usar o artefato da tag/CI e suas próprias verificações.

## Rodadas anteriores de validação

A rodada documentada abaixo registrou **NO-GO histórico** por semântica
preliminar de backups (`Restorable=true`/keys amplos para shared, corrupt,
uncertain ou superseded). Esse bloqueio foi corrigido depois, com separação
explícita entre listagem preliminar e verificação exata. O restante desta seção
permanece como evidência histórica da rodada anterior, não como estado atual.

**Correção final aplicada:** `all`/keys agora são metadados preliminares
(`CanReview=true`, `Restorable=false`, `Retirable=false`, `Verified=false`, sem
hash/varredura global); filtros `restorable`/`retirable` e suas keys são
verificação real explícita; registros shared/corrupt/uncertain/superseded ficam
fora deles. Foi adicionado `POST /Backups/Review` bounded/read-only por IDs
exatos, com inválidos por linha e sem descarte silencioso. A UI usa
`CanReview` para selecionar, mantém `all` como padrão barato, mostra
“verificar antes de restaurar” e remove inválidos apenas por ação explícita.
`GetStatus.VerifiedSavingsBytes` deixou de alegar verificação (retorna 0;
`PreliminarySavingsBytes` é separado). Contagens ausentes em restore/retire
fazem fallback fresco em vez de virar zero.

**Pacote final corrigido:** `/tmp/opencode/jellyp-0.4.0.1-final2/JellyP_0.4.0.1.zip` (build determinístico; mesma DLL após o último ajuste de timeout de Review)
SHA-256 `6d9a0f20f8bc1e26468c2cca9b30d0f673d82155c08f8e1b3afb5df0dfe06d48`;
DLL `6745027707697a2cb59b2f5f05853b9cde0dd7d6bfa33a6d36e4b846299d79cd`.
.NET **232/232**, release **59/59**, audit aprovado, UI nativa real com jornada
**820/820**, upgrade nativo **33/33**, e Review nativo de ID inválido exato
retornou `Eligible=0/Invalid=1/BackupNotFound`, com backups/operações
inalterados e `PendingRecovery=0`. O host desta
validação estava em `/tmp/opencode/jellyp-0.4.0.1-ui-backupfix` e foi encerrado
ao final.

Data: 10/11/2026. Base: `53a09750301f7288eaeb9e3fbc74844a17205299`.

## Identidade verificada

| Item | Valor |
| --- | --- |
| Pacote candidato final | `/tmp/opencode/jellyp-0.4.0.1-final2/JellyP_0.4.0.1.zip` |
| SHA-256 do ZIP final | `6d9a0f20f8bc1e26468c2cca9b30d0f673d82155c08f8e1b3afb5df0dfe06d48` |
| SHA-256 da DLL no ZIP final | `6745027707697a2cb59b2f5f05853b9cde0dd7d6bfa33a6d36e4b846299d79cd` |
| Build Release da fonte atual | mesmo SHA-256 da DLL empacotada, 0 avisos e 0 erros |
| Baseline | tag anotada `v0.4.0.0` → commit `268a1d6ff07a8563c74d02ed2bd005cb0dd3957a` |
| SHA-256 da DLL baseline compilada | `6eb14ad8ddadcd05dbd9df4fc8ca1a8eccb8311fcedd932a9ccca445db907146` |

Comando de identidade da candidata:

```sh
dotnet build Jellyfin.Plugin.JellyP/Jellyfin.Plugin.JellyP.csproj -c Release --no-restore
sha256sum Jellyfin.Plugin.JellyP/bin/Release/net10.0/Jellyfin.Plugin.JellyP.dll
unzip -p /tmp/opencode/jellyp-0.4.0.1-release-candidate2/JellyP_0.4.0.1.zip \
  Jellyfin.Plugin.JellyP.dll | sha256sum
```

O rebuild da fonte atual reproduziu exatamente a DLL do ZIP. O baseline foi
extraído com `git archive 268a1d6 Jellyfin.Plugin.JellyP | tar -x -C
/tmp/opencode/jellyp-0.4.0.0-baseline` e compilado no projeto isolado; nenhum
worktree ou artefato de baseline foi gravado no checkout.

## Gates nativos em raiz fresca

Raiz: `/tmp/opencode/jellyp-0.4.0.1-clean-gates` (marcada, host imutável
copiado por hardlink de `/tmp/opencode/jellyp-0.4.0.1-native/host`, credenciais
0600, mídia sintética). Não foi reutilizado state/journal/backup da fixture
anterior.

```sh
python3 scripts/smoke-upgrade.py --root /tmp/opencode/jellyp-0.4.0.1-clean-gates \
  --phase bootstrap --baseline artifacts/JellyP_0.3.0.1.zip \
  --expected-baseline-version 0.3.0.1

python3 scripts/smoke-host-gates.py --root /tmp/opencode/jellyp-0.4.0.1-clean-gates \
  --phase all \
  --candidate /tmp/opencode/jellyp-0.4.0.1-release-candidate2/JellyP_0.4.0.1.zip \
  --expected-dll-sha256 da0ff19ca56e0874b14bb8c6f94c608b89437df50aa650baca1cad092a139bbf \
  --expected-candidate-version 0.4.0.1
```

Resultado: **exit 0, 85 checks PASS, 0 FAIL**. Evidência:
`/tmp/opencode/jellyp-0.4.0.1-clean-gates/host-gates-evidence.json` e
`/tmp/opencode/jellyp-0.4.0.1-clean-gates-all.log`.

| Gate | Observação |
| --- | --- |
| V08a | plano `Reserved`, cancelamento terminal `Cancelled` antes de mutação; `mutationStarted=false`; plano volta a `Ready`; zero journal; PNG idêntico (`FEB7DE0792E6E486D34CA0AEDD40851BFBDD0534FB62FBBE30C875763A080E4F`) |
| V08b | primeira transação committada (`2B8F94E6D84A9008F740735B3E0D830FC9C5990B4EFFE5DCDEE87748F3F75015`), plano `Consumed`; segunda imagem byte-idêntica; nenhum segundo journal; cancelamento terminal `Cancelled` |
| V10 related | refresh `Active` antes do trigger; execução em `WaitingForMetadata`; release do provider antes de `Idle`; mesmo `executionId` até retomar; conversão e journal `Committed` após `Idle` |
| V10 unrelated | output `Committed` enquanto refresh não relacionado estava `Active`; sem `WaitingForMetadata`; conversão e hash ativo conferidos; outra biblioteca intocada |
| V15 | 6 itens; output existe antes do kill e hash `66E7862D125ABB0FE527A3186960F3463D010176D8DAD513135B8AEBA6AF127B` igual ao `OutputHash` do journal `Prepared`; source/backup `FE84D7D80047B1696D72D46EE606B5B711457DB9DCC1E5EB5B4E0D1EB6BD4C97` |

Detalhes V15: PID `1903212`/starttime `17549723` morto por `SIGKILL` via pidfd
após guarda de argv/cwd/uid/starttime/socket; restart PID `1906167`/starttime
`17564891`, mesma DLL `DA0FF19C...`; operação `Interrupted`, `pendingRecovery=1`,
nenhum replay e nenhum journal novo. Recovery explícita: journal
`bb6efb2f-2f88-43e7-bd14-24fe4ad3148b` de `Prepared` para `RolledBack`, backup
removido, `pendingRecovery=0`, PNG restaurado com o hash `FE84D7D8...`, caminho
WebP ausente e `outputRemovalProven=true`; contagem de journals 1→1 e de
operações 3→3. A operação permanece `Interrupted` após a recovery explícita,
com `Processed=1`, `Changed=0`, `Failed=1`, `NotStarted=5`; o journal
`RolledBack` explica a ausência de mudança commitada, sem replay ou journal
extra.

Cleanup final: `enabled=false`, `dryRun=true`, `preserveOriginals=true`,
`scopeMode=legacy`, `triggers=[]`, `pendingRecovery=0`; host parado,
`stopped=true`, `portFree=true`, `pidFilePresent=false`. A porta 18096 ficou
livre e nenhum processo Jellyfin da fixture permaneceu.

## Benchmark comparativo (rodada histórica; semântica `all` não comparável)

Harness reproduzível em `tests/benchmark/JellyP.Benchmark`
(`Program.cs` + projeto candidato). O mesmo `Program.cs` foi compilado contra a
fonte baseline extraída em `/tmp/opencode/jellyp-0.4.0.0-baseline`, com a
constante `BASELINE`; o projeto temporário do baseline é
`/tmp/opencode/jellyp-benchmark-baseline/JellyP.Benchmark.csproj`.

Entrada: 100.000 itens de metadados fake com um slot `Primary` virtual cada;
10.000 registros de backup/journal com 10.000 originals minúsculos e 10.000
WebPs ativos minúsculos; apply de 100 PNGs sintéticos 32×32 com
`PreserveOriginals=false` (`no Keep`). O `GetCurrent` do histórico é lookup O(1).
O baseline ainda valida hashes de arquivos sintéticos de 3 bytes em
`IsActiveRecord`; a candidata não hasheia no listing preliminar. Nenhum original
grande foi hasheado. Cada caso foi executado 3 vezes; a tabela usa a mediana. O adapter é fake: **não é um servidor Jellyfin
real, não é latência de produção e não mede throughput**.

Comando candidato:

```sh
dotnet run --project tests/benchmark/JellyP.Benchmark/JellyP.Benchmark.csproj \
  -c Release -- /tmp/opencode/jellyp-0.4.0.1-benchmark/candidate.json
```

Comando baseline (projeto temporário com o mesmo `Program.cs`):

```sh
cd /tmp/opencode/jellyp-benchmark-baseline
dotnet run --project JellyP.Benchmark.csproj -c Release -- \
  /tmp/opencode/jellyp-0.4.0.1-benchmark/baseline.json
```

| Caso | Baseline 0.4.0.0 | Candidata 0.4.0.1 |
| --- | --- | --- |
| 5 refreshes da 1ª página (100k metadados) | 4.637,8 ms; 1.000 `GetItemList`; 500.000 itens hidratados; 5.005 linhas utilizáveis | 549,1 ms; 215 `GetItemList`; 107.500 hidratados; 5.005 linhas |
| `DetailedInventory(maximumEntries=5000)` | 53,0 ms e falha `PlanScopeTooLarge` após 5.500 hidratados; 0 linhas | 581,1 ms; 5.001 linhas; 200 `GetItemList`; 100.000 hidratados |
| `Inventory()` global | 621,7 ms; 200 `GetItemList`; 100.000 hidratados | 572,7 ms; 200 `GetItemList`; 100.000 hidratados |
| Continuação de páginas 2–5 | API antiga não continua | 458,0 ms (inclui 1ª página); 215 `GetItemList`; 107.500 hidratados; 5.005 linhas |
| `AnalyzePlan` sobre 100k | 47,0 ms e falha `PlanScopeTooLarge` | 490,9 ms; `total=1001`, `hasMore=true`; 203 `GetItemList`; 101.500 hidratados |
| `GetBackups("all", 0, 50)` com 10k backups (baseline antigo era verificado; candidata preliminar — não comparar) | 1.233,6 ms; 1 `DetailedInventory` (1 `GetItemIds` + 200 `GetItemList`); 100.000 hidratados; 10.000 `GetCurrent`; 10.000 `IsSlotSafeForMutation`; 50 linhas | 212,8 ms; 0 inventário/hidratação (0 `GetItemIds` + 0 `GetItemList`); 50 `GetCurrent`; 50 `IsSlotSafeForMutation`; 50 linhas |
| `GetBackupKeys("all")` com 10k backups (mesma ressalva semântica) | 1.155,1 ms; 1 `DetailedInventory` (1 `GetItemIds` + 200 `GetItemList`); 100.000 hidratados; 10.000 `GetCurrent`; 10.000 `IsSlotSafeForMutation`; 10.000 chaves | 181,5 ms; 0 inventário (0 `GetItemIds` + 0 `GetItemList`); 0 `GetCurrent`; 0 `IsSlotSafeForMutation`; 10.000 chaves |
| Apply 100 sem manter originais | 15.092,5 ms (100,6 análise + 15.017,4 apply); 100 `CountPathReferences(string)` + 1 coleção; 10.000.000 linhas de referência varridas; 500 `GetCurrent`; 100 saves | 14.602,9 ms (125,2 + 14.519,6); 0 chamadas unitárias + 1 coleção; 100.000 linhas; 500 `GetCurrent`; 100 saves |

**Atualização pós-correção (benchmark `backupfix-*.json`):** `GetBackups("all")`
página 50: candidata **193,0 ms**, 0 inventário/0 `CountRef`, 50 `GetCurrent`;
baseline antigo `all` era verificado (1.139,5 ms, 1 `DetailedInventory`, 10.000
`GetCurrent`) — mesma linha não é trabalho equivalente. Filtro explícito
`restorable` página 50: candidata **1.050,5 ms** (1 `CountRef`, 20.000
`GetCurrent`), baseline **1.099,0 ms** (1 `DetailedInventory`, 10.000
`GetCurrent`); essa é a comparação verificada correta. `GetBackupKeys("all")`
preliminar: 174,0 ms, 0 `GetCurrent`; explícito verificado: 946,5 ms. O benchmark
usa biblioteca fake, não um host real de 100k; os arquivos `backupfix-*.json`
estão em `/tmp/opencode/jellyp-0.4.0.1-benchmark/`.

Leitura dos números:

- O baseline **não tem página limitada**: para mostrar as mesmas primeiras
  1.001 linhas ele precisa materializar as 100.000 entradas; por isso o caso de
  5 refreshes é uma comparação de caminhos diferentes, não de trabalho idêntico.
  A candidata devolve 1.001 linhas por página e reutiliza o snapshot por 30 s.
- `DetailedInventory(maximumEntries=5000)` e `AnalyzePlan` no baseline falham
  com `PlanScopeTooLarge`; a candidata sucede com parte limitada de 1.001 linhas.
  Esses dois casos são **falha vs. sucesso**, não razão de tempo.
- `Inventory()` global é a API comum e ficou equivalente (621,7 ms vs. 572,7 ms).
- O histórico de 10.000 backups lê os mesmos 10.000 arquivos JSON do journal em
  ambas as versões. O payload JSON relido por chamada foi **9.827.774 bytes** no
  baseline e **9.827.780 bytes** na candidata (a pequena diferença vem dos campos
  serializados do modelo de cada assembly). O baseline ainda hidrata 100.000
  itens e chama `GetCurrent`/`IsSlotSafeForMutation` 10.000 vezes; a candidata
  usa somente metadados preliminares e toca 50 registros da página.
- No apply, o custo dominante observado foi a persistência durável/fsync do
  harness (cerca de 10 s em ambos), não a varredura. A diferença de chamadas é
  explícita: o baseline faz 100 varreduras unitárias sobre 100.000 caminhos
  (10.000.000 linhas); a candidata faz uma varredura em lote (100.000 linhas).
  Não interpretar 15,1 s vs. 14,6 s como ganho de throughput de produção; a diferença de tempo variou entre execuções e o fsync domina.
- Os bytes de JSON acima são o tamanho do payload dos arquivos relidos, não uma
  contagem de syscalls `read()` do sistema operacional.

Limitações: os 100.000 caminhos de metadados são sintéticos/virtuais (sem
decodificação de imagem na varredura); os originals de 10.000 backups são
minúsculos (3 bytes) e o baseline valida seus hashes por registro, enquanto a
candidata não os hasheia no listing; nenhum original grande foi hasheado; o apply usa um
`IArtworkStore` fake com 100 PNGs pequenos e uma varredura sintética de 100.000
referências; o host real não foi usado no benchmark. O benchmark não repete a
jornada de UI 121/819 nem os 20 casos de planejamento.

Evidências brutas:
`/tmp/opencode/jellyp-0.4.0.1-benchmark/candidate.json`,
`/tmp/opencode/jellyp-0.4.0.1-benchmark/baseline.json`,
`candidate.stdout`/`baseline.stdout` e os logs `candidate.stderr`/`baseline.stderr`.

## Testes focados da fonte atual

```sh
dotnet test tests/Jellyfin.Plugin.JellyP.Tests/Jellyfin.Plugin.JellyP.Tests.csproj \
  -c Release --no-restore \
  --filter "FullyQualifiedName~AnalysisPageHydratesOnlyTheRequestedRemainderAfterTheFirstReferencePass|FullyQualifiedName~ApplyingHundredImagesUsesBoundedGlobalReferenceScans|FullyQualifiedName~HundredThousandMetadataEntriesStillProduceABoundedPart|FullyQualifiedName~PagedInventoryUnionIsCompleteOrderedAndHasNoSkipsWhenHydrationOrderDiffers|FullyQualifiedName~AnalysisCacheHitDoesNotRenewItsFixedTimestamp"
```

Resultado: `Aprovado: 5, Com falha: 0`, exit `0`. A suíte completa foi repetida
na rodada final: **232/232 .NET**, **59/59 release** e audit de publicação
aprovado para 106 arquivos.

## Histórico: bloqueio remanescente da rodada anterior (corrigido)

- **NO-GO histórico (corrigido):** a revisão reportou semântica preliminar de backups insegura
  para casos shared/corrupt/uncertain/superseded; `Restorable=true` e `Keys`
  amplos podem selecionar um lote que o apply rejeita. A correção de backend e a validação específica foram feitas na rodada final
  acima (232/232, 59/59, UI 820/820 e Review nativo de ID inválido).
- Os gates V08/V10/V15 provam cancelamento, espera por metadata e crash/recovery
  de imagem, mas não provam a seleção semântica de backups.
- O benchmark prova contagem de chamadas e tempo de harness com adapter fake;
  não prova latência de produção, capacidade ou integridade de banco real.
- Não houve UI redesign, não houve alteração de SO/serviços/permissões, não
  houve preenchimento de disco e nenhum host foi deixado rodando.
