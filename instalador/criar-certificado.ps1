# ---------------------------------------------------------------------------
# Cria um certificado autoassinado para o pacote MSIX do zFinancas e o instala
# como confiável nesta máquina (o erro 0x800B010A é justamente "certificado não
# confiável").
#
# Uso (PowerShell como ADMINISTRADOR):
#   .\instalador\criar-certificado.ps1
#   .\instalador\criar-certificado.ps1 -Assunto "CN=zFinancas" -Senha "minhaSenha"
#
# O que ele faz:
#   1. cria/atualiza um certificado de assinatura de código (Code Signing);
#   2. exporta instalador\zFinancas.pfx (com senha) e zFinancas.cer (público);
#   3. confia no certificado em Trusted Root + Trusted People (máquina);
#   4. mostra o thumbprint para usar no csproj/publish.
#
# IMPORTANTE: o campo Subject do certificado precisa ser IGUAL ao Publisher do
# Platforms\Windows\Package.appxmanifest.
# ---------------------------------------------------------------------------
param(
    [string]$Assunto = "CN=User Name",
    [string]$Senha = "zFinancas#2026",
    [int]$ValidadeAnos = 5
)

$ErrorActionPreference = 'Stop'

$raiz = Split-Path -Parent $PSScriptRoot
$pasta = $PSScriptRoot

Write-Host "> assunto do certificado: $Assunto" -ForegroundColor Cyan

# ---------------------------------------------------------------- certificado
$existente = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Assunto -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($existente -and $existente.NotAfter -gt (Get-Date).AddMonths(6)) {
    $certificado = $existente
    Write-Host "> reaproveitando certificado existente ($($certificado.Thumbprint))" -ForegroundColor DarkGray
}
else {
    $certificado = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Assunto `
        -FriendlyName "zFinancas (autoassinado)" `
        -KeyUsage DigitalSignature `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -NotAfter (Get-Date).AddYears($ValidadeAnos) `
        -CertStoreLocation 'Cert:\CurrentUser\My'

    Write-Host "> certificado criado: $($certificado.Thumbprint)" -ForegroundColor Green
}

# ---------------------------------------------------------------- exportação
$senhaSegura = ConvertTo-SecureString -String $Senha -Force -AsPlainText
$pfx = Join-Path $pasta 'zFinancas.pfx'
$cer = Join-Path $pasta 'zFinancas.cer'

Export-PfxCertificate -Cert $certificado -FilePath $pfx -Password $senhaSegura | Out-Null
Export-Certificate -Cert $certificado -FilePath $cer | Out-Null

Write-Host "> exportado: $pfx" -ForegroundColor Green
Write-Host "> exportado: $cer" -ForegroundColor Green

# ---------------------------------------------------------------- confiança
$administrador = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $administrador) {
    Write-Host ""
    Write-Host "! Sem privilégio de administrador: o certificado NÃO foi instalado como confiável." -ForegroundColor Yellow
    Write-Host "  Rode este script em um PowerShell como Administrador, ou instale manualmente:" -ForegroundColor Yellow
    Write-Host "    Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\Root" -ForegroundColor Yellow
    Write-Host "    Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor Yellow
}
else {
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

    Write-Host "> certificado confiável nesta máquina (Root + TrustedPeople)" -ForegroundColor Green
}

# ---------------------------------------------------------------- resumo
Write-Host ""
Write-Host "Thumbprint (use em PackageCertificateThumbprint):" -ForegroundColor Cyan
Write-Host "  $($certificado.Thumbprint)"
Write-Host ""
Write-Host "Assinatura manual de um pacote já gerado:" -ForegroundColor Cyan
Write-Host "  signtool sign /fd SHA256 /a /f `"$pfx`" /p `"$Senha`" `"caminho\zFinancas.msix`""
Write-Host ""
Write-Host "Lembrete: o Publisher do Package.appxmanifest precisa ser '$Assunto'." -ForegroundColor DarkGray
