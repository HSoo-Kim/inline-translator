param(
    [switch]$Screenshots,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $dotnet = Join-Path $env:USERPROFILE ".dotnet/dotnet.exe"
}
if (-not (Test-Path $dotnet)) {
    throw ".NET 10 SDK is required. Install it, then run this script again."
}

function Run-Dotnet {
    & $dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

Run-Dotnet build "$root/src/InlineTranslator/InlineTranslator.csproj" -c Release --nologo
Run-Dotnet run --project "$root/tests/InlineTranslator.Tests/InlineTranslator.Tests.csproj" -c Release

if ($Screenshots) {
    # Settings windows are rendered on a private desktop, never on the desktop you are using.
    Run-Dotnet build "$root/tools/PrivateDesktopQa/PrivateDesktopQa.csproj" -c Release --nologo
    Run-Dotnet build "$root/tools/Preview/Preview.csproj" -c Release --nologo
    $log = Join-Path ([IO.Path]::GetTempPath()) ("InlineTranslator-QA-" + [Guid]::NewGuid().ToString("N") + ".log")
    try {
        & $dotnet "$root/tools/PrivateDesktopQa/bin/Release/net10.0-windows/PrivateDesktopQa.dll" `
            --log $log --timeout 120 -- $dotnet `
            "$root/tools/Preview/bin/Release/net10.0-windows/Preview.dll" render "$root/docs/images"
        $exit = $LASTEXITCODE
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log }
        if ($exit -ne 0) { throw "Screenshot rendering failed with exit code $exit." }
    }
    finally {
        if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
    }
}

if ($Publish) {
    Run-Dotnet publish "$root/src/InlineTranslator/InlineTranslator.csproj" -c Release -r win-x64 `
        --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -o "$root/artifacts/InlineTranslator"
    Compress-Archive -Path "$root/artifacts/InlineTranslator/InlineTranslator.exe", "$root/README.md", `
        "$root/README.en.md", "$root/README.zh-CN.md", "$root/README.ja.md", "$root/LICENSE" `
        -DestinationPath "$root/artifacts/inline-translator-win-x64.zip" -Force
    $hash = (Get-FileHash "$root/artifacts/inline-translator-win-x64.zip" -Algorithm SHA256).Hash
    Write-Output "artifacts/inline-translator-win-x64.zip SHA256 $hash"
}
