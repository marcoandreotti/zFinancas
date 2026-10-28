# Regras de negócio do zFinancas

Documento de referência do comportamento implementado em `zFinancas.Core` — cada regra
abaixo tem teste automatizado correspondente.

## 1. Usuários e acesso

| Regra | Onde |
| --- | --- |
| Nome obrigatório, até 30 caracteres, único (sem diferenciar maiúsculas) | `Usuario.Validar`, `UsuarioService` |
| Senha de 4 a 50 caracteres | `ValidadorSenha` |
| Perfil `ADM` gerencia tudo; perfil `USR` opera apenas as contas vinculadas | `AutenticacaoService`, `SessaoAtual` |
| Usuário inativo não autentica e não acessa contas | `AutenticacaoService.ObterUsuarioAtivoAsync` |
| O sistema nunca fica sem um ADM ativo | `UsuarioService.GarantirOutroAdministradorAtivoAsync` |
| Senha nunca é gravada em claro | `SenhaHasher` (PBKDF2-SHA256 + salt + tempo constante) |
| Código de recuperação é de uso único e só existe como hash | `AutenticacaoService.RecuperarSenhaAsync` |

### Fluxo do primeiro acesso

1. Se não existe usuário, o app abre o cadastro inicial.
2. É criado o ADM, uma conta corrente principal (`Conta Principal`) e um centro de custo
   principal (`Geral`) — o app já nasce usável.
3. Um código de recuperação é exibido uma única vez.

## 2. Contas correntes e centros de custo

- Descrição obrigatória (até 50 caracteres) e única.
- Existe **no máximo uma** conta principal ativa e **no máximo um** centro de custo
  principal ativo (comando SQL atômico em `DefinirComoPrincipalAsync`).
- Conta/centro inativo: some das seleções e não pode ser principal, mas o histórico de
  lançamentos permanece.
- É obrigatório manter ao menos uma conta ativa e um centro de custo ativo.

## 3. Lançamentos

### 3.1 Sinal e valor

- `VALOR < 0` → despesa; `VALOR > 0` → recebimento; `VALOR = 0` é inválido.
- Na UI o usuário digita o valor positivo e escolhe **Despesa** ou **Recebimento**; o sinal
  é aplicado ao salvar.

### 3.2 Recorrência e parcelas

| Recorrência | Intervalo | Exemplo (1ª parcela 31/01/2025) |
| --- | --- | --- |
| Não repetir | — | 1 parcela |
| Semanal | +7 dias | 07/02, 14/02… |
| Quinzenal | +15 dias | 15/02, 02/03… |
| Mensal | +1 mês (`AddMonths`) | 28/02, 31/03 (sem desvio acumulado) |
| Anual | +1 ano (`AddYears`) | 31/01/2026… |

- `PARCELA` vai de 1 a `QUANTIDADEPARCELAS`; o rótulo exibido é `3/10`.
- Série recebe `AGRUPADOR` (incremental, local) **e** `AGRUPADORUUID` (global). Operações de
  série usam o UUID quando existir, garantindo o mesmo agrupamento após sincronizar.
- Lançamento avulso: `AGRUPADOR = 0`, `AGRUPADORUUID = null`, `PARCELA = 1`.
- Limite de 360 parcelas por série.
- “Já compensado” na criação baixa **apenas a primeira parcela** (menor vencimento).

### 3.3 Compensação

- `COMPENSADO = true` exige `DATACOMPENSADO`; `COMPENSADO = false` exige data nula.
- A data da compensação é **escolhida na hora** (padrão: o vencimento); há atalho para
  “hoje”. O saldo compensado considera essa data, não a data em que o registro foi feito.
- Lançamento compensado **não pode ser alterado nem excluído** — descompense primeiro.
- `Descompensar` zera a data de compensação.

### 3.4 Exclusão

- Exclusão é **lógica** (`EXCLUIDO = 1`, lápide), necessária para propagar remoções na
  sincronização.
