# build.ps1 é orquestra build, testes e execução do zFinancas
#
# Mantém caches (NuGet, template engine) e arquivos temporários dentro do repositório.
# Isso deixa o build reprodutível e evita depender de permissões do perfil do usuário
# (no Windows, o MakePri exige um %TEMP% gravável durante o empacotamento de recursos).
#
# Exemplos:
#   .\build.ps1                      # compila o app desktop (Windows)
#   .\build.ps1 -Target android      # compila o APK Android
#   .\build.ps1 -Test                # roda a suíte de testes
#   .\build.ps1 -Demo -Run           # cria banco de demonstração e abre o app nele
#   .\build.ps1 -Run                 # abre o app com o banco real do usuário
param(
    [ValidateSet('windows', 'android', 'all', 'none')]
    [string]$Target = 'windows',
    [switch]$Test,
    [switch]$Demo,
    [switch]$Run,
    [switch]$Clean,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# ----------------------------------------------------------------- ambiente isolado
$cliHome = Join-Path $root '.dotnetcli'
$temp = Join-Path $root '.tmp'
New-Item -ItemType Directory -Force -Path $cliHome, $temp | Out-Null

$env:DOTNET_CLI_HOME = $cliHome
$env:NUGET_PACKAGES = Join-Path $cliHome '.nuget\packages'
$env:TEMP = $temp
$env:TMP = $temp
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$appProject = Join-Path $root 'src\zFinancas.App\zFinancas.App.csproj'
$runnerProject = Join-Path $root 'tools\zFinancas.TestRunner\zFinancas.TestRunner.csproj'
$demoProject = Join-Path $root 'tools\zFinancas.Demo\zFinancas.Demo.csproj'
$demoDb = Join-Path $root '.tmp\zfinancas-demo.db'

function Invoke-DotNet {
    param([string[]]$Arguments)
    Write-Host "> dotnet $($Arguments -join ' ')" -ForegroundColor DarkGray
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Falha: dotnet $($Arguments -join ' ') (exit $LASTEXITCODE)" }
}

if ($Clean) {
    Get-ChildItem $root -Recurse -Directory -Include bin, obj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\\.dotnetcli\\' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Demo) {
    Invoke-DotNet @('run', '--project', $demoProject, '-v:q', '--', $demoDb)
}

if ($Target -in 'windows', 'all') {
    Invoke-DotNet @('build', $appProject, '-f', 'net9.0-windows10.0.19041.0', '-c', $Configuration, '-v:m')
}

if ($Target -in 'android', 'all') {
    $argumentos = @('build', $appProject, '-f', 'net9.0-android', '-c', $Configuration, '-v:m')

    # Localiza um SDK Android que tenha a plataforma exigida (android-35).
    $candidatos = @(
        $env:ANDROID_HOME,
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
        'C:\Program Files (x86)\Android\android-sdk'
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($candidato in $candidatos) {
        if (Test-Path (Join-Path $candidato 'platforms\android-35\android.jar')) {
            $argumentos += "-p:AndroidSdkDirectory=$candidato"
            Write-Host "> Android SDK: $candidato" -ForegroundColor DarkGray
            break
        }
    }

    Invoke-DotNet $argumentos
}

if ($Test) {
    # O runner próprio existe porque o vstest.console falha em ambientes restritos
    # (não consegue abrir o handle do processo pai). Em uma máquina normal,
    # "dotnet test tests\zFinancas.Tests" roda exatamente a mesma suíte.
    Invoke-DotNet @('run', '--project', $runnerProject, '-c', $Configuration, '-v:q')
}

if ($Run) {
    $exe = Join-Path $root "src\zFinancas.App\bin\$Configuration\net9.0-windows10.0.19041.0\win10-x64\zFinancas.App.exe"
    if (-not (Test-Path $exe)) { throw "Executável não encontrado: $exe (rode sem -Run primeiro)" }

    if ($Demo) {
        $env:ZFINANCAS_DB = $demoDb
        Write-Host "> usando banco de demonstração: $demoDb" -ForegroundColor DarkGray
    }

    Write-Host "> $exe" -ForegroundColor DarkGray
    & $exe
}

