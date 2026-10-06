param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repositoryRoot "src\ModelEvaluator.Cli\ModelEvaluator.Cli.csproj"
$packageDirectory = Join-Path $repositoryRoot "artifacts\packages"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "model-evaluator-tool-test-$PID"
$toolPath = Join-Path $testRoot "install"
$workingDirectory = Join-Path $testRoot "working"
$packageId = "Sujithq.ModelEvaluator.Tool"

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Command,
        [Parameter(Mandatory)]
        [string]$Description
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ExpectExitCode {
    param(
        [Parameter(Mandatory)]
        [scriptblock]$Command,
        [Parameter(Mandatory)]
        [int]$ExpectedExitCode,
        [Parameter(Mandatory)]
        [string]$Description
    )

    & $Command
    if ($LASTEXITCODE -ne $ExpectedExitCode) {
        throw "$Description returned exit code $LASTEXITCODE; expected $ExpectedExitCode."
    }
}

if (Test-Path $testRoot) {
    Remove-Item -Recurse -Force $testRoot
}
New-Item -ItemType Directory -Path $workingDirectory | Out-Null

Invoke-Checked { dotnet pack $project --configuration $Configuration } "Packing the .NET tool"

$versionOutput = & dotnet msbuild $project -nologo -getProperty:Version
if ($LASTEXITCODE -ne 0) {
    throw "Reading the tool version failed with exit code $LASTEXITCODE."
}
$version = ($versionOutput | Select-Object -Last 1).Trim()
$package = Join-Path $packageDirectory "$packageId.$version.nupkg"
if (-not (Test-Path $package)) {
    throw "Expected package '$package' was not created."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($package)
try {
    $entries = $archive.Entries.FullName
    $requiredEntries = @(
        "README.md",
        "tools/net10.0/any/ModelEvaluator.Cli.dll",
        "tools/net10.0/any/config/evaluation.json",
        "tools/net10.0/any/benchmarks/v1/shared/instructions.md",
        "tools/net10.0/any/benchmarks/smoke-v1/scenarios/smoke-add/starter/.editorconfig"
    )

    foreach ($entry in $requiredEntries) {
        if ($entries -notcontains $entry) {
            throw "The tool package is missing '$entry'."
        }
    }

    $unexpected = $entries | Where-Object { $_ -match "(^|/)(bin|obj|artifacts)/" }
    if ($unexpected) {
        throw "The tool package contains build output: $($unexpected -join ', ')"
    }
}
finally {
    $archive.Dispose()
}

Invoke-Checked {
    dotnet tool install --tool-path $toolPath --add-source $packageDirectory --version $version --no-cache $packageId
} "Installing the packed tool"

$commandName = if ($IsWindows) { "modelevaluator.exe" } else { "modelevaluator" }
$toolCommand = Join-Path $toolPath $commandName

Push-Location $workingDirectory
try {
    Invoke-Checked { & $toolCommand --help } "Running --help"
    Invoke-Checked { & $toolCommand version } "Running version"
    Invoke-Checked { & $toolCommand list-presets } "Listing packaged presets"
    Invoke-Checked { & $toolCommand list-scenarios --preset default } "Listing bundled scenarios"
    Invoke-Checked { & $toolCommand list-models --preset auto } "Listing bundled models"
    Invoke-Checked { & $toolCommand validate --preset default } "Validating bundled assets"
    Invoke-ExpectExitCode {
        & $toolCommand validate --config config/evaluation.auto.example.json
    } 2 "Resolving a missing caller-owned config"
    Invoke-Checked {
        & $toolCommand evaluate `
            --preset smoke-reference `
            --models reference-good `
            --repetitions 1
    } "Running the bundled reference smoke evaluation"

    $result = Get-ChildItem (Join-Path $workingDirectory "artifacts\smoke-reference\*\results.json") |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $result) {
        throw "The installed tool did not create a result in the caller's working directory."
    }

    $report = Get-Content -Raw $result.FullName | ConvertFrom-Json
    if ($report.attempts.Count -ne 1 -or $report.attempts[0].outcome -ne "Success") {
        throw "The installed tool smoke evaluation did not succeed."
    }
}
finally {
    Pop-Location
}

Remove-Item -Recurse -Force $testRoot
Write-Host "Installed tool package $packageId $version passed end-to-end validation."
