# Arquitetura do zFinancas

## Princípios

1. **Dependências apontam para dentro**: `App (MAUI) → Data (SQLite) → Core (domínio)`.
   O Core não referencia MAUI, SQLite ou JSON — apenas a BCL.
2. **Regra de negócio em um lugar só**: cálculo de saldo, geração de parcelas, quitação,
   interpretação de voz e validações vivem no Core e são cobertos por testes.
3. **Interfaces no Core, implementação na borda**: repositórios e serviços de infraestrutura
   (`IArmazenamentoCompartilhado`, `IInfoDispositivo`, `IRelogio`) são contratos do domínio.
4. **Composição explícita**: todo o grafo de objetos é montado em `MauiProgram` (DI).
5. **Erros como exceção de domínio**: `DominioException` carrega mensagem pronta para o
   usuário; a UI nunca mostra stack trace.

## Camadas

### `zFinancas.Core` (net9.0, zero dependências)

```
Dominio/
  Entidades/      Usuario, ContaCorrente, CentroCusto, Lancamento, UsuarioContaCorrente
  Enums/          PerfilUsuario (ADM/USR), Recorrencia
  Servicos/       GeradorDeParcelas, CalculadoraDeSaldo, PoliticaDeQuitacao
  Voz/            InterpretadorDeVoz (frase falada → rascunho de lançamento)
  Excecoes/       DominioException
Seguranca/        SenhaHasher (PBKDF2), GeradorCodigoRecuperacao
Abstracoes/       I*Repositorio, IUnidadeDeTrabalho, ISessaoAtual, IRelogio,
                  ISerializadorJson, IArmazenamentoCompartilhado, IInfoDispositivo
Aplicacao/
  Servicos/       PrimeiroAcesso, Autenticacao, Usuario, ContaCorrente, CentroCusto,
                  Lancamento, Extrato, Dashboard, Sincronizacao
  Modelos/        DTOs (requisições, resumos, pacote de sincronização)
  Validacao/      ValidadorSenha
  SessaoAtual.cs  estado da sessão (perfil + contas permitidas)
```

Entidades são ricas onde importa: `Validar()`, `Compensar()`, `Descompensar()`,
`MarcarExclusao()` (lápide), `ChaveDaSerie`, `EstaVencido()`.

### `zFinancas.Data` (net9.0, Microsoft.Data.Sqlite)

- **`ContextoBanco`**: dona da conexão, aplica `WAL`, `foreign_keys` e `busy_timeout`;
  serializa o acesso com semáforo e oferece a transação corrente.
- **`MigradorBanco`**: esquema versionado por `PRAGMA user_version` (script SQL idempotente).
- **Repositórios**: SQL explícito, sem ORM; datas em ISO-8601 (preserva `DateTimeKind`) e
  `decimal` como texto (sem perda de precisão).
- **`UnidadeDeTrabalho`**: abre/commita transação; se já houver uma aberta no fluxo, apenas
  participa dela.

> **Reentrância do bloqueio**: um `AsyncLocal<int>` de profundidade não funciona aqui — o
> valor gravado dentro do método assíncrono não volta ao contexto do chamador. A solução é
> um **token de escopo**: quem adquire o semáforo publica o token
> (`AssumirEscopoNesteFluxo()`) e as chamadas aninhadas o reconhecem por identidade.

### `zFinancas.App` (MAUI: `net9.0-android` + `net9.0-windows`)

```
AppShell (FlyoutPage) ── Flyout: MenuPage → MenuLateral (menu do app)
                      └─ Detail: NavigationPage (seção atual)
Pages/       Login, PrimeiroAcesso, RecuperarSenha, Dashboard, Lancamentos,
             LancamentoEdicao, Extrato, Contas, CentrosCusto, Usuarios,
             UsuarioEdicao, Sincronizacao, Configuracoes
ViewModels/  um por página + BaseViewModel/ComandoAssincrono (MVVM próprio, sem plugins)
Servicos/    Navegação, diálogos, biometria (Android), voz (SpeechToText),
             compartilhamento, preferências, inicialização do banco, registro de falhas
Resources/   Colors.xaml + Styles.xaml (design system)
Controles/   MenuLateral (menu), MenuPage (hospeda o menu)
```

