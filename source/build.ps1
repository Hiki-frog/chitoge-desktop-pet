param([string]$ExecutableName = 'Chitoge.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF/WindowsBase.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$taskExe = Join-Path $taskRoot $ExecutableName
$taskManifest = Join-Path $PSScriptRoot 'app.manifest'
$taskSources = @('Pet.cs','Recycle.cs','SelfTest.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$taskExe" "/win32manifest:$taskManifest" @refs @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Write-Output "Built $taskExe"
