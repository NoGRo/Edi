param(
    [string]$OutputDirectory = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "Edi.Wpf\Edi.Wpf.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path `
        $repositoryRoot `
        "artifacts\Edi-win-x64"
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

dotnet publish $projectPath `
    --configuration $Configuration `
    --framework net8.0-windows10.0.19041 `
    --runtime win-x64 `
    --self-contained true `
    --output $OutputDirectory `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "Windows publish failed with exit code $LASTEXITCODE."
}

$publishedItems = @(
    Get-ChildItem `
        -LiteralPath $OutputDirectory `
        -Force
)

$expectedNames = @("Edi.exe", "certificate.pfx")
$publishedNames = @($publishedItems.Name | Sort-Object)
$unexpectedItems = @($publishedItems | Where-Object { $_.PSIsContainer })
$nameDifferences = @(
    Compare-Object `
        -ReferenceObject ($expectedNames | Sort-Object) `
        -DifferenceObject $publishedNames
)

if (
    $publishedItems.Count -ne $expectedNames.Count -or
    $unexpectedItems.Count -ne 0 -or
    $nameDifferences.Count -ne 0
) {
    $publishedNames = $publishedItems.Name -join ", "
    throw "Expected only Edi.exe and certificate.pfx, but publish produced: $publishedNames"
}

Write-Host "EDI Windows build published to $OutputDirectory (Edi.exe and certificate.pfx)"
