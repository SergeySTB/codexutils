param([string]$SdkRoot = $env:ANDROID_HOME)

$ErrorActionPreference = 'Stop'
if (-not $SdkRoot) { throw 'Set ANDROID_HOME to an Android SDK with Platform 35 and Build Tools 35.0.0.' }
$platform = Join-Path $SdkRoot 'platforms\android-35\android.jar'
$tools = Join-Path $SdkRoot 'build-tools\35.0.0'
if (-not (Test-Path -LiteralPath $platform) -or -not (Test-Path -LiteralPath $tools)) {
    throw 'Android SDK Platform 35 and Build Tools 35.0.0 are required.'
}

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $PSScriptRoot 'app\src\main'
$gradle = Get-Content -Raw (Join-Path $PSScriptRoot 'app\build.gradle')
$codeMatch = [regex]::Match($gradle, '(?m)^\s*versionCode\s+(\d+)\s*$')
$nameMatch = [regex]::Match($gradle, "(?m)^\s*versionName\s+'(\d+)\.(\d+)'\s*$")
if (-not $codeMatch.Success -or -not $nameMatch.Success) { throw 'Android version is missing from app/build.gradle.' }
$versionCode = [int]$codeMatch.Groups[1].Value
$versionName = $nameMatch.Groups[1].Value + '.' + $nameMatch.Groups[2].Value
if ($versionCode -ne (100 * [int]$nameMatch.Groups[1].Value + [int]$nameMatch.Groups[2].Value)) {
    throw 'Android versionCode must match major.minor (for example, 4.4 = 404).'
}
$work = Join-Path $root ('.build\android\' + [guid]::NewGuid().ToString('N'))
$classes = Join-Path $work 'classes'
$dex = Join-Path $work 'dex'
New-Item -ItemType Directory -Path $classes, $dex -Force | Out-Null

$resources = Join-Path $work 'resources.zip'
& (Join-Path $tools 'aapt2.exe') compile --dir (Join-Path $source 'res') -o $resources
if ($LASTEXITCODE -ne 0) { throw 'Resource compilation failed.' }

$unsigned = Join-Path $work 'unsigned.apk'
$generated = Join-Path $work 'generated'
& (Join-Path $tools 'aapt2.exe') link -o $unsigned --manifest (Join-Path $source 'AndroidManifest.xml') `
    -I $platform --java $generated --min-sdk-version 26 --target-sdk-version 35 `
    --version-code $versionCode --version-name $versionName $resources
if ($LASTEXITCODE -ne 0) { throw 'APK linking failed.' }

$sources = @(Get-ChildItem -LiteralPath (Join-Path $source 'java') -Filter '*.java' -Recurse | ForEach-Object FullName)
$sources += @(Get-ChildItem -LiteralPath $generated -Filter '*.java' -Recurse | ForEach-Object FullName)
& javac --release 17 -classpath $platform -d $classes $sources
if ($LASTEXITCODE -ne 0) { throw 'Java compilation failed.' }

$classFiles = @(Get-ChildItem -LiteralPath $classes -Filter '*.class' -Recurse | ForEach-Object FullName)
& (Join-Path $tools 'd8.bat') --min-api 26 --lib $platform --output $dex $classFiles
if ($LASTEXITCODE -ne 0) { throw 'DEX compilation failed.' }

& jar uf $unsigned -C $dex classes.dex
if ($LASTEXITCODE -ne 0) { throw 'Could not add app code to APK.' }

$aligned = Join-Path $work 'aligned.apk'
& (Join-Path $tools 'zipalign.exe') -f 4 $unsigned $aligned
if ($LASTEXITCODE -ne 0) { throw 'APK alignment failed.' }

$key = Join-Path $root '.build\android-debug.keystore'
if (-not (Test-Path -LiteralPath $key)) {
    & keytool -genkeypair -keystore $key -storepass android -keypass android -alias androiddebugkey `
        -dname 'CN=Android Debug,O=Android,C=US' -keyalg RSA -keysize 2048 -validity 10000
    if ($LASTEXITCODE -ne 0) { throw 'Debug signing key creation failed.' }
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$apk = Join-Path $dist "AIUsageMonitor-Android_v$versionName-debug.apk"
& (Join-Path $tools 'apksigner.bat') sign --ks $key --ks-key-alias androiddebugkey `
    --ks-pass pass:android --key-pass pass:android --out $apk $aligned
if ($LASTEXITCODE -ne 0) { throw 'APK signing failed.' }
& (Join-Path $tools 'apksigner.bat') verify --verbose $apk
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
Write-Output $apk
