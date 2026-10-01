$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$distribution = Join-Path $projectRoot 'dist'
$extensionProject = Join-Path $projectRoot 'src/SsmsMcp.Extension/SsmsMcp.Extension.csproj'
$serverProject = Join-Path $projectRoot 'src/SsmsMcp.Server/SsmsMcp.Server.csproj'
$probeProject = Join-Path $projectRoot 'tests/SsmsMcp.Net48Probe/SsmsMcp.Net48Probe.csproj'
$solution = Join-Path $projectRoot 'SsmsMcp.slnx'

dotnet test $solution -c Release -v:q
if ($LASTEXITCODE -ne 0) { throw 'Testene feilet.' }

$probeArguments = @('run', '--project', $probeProject, '-c', 'Release', '--no-launch-profile', '--')
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (Test-Path -LiteralPath $vswhere) {
    $instances = & $vswhere -all -products '*' -format json | ConvertFrom-Json
    $ssms = $instances | Where-Object productId -eq 'Microsoft.VisualStudio.Product.Ssms' | Sort-Object installationVersion -Descending | Select-Object -First 1
    if ($ssms) {
        $olderAi = Join-Path $ssms.installationPath 'Common7/IDE/PrivateAssemblies/Microsoft.Extensions.AI.Abstractions.dll'
        if (Test-Path -LiteralPath $olderAi) { $probeArguments += $olderAi }
    }
}
dotnet @probeArguments
if ($LASTEXITCODE -ne 0) { throw 'HTTP-prøven på .NET Framework feilet.' }

dotnet build $serverProject -c Release -v:q
if ($LASTEXITCODE -ne 0) { throw 'HTTP-serveren kunne ikke bygges.' }

dotnet build $extensionProject -c Release -v:q
if ($LASTEXITCODE -ne 0) { throw 'VSIX-pakken kunne ikke bygges.' }

New-Item -ItemType Directory -Path $distribution -Force | Out-Null
$extensionOutput = Join-Path $projectRoot 'src/SsmsMcp.Extension/bin/Release/net48'
$serverOutput = Join-Path $projectRoot 'tests/SsmsMcp.Net48Probe/bin/Release/net48'
$sourceVsix = Join-Path $extensionOutput 'SsmsMcp.Extension.vsix'
$targetVsix = Join-Path $distribution 'SsmsMcp.Extension.vsix'
Copy-Item -LiteralPath $sourceVsix -Destination $targetVsix -Force

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($targetVsix, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $known = @{}
    foreach ($entry in $archive.Entries) { $known[$entry.FullName] = $true }
    foreach ($file in Get-ChildItem -LiteralPath $serverOutput -File -Filter '*.dll') {
        $existing = $archive.GetEntry($file.Name)
        if ($existing) { $existing.Delete() }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $file.Name) | Out-Null
        $known[$file.Name] = $true
    }
    foreach ($file in Get-ChildItem -LiteralPath $extensionOutput -File -Filter '*.dll') {
        if ($known.ContainsKey($file.Name)) { continue }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $file.Name) | Out-Null
        $known[$file.Name] = $true
    }
}
finally {
    $archive.Dispose()
}

Write-Output "VSIX: $targetVsix"
Write-Output 'Streamable HTTP-serveren er pakket i VSIX-utvidelsen.'
