# P3 — Contrato backend implementado para integração nativa

Data: **06/11/2026**. Baseline do código: **0.3.0.1** + implementação
incremental desta entrega. Referências: [PLAN.md](PLAN.md),
[contrato P1](UX-P1-CONTRACT.pt-BR.md), [desenho P2](UX-P2-DESIGN.pt-BR.md),
[especificação](UX-REFACTOR-PLAN.pt-BR.md).

**Status:** este documento descreve o **backend e os DTOs implementados** nesta
árvore, não uma proposta. A página nativa foi integrada em `Web/config.html`,
sem portar os dados/efeitos do protótipo e sem alterar o backend pelo escritor
de UI. Resultados reais, capturas e lacunas estão em
[validação nativa](UX-P3-NATIVE-VALIDATION.pt-BR.md). O protótipo P2 permanece
separado; seus 311 checks não contam como testes desta integração.

A entrega cobre:

- análise manual única e somente leitura de conversão + regeneração;
- seleção exata de chaves por imagem, revisão de faturamento e retenção;
- aplicação persistida e idempotente em uma tarefa nativa existente;
- progresso, resultado individual e cancelamento seguro;
- restauração e aposentadoria seletivas de originais;
- separação explícita entre automação, padrões manuais e limites globais;
- inventário detalhado por imagem e revalidação global de referências;
- compatibilidade de journals, tarefas nativas, endpoints e configuração antiga.

Os testes automatizados comprovam as regras abaixo em armazenamento temporário.
Isso **não** substitui a execução no Jellyfin real; ver a seção final.

---

## 1. Autorização, formato e limites

Todos os endpoints em `JellyPController` — legados e novos — estão sob:

```csharp
[Authorize(Policy = "RequiresElevation")]
```

Não há endpoint anônimo, upload de caminho de arquivo ou seleção por caminho
filesystem. O cliente envia somente IDs do Jellyfin e chaves de imagem
geradas pelo servidor. Nenhuma resposta nova expõe caminhos internos.

**Correção comprovada no host oficial Jellyfin 12.1:** os DTOs retornam nomes
JSON **PascalCase**, por exemplo `Settings.Automation`, `Defaults`, `Globals`,
`PlanId`, `Items`, `Operation.OperationId` e `Billing.ActiveBytesBefore`.
GUIDs retornam no formato **N (32 caracteres, sem hífens)**. Chaves são opacas
e devem ser devolvidas literalmente. A UI usa esses DTOs reais, sem inferir
camelCase da configuração padrão do ASP.NET.

Os exemplos camelCase nas seções abaixo registram a representação conceitual
original deste contrato; **não são transcrições do wire 12.1**. Para implementar,
use os nomes PascalCase de `Migration/PlanningModels.cs` e as respostas reais.
Exceção comprovada: erros novos são um objeto anônimo e usam o envelope
**camelCase**, não PascalCase:

```json
{ "code": "PlanExpired", "message": "..." }
```

Mensagens de `IOException`/acesso negado são substituídas por texto genérico;
detalhes e caminhos permanecem apenas nos logs do servidor. Exceções
`InvalidOperationException`/`NotSupportedException` desconhecidas em operações
novas também são genéricas na API. O endpoint legado `GET /JellyP/Status`
continua expondo `MigrationRecord.Source.Path` por compatibilidade privilegiada
histórica; ele não é usado como evidência path-free pela UI nova. `BackupView.error`
e o relatório legado em `GET /Tasks` também são sanitizados (mensagem genérica
de recuperação/atenção), inclusive para journals antigos que tenham gravado um
caminho em `Error`/`Message`.

### 1.1 Unidades

- Todo campo terminado em `Bytes` está em **bytes** (`long`), não KiB/MiB.
- `ssim` é um número entre 0 e 1.
- `deltaBytes` é `totalBytesBefore - totalBytesAfter`. **Positivo** significa
  liberação; **negativo** significa uso adicional.
- `currentBytes`, `originalBytes`, `outputBytes` são tamanhos codificados
  atuais/propostos.
- Contagens (`evaluated`, `proposed`, `changed`, `failed` etc.) são imagens,
  nunca itens de biblioteca.

### 1.2 Hardcaps explícitos

| Limite | Valor |
|---|---:|
| Imagens por plano (`totalImages`) | 5000 |
| Imagens avaliadas por análise (`batchSize`) | 1000 |
| Chaves por seleção/revisão/aplicação | 1000 |
| Página de imagens do plano / revisão / backup / itens de execução | 200 |
| Página de execuções | 100 |
| IDs por lote de restauração/aposentadoria | 200 |
| Chaves de backup retornadas por `/Backups/Keys` | 10000 |
| Retenção de planos | 20 não referenciados / 64 MiB / 24 h; planos ligados a operações retidas ficam protegidos |
| Retenção de operações | 200 arquivos |
| Referências globais de inventário | 1.000.000 |

Acima dos limites de seleção/página a API falha com `400` ou `409`; não há
truncamento silencioso. A análise de um escopo com mais de 5000 imagens falha
antes de gravar o plano, com `PlanScopeTooLarge`.

---

## 2. Fluxo nativo e persistência

### 2.1 Análise não é execução

`POST /JellyP/Plan/Analyze` é **síncrono e somente leitura**. Ele pode ler bytes
para calcular SSIM/economia real, mas não altera imagens, configuração,
`last-run.json` ou cursores. Ele grava apenas:

```
<dados do plugin>/migration/plans/<planId>.json
```

Planos usam schema de índice de imagem versionado (`PlanSchemaVersion`). A
identidade de slots repetidos é reidratada pelo mesmo loader de `GetCurrent`
antes de enumerar `ImageInfos`; planos de schema anterior são recusados com
`PlanSchemaChanged` em vez de aplicar índices possivelmente deslocados.

O plano é imutável para a análise. Ele contém os paths/hashes internos
necessários para revalidar a escrita; **os DTOs de API não expõem esses paths**.
Buffers WebP de candidatos não são salvos no plano.

### 2.2 Aplicação, restauração e aposentadoria são operações persistidas

Mutações não dependem da vida da requisição HTTP. Para aplicação, restauração
em lote ou aposentadoria em lote:

