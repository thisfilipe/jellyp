# Validação de upgrade Jellyfin 12.1 — baseline 0.3.0.1

## Conferência final dos guards (08/11/2026)

Depois da revisão independente, o startup também passou a encerrar e aguardar
somente o filho que acabou de criar se falhar a escrita do PID, readiness ou
qualquer guarda (inclusive `SystemExit`). O cleanup cobre falhas de bind sem
depender da própria guarda que falhou. A fase `upgrade` isolada e o caminho
`all` agora compartilham `upgrade_host`: `ProgramDataPath` autenticado antes da
substituição, hash instalado comparado ao SHA solicitado antes de iniciar o
candidato e nova checagem de `ProgramDataPath` após reinício.

Os **32 testes de release** passaram, incluindo falhas mockadas de bind/escrita
do PID, encerramento do filho e recusa de raiz autenticada/hash incorretos no
upgrade. Esses testes de falha não expõem um socket real fora de loopback.

O harness final foi reexecutado em **raiz nova**, com credenciais novas:
`/tmp/opencode/jellyp-upgrade-final-guards`. Resultado: **33 checks reais**,
restauração exata do original e encerramento do processo verificado ao final.
Log temporário: `/tmp/opencode/jellyp-upgrade-final-real.log`; testes locais:
`/tmp/opencode/jellyp-upgrade-final-guards.log`. UI/planning e .NET de produto
não foram repetidos nessa conferência; o código do produto não mudou.

Os relatos seguintes preservam as rodadas anteriores e seus respectivos caminhos;
não somar suas contagens às da conferência final.

Data da execução: **08/11/2026**. Fonte do produto: **`30401c5`**.
Este documento registra execuções reais em fixtures descartáveis. Não é uma
candidata de release, não autoriza produção e não substitui a identidade nova
exigida para a alpha.

## 1. Escopo e segurança

- Fixture principal de UI: `/tmp/opencode/jellyp-native`.
- Fixture nova do upgrade endurecido: `/tmp/opencode/jellyp-upgrade-harness6`.
- Jellyfin oficial 12.1.0 escutando **somente** em `127.0.0.1:18096`.
- Pacotes oficiais extraídos localmente; nenhum pacote instalado no SO, nenhum
  serviço systemd, nenhum acesso ao alias de produção ou SSH.
- O harness confere o processo exato (comando e `cwd`) e o socket público de
  loopback **antes** do wizard. `ProgramDataPath` é autenticado: só pode ser
  conferido **depois** do login e é verificado antes da biblioteca, mídia e
  mutações de plugin. Não se afirma que esse path foi validado antes do wizard.
- Credenciais, token e `test-password` são gerados por raiz, modo `0600`,
  nunca impressos ou commitados. A raiz nova não reutilizou credenciais.
- Baseline: artefato local `artifacts/JellyP_0.3.0.1.zip`, SHA256
  `9eb58165657f30d9c633848d4ed707a33fbcd079688b4c966be36030086657ca`.
- Candidato: build local da árvore atual, ainda com versão `0.3.0.1` apenas para
  o teste de upgrade. O ZIP e a DLL não são a identidade de release da alpha.

## 2. Recriar o host oficial

Pacotes usados (Ubuntu 24.04, extraídos com `dpkg-deb -x`):

| Pacote | SHA256 |
|---|---|
| `jellyfin-server_12.1+ubu2404_amd64.deb` | `b57edb75243b24c2ef619b62732a51d18ce1b8a0bf2f2bab9be47d1461b424e9` |
| `jellyfin-web_12.1+ubu2404_all.deb` | `618e9b9c5754c587e05056d71f90ca14241a27f9abead7a2337f38914c0c3a42` |

URLs oficiais usadas (o repositório redireciona para um mirror; `curl -L` é
necessário):

