# P2 — Roteiro do exercício de usabilidade

Revisado em **05/11/2026** a partir do feedback de produto com contexto.
**Exercício com pessoa sem contexto ainda não realizado.** Um teste básico com
contexto foi relatado; suas fricções constam do registro sanitizado no desenho P2,
sem identidade ou citações pessoais. Não conta como aprovação desta nova iteração.
A revisão técnica automatizada está registrada em
[UX-P2-DESIGN.pt-BR.md](UX-P2-DESIGN.pt-BR.md); não conta como este exercício.

Fase: **P2 autorizada** (artefatos + exercício). Artefatos:
[UX-P2-DESIGN.pt-BR.md](UX-P2-DESIGN.pt-BR.md) ·
[ux-prototype/jellyp-p2.html](ux-prototype/jellyp-p2.html).

**Participante**: alguém **sem contexto** do JellyP (não deve ter lido o plano,
o contrato ou este documento). Uma pessoa basta nesta fase.
Não precisa usar ou conhecer o Jellyfin: nesse caso, apresentar a introdução
abaixo. “Sem contexto” significa não conhecer os caminhos da interface testada,
não desconhecer o domínio. Registrar a familiaridade prévia; um ensaio com quem
não usa Jellyfin avalia descoberta e compreensão inicial, mas não substitui a
validação posterior com administradores reais do Jellyfin.

**Registro**: preencher o template da seção 5 e guardar o resultado em
`.local/` (privado). Nunca colocar nome do participante ou dados pessoais em
documentos públicos do repositório.

## 1. Preparação (facilitador, ~5 min)

1. Abrir `docs/ux-prototype/jellyp-p2.html` no navegador, com seus arquivos `.css`
   e `.js` ao lado (arquivo local; sem servidor). Começar em **Páginas internas**.
    Manter a configuração inicial: Filmes; qualidade 85; Capa principal + Miniatura;
    guardar originais. A tarefa básica usará **somente a capa de Viagem Sintética**.
    Páginas internas são a direção recomendada; não pedir uma escolha de estrutura
    antes de começar. Abas ficam como comparação opcional da bancada.
2. Antes de mostrar os controles, perguntar se a pessoa já conhece Jellyfin ou
   administra uma coleção de filmes/séries. Registrar sem exigir experiência.
   Para quem não conhece Jellyfin, ler esta contextualização (~1 minuto):

   > "O Jellyfin é um programa para organizar e assistir a uma coleção própria
   > de filmes e séries. Ele apresenta essa coleção com capas e outras imagens,
   > como um catálogo de streaming. Uma biblioteca é um grupo dessa coleção,
   > por exemplo Filmes ou Séries; um item pode ser um filme ou uma série.
   >
   > Um plugin acrescenta funções ao programa. Este se chama JellyP e serve
   > para reduzir o espaço ocupado pelas imagens do catálogo no disco.
   > Ele não reduz nem apaga os arquivos dos filmes ou dos episódios.
   > Imagine que você administra essa coleção e quer experimentar ajustes nas
   > imagens, decidir o que alterar e, em alguns casos, poder desfazer depois."

   Em seguida, ler para qualquer participante:

   > "Vamos usar um protótipo, não o Jellyfin de verdade. Os filmes, imagens e
   > números são inventados; nenhuma ação afeta arquivos reais. Algumas imagens
   > do exemplo já passaram pelo plugin antes.
   > Vou pedir algumas tarefas. Estamos avaliando a interface, não você.
   > Pode dizer em voz alta o que está procurando ou o que espera que aconteça.
   > Se algo não fizer sentido, diga; não precisa saber termos técnicos nem
   > acertar um caminho."

   **Limite da contextualização:** explicar o que são Jellyfin, biblioteca,
   item, plugin e imagens do catálogo é permitido. Não apontar áreas/botões,
   demonstrar uma tarefa, explicar antecipadamente onde restaurar, como usar
   ajustes próprios ou como funcionam seleção, saldo e retenção. Essas são
   compreensões que o exercício deve observar. Não exigir instalação, conta ou
   aprendizado do Jellyfin real antes da sessão.
