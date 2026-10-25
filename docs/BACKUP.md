# Backup e restauração

O backup do zFinancas é uma **cópia completa do banco** (contas, centros, usuários, vínculos,
lançamentos, senhas em hash e configurações). Serve para trocar de máquina, guardar uma cópia
de segurança ou voltar atrás depois de um erro.

## Criar backup

`Configurações → BACKUP → Criar backup`

1. O app gera a cópia com `VACUUM INTO` — um arquivo SQLite íntegro, **sem** os arquivos
   `-wal`/`-shm`, gerado com o app aberto.
2. Abre o **“Salvar como”** do sistema (Windows: FileSaver; Android: SAF).
3. A tela mostra o caminho e a data do último backup.

Nome sugerido: `zfinancas-backup-AAAAMMDD-HHmm.db`.

## Restaurar backup

`Configurações → BACKUP → Restaurar backup`

1. Escolhe o arquivo `.db`.
2. O app valida **antes** de tocar em qualquer coisa:
   - cabeçalho `SQLite format 3`;
   - `PRAGMA integrity_check` = `ok`;
   - `PRAGMA user_version` **não pode ser maior** que a versão que o app entende
     (backup de uma versão futura é recusado com mensagem explicando).
3. Pede confirmação (os dados atuais serão substituídos).
4. Faz uma **cópia de segurança do banco atual** (`zfinancas-antes-restauracao-AAAAMMDD-HHmmss.db`,
   na mesma pasta do banco).
5. Fecha a conexão, troca o arquivo, apaga `-wal`/`-shm` antigos e reinicia a inicialização.
6. O app volta para a tela de entrada.

## Começar do zero

Configurações → BACKUP → Começar do zero apaga o banco deste aparelho e volta para o
primeiro acesso (útil para demonstrações e testes).

1. Pede **confirmação dupla**: um aviso e depois digitar ZERAR.
2. Guarda uma cópia do banco atual na pasta do banco:
   zfinancas-antes-de-zerar-AAAAMMDD-HHmmss.db (é restaurável pelo próprio app).
3. Apaga zfinancas.db, zfinancas.db-wal e zfinancas.db-shm.
4. Recria o esquema vazio e volta para a tela de primeiro acesso.

### Alternativa sem entrar no app

O publish (`.\build.ps1 -Publish`) gera `zfinancas-zerado.db` — o banco **só com o esquema**,
sem nenhum dado. Com o app fechado, substitua `zfinancas.db` por esse arquivo e apague
`zfinancas.db-wal` e `zfinancas.db-shm`. Ao abrir, o app cai no primeiro acesso.

> A limpeza de %TEMP% **não** tem relação com o banco: lá ficam só arquivos temporários do
> instalador.

## Onde os arquivos ficam

- **Banco**: pasta mostrada em `Configurações → BANCO DE DADOS` (por padrão em `%LOCALAPPDATA%`).
- **Backup**: onde você escolher no diálogo.
- **Cópia de segurança da restauração**: ao lado do banco.

O app **nunca** grava na pasta em que foi instalado — pode ficar em `Program Files`.

## O que não entra no backup

Nada fica de fora: o arquivo é o banco inteiro. Só lembre que ele contém **hashes de senha**;
trate como documento sensível.

## Backup × sincronização

| | Backup (`.db`) | Sincronização (`.zfsync`) |
| --- | --- | --- |
| Leva tudo | sim (inclusive preferências) | dados + usuários (sem códigos de recuperação) |
| Substitui a base | sim (restauração) | não (mescla por UUID) |
| Uso típico | segurança, troca de máquina | manter dois aparelhos iguais |

Para o dia a dia com PC + celular, use a sincronização. Para guardar uma cópia do passado,
use o backup.
