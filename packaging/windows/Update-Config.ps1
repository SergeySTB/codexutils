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

$template = Get-Content -LiteralPath $TemplatePath -Raw -Encoding UTF8 | ConvertFrom-Json
$exists = Test-Path -LiteralPath $ConfigPath
if ($exists) {
    $previous = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $previous) { throw 'Existing configuration is empty.' }
    $template = Merge-Config $template $previous
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
