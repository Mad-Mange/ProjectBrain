# Uses only the dedicated synthetic fixture mount in .local/compose-fixtures.
param([switch]$Browser, [ValidateRange(1024,65535)][int]$ApiPort=14317, [ValidateRange(1024,65535)][int]$WebPort=14318)
$ErrorActionPreference='Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $env:PROJECTBRAIN_API_PORT="$ApiPort"
    $env:PROJECTBRAIN_WEB_PORT="$WebPort"
    $apiUrl="http://127.0.0.1:$ApiPort"
    $env:PROJECTBRAIN_E2E_API_URL=$apiUrl
    $env:PROJECTBRAIN_E2E_WEB_URL="http://127.0.0.1:$WebPort"
    $fixture=Join-Path $PWD '.local/compose-fixtures'
    New-Item -ItemType Directory -Force -Path $fixture | Out-Null
    Copy-Item -Path 'tests/fixtures/*' -Destination $fixture -Recurse -Force
    'Synthetic read-only smoke fixture.' | Set-Content (Join-Path $fixture 'README.md')
    $env:PROJECTS_ROOT=$fixture.Replace('\','/')
    $randomBytes=New-Object byte[] 32
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($randomBytes)
    $rng.Dispose()
    $env:PROJECTBRAIN_AUTH_TOKEN=[Convert]::ToBase64String($randomBytes)
    $env:PROJECTBRAIN_AGENT_TOKEN=''
    docker compose -p projectbrain-smoke config --quiet
    if($LASTEXITCODE){throw 'Invalid Compose config'}
    docker compose -p projectbrain-smoke up -d --build --wait
    if($LASTEXITCODE){throw 'Compose did not become healthy'}
    $runtimeUid=docker compose -p projectbrain-smoke exec -T api id -u
    if($LASTEXITCODE -or $runtimeUid -eq '0'){throw 'API must run as a non-root user'}
    docker compose -p projectbrain-smoke exec -T api touch /sources/projects/.projectbrain-readonly-probe
    if($LASTEXITCODE -eq 0){throw 'Source mount unexpectedly allows writes'}
    $headers=@{Authorization='Bearer '+$env:PROJECTBRAIN_AUTH_TOKEN}
    $capture=Invoke-RestMethod "$apiUrl/api/v1/captures" -Method Post -Headers $headers -ContentType application/json -Body '{"originalText":"Compose persistence fixture","intentFlag":"try_next"}'
    docker compose -p projectbrain-smoke restart
    if($LASTEXITCODE){throw 'Restart failed'}
    $ready=$false
    for($i=0;$i -lt 30;$i++){
        try { $null=Invoke-RestMethod "$apiUrl/health/ready"; $ready=$true; break } catch { Start-Sleep -Seconds 1 }
    }
    if(!$ready){throw 'API not ready after restart'}
    $item=Invoke-RestMethod ($apiUrl+'/api/v1/knowledge/'+$capture.knowledgeId) -Headers $headers
    if($item.intentFlag -ne 'try_next' -or $item.isVerified){throw 'Persistence or trust invariant failed'}
    python scripts/mcp-smoke.py
    if($LASTEXITCODE){throw 'MCP protocol smoke failed'}
    if($Browser){
        $env:PROJECTBRAIN_E2E_EXTERNAL='1'
        $env:PROJECTBRAIN_E2E_SOURCES=$fixture
        npm.cmd --prefix frontend run e2e
        if($LASTEXITCODE){throw 'Container browser journey failed'}
        $beforeRuns=Invoke-RestMethod "$apiUrl/api/v1/experiments" -Headers $headers
        $beforeJson=ConvertTo-Json -InputObject $beforeRuns -Depth 20 -Compress
        docker compose -p projectbrain-smoke restart api
        if($LASTEXITCODE){throw 'Post-experiment restart failed'}
        docker compose -p projectbrain-smoke up -d --wait
        if($LASTEXITCODE){throw 'API unhealthy after post-experiment restart'}
        $afterRuns=Invoke-RestMethod "$apiUrl/api/v1/experiments" -Headers $headers
        $afterJson=ConvertTo-Json -InputObject $afterRuns -Depth 20 -Compress
        if($beforeJson -ne $afterJson){throw 'Experiment history changed across restart'}
    }
    docker compose -p projectbrain-smoke up -d --wait
    if($LASTEXITCODE){throw 'Services unhealthy after restart'}
    docker compose -p projectbrain-smoke ps
    docker compose -p projectbrain-smoke ps --format json | Set-Content .local/compose-services.json
    docker compose -p projectbrain-smoke logs --no-color | Set-Content .local/compose-smoke.log
    'PASS: Compose startup, non-root, source read-only, capture, restart persistence, real MCP search.'
} catch {
    docker compose -p projectbrain-smoke logs --no-color --tail 50
    throw
} finally {
    # Keep the isolated volume for review; do not delete any data automatically.
    docker compose -p projectbrain-smoke down
    Pop-Location
}


