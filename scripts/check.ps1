$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet restore ProjectBrain.slnx --configfile NuGet.Config
    if ($LASTEXITCODE) { throw 'NuGet restore failed' }
    dotnet build ProjectBrain.slnx --no-restore
    if ($LASTEXITCODE) { throw '.NET build failed' }
    dotnet test ProjectBrain.slnx --no-build
    if ($LASTEXITCODE) { throw '.NET tests failed' }
    npm.cmd --prefix frontend ci
    if ($LASTEXITCODE) { throw 'npm install failed' }
    npm.cmd --prefix frontend run build
    if ($LASTEXITCODE) { throw 'Frontend build failed' }
    npm.cmd --prefix frontend test
    if ($LASTEXITCODE) { throw 'Frontend tests failed' }
    docker compose config --quiet
    if ($LASTEXITCODE) { throw 'Compose validation failed' }
} finally { Pop-Location }

