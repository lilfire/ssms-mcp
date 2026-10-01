$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$distribution = Join-Path $projectRoot 'dist'
$vsix = Join-Path $distribution 'SsmsMcp.Extension.vsix'

if (Get-Process -Name SSMS -ErrorAction SilentlyContinue) {
    throw 'Lagre arbeidet og lukk alle SSMS-vinduer før installasjon.'
}

if (-not (Test-Path -LiteralPath $vsix)) {
    throw 'Kjør scripts/Build.ps1 først.'
}

$packageModified = (Get-Item -LiteralPath $vsix).LastWriteTimeUtc
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Recurse -File | Where-Object {
    $_.Extension -in @('.cs', '.csproj', '.vsixmanifest') -and
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}
if ($sourceFiles | Where-Object LastWriteTimeUtc -GT $packageModified | Select-Object -First 1) {
    throw 'VSIX-pakken er eldre enn kildekoden. Kjør scripts/Build.ps1 på nytt før installasjon.'
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$instances = & $vswhere -all -products '*' -format json | ConvertFrom-Json
$ssms = $instances | Where-Object productId -eq 'Microsoft.VisualStudio.Product.Ssms' | Sort-Object installationVersion -Descending | Select-Object -First 1
$studio = $instances | Where-Object productId -eq 'Microsoft.VisualStudio.Product.Enterprise' | Sort-Object installationVersion -Descending | Select-Object -First 1
if ($null -eq $ssms -or $null -eq $studio) { throw 'SSMS 22 eller Visual Studio VSIXInstaller ble ikke funnet.' }

$installer = Join-Path $studio.installationPath 'Common7/IDE/VSIXInstaller.exe'
$installation = Start-Process -FilePath $installer -ArgumentList @('/quiet', "/instanceIds:$($ssms.instanceId)", "`"$vsix`"") -Wait -PassThru -WindowStyle Hidden
if ($installation.ExitCode -ne 0) { throw "VSIXInstaller feilet med kode $($installation.ExitCode)." }

$token = [Environment]::GetEnvironmentVariable('SSMS_MCP_TOKEN', 'User')
if ([string]::IsNullOrWhiteSpace($token)) {
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] 32
        $random.GetBytes($bytes)
        $token = [Convert]::ToBase64String($bytes)
    }
    finally { $random.Dispose() }
    [Environment]::SetEnvironmentVariable('SSMS_MCP_TOKEN', $token, 'User')
}

$stableDirectory = Join-Path (Join-Path $env:LOCALAPPDATA 'Microsoft\SSMS') 'SsmsMcpBridge'
$parent = Split-Path -Parent $stableDirectory
$existing = Get-ChildItem -LiteralPath $parent -Force -ErrorAction SilentlyContinue | Where-Object Name -eq 'SsmsMcpBridge'
if ($existing -and $existing.LinkType -eq 'Junction') {
    [System.IO.Directory]::Delete($stableDirectory)
}

Write-Output 'VSIX installert. Start SSMS og les MCP-adressen under Tools -> SSMS MCP Server status.'
Write-Output 'Start MCP-klienten på nytt slik at den leser SSMS_MCP_TOKEN fra brukermiljøet.'