- Na lista de lançamentos as ações de exclusão são: **Excluir esta parcela**,
  **Excluir esta e as seguintes em aberto** e **Escolher parcelas para excluir…**
  (painel com caixas de seleção e total em tempo real; só parcelas pendentes aparecem).
- O painel exclui todas as escolhidas em uma única transação: se alguma estiver
  compensada, nada é excluído (tudo ou nada).

### 3.5 Quitação de parcelas

Regra pedida no projeto: *“ao quitar, os lançamentos são excluídos e é lançado apenas um
registro com o valor da quitação”*.

0. A quitação começa por um **painel de seleção** com todas as parcelas em aberto da série
   (marcadas por padrão); o usuário desmarca o que não quer consolidar.
1. Só parcelas **não compensadas** podem ser quitadas.
2. Todas precisam ser da mesma conta corrente.
3. O valor total é a soma dos valores (sinal preservado).
4. As parcelas escolhidas são marcadas como excluídas.
5. Um único lançamento compensado é criado:
   `Descrição = "Quitação N parcelas - <descrição original>"`, `VALOR = total`,
   `DATACOMPENSADO = data da quitação`, `AGRUPADOR = 0`, `PARCELA = 0`,
   `QUANTIDADEPARCELAS = 1`, `OBSERVACAO` registrando a consolidação.

## 4. Saldos

`SaldoConta` de cada conta:

```
Compensado           = Σ valor (compensado = true)
NãoCompensado        = Σ valor (compensado = false)
NãoCompensadoVencido = Σ valor (pendente e vencimento < hoje)
Total                = Compensado + NãoCompensado
```

No dashboard, o cartão de cada conta e o total geral usam exatamente esses campos.

## 5. Extrato

Entrada: conta + período (`inicio`, `fim`).

1. **Linha do tempo = `DATAVENCIMENTO`**.
2. Entram no resultado:
   - lançamentos com vencimento dentro do período; **e**
   - lançamentos **não compensados** com vencimento anterior ao início
     (marcados como `ForaDoPeriodo`, exibidos com fundo âmbar e selo “Pendente anterior”).
3. `SaldoAnterior` = soma **apenas dos compensados** com vencimento anterior ao período
   (os pendentes ficam de fora — regra explícita do projeto).
4. Cada linha acumula `SaldoApos` (projetado, inclui pendentes) e `SaldoCompensadoApos`
   (apenas compensados).
5. Cores: **valor negativo em vermelho**, **valor positivo em azul**.
6. Totais do período: entradas (soma dos positivos) e saídas (soma dos negativos).

## 5.1 Seleção de contas (exibição/conciliação)

- Lançamentos e Extrato aceitam **várias contas** selecionadas; sem seleção, valem todas
  as contas permitidas ao usuário.
- **Extrato com mais de uma conta** é consolidado:
  - `SaldoAnterior`, `SaldoFinal`, `SaldoCompensado`, entradas e saídas são **somados**;
  - os lançamentos são ordenados por `DATAVENCIMENTO` (empate por `ID`) e o saldo
    consolidado acumula sobre essa ordem;
  - a regra de “pendente anterior ao período” continua valendo por conta.

## 6. Dashboard

- Carrossel com todas as contas permitidas ao usuário (cor, ícone, principal).
- Saldo consolidado: compensado, a compensar, vencido e total.
- Entradas e saídas da janela de 7 dias.
- Lista dos lançamentos dos últimos 7 dias (por data de lançamento), com selo de situação.

## 7. Sincronização

- Identidade por `UUID`; conflito por `ATUALIZADOEM` (UTC) — “última escrita vence”.
- Lápides (`EXCLUIDO`) impedem que um registro apagado “ressuscite”.
- Importação idempotente: reimportar o mesmo pacote não duplica nada.
- Vínculos usuário↔conta também são sincronizados (com `ATUALIZADOEM`/`EXCLUIDO`).
- Códigos de recuperação **não** são sincronizados (cada aparelho mantém o seu).

