# Pendências futuras do JellyP

## Limites opcionais — feedback após a publicação 0.4.0.0

O mantenedor solicitou registrar duas melhorias, sem implementá-las nesta versão:

- [ ] Permitir processamento sem um limite configurado de imagens por lote,
  com uma opção explícita de “Sem limite” (considerar `0` como representação).
  Distinguir essa preferência dos limites técnicos de planos, seleção e operações;
  avaliar paginação/processamento incremental para não carregar tudo em memória.
- [ ] Permitir guardar originais sem um orçamento máximo configurado de tamanho,
  com uma opção explícita de “Sem limite” (considerar `0` como representação).
  Manter a reserva mínima de espaço livre, a proteção de recuperação e a retenção
  por idade como regras independentes. “Sem limite” não significa espaço infinito.

### Compatibilidade e apresentação

- Não reinterpretar valores existentes silenciosamente; definir e testar os
  contratos de configuração/API, UI e automação antes de alterar a semântica.
- Mostrar claramente a unidade, o alcance e o significado de zero em cada campo.
- O limite de imagens não deve ser confundido com paralelismo ou tamanho da página.
- Registrar que, em 0.4.0.0, zero é inválido para lote e orçamento de originais.
  Só a retenção em dias aceita zero como ausência de expiração por idade.
- Valores altos não eliminam os tetos atuais: até 1.000 candidatas no lote manual
  e até 10.000 no lote da automação. O orçamento de originais também protege a
  cópia temporária necessária à conversão, mesmo sem retenção posterior.

Essas pendências não autorizam mudanças de código, nova release ou alterações em
produção. O comportamento publicado de 0.4.0.0 permanece inalterado.