1. o servidor valida e grava a intenção em
   `<dados do plugin>/migration/operations/<operationId>.json`;
2. reserva o plano/registros, quando aplicável;
3. enfileira a tarefa nativa existente `OptimizeArtworkTask`;
4. responde `202 Accepted` com `operationId` e `poll`;
5. o cliente consulta `GET /JellyP/Executions/{id}`.

Não existe `Task.Run` sem hospedagem nem promessa de execução após reinício.
A tarefa nativa `JellyPOptimizeArtwork` continua com a mesma chave e os mesmos
disparos. Quando uma operação persistida está na frente, a invocação nativa
executa a operação antes do lote legado de automação. Operações em fila
bloqueiam `Run`/`Sync` legados e novas mutações JellyP (`409`).

### 2.3 Cancelamento

`POST /JellyP/Executions/{id}/Cancel` (e o legado `POST /JellyP/Cancel`) marca
`cancelRequested` e pede cancelamento à tarefa nativa. O cancelamento é
observado entre imagens e durante esperas de metadata; uma transação de imagem
já iniciada chega a um limite seguro antes de parar. Itens ainda não iniciados
ficam `NotStarted`; transações concluídas não são desfeitas.

### 2.4 Reinício do servidor

Na criação do coordenador, operações `Queued`/`Running` de um processo anterior
são marcadas **`Interrupted`** e os itens ainda pendentes viram `NotStarted`.
Transações de journal com `ExecutionId` igual ao da operação são reconciliadas
como `Changed` quando o commit existe. **Não há replay automático**; a mesma
seleção reaplicada retorna o recibo existente em estado interruptado. Recuperação
incerta continua no endpoint explícito `POST /JellyP/Rollback/{id}`, fora dos
lotes.

### 2.5 Persistência

```
migration/plans/<guid>.json        plano imutável, expira em 24 h
migration/operations/<guid>.json   intenção, progresso e resultado individual
migration/journal/<guid>.json      journal existente, preservado
migration/last-run.json            relatório legado de Run/Sync
optimize-*/sync-*cursor*.json      cursores legados
```

Planos expirados/excedentes são limpos na criação, no startup e ao acessar um
plano (preservando o próprio alvo e planos de operações retidas). Não há timer
diário prometendo remoção exata em 24 h sem servidor em execução; 24 h é a
validade, e a limpeza ocorre no próximo startup/uso. Operações têm histórico
limitado; journals nunca são apagados pela retenção de planos/operações.
Arquivos temporários únicos de uma publicação interrompida por crash não são
varridos enquanto puder haver escritor ativo; a limpeza oportunista desses
restos é uma limitação operacional documentada, não um dado de recuperação.

`MigrationRecord` ganhou campos opcionais, compatíveis com journals antigos:
`ExecutionId`, `PolicySnapshot`, `OriginalWidth`, `OriginalHeight`,
`OutputWidth`, `OutputHeight`, `Ssim`, `BackupDeletionIntent` e
`CleanupWarning`. Registros antigos sem esses campos continuam legíveis; a API
mostra `null` em vez de inventar valores.

Antes de remover um original, `RetireRecordAsync` grava `BackupDeletionIntent`;
se o processo morrer entre o delete e o journal final, o próximo startup/uso
reconcilia o intent: backup ausente vira `BackupDeleted`, backup ainda presente
tem o intent limpo e continua restaurável. Um commit de imagem seguido de falha
de limpeza do original mantém a transação `Changed` e expõe
`cleanupWarning`/a mensagem de recuperação em vez de marcar a imagem como
falha. Journals/planos/estado de operação corrompidos carregam em modo
recuperação e bloqueiam novas mutações; não são ignorados.

---

## 3. Configuração: automação, padrões manuais e globais

Os campos no topo de `PluginConfiguration` continuam sendo a configuração
**legada/de automação** (`Enabled`, `DryRun`, escopo, políticas, preservação e
`BatchSize`). Isso preserva agendamentos, `Run`/`Sync` e rollback existentes.

Foi adicionado `ManualDefaults` (**nullable**), com:

```json
{
  "preserveOriginals": true,
  "quality": 85,
  "lossless": false,
  "resizeImages": false,
  "maximumDimension": 2400,
  "minimumSsim": 0.96,
  "batchSize": 250,
  "policies": [ { "category": "People", "enabled": true, "...": "..." } ]
}
```

Regra de migração:

- `ManualDefaults == null` → a leitura projeta os campos legados de automação,
  retorna `defaultsSource: "legacy"` e `legacyDefaultsWarning: true`;
- **a leitura não grava** esses valores projetados;
- `PUT /Settings/Defaults` grava somente `ManualDefaults` e os limites globais
  explícitos (`minimumFreeSpaceBytes`, `maximumBackupBytes`,
  `backupRetentionDays`, `minimumSavingsBytes`, `minimumSavingsRatio`);
- `PUT /Settings/Automation` grava somente os campos legados de automação;
- salvar automação **não** altera padrões manuais; salvar padrões **não** altera
  automação;
- `Enabled` é o gate legado **somente de mutação em modo aplicar**. Com
  `DryRun: true`, a tarefa agendada continua analisando e gravando relatório
  mesmo com `Enabled: false`; com `DryRun: false` e `Enabled: false`, a
  aplicação é bloqueada. `Enabled` não desabilita disparos/agendamentos e não
  significa “tarefa desligada”. A UI deve rotulá-lo como “Permitir que tarefas
  agendadas alterem imagens” e mostrar o modo real, nunca “desabilitada” para
  a análise;
- um plano já criado carrega seu snapshot de políticas/SSIM/economia. Salvar
  **automação** não invalida um plano manual criado com padrões explícitos,
  porque automação e padrões são campos separados;
- salvar **padrões manuais ou limites globais** depois da análise muda o
  fingerprint manual do plano. A aplicação retorna
  `409 ManualDefaultsChanged` e exige nova análise, mesmo que o plano ainda não
  tenha expirado;
- planos criados a partir da projeção legada (`ManualDefaults == null`) usam
  um fingerprint dos defaults efetivos projetados **e** dos limites globais;
  ele não é vazio. Alterar os campos legados que compõem essa projeção ou criar
  padrões explícitos depois da análise exige reanálise
  (`409 ManualDefaultsChanged`). A UI deve tratar o aviso
  `legacyDefaultsWarning` como instabilidade adicional. O backend não troca a
  seleção silenciosamente.

