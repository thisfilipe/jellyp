# P2 — Roteiro do exercício de usabilidade

Fase: **P2 autorizada** (artefatos + exercício). Artefatos:
[UX-P2-DESIGN.pt-BR.md](UX-P2-DESIGN.pt-BR.md) ·
[ux-prototype/jellyp-p2.html](ux-prototype/jellyp-p2.html).

**Participante**: alguém **sem contexto** do JellyP (não deve ter lido o plano,
o contrato ou este documento). Uma pessoa basta nesta fase.

**Registro**: preencher o template da seção 5 e guardar o resultado em
`.local/` (privado). Nunca colocar nome do participante ou dados pessoais em
documentos públicos do repositório.

## 1. Preparação (facilitador, ~5 min)

1. Abrir `docs/ux-prototype/jellyp-p2.html` no navegador (arquivo local; não
   precisa de servidor). Começar na estrutura **Páginas internas**.
2. Ler em voz alta a introdução:
   > "Este é um protótipo de um plugin do Jellyfin para reduzir espaço de
   > imagens. Nada é real: os dados são inventados e nenhum botão altera nada.
   > Vou pedir algumas tarefas. Não há resposta certa ou errada, e o que me
   > interessa é como você procura."
3. **Regras do facilitador** (as mais importantes):
   - Ler a tarefa como está escrita, **sem ensinar nomes de botões ou áreas**.
   - Não corrigir o caminho; não apontar elementos; não dizer "é ali em cima".
   - Se a pessoa ficar parada > 60 s, registrar o travamento e oferecer **uma**
     dica mínima, anotando que houve intervenção.
   - Anotar hesitações, idas e vindas, e frases sobre alcance ("acho que isso
     vale para tudo…").
   - Para trocar de estado, use a **bancada tracejada** no topo (não faz parte
     do produto; diga isso antes de usá-la quando necessário).

## 2. Bloco A — estrutura "Páginas internas" (~20 min)

Tarefas por objetivo, na ordem. Cada uma começa no estado indicado.

| # | Estado inicial (bancada) | Tarefa (ler como está) |
|---|---|---|
| 1 | Simular; qualidade 85; tipos Capa principal + Miniatura; guardar originais; execução ociosa | "Descubra o que mudaria nestas imagens se você usar qualidade 80." |
| 2 | Logo após a tarefa 1 (análise na tela) | "Aplique somente a estas duas imagens e mantenha a possibilidade de desfazer." |
| 3 | Após iniciar a aplicação, facilitador muda a bancada para "aguardando metadata" | "Encontre a operação enquanto ela espera por metadata e interrompa a espera." |
| 4 | Histórico; execução ociosa | "Desfaça uma das alterações e confirme o formato armazenado." |
| 5 | Voltar a Simular; mudar rascunho para "remover originais" e qualidade 80 sem aplicar | "Configure trabalho futuro em uma biblioteca sem alterar seu teste manual." |
| 6 | Histórico | "Libere somente o original guardado que você escolheu." |

**Critério de sucesso por tarefa** (sem ajuda):

1. Encontra onde mudar a qualidade, executa a análise e identifica que a capa
   já convertida seria **gerada novamente a partir do original** — sem procurar
   um controle separado de "sincronizar".
2. Autoriza **exatamente duas** imagens (não tudo) e entende que os originais
   serão guardados / restauráveis.
3. Localiza o andamento **na própria operação**, interrompe a espera e entende
   que a imagem atual termina antes de parar.
4. Acha a transação, restaura, e lê formato/dimensões/tamanho armazenados,
   entendendo que o navegador pode continuar recebendo WebP.
5. Salva a automação e explica que o teste manual não mudou (escopo/modo/
   preservação manuais intactos).
6. Remove **um** original escolhido, e nota que o incerto foi preservado e que
   "todos os elegíveis" era uma escolha explícita e separada.

## 3. Bloco B — estrutura "Abas" (~7 min)

Trocar a estrutura para **Abas** (botão no topo) e repetir:

1. "Você estava em Executar. Vá para Configurações e volte para onde estava,
   usando o navegador (botão voltar)." — observar se voltar/avançar funciona e
   se a pessoa reencontra o que estava fazendo.
2. Repetir rapidamente as tarefas **1** e **4** na estrutura de abas.
3. Viewport estreito (360×640, modo dispositivo do navegador): "Encontre cada
   uma das cinco áreas e diga qual é a ação principal de cada uma." — observar
   se algo essencial fica fora da tela ou depende de hover.

## 4. Perguntas finais (2 min)

- "Sem olhar a tela: quais áreas o plugin tem? O que cada uma resolve?"
- "Você conseguiria dizer o que ainda dá para desfazer e o que já não dá?"
- "Qual das duas estruturas (páginas ou abas) você escolheria para este tipo
  de trabalho? Por quê?" (registrar a justificativa, não só a preferência)

## 5. Template de registro (preencher; guardar em `.local/`)

```markdown
# Exercício P2 — registro (privado)
Data: ____  Participante: (não registrar nome; usar "P1")  Duração: ____
Navegador/viewport: ____   Estrutura inicial: Páginas internas

| Tarefa | Tempo até a 1ª ação | 1ª ação dada | Hesitações / procuras | Interpretação de alcance (citação) | Intervenções | Resultado |
|---|---|---|---|---|---|---|
| 1 qualidade 80 | | | | | | sucesso sem ajuda / com dica / não concluiu |
| 2 aplicar 2 imagens | | | | | | |
| 3 espera + interromper | | | | | | |
| 4 desfazer + formato | | | | | | |
| 5 automação vs manual | | | | | | |
| 6 liberar um original | | | | | | |

Bloco B (Abas): voltar/avançar: ____ ; tarefas 1 e 4: ____ ; viewport estreito: ____

Perguntas finais: D1 preferência + motivo: ____ ; D2 texto "gerar novamente a
partir do original" foi entendido? ______ ; frases literais úteis: ______

Estados ambíguos ou confusos (com tarefa e tela): ______
```

## 6. Depois da sessão

1. Guardar o registro preenchido em `.local/` (privado).
2. Trazer o registro (ou as observações) para o agente: as hesitações viram
   revisão de fluxo e de texto antes de fechar P2.
3. Só então P2 é marcada como concluída, D1/D2 são registradas com a evidência
   do exercício e P3 pode ser considerada para autorização.
