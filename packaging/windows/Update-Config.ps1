param(
    [string]$ConfigPath = (Join-Path $env:LOCALAPPDATA 'CodexLimits/config.json'),
    [string]$TemplatePath = (Join-Path $PSScriptRoot 'config.example.json')
)
$ErrorActionPreference = 'Stop'

function Remove-JsonComments([string]$Json) {
    $output = [Text.StringBuilder]::new()
    $inString = $false
    $escape = $false
    for ($i = 0; $i -lt $Json.Length; $i++) {
        $char = $Json[$i]
        if ($inString) {
            [void]$output.Append($char)
            if ($escape) { $escape = $false }
            elseif ($char -eq '\') { $escape = $true }
            elseif ($char -eq '"') { $inString = $false }
            continue
        }
        if ($char -eq '"') {
            $inString = $true
            [void]$output.Append($char)
            continue
        }
        if ($char -eq '/' -and $i + 1 -lt $Json.Length) {
            $next = $Json[$i + 1]
            if ($next -eq '/') {
                while ($i + 1 -lt $Json.Length -and $Json[$i + 1] -notin "`r", "`n") { $i++ }
                continue
            }
            if ($next -eq '*') {
                $i += 2
                $closed = $false
                while ($i -lt $Json.Length) {
                    if ($Json[$i - 1] -eq '*' -and $Json[$i] -eq '/') { $closed = $true; break }
                    if ($Json[$i] -in "`r", "`n") { [void]$output.Append($Json[$i]) }
                    $i++
                }
                if (-not $closed) { throw 'Unterminated block comment in configuration.' }
                continue
            }
        }
        [void]$output.Append($char)
    }
    return $output.ToString()
}

function ConvertFrom-ConfigJson([string]$Path) {
    Remove-JsonComments (Get-Content -LiteralPath $Path -Raw -Encoding UTF8) | ConvertFrom-Json
}

function ConvertTo-ConfigJson($Config) {
    $json = $Config | ConvertTo-Json -Depth 32
    $accountLine = [regex]::Match($json, '(?m)^(?<indent>[ \t]*)"accounts"\s*:')
    if (-not $accountLine.Success) { throw 'The accounts property is missing from the configuration.' }
    $indent = $accountLine.Groups['indent'].Value
    $comment = $indent + '// Add another object to accounts for a second account, for example:' + [Environment]::NewLine +
        $indent + '// { "name": "Work", "codexHome": "%LOCALAPPDATA%/CodexLimits/profiles/work" }' + [Environment]::NewLine
    return $json.Insert($accountLine.Index, $comment)
}

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
        if ($Previous -isnot [array] -or $Previous.Count -eq 0) {
            throw 'Unexpected number of accounts in configuration.'
        }
        $itemTemplate = $Template[0]
        $items = foreach ($item in $Previous) {
            Merge-Config $itemTemplate $item
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

$template = ConvertFrom-ConfigJson $TemplatePath
$exists = Test-Path -LiteralPath $ConfigPath
if ($exists) {
    $previous = ConvertFrom-ConfigJson $ConfigPath
    if ($null -eq $previous) { throw 'Existing configuration is empty.' }
    $template = Merge-Config $template $previous
}
if ([string]::IsNullOrWhiteSpace([string]$template.codexExecutable)) {
    $detectedCodex = Find-CodexExecutable
    if ($null -ne $detectedCodex) { $template.codexExecutable = $detectedCodex }
}
$json = ConvertTo-ConfigJson $template
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