3. **Regras do facilitador** (as mais importantes):
   - Ler a tarefa como está escrita, **sem ensinar nomes de botões ou áreas**.
   - Não corrigir o caminho; não apontar elementos; não dizer "é ali em cima".
   - Se a pessoa ficar parada > 60 s, registrar o travamento e oferecer **uma**
     dica mínima, anotando que houve intervenção.
   - Anotar hesitações, idas e vindas, e frases sobre alcance ("acho que isso
     vale para tudo…").
   - Se houver dúvida de vocabulário do domínio, responder de forma breve e
     neutra, sem indicar a solução na tela, e registrar o termo e a explicação.
     Separar esse esclarecimento de uma dica de navegação. Se for necessário
     explicar uma regra do produto (por exemplo, retenção), registrar que essa
     compreensão só ocorreu com ajuda; não contabilizar como descoberta autônoma.
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
| 1 | Simular; estado inicial; execução ociosa | "Descubra o que mudaria somente na capa principal de Viagem Sintética (2024) com qualidade 80. Mantenha os ajustes gerais do teste em 85." |
| 2 | Logo após a tarefa 1 (análise na tela, zero propostas selecionadas) | "Aplique somente a essa capa e mantenha a possibilidade de desfazer." |
| 3 | Após concluir a aplicação, injetar a operação de exemplo em espera; ou usar a variante pré-armada descrita acima | "Encontre a operação enquanto ela espera por metadata e interrompa a espera." |
| 4 | Após aplicação da capa; execução ociosa | "Desfaça somente a alteração dessa capa e confirme o formato armazenado." |
| 5 | Voltar a Simular; mudar teste para "remover originais" e qualidade 80 sem aplicar | "Configure e salve trabalho futuro para Filmes e Séries sem alterar seu teste manual. Explique o que cada tarefa fará e quando começa." |
| 6 | Histórico e restauração | "Libere somente o original guardado que você escolheu. Não altere a imagem ativa." |
| 7 | Configurações | "Descubra quanto espaço deve continuar livre, qual o limite de originais e o que acontece depois de 30 dias. Isso remove vídeos? Quando a limpeza acontece? O que deixa de ser possível? Não aplique nada." |

**Critério de sucesso por tarefa** (sem ajuda):

1. Encontra item específico, seleciona só Capa principal, descobre o editor por
   tipo como ação, expande, escolhe ajustes próprios e muda só a qualidade da capa.
   Distingue estado e ação e reconhece que os ajustes gerais continuam em 85.
   Executa a análise, vê o status de conclusão sem alteração e identifica que a capa
   já convertida seria **gerada novamente a partir do original** — sem procurar
   um controle separado de "sincronizar".
2. Escolhe **exatamente a capa proposta**, revisa sua identidade,
    confirma a aplicação e entende preservação/restauração e impacto líquido real.
   Registrar se marcou todas por engano ou confundiu simulação com autorização.
3. Localiza o andamento **na própria operação** e interrompe a espera sem
   inventar imagens processadas. Distingue esse caso de interromper um
   processamento já iniciado, no qual a transação atual termina com segurança.
4. Descobre Histórico e restauração ou o acesso direto, encontra a transação ativa,
    restaura após revisar e lê formato/dimensões/tamanho armazenados, entendendo que
    o navegador pode continuar recebendo WebP. Não confunde restaurar com remover.
5. Seleciona e salva **duas bibliotecas**, distingue edição e conjunto salvo,
    modo apenas analisar e gerar relatório/aplicar e preservação. Explica que seu teste não mudou e que
    salvar não dispara trabalho; os horários são definidos no Jellyfin, indisponíveis
    nesta bancada. Entende otimizar novas imagens vs recriar dos originais quando
    ajustes mudam. Consentimento destrutivo é próprio da automação.
6. Marca **um** original escolhido, revisa lista/bytes, confirma a consequência e
   só então remove. Nota que o incerto foi preservado e que “todos os elegíveis”
    apenas seleciona, nunca exclui imediatamente.
7. Explica mínimo livre não consumível e teto total dos originais (não gasto por
   execução). Retenção só afeta originais guardados; idade desde conclusão da
   conversão/regeneração, elegibilidade no início de lote, não à meia-noite. Perde
   restauração/recriação; não remove vídeos nem WebP ativo. Não interpreta salvar
   como aplicar nem simulação como mudança no disco.

### Extensões do exercício básico

- **Restauração em lote:** recomeçar a bancada e abrir Histórico e restauração,
  sem execução ativa. Pedir: “Desfaça juntas as duas alterações que ainda podem
  voltar aos originais; não toque no registro incerto.” Observar seleção de todos
  elegíveis vs subconjunto, revisão da lista exata, confirmação e resultado por
  imagem. Repetir selecionando só uma; voltar do diálogo não deve alterar nada.
  Incerto/recuperação é separado; não pedir que o lote contorne essa proteção.
- **Padrões globais:** após alterar Configurações e salvar, pedir: “Use os ajustes
  globais neste teste, sem mudar a automação ou o que você selecionou.” Observar
  descoberta da origem, confirmação da cópia, preservação de alcance/tipos/originais
  e invalidação da simulação anterior.
- **Escopo da automação:** desmarcar bibliotecas e observar que vazio bloqueia;
  selecionar todas explicitamente, salvar e explicar o conjunto. A bancada também
  pode retirar Séries para observar revisão de biblioteca salva indisponível.
- **Volume e alcance entre páginas:** preparar o cenário de volume na bancada e
  recolher. Pedir duas imagens em páginas diferentes da simulação e conferir se
  a pessoa entende que o saldo principal conta só a seleção. Zero, uma, subset e
  todas devem diferir. Repetir restauração/exclusão de um subset entre páginas,
  distinguindo selecionar página de todos os resultados/plugin. Conferir lista
  exata na revisão paginada, inclusive a operação longa, sem inferir autorização
  só pelas linhas visíveis. Escape deve preservar seleção, sem efeitos.
- **Exclusão própria:** a partir de Histórico, pedir para liberar um original sem
  desfazer a conversão. Observar descoberta da subpágina e retorno, texto permanente
  e diferença entre as seleções; não ensinar o link antecipadamente.
- **Automação e globais:** pedir modo apenas análise e perguntar qual será o
  resultado concreto e onde consultá-lo. Sucesso inclui entender relatório, sem
  alteração/guarda de originais, sem aprovação/aplicação automática futura. O
  exemplo não comprova tarefa executada. Pedir consulta de proteções e retorno,
  preservando ajustes não salvos; salvar globais não deve salvar automação. Em
  celular, observar salvar após a configuração sem campo coberto por painel fixo.

## 3. Bloco B opcional — comparação "Abas" (~7 min)

Somente se a comparação trouxer informação útil: trocar para **Abas** (controle
“Estrutura de comparação” na bancada), recolher e repetir. Este bloco não é uma
escolha D1 obrigatória, e não substitui validar as páginas recomendadas:

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
- "Você encontrou as áreas nas páginas internas? Onde esperava encontrar o que
  faltou?" Se o bloco opcional foi realizado, perguntar sobre diferenças sem
  exigir escolha binária ou tratá-la como validação de integração com o host.

## 5. Template de registro (preencher; guardar em `.local/`)

```markdown
# Exercício P2 — registro (privado)
Data: ____  Participante: (não registrar nome; usar "P1")  Duração: ____
Navegador/viewport: ____   Estrutura inicial: Páginas internas
Familiaridade prévia com Jellyfin: nenhuma / conhece / usa / administra
Familiaridade prévia com JellyP: ____
Introdução de domínio apresentada? ____
Esclarecimentos de vocabulário oferecidos (termo + explicação): ____
Variante de espera: operação de exemplo após conclusão / própria seleção pré-armada

| Tarefa | Tempo até a 1ª ação | 1ª ação dada | Hesitações / procuras | Interpretação de alcance (citação) | Intervenções | Resultado |
|---|---|---|---|---|---|---|
| 1 qualidade 80 | | | | | | sucesso sem ajuda / com dica / não concluiu |
| 2 aplicar só a capa | | | | | | |
| 3 espera + interromper | | | | | | |
| 4 desfazer + formato | | | | | | |
| 5 automação vs manual | | | | | | |
| 6 liberar um original | | | | | | |
| 7 espaço e retenção | | | | | | |
| Extensão: restauração em lote | | | | | | |
| Extensão: padrões globais | | | | | | |
| Extensão: escopo automação | | | | | | |

Bloco B opcional realizado? ____ ; voltar/avançar: ____ ; tarefas 1 e 4: ____ ; viewport estreito: ____
Celular: abas roláveis vs seletor de páginas: ____ ; revisão/diálogo: ____

Perguntas finais: descoberta da navegação + motivo: ____ ; D2 texto "gerar novamente a
partir do original" foi entendido? ______ ; frases literais úteis: ______

Estados ambíguos ou confusos (com tarefa e tela): ______
```

## 6. Depois da sessão

1. Guardar o registro preenchido em `.local/` (privado).
2. Trazer o registro (ou as observações) para o agente: as hesitações viram
   revisão de fluxo e de texto antes de fechar P2.
3. Só depois de observar e tratar as fricções, considerar o aceite de P2.
   Registrar navegação recomendada e entendimento D2 com a evidência real, sem
   transformar o relato com contexto em teste sem contexto. P3 exige autorização
   própria; esta revisão não a concede.
