param([switch]$SkipChecks)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$previousCliHome = $env:DOTNET_CLI_HOME
$previousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
$previousCertificate = $env:DOTNET_GENERATE_ASPNET_CERTIFICATE
try {
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build/dotnet'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    if (-not $SkipChecks) {
        & powershell -NoProfile -ExecutionPolicy Bypass -File tests/AIUsageMonitor.Checks/ConfigMigrationChecks.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Configuration migration checks failed' }
        & powershell -NoProfile -ExecutionPolicy Bypass -File tests/AIUsageMonitor.Checks/InstallerChecks.ps1
        if ($LASTEXITCODE -ne 0) { throw 'Installer checks failed' }
        dotnet run --project tests/AIUsageMonitor.Checks/AIUsageMonitor.Checks.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Checks failed' }
    }
    $publishRoot = Join-Path $PSScriptRoot '.build/publish'
    Remove-Item -LiteralPath $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish src/AIUsageMonitor/AIUsageMonitor.csproj -c Release --self-contained false -o $publishRoot --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    [xml]$project = Get-Content -LiteralPath src/AIUsageMonitor/AIUsageMonitor.csproj
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+$') { throw "Installer version must use major.minor format: $version" }
    $downloadUrl = "https://github.com/SergeySTB/codexutils/releases/download/v$version/AIUsageMonitor-Setup_v$version.exe"
    if (-not (Get-Content -LiteralPath README.md -Raw -Encoding UTF8).Contains($downloadUrl)) { throw "README download link must reference version $version" }
    Copy-Item -LiteralPath README.md -Destination $publishRoot/README.md
    $payloadRoot = Join-Path $PSScriptRoot '.build/installer/payload'
    $setupPath = Join-Path $PSScriptRoot "dist/AIUsageMonitor-Setup_v$version.exe"
    Remove-Item -LiteralPath $payloadRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $setupPath -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $payloadRoot, (Split-Path $setupPath) | Out-Null
    $payloadFiles = @('AIUsageMonitor.exe', 'AIUsageMonitor.dll', 'AIUsageMonitor.deps.json', 'AIUsageMonitor.runtimeconfig.json', 'config.example.json', 'README.md')
    foreach ($file in $payloadFiles) { Copy-Item -LiteralPath (Join-Path $publishRoot $file) -Destination $payloadRoot }
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$payloadRoot\SetupLauncher.exe" (Join-Path $PSScriptRoot 'packaging/windows/SetupLauncher.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Installer launcher build failed' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/windows/Install.ps1') -Destination $payloadRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/windows/Uninstall.ps1') -Destination $payloadRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/windows/Update-Config.ps1') -Destination $payloadRoot
    $sedPath = Join-Path $PSScriptRoot '.build/installer/AIUsageMonitor.sed'
    $sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=1
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$setupPath
FriendlyName=AI Usage Monitor for Windows 11
AppLaunched=SetupLauncher.exe
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=SetupLauncher.exe
SourceFiles=SourceFiles
[Strings]
FILE0="SetupLauncher.exe"
FILE1="Install.ps1"
FILE2="Uninstall.ps1"
FILE3="AIUsageMonitor.exe"
FILE4="AIUsageMonitor.dll"
FILE5="AIUsageMonitor.deps.json"
FILE6="AIUsageMonitor.runtimeconfig.json"
FILE7="config.example.json"
FILE8="README.md"
FILE9="Update-Config.ps1"
[SourceFiles]
SourceFiles0=$payloadRoot\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
%FILE4%=
%FILE5%=
%FILE6%=
%FILE7%=
%FILE8%=
%FILE9%=
"@
    Set-Content -LiteralPath $sedPath -Value ($sed -replace '\r?\n', "`r`n") -Encoding Default
    $packager = Start-Process -FilePath "$env:WINDIR\System32\iexpress.exe" -ArgumentList '/N /Q AIUsageMonitor.sed' -WorkingDirectory (Split-Path $sedPath) -WindowStyle Hidden -Wait -PassThru
    if ($packager.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $setupPath)) { throw 'Installer build failed' }
    & powershell -NoProfile -ExecutionPolicy Bypass -File packaging/windows/Set-ExeIcon.ps1 `
        -ExecutablePath $setupPath -IconPath src/AIUsageMonitor/Assets/AIUsageMonitor.ico
    if ($LASTEXITCODE -ne 0) { throw 'Installer icon update failed' }
    $verifyRoot = Join-Path $PSScriptRoot '.build/installer/verify'
    $cabPath = Join-Path $PSScriptRoot '.build/installer/verify.cab'
    Remove-Item -LiteralPath $verifyRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $cabPath -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $verifyRoot | Out-Null
    try {
        $installer = [IO.File]::ReadAllBytes($setupPath)
        $cabOffset = -1
        $cabLength = 0
        for ($index = 0; $index -le $installer.Length - 12; $index++) {
            if ($installer[$index] -eq 0x4D -and $installer[$index + 1] -eq 0x53 -and $installer[$index + 2] -eq 0x43 -and $installer[$index + 3] -eq 0x46) {
                $candidate = [BitConverter]::ToUInt32($installer, $index + 8)
                if ($candidate -ge 36 -and [uint64]$index + $candidate -le $installer.Length) { $cabOffset = $index; $cabLength = $candidate; break }
            }
        }
        if ($cabOffset -lt 0) { throw 'Installer cabinet was not found after icon update' }
        [byte[]]$cabinet = $installer[$cabOffset..($cabOffset + $cabLength - 1)]
        [IO.File]::WriteAllBytes($cabPath, $cabinet)
        & "$env:WINDIR\System32\expand.exe" '-F:*' $cabPath $verifyRoot | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Installer extraction check failed' }
        foreach ($file in @('SetupLauncher.exe', 'Install.ps1', 'Uninstall.ps1', 'AIUsageMonitor.exe', 'AIUsageMonitor.dll', 'AIUsageMonitor.deps.json', 'AIUsageMonitor.runtimeconfig.json', 'config.example.json', 'README.md', 'Update-Config.ps1')) {
            $extracted = Join-Path $verifyRoot $file
            if (-not (Test-Path -LiteralPath $extracted)) { throw "Installer payload missing: $file" }
            if ((Get-FileHash -LiteralPath $extracted).Hash -ne (Get-FileHash -LiteralPath (Join-Path $payloadRoot $file)).Hash) { throw "Installer payload changed: $file" }
        }
    } finally {
        Remove-Item -LiteralPath $verifyRoot -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $cabPath -Force -ErrorAction SilentlyContinue
    }
    Write-Output "Ready: $setupPath"
} finally {
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetry
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = $previousCertificate
    Pop-Location
}
