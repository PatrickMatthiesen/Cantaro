param()

# Requires Docker and the repository's dotnet-ef tool. Runs only against a new,
# disposable PostgreSQL container, with no host ports or application volumes.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$containerName = 'cantaro-observation-migration-' + [Guid]::NewGuid().ToString('N')
$scratch = Join-Path ([IO.Path]::GetTempPath()) $containerName
$previousMigration = '20260830204931_AddTrackMatchRetryCycleCount'

function Assert-CommandSucceeded {
    if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE" }
}

function Invoke-SqlFile([string] $path, [string] $name) {
    docker cp $path "${containerName}:/tmp/$name"
    Assert-CommandSucceeded
    docker exec $containerName psql -q -U postgres -d cantaro -v ON_ERROR_STOP=1 -f "/tmp/$name"
    Assert-CommandSucceeded
}

New-Item -ItemType Directory -Path $scratch | Out-Null
Push-Location $repoRoot
try {
    dotnet build src/Cantaro.Api --nologo --verbosity quiet
    Assert-CommandSucceeded
    dotnet ef migrations script 0 $previousMigration --project src/Cantaro.Api --no-build --output (Join-Path $scratch 'before.sql')
    Assert-CommandSucceeded
    dotnet ef migrations script $previousMigration MinimizeMediaObservationStorage --project src/Cantaro.Api --no-build --output (Join-Path $scratch 'upgrade.sql')
    Assert-CommandSucceeded
    docker run --detach --rm --name $containerName --network none -e POSTGRES_PASSWORD=disposable-test -e POSTGRES_DB=cantaro postgres:17
    Assert-CommandSucceeded
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        # The image briefly starts a bootstrap server before restarting. Wait
        # for initialization to finish before probing the final server.
        $startupLog = docker logs $containerName 2>&1 | Out-String
        if ($startupLog.Contains('PostgreSQL init process complete; ready for start up.')) {
            docker exec $containerName pg_isready -U postgres -d cantaro *> $null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'Disposable PostgreSQL did not become ready.' }
    Invoke-SqlFile (Join-Path $scratch 'before.sql') 'before.sql'
    Invoke-SqlFile (Join-Path $repoRoot 'tests/Cantaro.Api.Tests/Fixtures/media-observation-migration-before.sql') 'seed.sql'
    Invoke-SqlFile (Join-Path $scratch 'upgrade.sql') 'upgrade.sql'
    Invoke-SqlFile (Join-Path $repoRoot 'tests/Cantaro.Api.Tests/Fixtures/media-observation-migration-after.sql') 'assert.sql'
    dotnet ef migrations script MinimizeMediaObservationStorage RemoveCompletedObservationMetadata --project src/Cantaro.Api --no-build --output (Join-Path $scratch 'lifecycle.sql')
    Assert-CommandSucceeded
    Invoke-SqlFile (Join-Path $repoRoot 'tests/Cantaro.Api.Tests/Fixtures/media-observation-lifecycle-before.sql') 'lifecycle-seed.sql'
    Invoke-SqlFile (Join-Path $scratch 'lifecycle.sql') 'lifecycle.sql'
    Invoke-SqlFile (Join-Path $repoRoot 'tests/Cantaro.Api.Tests/Fixtures/media-observation-lifecycle-after.sql') 'lifecycle-assert.sql'
    Write-Output 'Media observation PostgreSQL migration assertions passed.'
}
finally {
    docker rm --force $containerName 2>$null | Out-Null
    Pop-Location
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    $expectedScratch = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) $containerName))
    if ($resolvedScratch -eq $expectedScratch -and (Split-Path $resolvedScratch -Leaf) -eq $containerName) {
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }
}
