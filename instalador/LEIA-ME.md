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

1. Instale o [Inno Setup 6 ou 7](https://jrsoftware.org/isdl.php).
2. Compile o script:

```powershell
# Inno Setup 6
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" instalador\zFinancas.iss

# Inno Setup 7
& "C:\Program Files\Inno Setup 7\ISCC.exe" instalador\zFinancas.iss
```

> `SetupIconFile` exige **.ico** (não .png): o arquivo usado é `instalador\zFinancas.ico`.

3. O instalador sai em `instalador\saida\zFinancas-Setup-1.0.0.exe`.

O que o instalador faz:

- copia o conteúdo de `.tmp\publicacao\win-x64` para `{app}`;
- cria atalhos no Menu Iniciar e (opcional) na Área de Trabalho;
- registra desinstalador;
- **não apaga** os dados do usuário ao desinstalar (banco e backups permanecem).

Para instalar para todos os usuários, edite `PrivilegesRequired=admin` no
`instalador\zFinancas.iss`.

## 4. Erro 0x800B010A ao instalar um MSIX (certificado)

Se você gerou o pacote **pelo Visual Studio** (*Publish → MSIX*), ele sai **sem assinatura**
(AppxPackageSigningEnabled=False) e o Windows recusa com:

> Não foi possível verificar o certificado de fornecedor deste pacote (0x800B010A)

Isso **não acontece** no caminho ZIP/Inno: lá não existe MSIX nem assinatura.

Se quiser mesmo MSIX, o pacote precisa ser assinado por um certificado em que a máquina
confie (cadeia até uma raiz confiável). Receita:

`powershell
# 1. crie o certificado e confie nele (PowerShell como ADMINISTRADOR)
.\instalador\criar-certificado.ps1

# 2. publique assinando (usa o thumbprint devolvido pelo script)
dotnet publish src\zFinancas.App -f net9.0-windows10.0.19041.0 -c Release 
  -p:WindowsPackageType=MSIX 
  -p:GenerateAppxPackageOnBuild=true 
  -p:AppxPackageSigningEnabled=true 
  -p:PackageCertificateThumbprint=<THUMBPRINT> 
  -p:PackageCertificateKeyFile=instalador\zFinancas.pfx 
  -p:PackageCertificatePassword=<SENHA>
`

Regras que costumam quebrar:

1. **Subject do certificado = Publisher do manifest.** O Package.appxmanifest usa
   Publisher="CN=User Name" (valor do template). Se mudar um, mude o outro.
2. O certificado precisa ser **Code Signing** com a chave privada (o script já faz).
3. A máquina que instala precisa confiar no certificado — LocalMachine\Root **e**
   LocalMachine\TrustedPeople (o script faz isso quando roda como administrador).
4. .pfx e .cer são **segredos**: já estão no .gitignore, não versione.

Assinatura manual de um pacote já gerado:

`powershell
signtool sign /fd SHA256 /a /f instalador\zFinancas.pfx /p <SENHA> caminho\zFinancas.msix
`

> **Recomendação:** para uso pessoal e para distribuir fora da Store, fique com o
> ZIP/instalador Inno — zero certificado, zero atrito.

## 5. Alternativa: MSIX (Microsoft Store)

O projeto MAUI também pode ser empacotado como MSIX:

```powershell
dotnet publish src\zFinancas.App -f net9.0-windows10.0.19041.0 -c Release `
  -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false
```

O MSIX exige um certificado de assinatura para instalar fora da Store. Para distribuição
interna, o Inno Setup costuma ser mais simples.
