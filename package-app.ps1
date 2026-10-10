$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskSource = Join-Path $taskRoot 'app-1.1.3'
$taskPortable = Join-Path $taskRoot 'dist\SupertonicVoiceBuilder-1.1.3'
$taskEntries = [Collections.Generic.List[string]]::new()
function Copy-PackageFile([string]$relative) {
    $target = Join-Path $taskPortable $relative
    New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($target)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskSource $relative) -Destination $target -Force
    $taskEntries.Add($target)
}
foreach ($file in @('SupertonicVoiceBuilder.exe','model-assets.json','assets\voice-builder.png','assets\voice-builder.ico','engine\supertonic-clone.exe','engine\onnxruntime.dll','engine\onnxruntime_providers_shared.dll','engine\DirectML.dll','models\speaker_encoder.onnx','whisper\whisper-cli.exe')) { Copy-PackageFile $file }
foreach ($file in Get-ChildItem (Join-Path $taskSource 'whisper') -File -Filter '*.dll') { Copy-PackageFile ('whisper\' + $file.Name) }
foreach ($file in Get-ChildItem (Join-Path $taskSource 'licenses') -File -Filter '*.txt') { Copy-PackageFile ('licenses\' + $file.Name) }
foreach ($file in @('README.md','LICENSE.txt')) {
    $target = Join-Path $taskPortable $file
    Copy-Item -LiteralPath (Join-Path $taskRoot $file) -Destination $target -Force
    $taskEntries.Add($target)
}
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskArchivePath = Join-Path $taskRoot 'SupertonicVoiceBuilder-1.1.3-Windows-x64.zip'
$taskStream = [IO.File]::Open($taskArchivePath, [IO.FileMode]::Create)
$taskArchive = [IO.Compression.ZipArchive]::new($taskStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $taskEntries) {
        $name = $file.Substring($taskPortable.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, $file, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $taskArchive.Dispose(); $taskStream.Dispose() }
Get-Item -LiteralPath $taskArchivePath | Select-Object FullName,Length