Consentimentos de servidor:

- `PUT /Settings/Automation` que resulte em `enabled && !dryRun &&
  !preserveOriginals` exige `authorizePermanentRemoval: true`; caso contrário
  `409 PermanentRemovalAuthorizationRequired`.
- `POST /Plan/{id}/Apply` de plano com `preserveOriginals: false` exige
  `authorizePermanentRemoval: true`.
- Retenção global (`backupRetentionDays > 0`) com originais elegíveis no início
  da aplicação exige `authorizeRetentionPurge: true`, separado do consentimento
  de remoção da operação. Sem isso a aplicação falha antes de qualquer imagem,
  com `409 RetentionPurgeAuthorizationRequired`.

---

## 4. Análise: `POST /JellyP/Plan/Analyze`

### 4.1 Request

```json
{
  "scope": "items",
  "itemIds": ["2d9d9f44-7a10-4a6e-a3c7-3c0e28b5f9c1"],
  "libraryIds": [],
  "policies": [
    {
      "category": "People",
      "enabled": true,
      "overrideGlobal": false,
      "quality": 85,
      "lossless": false,
      "resizeImages": false,
      "maximumDimension": 2400,
      "minimumSsim": 0.96
    }
  ],
  "generalPolicy": {
    "quality": 80,
    "lossless": false,
    "resizeImages": false,
    "maximumDimension": 2400,
    "minimumSsim": 0.96
  },
  "preserveOriginals": true,
  "batchSize": 100
}
```

Regras:

- `scope` deve ser exatamente `items`, `libraries` ou `all`.
- `items`: 1–10000 IDs, nenhum `libraryIds`; vazio falha (`400`), nunca vira
  “tudo”.
- `libraries`: 1–1000 IDs, nenhum `itemIds`; vazio falha (`400`).
- `all`: precisa ser explícito; `itemIds` e `libraryIds` devem estar vazios.
- `policies` vazio/ausente usa os padrões manuais efetivos. Quando enviado,
  deve conter **todas** as categorias graváveis exatamente uma vez
  (`People`, `Primary`, `Thumb`, `Logo`, `Backdrop`, `Art`, `Banner`, `Disc`,
  `Box`, `BoxRear`, `Menu`), senão `400 IncompletePolicies`.
- `generalPolicy` ausente usa os padrões manuais efetivos; quando enviado deve
  ser completo.
- `preserveOriginals`/`batchSize` ausentes usam os padrões manuais.
- `batchSize` deve estar entre 1 e `min(batchSize dos padrões, 1000)`.
- Filtros de economia (`minimumSavingsRatio`, `minimumSavingsBytes`) continuam
  **globais** e não são alterados por este request.

A análise usa o inventário global de referências mesmo quando o escopo é um
subconjunto. Imagens compartilhadas com qualquer item fora do escopo continuam
protegidas.

**Ordem de slots repetidos:** `GetItemList` pode ordenar `ImageInfos` por
`ImageInfo.Id`, diferente de `GetItemById`/`GetImageInfo`/`SaveImage`. Para
itens com múltiplas imagens do mesmo tipo, o store reidrata o item via
`GetItemById` antes de enumerar índices em `Inventory`, `DetailedInventory` e
`InspectItems`. Não se ordena por nome de arquivo. Planos antigos são
invalidados pelo schema; a defesa de path/hash/índice continua no apply.

### 4.2 Ações e motivos por imagem

`action` é `Convert`, `Regenerate` ou `Keep`.

| Situação | `state` | `action` | `reason` típico |
|---|---|---|---|
| JPEG/PNG único, política habilitada, aprovado por SSIM/economia | `Ready` | `Convert` | `null` |
| JellyP anterior com original íntegro e política diferente | `AlreadyWebp` | `Regenerate` | `null` |
| JellyP anterior já com a mesma política | `AlreadyWebp` | `Keep` | `AlreadyUsesPolicy` |
| WebP sem journal/original JellyP | `AlreadyWebp` | `Keep` | `ExternalWebpNotManaged` |
| Original ausente/falhou checksum | `AlreadyWebp` | `Keep` | `OriginalUnavailable` / `OriginalCorrupt` |
| Filtro real de SSIM/economia | `Ready`/`AlreadyWebp` | `Keep` | enum `WebpSkipReason` (`QualityBelowThreshold`, `InsufficientSavings`, ...) |
| Referência remota | `Remote` | `Keep` | `RemoteReference` |
| Arquivo local ausente | `Missing` | `Keep` | `MissingFile` |
| Fora do metadata interno, tipo não suportado, item de canal etc. | `Excluded` | `Keep` | `ExternalPath`, `UnsupportedFormat`, `NotLibrary`, `OtherSlotUnavailable` |
| Mesmo path usado por mais de um slot/item | `Shared` | `Keep` | `SharedReference` |
| Categoria desabilitada na política | qualquer | `Keep` | `CategoryDisabled` |
| Candidato além do `batchSize` | qualquer | `Keep` | `NotEvaluatedBatchLimit` |

Uma imagem `Keep` nunca é enviada para aplicação. A análise não faz
“recompressão” de WebP externo; regeneração sempre parte do original guardado
e usa o mesmo `RestoreCoreAsync` + conversão da sincronização legada.

### 4.3 Response `PlanSummary`

```json
{
  "planId": "8b9e9b24a4d24b11a49c74e10b6c0001",
  "status": "Ready",
  "createdUtc": "2026-11-06T19:00:00Z",
  "expiresUtc": "2026-11-07T19:00:00Z",
  "scope": "items",
  "requestedBatchSize": 100,
  "totalImages": 8,
  "evaluated": 4,
  "notEvaluated": 0,
  "proposed": 2,
  "convert": 1,
  "regenerate": 1,
  "kept": 6,
  "estimatedSavingsBytes": 123456,
  "preserveOriginals": true,
  "defaultsSource": "legacy",
  "legacyDefaultsWarning": true,
  "operationId": null,
  "warnings": [
    {
      "code": "LegacyDefaultsProjected",
      "message": "Manual defaults were projected from legacy automation fields ..."
    }
  ]
}
```

