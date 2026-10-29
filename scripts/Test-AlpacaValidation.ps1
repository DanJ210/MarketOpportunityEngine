#requires -Version 7.6
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$source = Join-Path $PSScriptRoot 'Start-AlpacaValidation.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref] $tokens, [ref] $parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Launcher has PowerShell parse errors.' }

# Intercept only the SQL boundary. The actual launcher body, filesystem checks,
# clock artifact, environment handling and port checks execute offline.
$boundary = $ast.Find({
    param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Get-ValidationConnectionString'
}, $false)
if ($null -eq $boundary) { throw 'SQL interception boundary not found.' }
$replacement = @'
function Get-ValidationConnectionString {
    param([string] $WebOutput, [string] $Database)
    $testState.DatabaseChecks++
    if ($testState.DatabaseFailure) { throw 'Intercepted dedicated database verification failure.' }
    if ($Database -ne 'MarketOpportunityEngine_LiveValidationTests') { throw 'Wrong database requested.' }
    return 'Server=localhost,1433;Database=MarketOpportunityEngine_LiveValidationTests;Integrated Security=true'
}
'@
$text = [IO.File]::ReadAllText($source)
$text = $text.Remove($boundary.Extent.StartOffset, $boundary.Extent.EndOffset - $boundary.Extent.StartOffset).
    Insert($boundary.Extent.StartOffset, $replacement)
