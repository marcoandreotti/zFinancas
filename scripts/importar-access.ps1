# ---------------------------------------------------------------------------
# Importa a base do "finanças" antigo (Access .mdb) para o zFinancas.
#
# Uso (na pasta do projeto):
#   .\scripts\importar-access.ps1 -Mdb "C:\caminho\Financas.mdb"
#   .\scripts\importar-access.ps1 -Mdb "..." -Saida "D:\migracao"
#
# O que faz:
#   1. lê o .mdb com o driver ACE (só existe em 32 bits) e exporta JSON;
#   2. converte para um banco zFinancas (SQLite) com todo o histórico;
#   3. gera também um pacote .zfsync, para importar no celular pela tela
#      de Sincronização.
#
# Saída (por padrão em .tmp\migracao):
#   zfinancas-migrado.db   -> abra com o app (ZFINANCAS_DB=...)
#   migracao.zfsync        -> importe no outro aparelho
# ---------------------------------------------------------------------------
param(
    [Parameter(Mandatory = $true)]
    [string]$Mdb,

    [string]$Saida = ""
)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot

if (-not (Test-Path $Mdb)) { throw "Banco Access não encontrado: $Mdb" }
if ([string]::IsNullOrWhiteSpace($Saida)) { $Saida = Join-Path $raiz '.tmp\migracao' }

New-Item -ItemType Directory -Force $Saida | Out-Null
$json = Join-Path $Saida 'access-json'
Remove-Item $json -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $json | Out-Null

$banco = Join-Path $Saida 'zfinancas-migrado.db'
$pacote = Join-Path $Saida 'migracao.zfsync'

# ------------------------------------------------------------------ ambiente
$env:DOTNET_CLI_HOME = Join-Path $raiz '.dotnetcli'
$env:NUGET_PACKAGES = Join-Path $raiz '.dotnetcli\.nuget\packages'
$env:TEMP = Join-Path $raiz '.tmp'
$env:TMP = $env:TEMP
$env:DOTNET_NOLOGO = '1'

Write-Host "> lendo $Mdb" -ForegroundColor Cyan

# ------------------------------------------------- 1) dump com ACE (32 bits)
$dump = @'
param([string]$Arq, [string]$Destino)
$ErrorActionPreference = 'Stop'
$con = New-Object System.Data.OleDb.OleDbConnection "Provider=Microsoft.ACE.OLEDB.16.0;Data Source=$Arq;Mode=Read"
$con.Open()
$tabelas = @($con.GetSchema("Tables") | Where-Object { $_.TABLE_TYPE -eq 'TABLE' } | Select-Object -ExpandProperty TABLE_NAME)
foreach ($t in $tabelas) {
    $cmd = $con.CreateCommand()
    $cmd.CommandText = "SELECT * FROM [$t]"
    $ad = New-Object System.Data.OleDb.OleDbDataAdapter $cmd
    $ds = New-Object System.Data.DataSet
    [void]$ad.Fill($ds, $t)
    $tab = $ds.Tables[$t]
    $linhas = New-Object System.Collections.ArrayList
    foreach ($r in $tab.Rows) {
        $obj = [ordered]@{}
        foreach ($c in $tab.Columns) {
            if ($c.DataType -eq [byte[]]) { continue }
            $v = $r[$c]
            if ($v -is [DBNull] -or $null -eq $v) { $obj[$c.ColumnName] = $null }
            elseif ($v -is [datetime]) { $obj[$c.ColumnName] = $v.ToString("yyyy-MM-ddTHH:mm:ss") }
            else { $obj[$c.ColumnName] = $v }
        }
        [void]$linhas.Add([pscustomobject]$obj)
    }
    ConvertTo-Json -InputObject @{ tabela = $t; total = $linhas.Count; linhas = $linhas } -Depth 5 -Compress |
        Set-Content -Encoding UTF8 (Join-Path $Destino "$t.json")
    Write-Output "  $t -> $($linhas.Count)"
}
$con.Close()
'@

$scriptDump = Join-Path $env:TEMP 'dump-access.ps1'
$dump | Out-File -Encoding utf8 $scriptDump

$ps32 = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path $ps32)) { throw "PowerShell 32 bits não encontrado (necessário para o driver ACE)." }

& $ps32 -NoProfile -ExecutionPolicy Bypass -File $scriptDump -Arq $Mdb -Destino $json

# --------------------------------------------------------- 2) conversão
Write-Host "> convertendo para zFinancas" -ForegroundColor Cyan

$projeto = Join-Path $raiz 'tools\zFinancas.ImportadorAccess\zFinancas.ImportadorAccess.csproj'
dotnet run --project $projeto -c Release -v:q -- $json $banco $pacote

# ------------------------------------------------------------ 3) resumo
Write-Host ""
Write-Host "Pronto!" -ForegroundColor Green
Write-Host "  banco   : $banco"
Write-Host "  pacote  : $pacote"
Write-Host ""
Write-Host "No PC, rode o app apontando para o banco migrado:" -ForegroundColor DarkGray
Write-Host "  `$env:ZFINANCAS_DB = `"$banco`""
Write-Host "No celular, use Sincronização -> Escolher arquivo e importar com o .zfsync." -ForegroundColor DarkGray
