#requires -Version 7.6
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('iex', 'test')]
    [string] $Feed,

    [ValidatePattern('^[A-Za-z0-9_-]{1,64}$')]
    [string] $SessionId = "alpaca-$Feed-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))",

    [switch] $PreflightOnly,

    # Retained for compatibility; validation always enables instrumentation.
    [switch] $Instrument,

    [ValidateRange(5, 3600)]
    [int] $DurationSeconds = 600,

    [string] $DiagnosticsDirectory,

    [string] $DiagnosticsRoot,

    [ValidateRange(1024, 65535)]
    [int] $Port = 5291,

    [string] $ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repositoryRoot 'src\MarketOpportunity.Web\MarketOpportunity.Web.csproj'
$database = 'MarketOpportunityEngine_LiveValidationTests'
$Instrument = $true
if (-not $PSBoundParameters.ContainsKey('DurationSeconds') -and $Feed -eq 'test') {
    $DurationSeconds = 30
}

function Get-ValidationConnectionString {
    param([string] $WebOutput, [string] $Database)

    $sqlClientPath = Join-Path $WebOutput 'runtimes\win\lib\net9.0\Microsoft.Data.SqlClient.dll'
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    $sniPath = Join-Path $WebOutput "runtimes\win-$architecture\native\Microsoft.Data.SqlClient.SNI.dll"
    [void] [System.Runtime.InteropServices.NativeLibrary]::Load($sniPath)
    Add-Type -LiteralPath $sqlClientPath
    $connection = [Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:MARKETOPPORTUNITY_SQL_TEST_CONNECTION)) {
        try {
            $connection.set_ConnectionString($env:MARKETOPPORTUNITY_SQL_TEST_CONNECTION)
        }
        catch {
            throw 'MARKETOPPORTUNITY_SQL_TEST_CONNECTION is not a valid SQL Server connection string.'
        }
        if ($connection['Initial Catalog'] -ne $Database -or $connection['Data Source'] -ne 'localhost,1433') {
            throw "The supplied SQL test connection must target localhost,1433 and $Database; it was not replaced."
        }
    }
    else {
        $password = docker compose exec -T sqlserver printenv MSSQL_SA_PASSWORD
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($password)) {
            throw 'Cannot read the running local SQL container credential; start the Compose SQL service first.'
        }
        $connection['Data Source'] = 'localhost,1433'
        $connection['Initial Catalog'] = $Database
        $connection['User ID'] = 'sa'
        $connection['Password'] = [string] $password
        $connection['Encrypt'] = 'True'
        $connection['Trust Server Certificate'] = $true
        $password = $null
    }

    $sql = [Microsoft.Data.SqlClient.SqlConnection]::new($connection.get_ConnectionString())
    try {
        $sql.Open()
        $command = $sql.CreateCommand()
        try {
            $command.CommandText = 'SELECT DB_NAME()'
            if ($command.ExecuteScalar() -ne $Database) {
                throw 'Unexpected validation database.'
            }
        }
        finally {
            $command.Dispose()
        }
    }
    catch {
        $cause = $_.Exception
        while ($null -ne $cause.InnerException) {
            $cause = $cause.InnerException
        }
        throw "Dedicated SQL database verification failed. Run the Compose validation initializer and verify local SQL access. No app was started. Error type: $($cause.GetType().Name)"
    }
    finally {
        $sql.Dispose()
    }
    return $connection.get_ConnectionString()
}