`status` do plano: `Ready`, `Reserved`, `Consumed`, `Expired`. `Expired` é
calculado na leitura. `totalImages` inclui keeps explicados; `evaluated` é o
número de candidatos realmente examinados (inclusive os que falharam filtros
reais); `notEvaluated` é o número que ficou fora por `batchSize`. Não há
estimativa apresentada como espaço livre em disco.

### 4.4 Leitura de planos

| Endpoint | Response |
|---|---|
| `GET /JellyP/Plan/{planId}` | `PlanSummary` acima; `404 PlanNotFound` |
| `GET /JellyP/Plan/{planId}/Images?offset=0&limit=50` | página abaixo |
| `GET /JellyP/Plan/{planId}/Keys?kind=selectable` | todas as chaves propostas |
| `GET /JellyP/Plan/{planId}/Keys?kind=all` | todas as chaves, inclusive keeps |

`GET /Plan/{id}/Images`:

```json
{
  "planId": "8b9e...",
  "offset": 0,
  "limit": 50,
  "total": 8,
  "items": [
    {
      "key": "2d9d9f44-7a10-4a6e-a3c7-3c0e28b5f9c1:000:000000",
      "itemId": "2d9d9f44-7a10-4a6e-a3c7-3c0e28b5f9c1",
      "name": "Synthetic Movie",
      "itemType": "Movie",
      "productionYear": 2026,
      "category": "Primary",
      "type": 0,
      "typeName": "Primary",
      "index": 0,
      "isPerson": false,
      "state": "Ready",
      "action": "Convert",
      "reason": null,
      "sourceMimeType": "image/png",
      "currentBytes": 156700,
      "originalBytes": 156700,
      "outputBytes": 43210,
      "sourceWidth": 512,
      "sourceHeight": 512,
      "outputWidth": 512,
      "outputHeight": 512,
      "ssim": 0.987,
      "savingsBytes": 113490,
      "sourceHash": "…",
      "outputHash": "…",
      "policyFingerprint": "…",
      "managedRecordId": null
    }
  ]
}
```

`key` é opaco e estável dentro do plano: `{itemId:N}:{(int)ImageType:D3}:{index:D6}`.
O cliente deve devolvê-lo literalmente; não deve construir chaves nem enviar
`path`. `Type` é `MediaBrowser.Model.Entities.ImageType` no DTO C#, mas o
serializer real 12.1 o retorna como **string**, por exemplo `"Primary"` ou
`"Backdrop"`, não o número dos exemplos antigos. `TypeName` também é textual.
Não derivar a chave de nenhum desses campos.

`GET /Plan/{id}/Keys`:

```json
{
  "planId": "8b9e...",
  "kind": "selectable",
  "total": 2,
  "keys": [
    "2d9d9f44-7a10-4a6e-a3c7-3c0e28b5f9c1:000:000000",
    "…"
  ]
}
```

`kind` diferente de `selectable`/`all` → `400 InvalidKeyKind`. Planos são
limitados a 5000 linhas, então a lista de chaves não é truncada. Depois de
reservado/consumido ou expirado, `selectable` retorna vazio; `all` continua
consultável.

---

## 5. Revisão exata: `POST /JellyP/Plan/{planId}/Review`

Request:

```json
{
  "keys": [
    "2d9d9f44-7a10-4a6e-a3c7-3c0e28b5f9c1:000:000000"
  ],
  "offset": 0,
  "limit": 50,
  "retentionOffset": 0,
  "retentionLimit": 50
}
```

Regras:

- 1–1000 chaves, sem duplicatas (`400 DuplicateSelectionKey`);
- todas devem pertencer ao plano (`400 UnknownPlanKey`);
- todas devem ter `action` `Convert`/`Regenerate` (`409 PlanningKeyNotSelectable`);
- o plano precisa estar `Ready` e não expirado;
- a revisão é somente leitura e **não reserva** o plano.

Response:

```json
{
  "planId": "8b9e...",
  "totalSelected": 1,
  "offset": 0,
  "limit": 50,
  "billing": {
    "selectedImages": 1,
    "activeBytesBefore": 156700,
    "backupBytesBefore": 0,
    "activeBytesAfter": 43210,
    "backupBytesAfter": 156700,
    "totalBytesBefore": 156700,
    "totalBytesAfter": 199910,
    "deltaBytes": -43210,
    "newOriginalBytes": 156700,
    "existingBackupBytes": 0,
    "removedBackupBytes": 0
  },
  "retention": {
    "eligibleCount": 0,
    "eligibleBytes": 0,
    "protectedCount": 0,
    "protectedBytes": 0,
    "total": 0,
    "offset": 0,
    "limit": 50,
    "hasMore": false,
    "truncated": false,
    "fingerprint": "",
    "items": []
  },
  "items": [ { "...": "mesmo PlanImageView" } ],
  "warnings": [
    {
      "code": "RevalidateAtApply",
      "message": "The server revalidates global references, paths and hashes again before each write."
    }
  ]
}
```

### 5.1 Faturamento

A conta é **somente da seleção exata**, não de todas as propostas do plano.

- `Convert` + guardar original:
  `activeBefore = bytes do PNG/JPEG atual`,
  `activeAfter = outputBytes`,
  `backupBefore = 0`, `backupAfter = originalBytes`.
- `Regenerate` + guardar:
  `activeBefore = WebP atual`, `activeAfter = novo WebP`,
  `backupBefore = originalBytes`, `backupAfter = originalBytes`
  (o original já existente permanece; não é “novo original”).
- `Regenerate` + remover:
  `backupBefore = originalBytes`, `backupAfter = 0`,
  `removedBackupBytes = originalBytes`.
- `Convert` + remover:
  nenhum backup é mantido; `newOriginalBytes = 0`.

`deltaBytes = totalBytesBefore - totalBytesAfter`. Negativo é uso adicional e
deve aparecer com sinal/verbo, não como “espaço liberado”. A conta não inclui
temporários, logs, caches ou overhead de filesystem, e não se soma aos totais
legados do plugin.

### 5.2 Retenção

`retention` mostra o efeito **separado** da retenção global no início da
aplicação:

- `eligibleCount`/`eligibleBytes`: outros originais que expirariam agora,
  com path/hash/referência já validados para a prévia;