```sh
ROOT=/tmp/opencode/jellyp-upgrade-harness
mkdir -p "$ROOT"/{downloads,host,data,config,cache,log,media,browser}
touch "$ROOT/.jellyp-smoke-root"
curl -fL -o "$ROOT/downloads/server.deb" \
  'https://repo.jellyfin.org/files/server/ubuntu/stable/v12.1/amd64/jellyfin-server_12.1%2Bubu2404_amd64.deb'
curl -fL -o "$ROOT/downloads/web.deb" \
  'https://repo.jellyfin.org/files/server/ubuntu/stable/v12.1/amd64/jellyfin-web_12.1%2Bubu2404_all.deb'
sha256sum "$ROOT/downloads/server.deb" "$ROOT/downloads/web.deb"
dpkg-deb -x "$ROOT/downloads/server.deb" "$ROOT/host"
dpkg-deb -x "$ROOT/downloads/web.deb" "$ROOT/host"
```

Layout mínimo:

```text
/tmp/opencode/jellyp-upgrade-harness/
  .jellyp-smoke-root
  downloads/          pacotes oficiais
  host/               extração de server.deb + web.deb
  data/ config/ cache/ log/ media/ browser/
  host.pid            PID do processo exato iniciado pelo harness
```

`config/network.xml` fixa `InternalHttpPort`/`PublicHttpPort` em `18096`,
`EnableRemoteAccess=false`, `EnableIPv6=false` e
`LocalNetworkAddresses=<string>127.0.0.1</string>`. O harness valida esse XML
antes de gravar e, depois de iniciar, confere o bind efetivo em
`/proc/net/tcp`/`tcp6` e o inode do socket pertencente ao PID exato. Não basta
o XML declarar loopback. O log de inicialização deve mostrar
`Kestrel is listening on 127.0.0.1`; `ss -ltnp` deve mostrar o socket apenas em
`127.0.0.1:18096`.

Comando de inicialização (o harness usa exatamente estes argumentos):

```sh
/tmp/opencode/jellyp-upgrade-harness/host/usr/lib/jellyfin/bin/jellyfin \
  --datadir /tmp/opencode/jellyp-upgrade-harness/data \
  --configdir /tmp/opencode/jellyp-upgrade-harness/config \
  --cachedir /tmp/opencode/jellyp-upgrade-harness/cache \
  --logdir /tmp/opencode/jellyp-upgrade-harness/log \
  --webdir /tmp/opencode/jellyp-upgrade-harness/host/usr/share/jellyfin/web \
  --ffmpeg /usr/bin/ffmpeg
```

A prontidão é observada por `GET /System/Info/Public` com versão `12.1.0`, sem
sleep arbitrário. O wizard, a biblioteca sintética, o MP4 de um segundo e a
imagem PNG 512×512 são criados pelo harness ou por comandos equivalentes. O
provedor remoto de metadata e o monitor de tempo real ficam desabilitados; a
biblioteca usa apenas o arquivo sintético local.

## 3. Harness reproduzível endurecido

O harness é [scripts/smoke-upgrade.py](../scripts/smoke-upgrade.py). Ele recusa
raiz sem marcador em todas as fases, rejeita raiz/marker/caminhos symlinkados e
escapes, e inicia/para somente o processo exato daquela raiz. `upgrade` e
`verify` exigem `--expected-dll-sha256`; sem ele, não há como distinguir o
candidato do baseline.

```sh
python3 scripts/smoke-upgrade.py \
  --root /tmp/opencode/jellyp-upgrade-harness6 \
  --phase all \
  --candidate /tmp/opencode/jellyp-alpha-check/upgrade-candidate/JellyP_0.3.0.1.zip \
  --expected-dll-sha256 CA0CDBC73F5DED650011E0FBCCDD3CBF7849106E0C13502BAEF0A803432D3914
```

Fases:

- `bootstrap`: inicia o host 12.1, confere processo/cwd e bind efetivo, conclui
  o wizard com credenciais novas, autentica, confere `ProgramDataPath` e só
  então cria mídia/biblioteca; instala o baseline, confere DLL/folder/assembly
  carregado e deixa o host rodando.
