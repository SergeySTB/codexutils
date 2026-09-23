$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$root = Join-Path $repoRoot ('.build/config-checks/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$config = Join-Path $root 'config.json'
$template = Join-Path $repoRoot 'config/config.example.json'
$migration = Join-Path $repoRoot 'packaging/windows/Update-Config.ps1'
function Check($Condition, $Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    Write-Output "PASS: $Message"
}
function Read-Config($Path) {
    ([regex]::Replace([IO.File]::ReadAllText($Path), '(?m)^[ \t]*//.*(?:\r?\n|$)', '')) | ConvertFrom-Json
}
$fakeLocalAppData = Join-Path $root 'local-app-data'
$fakeCodex = Join-Path $fakeLocalAppData 'Programs/OpenAI/Codex/bin/codex.exe'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fakeCodex)) | Out-Null
[IO.File]::WriteAllText($fakeCodex, '')
$previousLocalAppData = $env:LOCALAPPDATA
$previousPath = $env:PATH
try {
    $env:LOCALAPPDATA = $fakeLocalAppData
    $env:PATH = ''
    & $migration -ConfigPath $config -TemplatePath $template
} finally {
    $env:LOCALAPPDATA = $previousLocalAppData
    $env:PATH = $previousPath
}
$generated = [IO.File]::ReadAllText($config)
$old = Read-Config $config
Check ($old.notifyOnLimitReset -eq $false) 'fresh installation uses current defaults'
Check ($old.codexExecutable -eq $fakeCodex) 'fresh installation records the detected Codex executable'
Check (@($old.accounts).Count -eq 1) 'fresh installation configures one account by default'
Check ($generated.Contains('//') -and $generated.Contains('%LOCALAPPDATA%/AIUsageMonitor/profiles/work')) 'generated configuration includes a commented second-account example'
$old.PSObject.Properties.Remove('notifyOnLimitReset')
$old.widget.PSObject.Properties.Remove('displayMode')
$old.widget.PSObject.Properties.Remove('iconWidthPx')
$old.widget.PSObject.Properties.Remove('iconHeightPx')
$old.widget | Add-Member -NotePropertyName widthPx -NotePropertyValue 120
$old.widget | Add-Member -NotePropertyName heightPx -NotePropertyValue 60
$old.widget.marginPx = -50
$old.widget.alwaysOnTop = $false
$old.accounts[0].name = 'Personal custom'
$old.accounts = @($old.accounts) + @(
    [pscustomobject]@{ name = 'Work custom'; codexHome = 'D:\Profiles\Work' },
    [pscustomobject]@{ name = 'Third custom'; codexHome = 'D:\Profiles\Third' }
)
$old.codexExecutable = 'D:\Custom\codex.exe'
$old | Add-Member -NotePropertyName retired -NotePropertyValue 123
$old.widget | Add-Member -NotePropertyName retired -NotePropertyValue 456
$old.accounts[0] | Add-Member -NotePropertyName retired -NotePropertyValue 789
$previousJson = $old | ConvertTo-Json -Depth 32
$previousJson = $previousJson -replace '(?m)^\s*"accounts"\s*:', "    // Existing comments are accepted during upgrades.`r`n    `"accounts`":"
[IO.File]::WriteAllText($config, $previousJson, [Text.UTF8Encoding]::new($false))
$original = [IO.File]::ReadAllText($config)
& $migration -ConfigPath $config -TemplatePath $template
$updated = Read-Config $config
Check ($updated.notifyOnLimitReset -eq $false) 'missing notification option is added'
Check ($updated.widget.displayMode -eq 'icons' -and $updated.widget.iconWidthPx -eq 120 -and $updated.widget.iconHeightPx -eq 60) 'legacy icon dimensions migrate with their custom values'
Check ($null -eq $updated.widget.PSObject.Properties['widthPx'] -and $null -eq $updated.widget.PSObject.Properties['heightPx']) 'legacy dimension names are removed'
Check ($updated.widget.marginPx -eq -50 -and $updated.widget.alwaysOnTop -eq $false) 'custom values including false survive'
Check (@($updated.accounts).Count -eq 3 -and $updated.accounts[0].name -eq 'Personal custom' -and
    $updated.accounts[1].codexHome -eq 'D:\Profiles\Work' -and $updated.accounts[2].name -eq 'Third custom') 'configured account count, order and paths survive'
Check ($updated.codexExecutable -eq 'D:\Custom\codex.exe') 'explicit Codex executable survives migration'
Check (-not ([IO.File]::ReadAllText($config).Contains('retired'))) 'obsolete root, widget and account keys are removed'
$claudeConfig = Join-Path $root 'claude-config.json'
$withClaude = Read-Config $template
$withClaude.accounts = @($withClaude.accounts) + @([pscustomobject]@{
    name = 'Claude work'; provider = 'claude'; claudeConfigDir = 'D:\Profiles\Claude-work'; retired = 1
})
[IO.File]::WriteAllText($claudeConfig, ($withClaude | ConvertTo-Json -Depth 32))
& $migration -ConfigPath $claudeConfig -TemplatePath $template
$migratedClaude = Read-Config $claudeConfig
Check (@($migratedClaude.accounts).Count -eq 2 -and $migratedClaude.accounts[0].provider -eq 'codex' -and
    $migratedClaude.accounts[1].provider -eq 'claude' -and
    $migratedClaude.accounts[1].claudeConfigDir -eq 'D:\Profiles\Claude-work' -and
    $null -eq $migratedClaude.accounts[1].PSObject.Properties['codexHome'] -and
    $null -eq $migratedClaude.accounts[1].PSObject.Properties['retired']) 'installer preserves Claude profiles without adding Codex fields'
$emptyConfig = Join-Path $root 'empty-config.json'
$withoutAccounts = Read-Config $template
$withoutAccounts.accounts = @()
[IO.File]::WriteAllText($emptyConfig, ($withoutAccounts | ConvertTo-Json -Depth 32))
& $migration -ConfigPath $emptyConfig -TemplatePath $template
Check ((Read-Config $emptyConfig).accounts -is [array] -and (Read-Config $emptyConfig).accounts.Count -eq 0) 'installer preserves an empty account list'
$backup = @(Get-ChildItem $root -Filter 'config.json.*.bak')
Check ($backup.Count -eq 1 -and [IO.File]::ReadAllText($backup[0].FullName) -ceq $original) 'original configuration is backed up exactly'
$updated.notifyOnLimitReset = $true
$updated.widget.displayMode = 'cards'
$updated.widget.cardWidthPx = 340
$updated.widget.cardHeightPx = 280
$updated | ConvertTo-Json -Depth 32 | Set-Content $config -Encoding UTF8
& $migration -ConfigPath $config -TemplatePath $template
Check ((Read-Config $config).notifyOnLimitReset -eq $true) 'repeat installation preserves enabled notifications'
Check ((Read-Config $config).widget.displayMode -eq 'cards' -and (Read-Config $config).widget.cardWidthPx -eq 340 -and
    (Read-Config $config).widget.cardHeightPx -eq 280) 'repeat installation preserves card mode and dimensions'
[IO.File]::WriteAllText($config, '{"codexExecutable":""} /* unfinished')
$failed = $false
try { & $migration -ConfigPath $config -TemplatePath $template } catch { $failed = $true }
Check ($failed -and [IO.File]::ReadAllText($config).EndsWith('/* unfinished')) 'unterminated comment is not overwritten'
[IO.File]::WriteAllText($config, '{broken')
$failed = $false
try { & $migration -ConfigPath $config -TemplatePath $template } catch { $failed = $true }
Check ($failed -and [IO.File]::ReadAllText($config) -ceq '{broken') 'invalid JSON is not overwritten'
# Exercise default-path selection using isolated data, without touching real accounts.
try {
    $env:LOCALAPPDATA = $fakeLocalAppData
    $env:PATH = ''
    $legacyConfig = Join-Path $fakeLocalAppData 'CodexLimits/config.json'
    $renamedConfig = Join-Path $fakeLocalAppData 'AIUsageMonitor/config.json'
    New-Item -ItemType Directory -Force -Path (Split-Path $legacyConfig) | Out-Null
    $legacySettings = Read-Config $template
    $legacySettings.accounts[0].codexHome = '%LOCALAPPDATA%/CodexLimits/profiles/personal'
    $legacySettings | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $legacyConfig -Encoding UTF8
    & $migration -TemplatePath $template
    Check (-not (Test-Path -LiteralPath $renamedConfig) -and (Read-Config $legacyConfig).accounts[0].codexHome -eq '%LOCALAPPDATA%/CodexLimits/profiles/personal') 'upgrade reuses the legacy configuration and profile path'
    $legacyAfterUpgrade = [IO.File]::ReadAllText($legacyConfig)
    & $migration -ConfigPath $renamedConfig -TemplatePath $template
    Check ((Read-Config $renamedConfig).accounts[0].codexHome -eq '%LOCALAPPDATA%/AIUsageMonitor/profiles/personal') 'explicit new configuration does not import legacy accounts'
    & $migration -TemplatePath $template
    Check ([IO.File]::ReadAllText($legacyConfig) -ceq $legacyAfterUpgrade) 'new default configuration takes precedence over legacy'
    $env:LOCALAPPDATA = Join-Path $root 'fresh-local-app-data'
    & $migration -TemplatePath $template
    Check (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'AIUsageMonitor/config.json')) 'fresh installation creates the renamed configuration path'
} finally {
    $env:LOCALAPPDATA = $previousLocalAppData
    $env:PATH = $previousPath
}
Write-Output 'All configuration migration checks passed.'