- `protectedCount`/`protectedBytes`: originais das regenerações selecionadas,
  que **não** serão purgados antes de serem reutilizados;
- `total`/`offset`/`limit`/`hasMore` paginam **todas** as identidades
  elegíveis; `items` contém somente a página corrente. `truncated` continua
  como alias compatível de `hasMore`, mas a UI não deve bloquear a aplicação
  por paginação: deve percorrer as páginas e autorizar pelo `fingerprint`.
  Todas as identidades são retornadas por página; nenhuma é escondida atrás de
  um limite fixo de 50. A revisão não promete validade eterna: a aplicação
  revalida os IDs autorizados antes da mutação.
- `fingerprint` liga o conjunto completo de IDs, hashes, bytes e os limites
  globais de segurança (retenção, espaço livre, orçamento de originais e
  filtros de economia). Ele é exigido em `apply` quando há candidatos.

`POST /Plan/{id}/Review` aceita `retentionOffset` e `retentionLimit` além de
`offset`/`limit` das imagens. O `fingerprint` é o mesmo em qualquer página da
mesma prévia.

Se a prévia mudar entre revisão e aplicação (por tempo, nova transação ou
limites), a aplicação recalcula apenas para validar: ela **não** purga
identidades novas e só executa os IDs autorizados no momento da reserva. Se um
ID autorizado ou os limites de segurança mudarem antes da mutação, falha sem
consumir o plano; não há purga silenciosa. Novos candidatos aparecem em uma
revisão posterior, nunca por expansão implícita.

---

## 6. Aplicação: `POST /JellyP/Plan/{planId}/Apply`

Request:

```json
{
  "keys": [
    "2d9d9f44-7a10-4a6e-a3c7-3c0e28b5f9c1:000:000000"
  ],
  "authorizePermanentRemoval": false,
  "authorizeRetentionPurge": false,
  "authorizeRetentionFingerprint": null
}
```

Response `202 Accepted`:

```json
{
  "operationId": "a4a4b1f9df3d4d77ac4c41d4f6e40001",
  "kind": "Apply",
  "state": "Queued",
  "poll": "/JellyP/Executions/a4a4b1f9df3d4d77ac4c41d4f6e40001"
}
```

Regras de servidor:

1. a seleção deve ser exatamente a mesma em chamadas repetidas do mesmo plano;
2. a primeira intenção grava a operação **antes** de enfileirar a tarefa;
3. duplicar a mesma seleção retorna **o mesmo `operationId`**, mesmo depois de
   concluída;
4. seleção diferente em plano já reservado/consumido → `409 PlanAlreadyApplied`;
5. se `QueueIfNotRunning` falhar e nenhuma mutação tiver ocorrido, a reserva é
   liberada e a operação removida; o cliente pode tentar de novo;
6. tarefa nativa indisponível → `503 NativeTaskUnavailable`;
7. tarefa nativa ocupada → `409 NativeTaskBusy`;
8. outra operação persistida/fila → `409 OperationBusy`;
9. `preserveOriginals: false` sem `authorizePermanentRemoval` → `409`;
10. retenção aplicável sem `authorizeRetentionPurge` → `409`;
11. retenção aplicável sem `authorizeRetentionFingerprint` igual ao
    `Review.retention.fingerprint` corrente → `409 RetentionReviewChanged`;
12. uma identidade autorizada ou um limite global mudou antes da mutação →
    falha sem consumir o plano.

O plano só passa a `Consumed` quando a primeira mutação da aplicação pode
começar (inclusive purga de expiração autorizada). Se defaults, limites,
autorização de retenção ou evidência falharem antes disso, a operação termina
sem mutação e a reserva volta a `Ready`; não é necessário criar uma nova
simulação sem motivo. Depois de qualquer mudança de imagem ou remoção de
original, o plano permanece consumido.

Antes de qualquer escrita, o servidor faz nova varredura global de referências
para os itens selecionados, confere path/hash/`outputHash` do plano, revalida o
original de regeneração e recalcula o candidato com o mesmo snapshot de
política. Itens alterados viram `Invalidated` com motivo, sem substitutos. Uma
exceção incerta durante uma transação para a operação e marca os itens
restantes como `NotStarted`.

Se o plano tiver `preserveOriginals: false`, a remoção permanente acontece
somente depois do commit verificado, pelo mesmo caminho legado.

---

## 7. Execuções: progresso, resultado e cancelamento

### 7.1 `GET /JellyP/Executions?offset=0&limit=50`

```json
{
  "offset": 0,
  "limit": 50,
  "total": 1,
  "items": [
    {
      "operationId": "a4a4...",
      "planId": "8b9e...",
      "kind": "Apply",
      "state": "Running",
      "phase": "Processing",
      "message": null,
      "cancelRequested": false,
      "createdUtc": "2026-11-06T19:01:00Z",
      "startedUtc": "2026-11-06T19:01:01Z",
      "completedUtc": null,
      "totalItems": 1,
      "processed": 0,
      "changed": 0,
      "failed": 0,
      "invalidated": 0,
      "notStarted": 1,
      "retentionPurgedCount": 0,
      "retentionPurgedBytes": 0,
      "billing": {
        "activeBytesBefore": 0,
        "activeBytesAfter": 0,
        "backupBytesBefore": 0,
        "backupBytesAfter": 0,
        "newOriginalBytes": 0,
        "existingBackupBytes": 0,
        "removedBackupBytes": 0,
        "releasedBackupBytes": 0
      }
    }
  ]
}
```

`phase` é texto para diagnóstico: `Queued`, `Preparing`, `Validating`,
`RetiringExpiredOriginals`, `Processing`, `WaitingForMetadata`, `Completed`,
`Cancelled`, `Interrupted`, `Failed`.

`state`: `Queued`, `Running`, `Completed`, `CompletedWithErrors`, `Cancelled`,
`Interrupted`, `Failed`.

### 7.2 `GET /JellyP/Executions/{id}?offset=0&limit=50`

Retorna `OperationDetail`:

