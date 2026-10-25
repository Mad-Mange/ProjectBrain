# One-time user-level setup. Credentials never appear in command arguments or output.
param(
    [string]$EnvFile = (Join-Path $PSScriptRoot '../.env'),
    [string]$ServerUrl = 'http://127.0.0.1:4317/mcp',
    [switch]$SessionOnly
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$serverUri = $null
if (![Uri]::TryCreate($ServerUrl, [UriKind]::Absolute, [ref]$serverUri) -or
    $serverUri.Scheme -ne 'http' -or $serverUri.Host -notin @('127.0.0.1', 'localhost') -or
    $serverUri.AbsolutePath -ne '/mcp' -or $serverUri.UserInfo -or $serverUri.Query -or $serverUri.Fragment) {
    throw 'Use a local HTTP ProjectBrain /mcp endpoint.'
}
$settings = @{}
Get-Content -LiteralPath $EnvFile -Encoding UTF8 | ForEach-Object {
    if ($_ -match '^\s*([A-Z_]+)\s*=(.*)$') {
        $value = $matches[2].Trim()
        if ($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or
            ($value.StartsWith("'") -and $value.EndsWith("'")))) { $value = $value.Substring(1, $value.Length - 2) }
        $settings[$matches[1]] = $value
    }
}
$token = $settings['PROJECTBRAIN_AGENT_TOKEN']
if (!$token -or $token.Length -lt 32 -or $token -eq $settings['PROJECTBRAIN_AUTH_TOKEN']) {
    throw 'Set a distinct PROJECTBRAIN_AGENT_TOKEN of at least 32 characters in the private .env and restart the API first.'
}
try {
    $null = Invoke-RestMethod ($serverUri.GetLeftPart([UriPartial]::Authority) + '/api/v1/diagnostics') -Headers @{Authorization = 'Bearer ' + $token}
} catch { throw 'ProjectBrain did not accept its agent credential. Check the running API and private .env.' }
$codexCommand = Get-Command codex -ErrorAction Stop
$codexDirectory = [Environment]::GetEnvironmentVariable('CODEX_HOME', 'Process')
if (!$codexDirectory) { $codexDirectory = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex' }
$codexDirectory = [IO.Path]::GetFullPath($codexDirectory)
$configPath = Join-Path $codexDirectory 'config.toml'
$instructionsPath = Join-Path $codexDirectory 'AGENTS.md'
$overridePath = Join-Path $codexDirectory 'AGENTS.override.md'
if ((Test-Path -LiteralPath $overridePath) -and [IO.File]::ReadAllText($overridePath).Trim()) { $instructionsPath = $overridePath }
$existingInstructions = if (Test-Path -LiteralPath $instructionsPath) { [IO.File]::ReadAllText($instructionsPath) } else { '' }
$begin = '<!-- projectbrain:begin -->'; $end = '<!-- projectbrain:end -->'
if ($existingInstructions.Contains($begin) -ne $existingInstructions.Contains($end)) { throw 'Repair the incomplete ProjectBrain instruction markers before setup.' }
$workflowPath = Join-Path $PSScriptRoot '../docs/agent-rules/PROJECTBRAIN_GLOBAL_INSTRUCTIONS.md'
$block = $begin + "`n" + [IO.File]::ReadAllText([IO.Path]::GetFullPath($workflowPath)).Trim() + "`n" + $end
if ($existingInstructions.Contains($begin)) {
    $matcher = New-Object System.Text.RegularExpressions.Regex('(?s)<!-- projectbrain:begin -->.*?<!-- projectbrain:end -->')
    $nextInstructions = $matcher.Replace($existingInstructions, [Text.RegularExpressions.MatchEvaluator]{ param($match) $block })
} else { $nextInstructions = if ($existingInstructions.Trim()) { $existingInstructions.TrimEnd() + "`n`n" + $block + "`n" } else { $block + "`n" } }
$backupDirectory = Join-Path $codexDirectory ('backups/projectbrain-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$null = New-Item -ItemType Directory -Path $backupDirectory -Force
$configExisted = Test-Path -LiteralPath $configPath
$instructionsExisted = Test-Path -LiteralPath $instructionsPath
if ($configExisted) { Copy-Item -LiteralPath $configPath -Destination (Join-Path $backupDirectory 'config.toml') }
if ($instructionsExisted) { Copy-Item -LiteralPath $instructionsPath -Destination (Join-Path $backupDirectory ([IO.Path]::GetFileName($instructionsPath))) }
$previousProcessToken = [Environment]::GetEnvironmentVariable('PROJECTBRAIN_AGENT_TOKEN', 'Process')
$previousUserToken = if (!$SessionOnly) { [Environment]::GetEnvironmentVariable('PROJECTBRAIN_AGENT_TOKEN', 'User') } else { $null }
try {
    [Environment]::SetEnvironmentVariable('PROJECTBRAIN_AGENT_TOKEN', $token, 'Process')
    if (!$SessionOnly) { [Environment]::SetEnvironmentVariable('PROJECTBRAIN_AGENT_TOKEN', $token, 'User') }
    & $codexCommand.Source mcp add projectbrain --url $ServerUrl --bearer-token-env-var PROJECTBRAIN_AGENT_TOKEN
    if ($LASTEXITCODE) { throw 'Codex MCP registration failed.' }
    $configuration = [IO.File]::ReadAllText($configPath)
    $rootTable = [regex]::Match($configuration, '(?ms)(^\[mcp_servers\.projectbrain\][ \t]*\r?\n)(.*?)(?=^\[|\z)')
    if (!$rootTable.Success) { throw 'Codex did not create the expected ProjectBrain configuration table.' }
    $body = $rootTable.Groups[2].Value
    $policy = 'default_tools_approval_mode = "writes"'
    if ($body -match '(?m)^default_tools_approval_mode[ \t]*=') {
        $body = [regex]::Replace($body, '(?m)^default_tools_approval_mode[ \t]*=.*$', $policy)
    } else { $body = $policy + "`n" + $body }
    $configuration = $configuration.Substring(0, $rootTable.Index) + $rootTable.Groups[1].Value + $body + $configuration.Substring($rootTable.Index + $rootTable.Length)
    [IO.File]::WriteAllText($configPath, $configuration, $utf8)
    [IO.File]::WriteAllText($instructionsPath, $nextInstructions, $utf8)
    $registration = & $codexCommand.Source mcp get projectbrain --json
    if ($LASTEXITCODE) { throw 'Codex could not read the registered MCP server.' }
    $registered = $registration | ConvertFrom-Json
    if ($registered.transport.url -ne $ServerUrl -or $registered.transport.bearer_token_env_var -ne 'PROJECTBRAIN_AGENT_TOKEN') { throw 'Codex registration does not match the requested local connection.' }
} catch {
    if ($configExisted) { Copy-Item -LiteralPath (Join-Path $backupDirectory 'config.toml') -Destination $configPath -Force }
    elseif (Test-Path -LiteralPath $configPath) { Remove-Item -LiteralPath $configPath }
    if ($instructionsExisted) { Copy-Item -LiteralPath (Join-Path $backupDirectory ([IO.Path]::GetFileName($instructionsPath))) -Destination $instructionsPath -Force }
    elseif (Test-Path -LiteralPath $instructionsPath) { Remove-Item -LiteralPath $instructionsPath }
    [Environment]::SetEnvironmentVariable('PROJECTBRAIN_AGENT_TOKEN', $previousProcessToken, 'Process')
    if (!$SessionOnly) { [Environment]::SetEnvironmentVariable('PROJECTBRAIN_AGENT_TOKEN', $previousUserToken, 'User') }
    throw
}
Write-Output 'ProjectBrain is registered in the Codex user configuration.'
Write-Output ('Global retrieval instructions: ' + $instructionsPath)
Write-Output ('Previous files backed up under: ' + $backupDirectory)
if (!$SessionOnly) { Write-Output 'Agent credential saved in the Windows user environment; no secret was written to project instructions or Codex config.' }
Write-Output 'Restart an already running Codex app so it loads the new connection and user environment.'