## 8. Lançamento por voz

O texto falado é convertido em um **rascunho** (`LancamentoFalado`) — nunca grava direto.
Regras do `InterpretadorDeVoz`:

1. **Tipo**: palavras de despesa (“gastei”, “paguei”, “comprei”, “gasto”, “despesa”) ou de
   recebimento (“recebi”, “ganhei”, “entrou”, “vendi”, “salário”). Sem palavra de
   recebimento, assume despesa.
2. **Valor**: primeiro número em dígitos (`32,50`, `1.234,56`, `1200`, `2 mil`); se não
   houver, soma o valor por extenso na ordem falada (“cento e vinte e cinco” = 125).
   Sem valor, a captura falha com mensagem orientando o que dizer.
3. **Data**: “hoje” (padrão), “ontem” (−1 dia), “anteontem” (−2), “amanhã” (+1).
4. **Situação**: “vou pagar”, “vou receber”, “vence”, “pendente”, “agendar” → **não
   compensado**; caso contrário, já compensado (a pessoa registra algo que fez).
5. **Centro de custo**: primeiro tenta o **nome cadastrado** dito na frase; depois usa
   palavras-chave do dia a dia (mercado→Alimentação, gasolina→Transporte,
   farmácia→Saúde, aluguel→Moradia, cinema→Lazer, curso→Educação…). Sem correspondência,
   mantém o centro já selecionado na tela.
6. **Conta**: se o nome da conta aparecer na frase, ela é usada; senão a principal.
7. **Descrição**: o que sobra da frase (sem palavras de ligação, valor e verbos), com a
   primeira letra maiúscula e limitada a 50 caracteres.
8. Se nada sobrar, usa o nome do centro de custo; se ainda faltar, “Lançamento por voz”.
9. Acentos e pontuação são normalizados antes da comparação: “farmácia” = “farmacia”.
10. O rascunho sempre passa pelo formulário — o usuário revisa e confirma.

## 9. Invariantes verificadas nos testes

- 31/01 mensal → 28/02 → 31/03 (sem desvio acumulado).
- Quitação de 2 parcelas de −100 gera um único registro de −200 compensado.
- Extrato com pendente anterior: saldo anterior ignora o pendente, mas a linha aparece
  destacada e o saldo linha a linha continua correto.
- USR vinculado a uma conta não acessa outra (erro de domínio).
- Último ADM ativo não pode ser desativado.
- Importar o mesmo pacote duas vezes → `Total = 0` na segunda vez.
- Alteração feita no “celular” volta para o “PC” (incluindo `DATACOMPENSADO`).
- Voz: “gastei 32,50 no mercado” → despesa 32,50 • Mercado • Alimentação;
  “paguei cinquenta reais de aluguel ontem” → 50 em Moradia com data de ontem;
  “recebi 2 mil de bônus” → recebimento 2.000; “vou pagar 300 de condomínio” → pendente.


## Séries de parcelas (recorrência)

Cada parcela guarda a **recorrência** da série (coluna `RECORRENCIA`). Lançamentos antigos ou
importados podem estar sem ela (`NULL`); nesse caso a edição **exige escolher** a repetição
antes de salvar — sem isso não há como espaçar novas parcelas.

| Ação | Regra |
| --- | --- |
| **Propagar valor** | aplica o novo valor somente nas parcelas **em aberto**; as compensadas ficam como estão (dinheiro que já se moveu) |
| **Adicionar parcelas** | informa-se a **nova quantidade total**; todas passam a `X/novo total` e as novas são criadas na sequência, espaçadas pela recorrência |
| **Reduzir quantidade** | não é feito por aqui: exclua as parcelas que sobram (mantém o histórico coerente) |
| **Totais da série** | `Total pago` (compensadas), `Total à pagar` (em aberto) e `Total geral` — no extrato, duplo clique na linha ou no botão **Σ** |