- `seed`: grava configuração legada com escopo `items` de um único filme,
  `Enabled=false`, `DryRun=true`, `PreserveOriginals=true`, limites explícitos
  e duas políticas habilitadas; instala um `IntervalTrigger` de 1 dia na tarefa
  de otimização e um `DailyTrigger` às 03:00 na de sincronização; exige que o
  Primary existente seja byte a byte o PNG sintético (`png(0)`), recusando
  substituir outro asset; executa uma aplicação manual real
  (`POST /JellyP/Run?dryRun=false`); exige que o journal e o backup usem o hash
  sintético; e grava `upgrade-snapshot.json` com schema 2, hash da DLL baseline,
  hash sintético e hashes de XML/journal/original/saída.
- `upgrade`: valida o ZIP antes de parar o host; exige DLL do ZIP igual ao
  `--expected-dll-sha256` e diferente da DLL baseline; só então para o processo
  exato, substitui a pasta do plugin, reinicia e confere hash instalado, folder
  único, DLL única, assembly carregado em `/proc/<pid>/maps` e página embutida
  exata da árvore atual.
- `verify`: repete as provas de identidade, compara XML/journal/backup byte a
  byte, confere escopo/modo seguro/limites/triggers, recusa segunda restauração
  se o backup já foi consumido ou se `upgrade-verify.json` já existe, restaura o
  journal antigo pela API atual e exige journal final `RolledBack`,
  `BackupDeleted=true`, backup ausente e bytes/SHA256 exatos.
- `stop`: encerra somente o PID exato registrado em `host.pid`.

O harness valida **33 condições**. Arquivos privados são criados com
`os.open(..., 0o600, O_NOFOLLOW)` e nunca através de symlink. O processo fica
rodando ao final para os smokes de UI; `--phase stop` o encerra. Os caminhos acima são o exemplo
reproduzível; nesta rodada, a UI foi exercitada na fixture principal
`/tmp/opencode/jellyp-native` e o harness endurecido foi executado em uma raiz
nova, `/tmp/opencode/jellyp-upgrade-harness6`, com credenciais novas.

## 4. Resultado real de 08/11/2026

Os IDs de item e os hashes de XML/journal variam quando a raiz é recriada; o
PNG sintético e o resultado de restauração são determinísticos. Os valores
abaixo são da raiz nova do harness (`jellyp-upgrade-harness6`).

Host e baseline:

- Jellyfin `12.1.0`; `ProgramDataPath=/tmp/opencode/jellyp-upgrade-harness6/data`
  conferido após login e antes da biblioteca/mídia/plugin.
- Baseline `0.3.0.1`, DLL SHA256
  `04BAB3FE75E068B00D8C91720FDC5C349B47322177C2C9525BBB49D2D8CB6EEC`.
- Configuração legada preservada em XML SHA256
  `7AA19590AA94D2A6A6125C5D5A92B0FEFB1BD2B64DC567C8173F2085D7581878`.
- Journal legado SHA256
  `EEF6E5A1CA6D264E7D263FF1D9E968882F7DA4F3C695145B5FD15D007DFCC101`.
- Original PNG sintético: 472.062 bytes, SHA256
  `5EAF6A54D80DCDB07B5018F8C7AC939B45EE038E9B109FB1A2D3106066435FC1`.
- Saída WebP: 2.606 bytes, SHA256
  `5A7FEF28E55166CBDBD9DECAAA7081D7B36398B06DA86A1604D5D7CEA82A37D9`.
- Triggers: `IntervalTrigger` `864000000000` ticks na otimização;
  `DailyTrigger` `108000000000` ticks na sincronização.

Upgrade para o build local:

- Pacote candidato: 4 membros, 950.493 bytes, SHA256
  `56975D308F763171056B8CD281D589CC3DB1432D332857D4CCFA92A1BC3DE7DF`.
- DLL instalada: SHA256
  `CA0CDBC73F5DED650011E0FBCCDD3CBF7849106E0C13502BAEF0A803432D3914`,
  idêntica à DLL do build usado no pacote e diferente do baseline.