$environmentNames = @(
    'ConnectionStrings__MarketOpportunity',
    'DOTNET_ENVIRONMENT',
    'ASPNETCORE_ENVIRONMENT'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

Push-Location $repositoryRoot
try {
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $configuredCount = 1
    $symbols = @('FAKEPACA')
    if ($Feed -eq 'iex') {
        [xml] $projectXml = Get-Content -LiteralPath $project -Raw
        $secretsId = [string] $projectXml.Project.PropertyGroup.UserSecretsId |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        if ([string]::IsNullOrWhiteSpace($secretsId)) {
            throw 'The Web project has no UserSecretsId.'
        }

        $secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId\secrets.json"
        $secrets = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json -AsHashtable
        $configuredSymbols = $secrets['Scanner:Universe:Symbols']
        if ($configuredSymbols -isnot [string] -or [string]::IsNullOrWhiteSpace($configuredSymbols)) {
            throw 'Scanner:Universe:Symbols must contain the comma-separated development universe.'
        }

        $entries = @($configuredSymbols.Split(','))
        $configuredCount = $entries.Count
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $symbols = @(foreach ($entry in $entries) {
            $symbol = $entry.Trim()
            if ([string]::IsNullOrWhiteSpace($symbol)) {
                throw 'Scanner:Universe:Symbols must not contain empty entries.'
            }
            $symbol = $symbol.ToUpperInvariant()
            if ($seen.Add($symbol)) { $symbol }
        })
        $secrets = $null
    }

    $buildArguments = @()
    $webOutput = Join-Path $repositoryRoot 'src\MarketOpportunity.Web\bin\Debug\net10.0'
    if (-not [string]::IsNullOrWhiteSpace($ArtifactsPath)) {
        $ArtifactsPath = [IO.Path]::GetFullPath($ArtifactsPath)
        $buildArguments = @('--artifacts-path', $ArtifactsPath)
        $webOutput = Join-Path $ArtifactsPath 'bin\MarketOpportunity.Web\debug'
    }
    dotnet build $project @buildArguments --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Web project build failed; the validation app was not started.'
    }

    $connectionString = Get-ValidationConnectionString -WebOutput $webOutput -Database $database

    $diagnosticArguments = @()
    if ($Instrument) {
        if ([string]::IsNullOrWhiteSpace($DiagnosticsDirectory)) {
            if ($PSBoundParameters.ContainsKey('DiagnosticsDirectory')) {
                throw '-DiagnosticsDirectory must not be empty.'
            }
            if ([string]::IsNullOrWhiteSpace($DiagnosticsRoot)) {
                if ($PSBoundParameters.ContainsKey('DiagnosticsRoot')) {
                    throw '-DiagnosticsRoot must not be empty.'
                }
                $DiagnosticsRoot = Join-Path $repositoryRoot 'artifacts\alpaca-validation'
            }
            $DiagnosticsDirectory = Join-Path $DiagnosticsRoot $SessionId
        }
        $DiagnosticsDirectory = [IO.Path]::GetFullPath($DiagnosticsDirectory)
        [void] [IO.Directory]::CreateDirectory($DiagnosticsDirectory)
        $probePath = Join-Path $DiagnosticsDirectory "$([Guid]::NewGuid().ToString('N')).write-test"
        $probe = $null
        try {
            $probe = [IO.File]::Open($probePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $probe.WriteByte(0)
            $probe.Flush($true)
        }
        finally {
            if ($null -ne $probe) {
                $probe.Dispose()
                [IO.File]::Delete($probePath)
            }
        }
        $clock = [ordered] @{
            SessionId = $SessionId
            CapturedUtc = [DateTimeOffset]::UtcNow.ToString('O')
            Clock = 'TimeProvider.System / host OS UTC'
            AuthoritativeOffsetOrErrorBound = 'UNAVAILABLE: w32tm status is not an independent UTC offset/error-bound measurement.'
            Query = 'w32tm /query /status /verbose (read-only; no resynchronization)'
            ExitCode = $null
            Status = @()
        }
        if ($IsWindows) {
            $clockProcess = [Diagnostics.Process]::new()
            $clockProcess.StartInfo = [Diagnostics.ProcessStartInfo] @{
                FileName = 'w32tm.exe'
                Arguments = '/query /status /verbose'
                UseShellExecute = $false
                RedirectStandardOutput = $true
                RedirectStandardError = $true
                CreateNoWindow = $true
            }
            $clockOutput = @()
            try {
                [void] $clockProcess.Start()
                $stdout = $clockProcess.StandardOutput.ReadToEndAsync()
                $stderr = $clockProcess.StandardError.ReadToEndAsync()
                if ($clockProcess.WaitForExit(5000)) {
                    $clock.ExitCode = $clockProcess.ExitCode
                    $clockOutput = $stdout.GetAwaiter().GetResult().Split("`n")
                    [void] $stderr.GetAwaiter().GetResult()
                }
                else {
                    $clockProcess.Kill()
                    $clockProcess.WaitForExit()
                }
            }
            catch [System.ComponentModel.Win32Exception] {
                $clock.Status = @('Windows time-service query unavailable; synchronization state is UNKNOWN.')
            }
            finally {
                $clockProcess.Dispose()
            }
            if ($clock.ExitCode -eq 0) {
                $clock.Status = @($clockOutput | Where-Object {
                    $_ -match '^\s*(Leap Indicator|Stratum|Precision|Root Delay|Root Dispersion|Last Successful Sync Time|Source|Poll Interval|Phase Offset|State Machine|Time Source Flags|Server Role|Last Sync Error|Time since Last Good Sync Time)\s*:'
                } | ForEach-Object { ([string] $_).Substring(0, [Math]::Min(([string] $_).Length, 256)) })
            }
            else {
                $clock.Status = @('Clock status query failed; synchronization state is UNKNOWN. No elevation or clock change attempted.')
            }
        }
        else {
            $clock.Status = @('Windows time-service diagnostics unavailable on this host; synchronization state is UNKNOWN.')
        }
        $clockPath = Join-Path $DiagnosticsDirectory "$SessionId.clock.json"
        $clock | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath $clockPath -Encoding utf8
        if (-not (Test-Path -LiteralPath $clockPath -PathType Leaf)) {
            throw 'Clock diagnostic was not created; the validation app was not started.'
        }
        Write-Host "Host clock diagnostics saved. $($clock.AuthoritativeOffsetOrErrorBound)"
        $diagnosticArguments = @(
            '--MarketData:Diagnostics:Enabled', 'true',
            '--MarketData:Diagnostics:DurationSeconds', [string] $DurationSeconds,
            '--MarketData:Diagnostics:Directory', $DiagnosticsDirectory
        )
    }

    if (-not $Instrument -or $diagnosticArguments.Count -eq 0) {
        throw 'Validation requires diagnostics; the validation app was not started.'
    }
    $listeners = @()
    try {
        $addresses = @([Net.IPAddress]::Loopback)
        if ([Net.Sockets.Socket]::OSSupportsIPv6) {
            $addresses += [Net.IPAddress]::IPv6Loopback
        }
        foreach ($address in $addresses) {
            $listener = [Net.Sockets.TcpListener]::new($address, $Port)
            $listeners += $listener
            $listener.ExclusiveAddressUse = $true
            $listener.Start()
        }
    }
    catch [Net.Sockets.SocketException] {
        throw "Port $Port is unavailable on localhost; the validation app was not started."
    }
    finally {
        foreach ($listener in $listeners) {
            $listener.Stop()
        }
    }

    Write-Host "Feed=$Feed SessionId=$SessionId DurationSeconds=$DurationSeconds Port=$Port"
    Write-Host "Environment=Development Database=$database DistinctSymbols=$($symbols.Count) ConfiguredEntries=$configuredCount"
    Write-Host "DiagnosticsDirectory=$DiagnosticsDirectory InstrumentationEnabled=$Instrument"
    Write-Host 'This is a requested universe, not proof of subscription or activity.'
    if ($PreflightOnly) {
        Write-Host 'Preflight passed. No market-data connection was opened; schema creation waits for app startup.'
        return
    }

    if ($Feed -eq 'iex') {
        Write-Host 'IEX is actual exchange data with Limited coverage. Validate during an open U.S. equity session.'
    }
    else {
        Write-Host 'FAKEPACA is synthetic data, not exchange data and not evidence of Milestone A.1 completion.'
    }

    $env:ConnectionStrings__MarketOpportunity = $connectionString
    Write-Host "Open http://localhost:$Port/ and use Ctrl+C in this terminal for normal shutdown."
    $launcherResultPath = Join-Path $DiagnosticsDirectory "$SessionId.launcher-result.json"
    $processStartedUtc = [DateTimeOffset]::UtcNow
    $processExitCode = $null
    $global:LASTEXITCODE = $null
    try {
        dotnet run --project $project --no-build --no-launch-profile @buildArguments -- `
            --urls "http://localhost:$Port" `
            --MarketData:Provider Alpaca `
            --MarketData:Alpaca:Feed $Feed `
            --Scanner:Universe:Symbols ($symbols -join ',') `
            --MarketData:Alpaca:SessionId $SessionId @diagnosticArguments
        $processExitCode = $LASTEXITCODE
    }
    finally {
        # Also runs when Ctrl+C stops this script after the child exits.
        if ($null -eq $processExitCode -and $LASTEXITCODE -is [int]) {
            $processExitCode = $LASTEXITCODE
        }
        $processExitedUtc = [DateTimeOffset]::UtcNow
        $launcherResult = [ordered] @{
            SessionId = $SessionId
            Feed = $Feed
            ConfiguredDurationSeconds = $DurationSeconds
            ProcessStartedUtc = $processStartedUtc.ToString('O')
            ProcessExitedUtc = $processExitedUtc.ToString('O')
            ProcessExitCode = $processExitCode
            ProcessExitCodeCaptured = $null -ne $processExitCode
            DiagnosticsPath = Join-Path $DiagnosticsDirectory "$SessionId.diagnostics.json"
            ClockPath = Join-Path $DiagnosticsDirectory "$SessionId.clock.json"
            ApplicationLogPath = Join-Path $DiagnosticsDirectory "$SessionId.app.log"
        }
        $launcherResult | ConvertTo-Json -Depth 3 |
            Set-Content -LiteralPath $launcherResultPath -Encoding utf8
        Write-Host "Launcher result saved: $launcherResultPath ProcessExitCode=$processExitCode"
    }
    if ($null -eq $processExitCode) {
        throw 'The validation application exit code was not captured. Inspect its sanitized session log.'
    }
    if ($processExitCode -ne 0) {
        throw "The validation application exited with code $processExitCode. Inspect its sanitized session log."
    }
}
finally {
    foreach ($name in $environmentNames) {
        if ($null -eq $previousEnvironment[$name]) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
        }
    }
    Pop-Location
}
