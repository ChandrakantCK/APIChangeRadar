<#
.SYNOPSIS
    Runs .NET commands for this repo inside the official Linux .NET SDK container.

.DESCRIPTION
    Windows Smart App Control can block freshly built, unsigned DLLs. Running builds,
    tests, migrations and the API inside Docker means Windows never loads them.

    The repo is copied into the container (without bin/obj), so Linux build output never
    mixes with Visual Studio's. Only new migration files are copied back.

.EXAMPLE
    .\scripts\dev.ps1 build
    .\scripts\dev.ps1 test
    .\scripts\dev.ps1 migration-add AddRefreshTokens
    .\scripts\dev.ps1 migration-remove
    .\scripts\dev.ps1 db-update
    .\scripts\dev.ps1 run
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('build', 'test', 'migration-add', 'migration-remove', 'db-update', 'run')]
    [string]$Command,

    [Parameter(Position = 1)]
    [string]$Name
)

$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sdkImage = 'mcr.microsoft.com/dotnet/sdk:10.0'
$composeNetwork = 'apichangeradar_default'
$efArgs = '--project src/Radar.Infrastructure --startup-project src/Radar.Api'
$migrationsDir = 'src/Radar.Infrastructure/Persistence/Migrations'

# Copy the repo into /w inside the container, skipping build output and things .NET doesn't need.
$copyIn = 'mkdir -p /w && tar -C /src --exclude=./.git --exclude=./.vs --exclude=./web --exclude=./docs --exclude=bin --exclude=obj -cf - . | tar -C /w -xf - && cd /w'
$restore = 'dotnet tool restore >/dev/null && dotnet restore --verbosity quiet'

function Test-Docker {
    docker info --format '{{.ServerVersion}}' 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker is not running. Start Docker Desktop, then run: docker compose up -d --wait'
    }
}

function Get-UserSecrets {
    $csproj = Get-Content (Join-Path $repo 'src/Radar.Api/Radar.Api.csproj') -Raw
    if ($csproj -notmatch '<UserSecretsId>([^<]+)</UserSecretsId>') {
        throw 'No UserSecretsId in src/Radar.Api/Radar.Api.csproj.'
    }
    $id = $Matches[1]
    $path = Join-Path $env:APPDATA "Microsoft\UserSecrets\$id\secrets.json"
    if (-not (Test-Path $path)) {
        throw "User secrets not found at $path. Set them with: dotnet user-secrets set ... --project src/Radar.Api"
    }
    return [pscustomobject]@{ Id = $id; Path = $path; Values = (Get-Content $path -Raw | ConvertFrom-Json) }
}

# The user-secret connection string points at localhost; inside Docker the database is the 'postgres' service.
function Get-ContainerConnectionString($secrets) {
    $value = $secrets.Values.'ConnectionStrings:Database'
    if (-not $value) {
        throw 'User secret ConnectionStrings:Database is not set.'
    }
    return ($value -replace 'Host=localhost', 'Host=postgres' -replace 'Host=127\.0\.0\.1', 'Host=postgres')
}

function Invoke-Sdk([string[]]$DockerArgs, [string]$Script) {
    $all = @('run', '--rm', '-v', "${repo}:/src", '-v', 'radar-nuget:/root/.nuget/packages') + $DockerArgs + @($sdkImage, 'bash', '-c', "set -e; $copyIn; $restore; $Script")

    # Windows PowerShell treats anything a native program writes to stderr as an error under 'Stop';
    # docker and dotnet write normal progress there, so judge success by the exit code instead.
    $ErrorActionPreference = 'Continue'
    & docker @all
    $ErrorActionPreference = 'Stop'

    if ($LASTEXITCODE -ne 0) {
        throw "'$Command' failed (exit code $LASTEXITCODE)."
    }
}

Test-Docker

switch ($Command) {
    'build' {
        Invoke-Sdk @() 'dotnet build --no-restore -warnaserror'
    }

    'test' {
        # Testcontainers starts its own Postgres through the Docker socket.
        $dockerArgs = @('-v', '/var/run/docker.sock:/var/run/docker.sock', '-e', 'TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal')
        Invoke-Sdk $dockerArgs 'dotnet test --no-restore'
    }

    'migration-add' {
        if (-not $Name) {
            throw 'Usage: .\scripts\dev.ps1 migration-add <MigrationName>'
        }
        # dotnet ef only builds the app here; it needs some connection string but never connects.
        $dockerArgs = @('-e', 'ConnectionStrings__Database=Host=unused;Database=unused')
        Invoke-Sdk $dockerArgs "dotnet ef migrations add $Name $efArgs --output-dir Persistence/Migrations && rm -rf /src/$migrationsDir && cp -r /w/$migrationsDir /src/$migrationsDir"
        Write-Host "Migration '$Name' created in $migrationsDir. Read it before applying it." -ForegroundColor Green
    }

    'migration-remove' {
        # Connects to your local database to make sure the last migration hasn't been applied yet.
        $secrets = Get-UserSecrets
        $dockerArgs = @('--network', $composeNetwork, '-e', "ConnectionStrings__Database=$(Get-ContainerConnectionString $secrets)")
        Invoke-Sdk $dockerArgs "dotnet ef migrations remove $efArgs && rm -rf /src/$migrationsDir && cp -r /w/$migrationsDir /src/$migrationsDir"
    }

    'db-update' {
        $secrets = Get-UserSecrets
        $dockerArgs = @('--network', $composeNetwork, '-e', "ConnectionStrings__Database=$(Get-ContainerConnectionString $secrets)")
        Invoke-Sdk $dockerArgs "dotnet ef database update $efArgs"
    }

    'run' {
        $secrets = Get-UserSecrets
        # Same user-secrets file as Visual Studio uses, mounted read-only where .NET looks for it on Linux.
        $dockerArgs = @()
        if (-not [Console]::IsInputRedirected) {
            # Interactive terminal: Ctrl+C reaches the API and stops it.
            $dockerArgs += '-it'
        }
        $dockerArgs += @(
            '--init', '--name', 'radar-api-dev', '--network', $composeNetwork, '-p', '5214:5214',
            '-v', "$($secrets.Path):/root/.microsoft/usersecrets/$($secrets.Id)/secrets.json:ro",
            '-e', 'ASPNETCORE_ENVIRONMENT=Development',
            '-e', "ConnectionStrings__Database=$(Get-ContainerConnectionString $secrets)",
            '-e', 'Email__Smtp__Host=mailpit'
        )
        Write-Host 'Starting the API on http://localhost:5214 (Ctrl+C to stop)...' -ForegroundColor Green
        Invoke-Sdk $dockerArgs 'dotnet run --no-restore --project src/Radar.Api --no-launch-profile --urls http://0.0.0.0:5214'
    }
}