$root = Join-Path ([IO.Path]::GetTempPath()) "moe-launcher-tests-$([Guid]::NewGuid().ToString('N'))"
$launcher = Join-Path $root 'scripts\Start-AlpacaValidation.ps1'
$environmentNames = @('APPDATA', 'ConnectionStrings__MarketOpportunity', 'DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT')
$originalEnvironment = @{}
foreach ($name in $environmentNames) {
    $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$originalLocation = (Get-Location).Path
$testState = @{
    Calls = [Collections.Generic.List[object]]::new()
    Passed = 0
    BuildExitCode = 0
    RunExitCode = 0
    DatabaseFailure = $false
    DatabaseChecks = 0
    Output = @()
}

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Get-AppArgument {
    param([string] $Name)
    $run = @($testState.Calls | Where-Object { $_[0] -eq 'run' })
    Assert-True ($run.Count -eq 1) 'Expected exactly one intercepted app launch.'
    $arguments = $run[0]
    $index = [Array]::IndexOf($arguments, $Name)
    Assert-True ($index -ge 0 -and $index + 1 -lt $arguments.Count) "Missing app argument $Name."
    return $arguments[$index + 1]
}

function dotnet {
    $testState.Calls.Add(@($args))
    $global:LASTEXITCODE = 0
    switch ($args[0]) {
        'build' { $global:LASTEXITCODE = $testState.BuildExitCode }
        'run' {
            # Never invoke a native app, even if an assertion fails.
            Assert-True ($testState.DatabaseChecks -eq 1) 'App launched before dedicated database check.'
            Assert-True ($env:DOTNET_ENVIRONMENT -eq 'Development') 'DOTNET environment is not explicit.'
            Assert-True ($env:ASPNETCORE_ENVIRONMENT -eq 'Development') 'ASPNETCORE environment is not explicit.'
            Assert-True ($env:ConnectionStrings__MarketOpportunity -match 'Database=MarketOpportunityEngine_LiveValidationTests') 'App database override missing.'
            Assert-True ((Get-AppArgument '--MarketData:Diagnostics:Enabled') -eq 'true') 'Instrumentation disabled.'
            $directory = Get-AppArgument '--MarketData:Diagnostics:Directory'
            $session = Get-AppArgument '--MarketData:Alpaca:SessionId'
            $clock = Join-Path $directory "$session.clock.json"
            Assert-True (Test-Path -LiteralPath $clock -PathType Leaf) 'App launched before clock diagnostic creation.'
            $clockData = Get-Content -LiteralPath $clock -Raw | ConvertFrom-Json
            Assert-True (-not [string]::IsNullOrWhiteSpace($clockData.CapturedUtc)) 'Invalid clock diagnostic.'
            $global:LASTEXITCODE = $testState.RunExitCode
        }
        default { throw 'Unexpected dotnet operation; no native execution is allowed.' }
    }
}

function Invoke-LauncherCase {
    param([string] $Name, [hashtable] $Parameters, [string] $FailurePattern)
    $testState.Calls.Clear()
    $testState.DatabaseChecks = 0
    $before = @{}
    foreach ($key in $environmentNames) {
        $before[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    }
    $failure = $null
    $testState.Output = @()
    try { $testState.Output = @(& $launcher @Parameters 6>&1) }
    catch { $failure = $_ }
    foreach ($key in $environmentNames) {
        Assert-True ([Environment]::GetEnvironmentVariable($key, 'Process') -ceq $before[$key]) "$Name did not restore $key."
    }
    Assert-True ((Get-Location).Path -eq $originalLocation) "$Name did not restore location."
    if ($FailurePattern) {
        Assert-True ($null -ne $failure -and $failure.ToString() -match $FailurePattern) "$Name expected failure '$FailurePattern', got '$failure'."
        if ($testState.RunExitCode -eq 0) {
            Assert-True (@($testState.Calls | Where-Object { $_[0] -eq 'run' }).Count -eq 0) "$Name launched app after failed preflight."
        }
    }
    elseif ($null -ne $failure) { throw "$Name failed: $failure`n$($failure.ScriptStackTrace)`n$($failure.InvocationInfo.PositionMessage)" }
    $testState.Passed++
    Write-Host "PASS $Name"
}

try {
    [void] [IO.Directory]::CreateDirectory((Split-Path $launcher -Parent))
    [void] [IO.Directory]::CreateDirectory((Join-Path $root 'src\MarketOpportunity.Web'))
    [IO.File]::WriteAllText($launcher, $text)
    [IO.File]::WriteAllText((Join-Path $root 'src\MarketOpportunity.Web\MarketOpportunity.Web.csproj'),
        '<Project><PropertyGroup><UserSecretsId>offline-launcher-test</UserSecretsId></PropertyGroup></Project>')
    $env:APPDATA = Join-Path $root 'appdata'
    $secretsDirectory = Join-Path $env:APPDATA 'Microsoft\UserSecrets\offline-launcher-test'
    [void] [IO.Directory]::CreateDirectory($secretsDirectory)
    [IO.File]::WriteAllText((Join-Path $secretsDirectory 'secrets.json'),
        '{"MarketData:Massive:Symbols":"AAA, BBB, aaa"}')

    $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $portProbe.Start()
    $port = $portProbe.LocalEndpoint.Port
    $portProbe.Stop()
    $base = @{ Feed = 'iex'; SessionId = 'offline-iex'; Port = $port }
    $env:ConnectionStrings__MarketOpportunity = 'previous-connection'
    $env:DOTNET_ENVIRONMENT = 'Production'
    $env:ASPNETCORE_ENVIRONMENT = 'Staging'
    Invoke-LauncherCase 'IEX implicitly enables diagnostics and restores existing environment' $base
    Assert-True ((Get-AppArgument '--MarketData:Diagnostics:DurationSeconds') -eq '600') 'IEX default must be 600 seconds.'
    $expectedDirectory = Join-Path $root 'artifacts\alpaca-validation\offline-iex'
    Assert-True ((Get-AppArgument '--MarketData:Diagnostics:Directory') -eq $expectedDirectory) 'Default directory is not repository/session scoped.'
    Assert-True ((Get-AppArgument '--MarketData:Alpaca:Symbols') -eq 'AAA,BBB') 'Distinct universe not preserved.'
    $summary = $testState.Output -join "`n"
    foreach ($field in @('Feed=iex', 'SessionId=offline-iex', 'DurationSeconds=600', "Port=$port",
        'Database=MarketOpportunityEngine_LiveValidationTests', 'DistinctSymbols=2',
        'DiagnosticsDirectory=', 'InstrumentationEnabled=True', 'Environment=Development')) {
        Assert-True ($summary.Contains($field)) "Missing summary field $field."
    }
    Assert-True (-not $summary.Contains('Integrated Security') -and -not $summary.Contains('previous-connection')) 'Summary leaked connection details.'

    $custom = $base.Clone()
    $custom.DurationSeconds = 123
    $custom.DiagnosticsRoot = Join-Path $root 'custom root'
    $custom.SessionId = 'custom-session'
    $custom.ArtifactsPath = Join-Path $root 'build artifacts'
    $custom.Instrument = $false
    Invoke-LauncherCase 'Overrides reach app and Instrument false cannot disable diagnostics' $custom
    Assert-True ((Get-AppArgument '--MarketData:Diagnostics:DurationSeconds') -eq '123') 'Duration override lost.'
    Assert-True ((Get-AppArgument '--MarketData:Diagnostics:Directory') -eq (Join-Path $custom.DiagnosticsRoot $custom.SessionId)) 'Diagnostics root override lost.'
    Assert-True ((Get-AppArgument '--urls') -eq "http://localhost:$port") 'Port override lost.'
    foreach ($call in $testState.Calls) {
        $index = [Array]::IndexOf($call, '--artifacts-path')
        Assert-True ($index -ge 0 -and $call[$index + 1] -eq $custom.ArtifactsPath) 'Artifact path override lost.'
    }

    foreach ($key in $environmentNames | Where-Object { $_ -ne 'APPDATA' }) {
        [Environment]::SetEnvironmentVariable($key, $null, 'Process')
    }
    $synthetic = @{ Feed = 'test'; SessionId = 'offline-test'; Port = $port }
    Invoke-LauncherCase 'Test feed has shorter duration and restores absent environment' $synthetic
    Assert-True ((Get-AppArgument '--MarketData:Diagnostics:DurationSeconds') -eq '30') 'Test default must be 30 seconds.'
    $synthetic.DurationSeconds = 45
    $synthetic.DiagnosticsDirectory = Join-Path $root 'exact evidence'
    Invoke-LauncherCase 'Test duration and exact diagnostics directory remain configurable' $synthetic
    Assert-True ((Get-AppArgument '--MarketData:Diagnostics:DurationSeconds') -eq '45') 'Test duration override lost.'
    Assert-True ((Get-AppArgument '--MarketData:Diagnostics:Directory') -eq $synthetic.DiagnosticsDirectory) 'Exact directory override lost.'

    $preflight = $base.Clone()
    $preflight.PreflightOnly = $true
    $preflight.SessionId = 'preflight'
    Invoke-LauncherCase 'Preflight creates clock evidence but never launches app' $preflight
    Assert-True (@($testState.Calls | Where-Object { $_[0] -eq 'run' }).Count -eq 0) 'Preflight launched app.'
    Assert-True (Test-Path -LiteralPath (Join-Path $root 'artifacts\alpaca-validation\preflight\preflight.clock.json')) 'Preflight omitted clock evidence.'

    foreach ($parameter in @('DiagnosticsDirectory', 'DiagnosticsRoot')) {
        $invalid = $base.Clone()
        $invalid[$parameter] = ''
        Invoke-LauncherCase "Explicit missing $parameter fails closed" $invalid "$parameter must not be empty"
    }
    $blocked = Join-Path $root 'not-a-directory'
    [IO.File]::WriteAllText($blocked, 'blocked')
    $invalid = $preflight.Clone()
    $invalid.DiagnosticsDirectory = Join-Path $blocked 'evidence'
    Invoke-LauncherCase 'Uncreatable diagnostics directory fails even in preflight-only mode' $invalid 'CreateDirectory'

    $clockPath = Join-Path $expectedDirectory 'offline-iex.clock.json'
    $lockedClock = [IO.File]::Open($clockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        Invoke-LauncherCase 'Unwritable clock artifact prevents app launch' $base 'another process'
    }
    finally { $lockedClock.Dispose() }

    if ($IsWindows) {
        $deniedDirectory = Join-Path $root 'denied'
        [void] [IO.Directory]::CreateDirectory($deniedDirectory)
        $acl = Get-Acl -LiteralPath $deniedDirectory
        $deniedAcl = Get-Acl -LiteralPath $deniedDirectory
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $identity, [Security.AccessControl.FileSystemRights]::CreateFiles,
            [Security.AccessControl.AccessControlType]::Deny)
        $deniedAcl.AddAccessRule($rule)
        try {
            Set-Acl -LiteralPath $deniedDirectory -AclObject $deniedAcl
            $invalid = $base.Clone()
            $invalid.DiagnosticsDirectory = $deniedDirectory
            Invoke-LauncherCase 'Unwritable diagnostics directory fails before app launch' $invalid 'denied'
        }
        finally { Set-Acl -LiteralPath $deniedDirectory -AclObject $acl }
    }

    $busy = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
    $busy.ExclusiveAddressUse = $true
    $busy.Start()
    try { Invoke-LauncherCase 'Occupied port fails preflight' $preflight 'Port .* unavailable' }
    finally { $busy.Stop() }
    $testState.BuildExitCode = 1
    Invoke-LauncherCase 'Current build failure prevents launch' $base 'build failed'
    Assert-True ($testState.DatabaseChecks -eq 0) 'Build failure continued to database.'
    $testState.BuildExitCode = 0
    $testState.DatabaseFailure = $true
    Invoke-LauncherCase 'Dedicated database failure prevents launch' $base 'database verification failure'
    $testState.DatabaseFailure = $false
    $testState.RunExitCode = 1
    Invoke-LauncherCase 'App failure restores environment' $base 'exited with code 1'
    Write-Host "$($testState.Passed) offline launcher interception tests passed. No app, SQL, Docker, or provider connection was started."
}
finally {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name], 'Process')
    }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
