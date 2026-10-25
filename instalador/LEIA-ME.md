# Instalação no Windows

Duas formas, dependendo de como você quer distribuir:

| Forma | Precisa de admin? | Quando usar |
| --- | --- | --- |
| **Portátil (ZIP)** | não | testes, pendrive, cópia rápida para outra máquina |
| **Instalador (Inno Setup)** | opcional | entrega para outra pessoa, com atalho e desinstalador |

Em ambas, o app **não escreve na pasta de instalação**: banco, logs, backups e pacotes de
sincronização ficam em `%LOCALAPPDATA%` (ou na pasta escolhida em Configurações). Por isso
funciona em `Program Files` sem problemas.

## 1. Publicar o aplicativo

```powershell
.\build.ps1 -Publish
```

Gera:

```
.tmp\publicacao\win-x64\             (self-contained: não exige .NET instalado)
.tmp\publicacao\zFinancas-win-x64.zip
```

## 2. Portátil (ZIP)

Basta descompactar `zFinancas-win-x64.zip` em qualquer pasta e executar
`zFinancas.App.exe`. Não precisa instalar nada.

> Se o app não conseguir gravar em algum lugar (por exemplo, rodando de uma pasta sem
> permissão), ele abre o banco em uma pasta alternativa e informa isso em Configurações.

## 3. Instalador (Inno Setup)

1. Instale o [Inno Setup 6](https://jrsoftware.org/isdl.php).
2. Compile o script:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" instalador\zFinancas.iss
```

3. O instalador sai em `instalador\saida\zFinancas-Setup-1.0.0.exe`.

O que o instalador faz:

- copia o conteúdo de `.tmp\publicacao\win-x64` para `{app}`;
- cria atalhos no Menu Iniciar e (opcional) na Área de Trabalho;
- registra desinstalador;
- **não apaga** os dados do usuário ao desinstalar (banco e backups permanecem).

Para instalar para todos os usuários, edite `PrivilegesRequired=admin` no
`instalador\zFinancas.iss`.

## 4. Alternativa: MSIX (Microsoft Store)

O projeto MAUI também pode ser empacotado como MSIX:

```powershell
dotnet publish src\zFinancas.App -f net9.0-windows10.0.19041.0 -c Release `
  -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false
```

O MSIX exige um certificado de assinatura para instalar fora da Store. Para distribuição
interna, o Inno Setup costuma ser mais simples.