```json
{
  "operation": { "...": "OperationView acima" },
  "offset": 0,
  "limit": 50,
  "totalItems": 1,
  "items": [
    {
      "key": "2d9d9f44-7a10-4a6e-a3c7-3c0e28b5f9c1:000:000000",
      "recordId": "9b9a...",
      "itemId": "2d9d9f44-7a10-4a6e-a3c7-3c0e28b5f9c1",
      "category": "Primary",
      "type": 0,
      "typeName": "Primary",
      "index": 0,
      "name": "Synthetic Movie",
      "action": "Convert",
      "status": "Changed",
      "reason": null,
      "originalBytes": 156700,
      "outputBytes": 43210,
      "originalWidth": 512,
      "originalHeight": 512,
      "outputWidth": 512,
      "outputHeight": 512,
      "originalMimeType": "image/png",
      "sourceHash": "…",
      "outputHash": "…",
      "policyFingerprint": "…"
    }
  ]
}
```

`status` individual: `Pending`, `Changed`, `Restored`, `Retired`,
`Invalidated`, `Failed`, `NotStarted`. `reason` nunca é um path; exemplos:
`SourceChanged`, `SharedReference`, `OriginalUnavailable`, `OriginalCorrupt`,
`SlotManagedSinceAnalysis`, `ConversionChanged`, `BackupChanged`,
`CancelledBeforeStart`, `StoppedAfterUncertainTransaction`.
`cleanupWarning` pode acompanhar `Changed` quando a imagem foi confirmada mas a
remoção/limpeza do original ficou pendente; nesse caso o original é retido e a
operação **não** deve ser apresentada como falha de conversão.

O resultado de cada execução é persistido no próprio arquivo da operação e não
sobrescreve o relatório de outra execução nem `last-run.json` legado.

### 7.3 Cancelamento

`POST /JellyP/Executions/{id}/Cancel` → `202` se ainda não terminal; `409
OperationTerminal` se já concluiu/falhou. Uma operação em `Queued` é marcada
`Cancelled` antes de começar; o plano volta a `Ready` porque não houve início
nem mutação. Uma operação `Running` recebe `cancelRequested` e a transação
corrente termina com segurança.

`POST /JellyP/Executions/{id}/Retry` reenvia uma operação ainda `Queued` sem
criar uma segunda reserva. Se a tarefa nativa estiver ausente/ocupada ou a
chamada `QueueIfNotRunning` falhar, retorna `503`/`409` e **mantém** a operação
durável para nova tentativa. `ActiveOperationId`/`currentRunnerOperationId`
identificam somente o executor já anexado; `Queued` é aceitação, não prova de
runner.

---

## 8. Backups: restauração e aposentadoria seletivas

### 8.1 `GET /JellyP/Backups?filter=restorable|retirable|all&offset=0&limit=50`

Só aparecem transações `Committed`, ativas e mais recentes por slot; registros
antigos do mesmo slot, incertos e já restaurados/removidos não são oferecidos.
Paths/hashes são revalidados; uma referência compartilhada não é apresentada
como restaurável.

```json
{
  "filter": "restorable",
  "offset": 0,
  "limit": 50,
  "total": 1,
  "items": [
    {
      "recordId": "9b9a...",
      "itemId": "2d9d...",
      "key": "2d9d...:000:000000",
      "category": "Primary",
      "type": 0,
      "typeName": "Primary",
      "index": 0,
      "state": "Committed",
      "restorable": true,
      "retirable": true,
      "reason": null,
      "originalMimeType": "image/png",
      "originalBytes": 156700,
      "outputBytes": 43210,
      "originalWidth": 512,
      "originalHeight": 512,
      "outputWidth": 512,
      "outputHeight": 512,
      "policyFingerprint": "…",
      "sourceHash": "…",
      "outputHash": "…",
      "createdUtc": "2026-11-06T19:01:00Z",
      "completedUtc": "2026-11-06T19:01:02Z",
      "error": null
    }
  ]
}
```

- `restorable`: a imagem ativa aponta para o output JellyP, hash/backup são
  válidos e o slot não é compartilhado.
- `retirable`: além de restaurável, o arquivo original de origem não existe
  mais; remover o backup é permanente.
- `reason` possíveis: `SharedReference`, `OriginalStillPresent`,
  `ActiveArtworkChanged`, `BackupChanged`, `NotActiveCommittedTransaction`.
- `cleanupWarning` (quando presente) é aviso de pós-commit; `error` é sempre
  genérico/seguro na API, mesmo que um journal antigo tenha gravado path.
- compartilhamento é decidido por contagem global de referências, **não** pelo
  estado visual `Ready/Excluded/Missing`. Um slot excluído por irmão remoto/
  ausente continua protegido se o path físico for compartilhado. A restauração
  legada também reconsulta a contagem e recusa antes de `SaveAsync` quando o
  path atual tem mais de uma referência; nunca usa um simples booleano que
  contaria a própria referência e bloquearia tudo.
- `Restorable`/`Retirable` exigem que o item esteja seguro para mutação: um
  irmão remoto ou ausente (diferente do próprio slot em recuperação) nunca
  entra em lote e o core legado recusa antes de `SaveAsync`; o `SaveCalls`
  permanece zero nesses casos. O slot próprio ausente pode ser recuperado
  explicitamente, mas nunca é substituto para um irmão indisponível.

`GET /JellyP/Backups/Keys?filter=...` retorna `{filter,total,keys:[...]}` com
todas as IDs elegíveis; acima de 10000 falha `409 SelectionTooLarge` em vez de
truncar.

### 8.2 `POST /JellyP/Backups/Restore`

```json
{ "recordIds": ["9b9a..."] }
```

### 8.3 `POST /JellyP/Backups/RetireSelected`

```json
{ "recordIds": ["9b9a..."], "authorizePermanentRemoval": true }
```

Ambos retornam `202` com `OperationReceipt` (`kind`: `Restore`/`Retire`). Antes
de gravar a operação, o servidor valida:

- 1–200 IDs, não vazios, sem duplicatas;
- todos existentes, `Committed`, ativos, mais recentes no slot;
- backup/hash/output atuais válidos;
- slot não compartilhado;
- para `RetireSelected`, autorização permanente explícita.

Unknown/duplicado/inelegível falha **antes de qualquer escrita**, com `400` ou
`409 BackupNotEligible`. Durante a execução, cada registro é revalidado; um
registro alterado vira `Invalidated` e os demais seguem, sem substituição. Uma
falha incerta para o lote e marca os restantes `NotStarted`. Recuperação de
transação incerta continua no endpoint legado individual; lotes não a
contornam.

