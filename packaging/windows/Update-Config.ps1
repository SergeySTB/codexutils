param(
    [string]$ConfigPath = (Join-Path $env:LOCALAPPDATA 'CodexLimits/config.json'),
    [string]$TemplatePath = (Join-Path $PSScriptRoot 'config.example.json')
)
$ErrorActionPreference = 'Stop'

function Merge-Config($Template, $Previous) {
    if ($Template -is [System.Management.Automation.PSCustomObject]) {
        if ($null -ne $Previous -and $Previous -isnot [System.Management.Automation.PSCustomObject]) {
            throw 'Expected a configuration object.'
        }
        $result = [ordered]@{}
        foreach ($property in $Template.PSObject.Properties) {
            $old = if ($null -ne $Previous) { $Previous.PSObject.Properties[$property.Name] } else { $null }
            $result[$property.Name] = if ($null -ne $old) {
                Merge-Config $property.Value $old.Value
            } else { $property.Value }
        }
        return [pscustomobject]$result
    }
    if ($Template -is [array]) {
        if ($Previous -isnot [array] -or $Previous.Count -ne $Template.Count) {
            throw 'Unexpected number of accounts in configuration.'
        }
        $items = for ($i = 0; $i -lt $Template.Count; $i++) {
            Merge-Config $Template[$i] $Previous[$i]
        }
        return ,@($items)
    }
    if ($null -eq $Previous -or $Previous.GetType() -ne $Template.GetType()) {
        throw 'Invalid configuration value type.'
    }
    return $Previous
}

function Find-CodexExecutable {
    $localAppData = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData) }
    $candidates = [Collections.Generic.List[string]]::new()
    $candidates.Add((Join-Path $localAppData 'Programs/OpenAI/Codex/bin/codex.exe'))

    foreach ($directory in ([Environment]::GetEnvironmentVariable('PATH') -split [IO.Path]::PathSeparator)) {
        if (-not [string]::IsNullOrWhiteSpace($directory)) {
            $candidates.Add((Join-Path $directory.Trim('"') 'codex.exe'))
        }
    }

    $versionedRoot = Join-Path $localAppData 'OpenAI/Codex/bin'
    if (Test-Path -LiteralPath $versionedRoot) {
        foreach ($directory in (Get-ChildItem -LiteralPath $versionedRoot -Directory -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)) {
            $candidates.Add((Join-Path $directory.FullName 'codex.exe'))
        }
    }

    $programFiles = if ($env:ProgramFiles) { $env:ProgramFiles } else { [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles) }
    if (-not [string]::IsNullOrWhiteSpace($programFiles)) {
        $candidates.Add((Join-Path $programFiles 'OpenAI/Codex/bin/codex.exe'))
    }

    $appData = if ($env:APPDATA) { $env:APPDATA } else { [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData) }
    $npmRoot = if ($appData) { Join-Path $appData 'npm/node_modules/@openai/codex/vendor' } else { $null }
    if ($npmRoot -and (Test-Path -LiteralPath $npmRoot)) {
        foreach ($file in (Get-ChildItem -LiteralPath $npmRoot -Filter codex.exe -File -Recurse -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)) {
            $candidates.Add($file.FullName)
        }
    }

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in $candidates) {
        try { $fullPath = [IO.Path]::GetFullPath($candidate) } catch { continue }
        if ($seen.Add($fullPath) -and (Test-Path -LiteralPath $fullPath -PathType Leaf)) { return $fullPath }
    }
    return $null
}

$template = Get-Content -LiteralPath $TemplatePath -Raw -Encoding UTF8 | ConvertFrom-Json
$exists = Test-Path -LiteralPath $ConfigPath
if ($exists) {
    $previous = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $previous) { throw 'Existing configuration is empty.' }
    $template = Merge-Config $template $previous
}
if ([string]::IsNullOrWhiteSpace([string]$template.codexExecutable)) {
    $detectedCodex = Find-CodexExecutable
    if ($null -ne $detectedCodex) { $template.codexExecutable = $detectedCodex }
}
$json = $template | ConvertTo-Json -Depth 32
$directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ConfigPath))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$temporary = Join-Path $directory ([IO.Path]::GetRandomFileName())
try {
    [IO.File]::WriteAllText($temporary, $json, [Text.UTF8Encoding]::new($false))
    if ($exists) {
        $backup = $ConfigPath + '.' + [Guid]::NewGuid().ToString('N') + '.bak'
        [IO.File]::Replace($temporary, $ConfigPath, $backup)
    } else {
        [IO.File]::Move($temporary, $ConfigPath)
    }
} finally {
    if (Test-Path -LiteralPath $temporary) { [IO.File]::Delete($temporary) }
}