- **MVVM sem dependências**: `BaseViewModel` (INotifyPropertyChanged + estado `Ocupado` +
  `ExecutarAsync` que traduz `DominioException` em diálogo) e comandos assíncronos com
  proteção contra duplo clique.
- **Coleções de exibição**: `LancamentoVm`, `ContaCartaoVm`, `UsuarioVm` etc. carregam
  valores já formatados (pt-BR) e cores, mantendo o XAML simples.
- **Navegação**: `ServicoNavegacao` centraliza troca de seção (recria a `NavigationPage` do
  detalhe), push/pop de formulários e a troca de raiz login ↔ app.
- **Falhas**: `RegistroDeFalhas` grava `falhas.log` na pasta do app (offline, sem telemetria).

## Lançamento por voz

```
Dashboard / LancamentoEdicao (botão Falar)
   └─ IServicoVoz (App) ── SpeechToText do .NET MAUI Community Toolkit
        ├─ Windows: reconhecedor do sistema (Windows.Media.SpeechRecognition)
        └─ Android: RecognizerIntent (Google), permissão RECORD_AUDIO
   └─ InterpretadorDeVoz (Core, puro)
        ├─ tipo (gastei/recebi)  ├─ valor (dígitos + por extenso)  ├─ data (hoje/ontem)
        ├─ centro de custo (nome ou palavra-chave)                 └─ descrição
   └─ LancamentoFalado → formulário pré-preenchido (usuário confirma e salva)
```

A separação é proposital: o reconhecimento (que depende de aparelho e permissão) fica na
borda; a **interpretação da frase** fica no Core, é determinística e tem 12 testes.
`ZFINANCAS_VOZ` (Debug) injeta um texto no lugar do microfone, permitindo exercitar a
interface sem voz.

## Fluxo de inicialização

```
App.CreateWindow
  └─ NavigationPage(LoginPage)  (sem barra de navegação)
       └─ LoginPage.OnAppearing → LoginViewModel.InicializarAsync
            1. ServicoInicializacao.GarantirProntoAsync  (migração do banco)
            2. PrimeiroAcessoService.PrecisaConfigurarAsync?
                 sim → push PrimeiroAcessoPage
            3. Listar usuários ativos + último usuário usado
            4. Biometria disponível? oferece o atalho
            5. Login (senha ou biometria) → AutenticacaoService monta a sessão
                 └─ ServicoNavegacao.AbrirAplicacaoAsync → AppShell (FlyoutPage)
```

## Persistência

Local do banco: `FileSystem.AppDataDirectory/zfinancas.db` (ou `ZFINANCAS_DB`).
Recursos: `PRAGMA journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout=5000`,
`foreign_keys=ON`. Um único arquivo, fácil de copiar/backupear.

## Tratamento de erros

| Origem | Comportamento |
| --- | --- |
| Regra de negócio (`DominioException`) | diálogo com a mensagem, sem stack trace |
| Falha inesperada em comando | capturada pelo comando, registrada em `falhas.log` |
| Falha não tratada na UI | handler WinUI/AppDomain grava `falhas.log` |
| Falha de voz (microfone/permissão) | mensagem amigável; o app continua utilizável |

## Decisões de UI

- Tema escuro fixo (`UserAppTheme = Dark`) para manter a identidade no Windows e Android.
- Ícones por emoji: zero dependências de fonte/asset, funciona nas duas plataformas.
- Cartões com gradiente derivado da cor da conta (`ContaCartaoVm.Gradiente`).
- `MaximumWidthRequest` nas páginas para não esticar demais em monitores grandes.
- Emoji em **Label** (não em Button): no Windows o glyph dentro do botão perde qualidade.