Billing de lote: `Restore` registra bytes ativos antes/depois e bytes de backup
liberados; `Retire` registra `releasedBackupBytes` e a contagem/bytes no resumo
da operação. Nada é reapresentado como espaço livre do filesystem.

---

## 9. Configurações

### 9.1 `GET /JellyP/Settings`

```json
{
  "automation": {
    "enabled": false,
    "dryRun": true,
    "preserveOriginals": true,
    "quality": 85,
    "lossless": false,
    "resizeImages": false,
    "maximumDimension": 2400,
    "minimumSsim": 0.96,
    "batchSize": 250,
    "scopeMode": "legacy",
    "itemIds": [],
    "libraryIds": [],
    "policies": [ { "category": "People", "enabled": true, "overrideGlobal": false, "quality": 85, "lossless": false, "resizeImages": false, "maximumDimension": 2400, "minimumSsim": 0.96 } ],
    "syncUnchangedPolicies": false
  },
  "defaults": {
    "preserveOriginals": true,
    "quality": 85,
    "lossless": false,
    "resizeImages": false,
    "maximumDimension": 2400,
    "minimumSsim": 0.96,
    "batchSize": 250,
    "policies": [ { "...": "..." } ]
  },
  "globals": {
    "minimumFreeSpaceBytes": 2147483648,
    "maximumBackupBytes": 536870912,
    "backupRetentionDays": 0,
    "minimumSavingsBytes": 4096,
    "minimumSavingsRatio": 0.05
  },
  "defaultsSource": "legacy",
  "legacyDefaultsWarning": true,
  "categories": ["People", "Primary", "..." ]
}
```

Se `defaultsSource == "legacy"`, a UI deve mostrar o aviso de projeção e pode
oferecer “salvar como padrões manuais”. A leitura não altera o XML.

### 9.2 `PUT /JellyP/Settings/Defaults`

Body: `{ "defaults": { ...ManualDefaultsSettings... }, "globals": { ... } }`.
`defaults: null` remove os padrões manuais e volta a projetar o legado. O
endpoint não toca em `automation`.

### 9.3 `PUT /JellyP/Settings/Automation`

Body:

```json
{
  "automation": { "...": "AutomationSettings completo" },
  "authorizePermanentRemoval": false
}
```

Não toca em `defaults` nem em `globals`. `scopeMode` continua validado pelas
regras legadas (`legacy`, `all`, `libraries`, `items`); vazio nunca vira “tudo”.

---

## 10. Tarefas nativas e legado preservado

`GET /JellyP/Tasks`:

```json
{
  "lastRun": { "...": "RunReport legado" },
  "tasks": [
    { "id": "task-id", "key": "JellyPOptimizeArtwork", "name": "Optimize library artwork", "state": "Idle" },
    { "id": "task-id-2", "key": "JellyPSynchronizePolicies", "name": "Synchronize artwork policies", "state": "Idle" }
  ],
  "activeOperationId": null,
  "currentRunnerOperationId": null,
  "pendingOperationId": null
}
```

Endpoints legados continuam funcionando com os mesmos formats e recursos:

| Endpoint | Comportamento preservado |
|---|---|
| `GET /JellyP/Selection?ids=` | inspeção read-only de até 100 IDs |
| `GET /JellyP/Status` | último `RunReport`, totais do plugin, registros recentes |
| `POST /JellyP/Run?dryRun=` | fila legada em `OptimizeArtworkTask` |
| `POST /JellyP/Sync?dryRun=` | fila legada em `SynchronizePoliciesTask` |
| `POST /JellyP/Cancel` | cancela fila + tarefas + operação persistida ativa |
| `POST /JellyP/Rollback/{id}` | restauração individual explícita/recovery |
| `POST /JellyP/Backups/Retire?olderThanDays=` | purga legada global — não usar como seleção nova |

Chaves `JellyPOptimizeArtwork`/`JellyPSynchronizePolicies`, IDs de tarefa,
disparos configurados e GUID do plugin permanecem. `Run`/`Sync` **não** são
usados pela nova jornada para aplicar seleção; uma seleção nova que apenas
chamasse `Run` com IDs/cursores seria uma implementação incorreta.

### 10.1 Erros comuns das novas APIs

| HTTP | `code` | Quando |
|---:|---|---|
| 400 | `InvalidScope`, `InvalidScopeIds` | escopo vazio/misturado/desconhecido |
| 400 | `IncompletePolicies`, `InvalidBatchSize` | policies incompletas/duplicadas ou batch inválido |
| 400 | `UnknownPlanKey`, `DuplicateSelectionKey`, `EmptySelection` | seleção inválida; nada é escrito |
| 400 | `InvalidBackupSelection`, `InvalidBackupFilter` | IDs/filtro inválidos; nada é escrito |
| 404 | `PlanNotFound`, `OperationNotFound` | plano/operação inexistente |
| 409 | `PlanScopeTooLarge`, `PlanExpired`, `PlanAlreadyApplied`, `PlanSchemaChanged` | plano acima do cap/expirado/já usado ou de schema antigo |
| 409 | `ManualDefaultsChanged` | padrões manuais/limites mudaram após a análise; reanalise |
| 409 | `PlanningKeyNotSelectable` | chave `Keep` enviada para revisão/aplicação |
| 409 | `PermanentRemovalAuthorizationRequired`, `RetentionPurgeAuthorizationRequired`, `RetentionReviewChanged` | consentimentos ausentes ou prévia de expiração alterada |
| 409 | `BackupNotEligible`, `SharedReference`, `OperationBusy`, `OperationTerminal` | mutação recusada sem escrita |
| 409 | `NativeTaskBusy`, `NativeQueueUnavailable` | tarefa nativa ocupada/falha de fila; reserva compensada |
| 503 | `NativeTaskUnavailable` | plugin/tarefa não carregada |
| 500 | `InternalError` | falha inesperada; detalhes apenas no log |

Recuperação incerta de uma transação de imagem **não** é resolvida por
aplicação, restauração em lote ou aposentadoria em lote: use
`POST /JellyP/Rollback/{recordId}` no fluxo de recuperação existente.

