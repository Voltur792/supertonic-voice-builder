param([switch]$Test)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$env:RUSTUP_HOME = Join-Path $taskRoot 'build-tools\rustup'
$env:CARGO_HOME = Join-Path $taskRoot 'build-tools\cargo'
$taskVC = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Tools\MSVC\14.29.30133'
$taskSDK = 'C:\Program Files (x86)\Windows Kits\10'
$env:PATH = "$env:CARGO_HOME\bin;$taskVC\bin\Hostx64\x64;$taskSDK\bin\10.0.19041.0\x64;$env:PATH"
$env:LIB = "$taskVC\lib\x64;$taskSDK\Lib\10.0.19041.0\um\x64;$taskSDK\Lib\10.0.19041.0\ucrt\x64"
$env:INCLUDE = "$taskVC\include;$taskSDK\Include\10.0.19041.0\ucrt;$taskSDK\Include\10.0.19041.0\shared;$taskSDK\Include\10.0.19041.0\um"
Push-Location (Join-Path $taskRoot 'engine-source')
try {
    if ($Test) { $env:ORT_DYLIB_PATH = Join-Path $taskRoot "engine\onnxruntime.dll"; & "$env:CARGO_HOME\bin\cargo.exe" test --release --lib --locked --features directml } else { & "$env:CARGO_HOME\bin\cargo.exe" build --release --locked --features directml }
    if ($LASTEXITCODE -ne 0) { throw 'Сборка движка завершилась с ошибкой.' }
} finally { Pop-Location }
