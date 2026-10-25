# PR: ciclo 1 — biometria, valor, voz, identidade visual e sincronização

> Este arquivo serve de descrição do Pull Request `develop` → `main`.
> Copie o conteúdo abaixo (a partir de "## Resumo") na descrição do PR.

## Resumo

Ciclo de correções e evoluções fechado com testes no **PC (Windows)** e em **celular Android
real**. O foco foi tornar o app utilizável no dia a dia: entrar com biometria, digitar valores
sem ambiguidade, capturar lançamento por voz e sincronizar duas bases criadas separadamente.

## O que mudou

### Biometria (Android)

- `USE_BIOMETRIC` e `USE_FINGERPRINT` no manifest — sem elas o `CanAuthenticate()` nunca
  retornava sucesso e a opção sequer aparecia.
- `BiometricPrompt` nativo com **botão "Cancelar"** obrigatório (sem ele o Android lança
  `Negative text must be set and non-empty`).
- Chamada pela sobrecarga **sem `CryptoObject`** (`Must supply a crypto object` ao passar `null`).
- Retorno tipado: `ResultadoBiometria` (Sucesso / Cancelado / Falhou / Indisponível), com
  mensagens específicas no login e nas configurações.
- Login pede a biometria **automaticamente** — fora do `ExecutarAsync` do carregamento, que
  descartava a chamada por causa da guarda de "ocupado".

### Campo de valor (PC e Android)

- **Máscara de moeda**: dígitos entram como centavos (`3250` → `32,50`, `150000` → `1.500,00`),
  eliminando a ambiguidade entre vírgula e ponto.
- Escrita no `EditText` feita no `TextChanged` com o handler desconectado e o cursor
  reposicionado depois. A causa do crash era o **AndroidX Emoji2**, desativado no manifest.

### Lançamento por voz

- Descrição sem restos de moeda: `reias`, `mil`, `milhão`, `R$`, `r` e tokens de 1 letra
  descartados (`"recebimento pagamento mil reias"` → *Recebimento, R$ 1.000,00, "Pagamento"*).

### Identidade visual

- Ícone e splash a partir da arte do logotipo, com foreground para o adaptive icon.
- Abertura animada (`AberturaPage`): fade + escala, mínimo de 1,6 s, banco preparado em paralelo.
- Logotipo claro (texto remapeado para `#F4F7FF`) no login e na abertura; símbolo no dashboard.
- Ícone de microfone nos botões de voz.

### Layout celular

- Extrato: título sem quebra, `↻` para atualizar, contas em linha própria, resumo 2×2, `✓`
  para compensar.
- Lançamentos: título responsivo e `✓` na linha; dashboard com "Falar" + "+" compactos.
- Seleção de cor/ícone por `CollectionView` (o toque em `Border` não funcionava no Android).
- Listas de contas e centros recarregam depois de salvar.

### Sincronização

- Exportação pelo **diálogo nativo "Salvar como"**: `FileSaver` (CommunityToolkit) no Windows e
  Storage Access Framework no Android — nunca escreve na pasta do executável.
- **Unificação de registros homônimos**: bases criadas separadamente geram "Conta Principal"
  com UUIDs diferentes; agora a importação reconhece o mesmo nome, adota o UUID remoto e liga os
  lançamentos (antes: `SQLite Error 19: UNIQUE constraint failed: CONTACORRENTE.DESCRICAO`).
- `UPDATE` de contas, centros e usuários passou a gravar o `UUID` (necessário para a adoção).

## Testes

- **64 testes** no runner próprio (`.\build.ps1 -Test`), incluindo:
  - `SincronizacaoDuasPontasTests`: cada aparelho cria um lançamento e os dois terminam com os dois;
    conflito no mesmo lançamento (vence o mais recente); exclusão de um lado apaga no outro.
  - `SincronizacaoHomonimosTests`: bases independentes com conta/centro/usuário homônimos.
  - `VozTests`: frases reais faladas.
- Validação manual: Windows + Android (biometria, voz, valor, extrato, lançamentos, sincronização
  nos dois sentidos).

## Como testar

```powershell
.\build.ps1 -Test                        # 64 testes
.\build.ps1 -Demo -Run                   # desktop com dados de demonstração
.\build.ps1 -Target android              # APK
adb install -r src\zFinancas.App\bin\Debug\net9.0-android\br.com.zfinancas.app-Signed.apk
```

Roteiro sugerido:

1. Primeiro acesso no PC → dashboard com conta e centro principais.
2. Celular: instalar, ativar biometria em Configurações, sair e entrar pela digital.
3. Lançamento por voz; conferir descrição e valor.
4. Exportar no PC (diálogo "Salvar como") → importar no celular → exportar no celular →
   importar no PC: os dois lados com os mesmos lançamentos.

## Riscos e observações

- Biometria exige API 28+; abaixo disso a opção fica oculta.
- O pacote `.zfsync` contém hashes de senha: tratar como documento sensível.
- O relógio dos aparelhos precisa estar razoavelmente correto (desempate por `ATUALIZADOEM`).

## Fora do escopo (próximos ciclos)

- Backup das bases em todas as plataformas.
- Projeto de instalador para Windows.
