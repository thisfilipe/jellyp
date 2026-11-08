# Fechamento técnico e preparação da alpha

Atualizado em **08/11/2026**. Código reconciliado até **`30401c5`**.
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

Os números não se somam e não significam uma execução única sobre a revisão
final. Axe não é certificação WCAG nem substitui leitor de tela. Resultados
históricos e limitações estão em [validação nativa](UX-P3-NATIVE-VALIDATION.pt-BR.md)
e [branding](../assets/branding/README.md).

A fixture em `/tmp` não sobreviveu ao reboot. Capturas finais, resumos filtrados
e instruções manuais foram preservados em um arquivo privado fora do repositório.
Não há backup executável, credenciais, banco ou instalação de host preservados.
Não publicar esse arquivo como evidência de release sem revisão de privacidade.

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
| V08 — desconexão/fila/cancelamento | Parcial: HTTP de análise e contexto testados | Cancelar operação identificada em espera/intertransação de forma determinística |
| V09 — manual independente da automação | Exercitado com configuração preservada | Repetir regressão final |
| V10 — scheduler e metadata | Parcial: contratos/testes backend | Disparo nativo com espera real controlada e tarefa não relacionada |
| V11 — restauração e journals novos/antigos | Restauração real e compatibilidade backend | Exercitar journal antigo em instalação atualizada |
| V12 — remoção seletiva | Exercitado, inclusive 52 originais e fingerprint | Repetir conjunto exato e proteção de incertos na rodada final |
| V13 — upgrade da baseline | Compatibilidade testada no backend; host completo pendente | Upgrade real preservando escopo, journals, horários e modo seguro |
| V14 — teclado/idioma/mobile | Chromium e axe exercitados | Regressão final; leitor de tela/dispositivos físicos não validados |
| V15 — espaço/persistência/interrupção | Regras e falhas cobertas no backend | Prova no host de interrupção/recuperação conservadora, sem produção |

Ver critérios integrais na seção 12 do [plano](UX-REFACTOR-PLAN.pt-BR.md).
Ainda faltam também duas bibliotecas reais sintéticas, aplicação sem guardar
originais e regeneração protegida sob retenção não-zero no host. Os caps de
5000/1000/200 não foram submetidos a stress na fixture anterior.

## 4. Ordem dos gates para fechar P7

1. **Revalidar checkout:** restore travado, build sem avisos, suíte .NET, release,
   sintaxe, auditoria e pacote local em saída separada. Registrar revisão exata.
2. **Recriar fixture:** Jellyfin/web 12.1, loopback, marcador, credenciais novas,
   dados sintéticos, providers remotos desabilitados; verificar ProgramDataPath
   antes de mutações. Não restaurar nem copiar dados de produção.
3. **Migração primeiro:** instalar baseline em fixture e criar configuração,
   disparos e journals sintéticos; atualizar com o pacote candidato. Comparar
   configuração/horários e provar recuperação/restauração de dados antigos.
4. **Integridade negativa:** reproduções determinísticas para V08/V10/V15 e
   casos adicionais acima. Se necessário criar um harness isolado de testes, sem
   endpoint público de injeção de falhas nem sleeps para mascarar readiness.
5. **Rodada integrada final:** `smoke-native-ui.cjs --journey`, planning, temas,
   imagens, seleção entre páginas, histórico por operação e segurança dos dados.
   Guardar resultado e capturas correspondentes à DLL realmente instalada.

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
- **17 testes de release**, sintaxe Python e dos quatro scripts CJS aprovados.
- `git diff --check` e auditorias do index/working tree aprovados; auditoria
  automatizada não substitui revisão manual.
- Pacote criado e validado em saída descartável separada, sem substituir
  `artifacts/`: quatro membros, 635.828 bytes, SHA256
  `c5c5fb199557297ce90552781d726b7dea1e0857ae603738b429cf6bc936d74d`.

**Bloqueio de identidade de release:** o pacote ainda informa `0.3.0.1`, mas o
código está 16 commits após a tag dessa versão e os bytes diferem do artefato
antigo. O ZIP local é apenas um teste de empacotamento, não uma candidata
publicável. É necessário definir nova versão de quatro partes, alinhar projeto,
`build.yaml` e changelog e repetir as validações sobre a candidata. Nenhuma versão
foi alterada nesta retomada.

Ambiente da revalidação: .NET SDK 10.0.114, Python 3.12.3 e Node 24.21.0,
Linux x64. Logs locais desta rodada em `/tmp/opencode/jellyp-alpha-check/` são
temporários e não estão no Git. Não houve instalação de dependências nem testes
de URLs públicas/catálogo remoto.

Não há host nativo ativo. UI/planning/axe, upgrade, bibliotecas sintéticas e
V08/V10/V15 **não foram reexecutados**; os gates de migração/falhas continuam
abertos. Próxima execução: recriar a fixture e fechar esses cenários; a validação
de instalação final deve usar a nova identidade candidata, sem sobrescrever a
versão publicada. Este checklist não autoriza publicação ou produção.
