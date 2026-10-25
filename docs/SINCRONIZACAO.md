# Sincronização PC ↔ celular

O zFinancas é offline por decisão de produto: não existe servidor nem conta em nuvem.
A sincronização troca **um arquivo** (`.zfsync`, JSON legível) entre os aparelhos.

## Como usar

1. **No aparelho de origem**: `Sincronização -> Exportar pacote`.

   | Plataforma | O que acontece |
   | --- | --- |
   | **Windows** | abre o diálogo nativo **"Salvar como"** (nome e filtro `.zfsync` já preenchidos); você escolhe a pasta |
   | **Android** | abre o **"Salvar como"** do sistema (Storage Access Framework) - sem permissão de armazenamento |

   Cancelar não grava nada.
2. Transfira o arquivo (cabo, e-mail, Drive pessoal, rede local...).
3. **No aparelho de destino**: `Sincronização -> Escolher arquivo e importar`.
4. Repita no sentido inverso para trazer as alterações do outro lado.

> A pasta do executável **não** é usada: o app nunca precisa de escrita onde foi instalado.

Pode importar o mesmo arquivo quantas vezes quiser: a operação é idempotente.

## Modelo de dados

| Campo | Papel |
| --- | --- |
| `UUID` | identidade global do registro (o `ID` é só local) |
| `ATUALIZADOEM` | carimbo UTC da última alteração |
| `EXCLUIDO` | lápide: propaga exclusões sem apagar o histórico |

Referências entre entidades usam **UUID**, nunca `ID`:

- `LancamentoSync.ContaUuid` → conta corrente
- `LancamentoSync.CentroCustoUuid` → centro de custo
- `VinculoSync.UsuarioUuid` + `ContaUuid` → vínculo

## Algoritmo de mesclagem

```
para cada tipo (contas → centros → usuários → vínculos → lançamentos):
    indexa os registros locais por UUID (incluindo excluídos)
    para cada registro remoto:
        se não existe localmente:
            se for lápide → ignora
            senão → insere (resolvendo FKs por UUID) e mapeia o novo ID local
        se existe:
            se remoto.AtualizadoEm > local.AtualizadoEm → sobrescreve campos + Excluido
            senão → mantém o local
```

Ordem importa: contas e centros primeiro, porque os lançamentos dependem deles.

### Por que `AGRUPADORUUID` existe

`AGRUPADOR` é um incremental local — dois aparelhos podem gerar o mesmo número para séries
diferentes. O `AGRUPADORUUID` (gerado na criação da série) identifica a série globalmente,
então quitação e exclusão de futuras continuam corretas depois de sincronizar.

### Vínculos usuário ↔ conta

`USUARIOCONTACORRENTE` ganhou `ATUALIZADOEM` e `EXCLUIDO`. Assim, “desvinculei a conta X do
usuário Y” viaja como lápide e não é desfeita pelo próximo pacote.

### Cenários práticos (duas pontas)

**Cada aparelho cria lançamentos diferentes.** Nada se perde: o pacote é a base inteira, então
“não existe do outro lado” **nunca** significa “foi excluído”.

| Passo | PC | Celular |
| --- | --- | --- |
| estado inicial | lançamento `A` | lançamento `B` |
| importa pacote do PC | — | recebe `A`, mantém `B` |
| importa pacote do celular | recebe `B`, mantém `A` | — |
| resultado | `A` + `B` | `A` + `B` |

São dois pacotes (um de cada lado) para ficar 100% convergente.

**O mesmo lançamento alterado nos dois lados.** Vence o `ATUALIZADOEM` mais recente —
a versão inteira do registro, não campo a campo.

**Exclusão de um lado.** Viaja como lápide (`EXCLUIDO = 1`) e apaga no outro. Se o outro lado
tiver editado depois da exclusão, a edição vence e o registro **volta** (a exclusão é antiga).

**Mesmo lançamento digitado nos dois aparelhos** (duas compras iguais, por exemplo): são UUIDs
diferentes, então ficam **os dois** — não há deduplicação automática.

Esses três casos estão cobertos por teste automatizado em
`tests/zFinancas.Tests/SincronizacaoDuasPontasTests.cs`.

## Segurança e privacidade

- O pacote contém os **hashes** de senha (nunca senhas em texto claro), para que o mesmo
  usuário entre nos dois aparelhos com a mesma senha.
- Os **códigos de recuperação não** são sincronizados: cada aparelho mantém o seu.
- O áudio da captura de voz nunca é gravado nem enviado: só o texto reconhecido é usado,
  e ele fica no aparelho.
- Trate o arquivo como documento sensível: quem tiver acesso ao pacote pode tentar quebrar
  hashes fracos fora do app. Transfira por canal confiável e apague cópias temporárias.

## Bases criadas em separado

Se os dois aparelhos foram configurados do zero, cada um criou a sua "Conta Principal" (e
talvez o mesmo centro de custo ou usuário) com **UUIDs diferentes**. Como DESCRICAO/NOME
são únicos, a importação trata nomes iguais como a **mesma entidade**:

- mantém o registro local e **adota o UUID remoto** (a partir daí os dois se reconhecem);
- liga os lançamentos do outro aparelho nesse registro;
- mostra o aviso *"...existia nos dois aparelhos: unificado"* na tela de sincronização.

Sem isso, a importação falhava com SQLite Error 19: UNIQUE constraint failed.

## Limitações conhecidas

- A transferência é manual (um arquivo por vez).
- Lápides não são expurgadas (o histórico de exclusões cresce devagar, sem impacto prático
  para um app pessoal).
- O relógio dos aparelhos deve estar razoavelmente correto: o desempate usa `ATUALIZADOEM`.

## Próximos passos sugeridos

1. Transferência direta por rede local (mDNS + HTTP) reaproveitando o mesmo pacote.
2. Sincronização automática ao detectar o outro aparelho na mesma rede.
3. Exportação incremental (apenas registros alterados desde a última sincronização).