---

## 11. Notas para o próximo escritor de frontend

1. A página nativa deve chamar `POST /Plan/Analyze` e renderizar
   `GET /Plan/{id}/Images`; não montar candidatos no cliente nem inferir
   “tudo” de arrays vazios.
2. “Selecionar todas as propostas” deve usar
   `GET /Plan/{id}/Keys?kind=selectable`; a revisão deve mostrar
   `Review.billing` e `Review.retention` antes de `Apply`.
3. `Apply` é `202`; a resposta não significa que as imagens já mudaram.
   Exibir estado/phase e fazer polling de `GET /Executions/{id}`. O mesmo
   `Apply` repetido com as mesmas chaves é idempotente; seleção diferente
   exige nova análise.
4. Um plano expira em 24 h. Salvar automação não troca o snapshot; salvar
   padrões manuais/limites faz o `Apply` falhar com `ManualDefaultsChanged`
   quando o plano nasceu de padrões explícitos. Descarte a revisão e peça nova
   análise; o backend não aplicará um plano expirado nem trocará imagens.
5. Não exibir paths, hashes como caminho, exceções cruas ou `recordId` como
   identidade visual principal. Use `name`, `productionYear`, `category`,
   `typeName`, `index`.
6. Operações de lote são persistidas; navegar, recarregar ou fechar a aba não
   as cancela. O indicador global pode usar `activeOperationId` de
   `GET /Tasks`.
7. “Remover originais” é uma subpágina seletiva: use `/Backups`,
   `/Backups/Keys`, `/Backups/RetireSelected` com `authorizePermanentRemoval`.
   O endpoint legado `Backups/Retire` continua sendo purga global e não deve
   ser reaproveitado como seleção.
8. A restauração em lote usa `/Backups/Restore` e não oferece registros
   incertos, antigos ou compartilhados; o resultado individual vem de
   `GET /Executions/{id}`.
9. `defaultsSource: "legacy"` exige aviso de projeção; não salve os valores
   projetados implicitamente ao abrir Configurações.
10. A análise é síncrona e limitada; não inventar porcentagem. Use
    `evaluated`/`totalImages`/`notEvaluated` da análise e
    `processed`/`totalItems` da execução, com unidades separadas.
11. `Type` é um enum C#, serializado como string no host 12.1 observado;
    prefira `TypeName`/`Category` para rótulos. Nunca reconstrua chaves.
12. O contrato de autenticação é o mesmo do host: sessão administrativa
    elevada. Não adicionar endpoint público para contornar isso.

### 11.1 Evidência local disponível

**Fidelidade P2 (07/11/2026):** a correção visual posterior ao commit `e4207bf`
preserva estes contratos. A revisão nativa novamente coloca conta, consequências,
consentimentos e Apply no aside **Antes de autorizar**, com identidades e lista
completa paginada de expiração na coluna principal. Resumo da expiração e sua
autorização separada ficam junto de Apply; o cliente continua enviando o mesmo
fingerprint, nunca limita a autorização à página visível nem inclui expiração no
saldo da seleção. Previews são do slot atual confirmado como local, via helper
oficial; não são candidatos WebP nem prova dos bytes originais. A microcopy usa
**Padrões globais / Global defaults**; `ManualDefaults` e `ScopeMode=legacy`
continuam com os mesmos nomes/contratos no wire, sem migração automática na UI.
Detalhes, capturas e testes: [validação nativa](UX-P3-NATIVE-VALIDATION.pt-BR.md).

O mantenedor preparou um Jellyfin 12.1 descartável em `127.0.0.1:18096`
(server amd64 + web, hashes verificados, wizard/admin sintéticos, biblioteca
com filme/MP4 sintético). O plugin 0.3.0.1 carregou e a página nativa existente
funciona. A observação para a próxima integração é que
`customElements.get('emby-*')` pode retornar `undefined` mesmo com os controles
nativos funcionando; portanto a aceitação visual deve inspecionar o **DOM
aprimorado/assets reais**, não apenas o registro de custom elements. Módulos
de navegador para Playwright/axe estão disponíveis em
`/tmp/opencode/jellyp-native/browser/node_modules` para o próximo escritor.

A entrega original de backend não executou smoke de UI. A integração posterior
implementou a página e a exercitou no host descartável; ver a evidência separada
em [validação nativa](UX-P3-NATIVE-VALIDATION.pt-BR.md), inclusive o defeito de
índices de fundos múltiplos e a ausência de paginação completa da retenção.
Nenhuma evidência local autoriza produção, SSH, publicação ou release.

---

## 12. Verificação executada nesta entrega

- `dotnet build JellyP.slnx -c Release --no-restore` — **sucesso, 0 warnings,
  0 erros**.
- `dotnet test JellyP.slnx -c Release --no-build --no-restore` — **181
  aprovados, 0 falhas** nesta árvore; a suíte antiga permanece nela.
- `python3 -m unittest discover -s tests/release` — **9 testes, OK**.
- A conferência original registrou **79 checks nativos de UI, incluindo 7 axe**,
  e **16 checks de planejamento nativo** contra a DLL instalada real. A correção
  posterior de fidelidade, preservando o backend, reexecutou o smoke ampliado:
  **248 verificações (15 axe; uma guarda unitária do renderer identificada
  separadamente)** e **16 checks de planejamento**, sem substituição de HTML/API
  nas rodadas finais. Hash da DLL, capturas comparativas, logs e limitações estão
  na [validação nativa atualizada](UX-P3-NATIVE-VALIDATION.pt-BR.md).
- Testes novos cobrem: projeção legada sem escrita, isolamento
  defaults/automação, análise mista conversão/regeneração/WebP externo,
  SSIM real, limite de batch explícito, subset exato e idempotência, replay
  com seleção diferente, fonte alterada sem substituto, referência
  compartilhada no apply, restauração/aposentadoria por IDs exatos e rejeição
  de desconhecidos, retenção protegendo regeneração, consentimento de remoção
  permanente, reinício sem replay, compatibilidade de journal e de XML antigo.
- Limites de unidade continuam em **bytes** e os caps documentados acima são
  os reais desta árvore. Contagens nativas não são reivindicadas sem execução
  no host; o deploy/validação nativa permanece com o pai.
