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
        dotnet run --project Checks/Checks.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Checks failed' }
    }
    $publishRoot = Join-Path $PSScriptRoot '.build/publish'
    Remove-Item -LiteralPath $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish CodexLimits/CodexLimits.csproj -c Release --self-contained false -o $publishRoot --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item -LiteralPath README.md -Destination $publishRoot/README.md
    $payloadRoot = Join-Path $PSScriptRoot '.build/installer/payload'
    $setupPath = Join-Path $PSScriptRoot 'releases/CodexLimits-Setup.exe'
    Remove-Item -LiteralPath $payloadRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $setupPath -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $payloadRoot, (Split-Path $setupPath) | Out-Null
    $payloadFiles = @('CodexLimits.exe', 'CodexLimits.dll', 'CodexLimits.deps.json', 'CodexLimits.runtimeconfig.json', 'config.example.json', 'README.md')
    foreach ($file in $payloadFiles) { Copy-Item -LiteralPath (Join-Path $publishRoot $file) -Destination $payloadRoot }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'installer/install.cmd') -Destination $payloadRoot
    $sedPath = Join-Path $PSScriptRoot '.build/installer/CodexLimits.sed'
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
FriendlyName=Codex Limits for Windows 11
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=install.cmd
SourceFiles=SourceFiles
[Strings]
FILE0="install.cmd"
FILE1="CodexLimits.exe"
FILE2="CodexLimits.dll"
FILE3="CodexLimits.deps.json"
FILE4="CodexLimits.runtimeconfig.json"
FILE5="config.example.json"
FILE6="README.md"
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
"@
    Set-Content -LiteralPath $sedPath -Value $sed -NoNewline
    & "$env:WINDIR\System32\iexpress.exe" /N $sedPath
    for ($attempt = 0; $attempt -lt 10 -and -not (Test-Path -LiteralPath $setupPath); $attempt++) { Start-Sleep -Seconds 1 }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setupPath)) { throw 'Installer build failed' }
    Write-Output "Ready: $setupPath"
} finally {
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = $previousTelemetry
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = $previousCertificate
    Pop-Location
}
