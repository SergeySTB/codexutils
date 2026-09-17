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
& $migration -ConfigPath $config -TemplatePath $template
$old = Get-Content $config -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($old.notifyOnLimitReset -eq $false) 'fresh installation uses current defaults'
$old.PSObject.Properties.Remove('notifyOnLimitReset')
$old.widget.marginPx = -50
$old.widget.alwaysOnTop = $false
$old.accounts[0].name = 'Personal custom'
$old.accounts[1].codexHome = 'D:\Profiles\Work'
$old | Add-Member -NotePropertyName retired -NotePropertyValue 123
$old.widget | Add-Member -NotePropertyName retired -NotePropertyValue 456
$old.accounts[0] | Add-Member -NotePropertyName retired -NotePropertyValue 789
$old | ConvertTo-Json -Depth 32 | Set-Content $config -Encoding UTF8
$original = [IO.File]::ReadAllText($config)
& $migration -ConfigPath $config -TemplatePath $template
$updated = Get-Content $config -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($updated.notifyOnLimitReset -eq $false) 'missing notification option is added'
Check ($updated.widget.marginPx -eq -50 -and $updated.widget.alwaysOnTop -eq $false) 'custom values including false survive'
Check ($updated.accounts[0].name -eq 'Personal custom' -and $updated.accounts[1].codexHome -eq 'D:\Profiles\Work') 'account order and paths survive'
Check (-not ([IO.File]::ReadAllText($config).Contains('retired'))) 'obsolete root, widget and account keys are removed'
$backup = @(Get-ChildItem $root -Filter '*.bak')
Check ($backup.Count -eq 1 -and [IO.File]::ReadAllText($backup[0].FullName) -ceq $original) 'original configuration is backed up exactly'
$updated.notifyOnLimitReset = $true
$updated | ConvertTo-Json -Depth 32 | Set-Content $config -Encoding UTF8
& $migration -ConfigPath $config -TemplatePath $template
Check ((Get-Content $config -Raw | ConvertFrom-Json).notifyOnLimitReset -eq $true) 'repeat installation preserves enabled notifications'
[IO.File]::WriteAllText($config, '{broken')
$failed = $false
try { & $migration -ConfigPath $config -TemplatePath $template } catch { $failed = $true }
Check ($failed -and [IO.File]::ReadAllText($config) -ceq '{broken') 'invalid JSON is not overwritten'
Write-Output 'All configuration migration checks passed.'
