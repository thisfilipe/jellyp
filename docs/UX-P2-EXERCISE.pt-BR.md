# P2 — Roteiro do exercício de usabilidade

Revisado em **05/11/2026** para o novo protótipo. **Sessão humana não realizada.**
A revisão técnica automatizada está registrada em
[UX-P2-DESIGN.pt-BR.md](UX-P2-DESIGN.pt-BR.md); não conta como este exercício.

Fase: **P2 autorizada** (artefatos + exercício). Artefatos:
[UX-P2-DESIGN.pt-BR.md](UX-P2-DESIGN.pt-BR.md) ·
[ux-prototype/jellyp-p2.html](ux-prototype/jellyp-p2.html).

**Participante**: alguém **sem contexto** do JellyP (não deve ter lido o plano,
o contrato ou este documento). Uma pessoa basta nesta fase.

**Registro**: preencher o template da seção 5 e guardar o resultado em
`.local/` (privado). Nunca colocar nome do participante ou dados pessoais em
documentos públicos do repositório.

## 1. Preparação (facilitador, ~5 min)

1. Abrir `docs/ux-prototype/jellyp-p2.html` no navegador, com seus arquivos `.css`
   e `.js` ao lado (arquivo local; sem servidor). Começar em **Páginas internas**.
   Marcar também a biblioteca **Séries** antes da sessão: haverá três propostas
   ao usar qualidade 80, permitindo observar a escolha de duas, não tudo.
   Qualidade inicial 85, Capa principal + Miniatura, guardar originais.
2. Ler em voz alta a introdução:
   > "Este é um protótipo de um plugin do Jellyfin para reduzir espaço de
   > imagens. Os dados são inventados; os botões alteram só este cenário de teste,
   > nunca uma biblioteca real.
   > Vou pedir algumas tarefas. Não há resposta certa ou errada, e o que me
   > interessa é como você procura."
3. **Regras do facilitador** (as mais importantes):
   - Ler a tarefa como está escrita, **sem ensinar nomes de botões ou áreas**.
   - Não corrigir o caminho; não apontar elementos; não dizer "é ali em cima".
   - Se a pessoa ficar parada > 60 s, registrar o travamento e oferecer **uma**
     dica mínima, anotando que houve intervenção.
   - Anotar hesitações, idas e vindas, e frases sobre alcance ("acho que isso
     vale para tudo…").
   - Para trocar de estado, abra a **bancada no rodapé** (fora do produto),
     prepare o cenário e recolha-a. Ativar um cenário abre a área afetada.
     Não deixe os controles de teste competindo com os controles do produto.
   - O processamento normal termina automaticamente em poucos segundos; esse
     tempo não representa desempenho real. Para a tarefa 3, aguarde a conclusão
     da tarefa 2 e injete **Espera por metadata (operação de exemplo)**. Ela tem
     identidade própria e não substitui o histórico da aplicação anterior.
   - Alternativamente, para observar cancelamento da própria seleção, prepare
     **Próxima aplicação aguardará metadata** antes da tarefa 2. Nesse caso, após
     cancelar não haverá conversões novas: a tarefa 4 usa uma alteração anterior
     de exemplo. Registrar qual variante foi usada, sem inventar uma restauração
     daquilo que não foi aplicado.

## 2. Bloco A — estrutura "Páginas internas" (~20 min)

Tarefas por objetivo, na ordem. Cada uma começa no estado indicado.

| # | Estado inicial (bancada) | Tarefa (ler como está) |
|---|---|---|
| 1 | Simular; Filmes e Séries; qualidade 85; tipos Capa principal + Miniatura; guardar originais; execução ociosa | "Descubra o que mudaria nestas imagens se você usar qualidade 80." |
| 2 | Logo após a tarefa 1 (análise na tela, zero propostas selecionadas) | "Aplique somente à capa principal e à miniatura de Viagem Sintética (2024), e mantenha a possibilidade de desfazer." |
| 3 | Após concluir a aplicação, injetar a operação de exemplo em espera; ou usar a variante pré-armada descrita acima | "Encontre a operação enquanto ela espera por metadata e interrompa a espera." |
| 4 | Histórico; execução ociosa | "Desfaça uma das alterações e confirme o formato armazenado." |
| 5 | Voltar a Simular; mudar rascunho para "remover originais" e qualidade 80 sem aplicar | "Configure trabalho futuro em uma biblioteca sem alterar seu teste manual." |
| 6 | Histórico | "Libere somente o original guardado que você escolheu." |

**Critério de sucesso por tarefa** (sem ajuda):

1. Encontra onde mudar a qualidade, executa a análise e identifica que a capa
   já convertida seria **gerada novamente a partir do original** — sem procurar
   um controle separado de "sincronizar".
2. Escolhe **exatamente duas das três propostas**, revisa suas identidades,
   confirma a aplicação e entende que os originais serão guardados/restauráveis.
   Registrar se marcou todas por engano ou confundiu simulação com autorização.
3. Localiza o andamento **na própria operação** e interrompe a espera sem
   inventar imagens processadas. Distingue esse caso de interromper um
   processamento já iniciado, no qual a transação atual termina com segurança.
4. Acha a transação, restaura, e lê formato/dimensões/tamanho armazenados,
   entendendo que o navegador pode continuar recebendo WebP.
5. Muda e salva a biblioteca/qualidade/modo/preservação próprios da automação e
   explica que o rascunho manual não mudou. A área mostra estado salvo separado
   das edições ainda não salvas. Consentimento destrutivo é próprio da automação.
6. Marca **um** original escolhido, revisa lista/bytes, confirma a consequência e
   só então remove. Nota que o incerto foi preservado e que “todos os elegíveis”
   apenas seleciona, nunca exclui imediatamente.

## 3. Bloco B — estrutura "Abas" (~7 min)

Trocar a estrutura para **Abas** (controle “Estrutura (D1)” na bancada do rodapé),
recolher a bancada e repetir:

1. "Você estava em Executar. Vá para Configurações e volte para onde estava,
   usando o navegador (botão voltar)." — observar se voltar/avançar funciona e
   se a pessoa reencontra o que estava fazendo.
2. Repetir rapidamente as tarefas **1** e **4** na estrutura de abas.
3. Viewport estreito (360×640): “Encontre cada uma das cinco áreas e diga qual é
   a ação principal de cada uma.” Observar se percebe a rolagem das abas e chega
   a Configurações. Repetir em **Páginas internas** com o seletor móvel de áreas;
   observar a descoberta das opções sem explicar o controle.
4. Ainda no celular, escolher uma proposta e chegar à revisão; abrir um diálogo
   de remoção e voltar sem alterar. Verificar alcance, leitura, rolagem, alvos de
   toque e se o usuário diferencia selecionar, revisar e confirmar.

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
Variante de espera: operação de exemplo após conclusão / própria seleção pré-armada

| Tarefa | Tempo até a 1ª ação | 1ª ação dada | Hesitações / procuras | Interpretação de alcance (citação) | Intervenções | Resultado |
|---|---|---|---|---|---|---|
| 1 qualidade 80 | | | | | | sucesso sem ajuda / com dica / não concluiu |
| 2 aplicar 2 imagens | | | | | | |
| 3 espera + interromper | | | | | | |
| 4 desfazer + formato | | | | | | |
| 5 automação vs manual | | | | | | |
| 6 liberar um original | | | | | | |

Bloco B (Abas): voltar/avançar: ____ ; tarefas 1 e 4: ____ ; viewport estreito: ____
Celular: abas roláveis vs seletor de páginas: ____ ; revisão/diálogo: ____

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