- O hash da DLL dentro do ZIP foi comparado ao `--expected-dll-sha256` antes
  de parar o host; o hash instalado foi conferido depois e era o mesmo
  `CA0CDBC7…`. A DLL carregada em `/proc/<pid>/maps` era exclusivamente
  `.../data/plugins/JellyP/Jellyfin.Plugin.JellyP.dll`, havia uma única pasta
  de plugin e uma única DLL JellyP, e a página embutida era exatamente a da
  árvore atual.
- XML, journal, original e saída WebP permaneceram byte a byte iguais após o
  upgrade. A leitura de `/JellyP/Settings` não reescreveu o XML.
- `Automation.Enabled=false`, `DryRun=true`, `PreserveOriginals=true`,
  `ScopeMode=items`, exatamente o mesmo `ItemIds`, `LibraryIds=[]`; padrões
  legados foram projetados sem gravação (`DefaultsSource=legacy`,
  `LegacyDefaultsWarning=true`).
- Os dois IDs de tarefa e os dois triggers permaneceram exatamente iguais; não
  houve novo agendamento.
- `PendingRecovery=0`; o journal antigo apareceu em
  `/JellyP/Backups?filter=restorable` com `Restorable=true`.
- Restauração atual: operação `Completed`, `Changed=1`, `Failed=0`,
  `Invalidated=0`, `NotStarted=0`; o Primary ativo voltou a ser PNG com
  472.062 bytes e SHA256
  `5EAF6A54D80DCDB07B5018F8C7AC939B45EE038E9B109FB1A2D3106066435FC1`.
  O ID da operação foi registrado no `upgrade-verify.json` temporário da raiz;
  não é um artefato durável no repositório.
- O journal antigo permaneceu no disco com `State=RolledBack`,
  `BackupDeleted=true`, e o arquivo de backup foi removido. Uma segunda
  execução de `verify` na raiz concluída foi recusada com a mensagem
  `Verification evidence already exists` antes de iniciar o host.

Outros checks executados na mesma rodada, em fixtures separadas:

- `scripts/smoke-upgrade.py --phase all` em raiz nova: **33** checks.
- `scripts/smoke-native-ui.cjs` sem `--journey` na fixture principal: **121**
  checks.
- `scripts/smoke-native-ui.cjs --journey`: **819** checks.
- `scripts/smoke-native-planning.cjs`: **20** checks.
- `python3 -m unittest discover -s tests/release -v`: **30** testes, OK
  (17 anteriores + 13 novos do harness).
- O build Release e os **186** testes .NET já haviam passado antes do hardening;
  o produto não foi alterado nesta rodada e a suíte geral .NET não foi repetida
  por instrução.

Logs e capturas sintéticas ficaram em `/tmp/opencode/jellyp-alpha-check/`,
`/tmp/opencode/jellyp-native/ui-evidence/`,
`/tmp/opencode/jellyp-native/planning-evidence/` e
`/tmp/opencode/jellyp-upgrade-harness6/`. Nada foi copiado para o Git.

## 5. Limitações

- O candidato ainda usa `0.3.0.1`; o teste prova compatibilidade do código
  atual, não uma identidade de release nova.
- V13 está fechado apenas para este gate local de migração. A instalação final
  deve repetir o upgrade com a identidade candidata e o catálogo público.
- As provas de bind/assembly usam `/proc` e `/proc/<pid>/maps` do Linux; não
  são portáveis para outros SOs.
- V08, V10 e V15 continuam dependentes dos gates de falha/interrupção; não
  houve disco cheio físico, alteração de permissões do SO ou serviço de sistema.
- Axe não é certificação WCAG; Firefox, Safari, leitor de tela e aparelhos
  físicos não foram validados.
- A fixture foi encerrada ao final pelo PID exato; a porta `18096` ficou livre.
  As fixtures e artefatos em `/tmp` são descartáveis e não sobrevivem ao reboot.
  O backup privado antigo não contém host, banco, credenciais ou mídia
  reutilizável.
