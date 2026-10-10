$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskApp = Join-Path $taskRoot 'app-1.1.3'
$taskCSC = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskSources = @('VoiceBuilder.cs','BuilderFeatures.cs','Downloads.cs','WaveAudio.cs','TabDeck.cs','ModernControls.cs','DropChoice.cs','NumberField.cs','Updates.cs') | ForEach-Object { Join-Path $taskRoot $_ }
& $taskCSC /nologo /target:winexe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/win32icon:$taskRoot\assets\voice-builder.ico" "/out:$taskApp\SupertonicVoiceBuilder.exe" $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать окно.' }
