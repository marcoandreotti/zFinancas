# Migração do "finanças" antigo (Access)

Converte a base `.mdb` do app antigo em uma base do zFinancas + um pacote de
sincronização para levar ao celular.

```powershell
.\scripts\importar-access.ps1 -Mdb "C:\Users\marco\Downloads\Financas.mdb"
```

Saída em `.tmp\migracao`:

| Arquivo | Uso |
| --- | --- |
| `zfinancas-migrado.db` | banco pronto: abra o app com `$env:ZFINANCAS_DB = "<caminho>"` |
| `migracao.zfsync` | importe no celular por **Sincronização → Escolher arquivo e importar** |
| `access-json\` | retrato das tabelas do Access (conferência) |

> Por que existe um script de dump? O driver ACE do Access só está disponível em
> **32 bits**; o script usa o PowerShell 32-bit para ler o `.mdb` e um utilitário .NET
> (`tools/zFinancas.ImportadorAccess`) para converter — assim o resto fica no ecossistema
> normal do projeto.

## De → para

| Access | zFinancas | Observação |
| --- | --- | --- |
| `Banco` + `ContaCorrente` | conta corrente | descrição vira `"<Banco> <número/conta>"` (ex.: `NU Bank MA-4024682-7`), porque o modelo novo tem uma descrição só |
| `ContaCorrente.padrao` | `Principal` | a marcada como padrão vira a conta principal |
| `ContaCorrente.exibir` | `Ativo` | todas as contas são importadas; só ficam **habilitadas** as que tinham `exibir` ativo (o Extrato lista as inativas também, para o histórico não sumir) |
| `CentroDeCusto` | centro de custo | nome normalizado (`DIVERSOS` → `Diversos`) e ícone escolhido pelo significado do nome |
| `CentroDeCusto.padrao` | `Principal` | |
| `CentroDeCusto.image` | — | as imagens do Access não são importadas; o app usa emoji |
| `Lancamentos` | lançamento | ver abaixo |
| `Usuario` + `UsuarioConta` | usuário + vínculos | |

## Lançamentos

| Campo | Regra |
| --- | --- |
| `historico` | `Descricao` (limpa, com correção de acentos do Access) |
| `valor` | mantido **com sinal** (negativo = despesa) |
| `dataCompensado` | é a data que vale: quando compensado, `DataVencimento` e `DataLancamento` recebem a data da compensação (é ela que ordena a lista e alimenta o saldo no app antigo) |
| `dataVencimento` | usada só para o que **não** foi compensado |
| `parcela` / `totalParcelas` | `Parcela` / `QuantidadeParcelas` |
| `agrupador` | vira `Agrupador` + um `AgrupadorUuid` por série (quitação e exclusão de parcelas continuam funcionando depois de sincronizar) |

Registros com **valor zero** (253 no total) são ignorados: o domínio do zFinancas não aceita
lançamento de valor zero e eles não afetam saldo.

## Conferência

O utilitário soma os valores do Access e os importados e compara no final:

```
Soma dos valores no Access : -46.836,25
Soma dos valores importados: -46.836,25
OK: totais batem.
```

## Usuário e senha

O Access guardava a senha em texto (`123`, no caso). O zFinancas exige mínimo de 4 caracteres,
então senhas curtas são trocadas por `123456` (e o script avisa). Troque a senha depois:
**Usuários → ⋯ → Redefinir senha**.

## Depois de migrar

1. Abra o PC com o banco migrado e confira o dashboard e o extrato.
2. Se estiver tudo certo, esse passa a ser o banco oficial (copie para a pasta mostrada em
   `Configurações → BANCO DE DADOS`, com o nome `zfinancas.db`, e apague `-wal`/`-shm`).
3. No celular, importe o `migracao.zfsync` — as duas bases ficam iguais.
