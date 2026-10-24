# Sincronização PC ↔ celular

O zFinancas é offline por decisão de produto: não existe servidor nem conta em nuvem.
A sincronização troca **um arquivo** (`.zfsync`, JSON legível) entre os aparelhos.

## Como usar

1. **No aparelho de origem**: `Sincronização → Exportar pacote de sincronização`.
   O arquivo é gravado em `<AppData>/Sincronizacao/zfinancas-AAAAMMDD-HHmm.zfsync` e o
   compartilhamento nativo do sistema é aberto quando disponível.
2. Transfira o arquivo (cabo, e-mail, drive pessoal, rede local…).
3. **No aparelho de destino**: `Sincronização → Escolher arquivo e importar`.
4. Repita no sentido inverso para trazer as alterações do outro lado.

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

## Segurança e privacidade

- O pacote contém os **hashes** de senha (nunca senhas em texto claro), para que o mesmo
  usuário entre nos dois aparelhos com a mesma senha.
- Os **códigos de recuperação não** são sincronizados: cada aparelho mantém o seu.
- O áudio da captura de voz nunca é gravado nem enviado: só o texto reconhecido é usado,
  e ele fica no aparelho.
- Trate o arquivo como documento sensível: quem tiver acesso ao pacote pode tentar quebrar
  hashes fracos fora do app. Transfira por canal confiável e apague cópias temporárias.

## Limitações conhecidas

- A transferência é manual (um arquivo por vez).
- Lápides não são expurgadas (o histórico de exclusões cresce devagar, sem impacto prático
  para um app pessoal).
- O relógio dos aparelhos deve estar razoavelmente correto: o desempate usa `ATUALIZADOEM`.

## Próximos passos sugeridos

1. Transferência direta por rede local (mDNS + HTTP) reaproveitando o mesmo pacote.
2. Sincronização automática ao detectar o outro aparelho na mesma rede.
3. Exportação incremental (apenas registros alterados desde a última sincronização).
