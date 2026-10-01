$ErrorActionPreference = 'Stop'

if (Get-Process -Name SSMS -ErrorAction SilentlyContinue) {
    throw 'Lagre arbeidet og lukk alle SSMS-vinduer før avinstallasjon.'
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$instances = & $vswhere -all -products '*' -format json | ConvertFrom-Json
$ssms = $instances | Where-Object productId -eq 'Microsoft.VisualStudio.Product.Ssms' | Sort-Object installationVersion -Descending | Select-Object -First 1
$studio = $instances | Where-Object productId -eq 'Microsoft.VisualStudio.Product.Enterprise' | Sort-Object installationVersion -Descending | Select-Object -First 1
if ($null -eq $ssms -or $null -eq $studio) { throw 'SSMS 22 eller Visual Studio VSIXInstaller ble ikke funnet.' }

$installer = Join-Path $studio.installationPath 'Common7/IDE/VSIXInstaller.exe'
$extensionId = 'SsmsMcp.Server.2fe2109e-94b4-4ce2-bca0-cb77a81c995c'
$uninstallation = Start-Process -FilePath $installer -ArgumentList @('/quiet', "/instanceIds:$($ssms.instanceId)", "/uninstall:$extensionId") -Wait -PassThru -WindowStyle Hidden
if ($uninstallation.ExitCode -ne 0) { throw "VSIXInstaller feilet med kode $($uninstallation.ExitCode)." }

$stableDirectory = Join-Path (Join-Path $env:LOCALAPPDATA 'Microsoft\SSMS') 'SsmsMcpBridge'
$parent = Split-Path -Parent $stableDirectory
$existing = Get-ChildItem -LiteralPath $parent -Force -ErrorAction SilentlyContinue | Where-Object Name -eq 'SsmsMcpBridge'
if ($existing -and $existing.LinkType -eq 'Junction') {
    [System.IO.Directory]::Delete($stableDirectory)
}

[Environment]::SetEnvironmentVariable('SSMS_MCP_TOKEN', $null, 'User')

Write-Output 'SSMS MCP Server er avinstallert fra SSMS.'
