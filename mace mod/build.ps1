[CmdletBinding()]
param([string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag')
$ErrorActionPreference='Stop'
$taskCsc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskManaged=Join-Path $GamePath 'Gorilla Tag_Data\Managed'
$taskCore=Join-Path $GamePath 'BepInEx\core'
$taskOutput=Join-Path $PSScriptRoot 'artifacts'
$null=[IO.Directory]::CreateDirectory($taskOutput)
$taskReferences=@('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll') | ForEach-Object { '/reference:'+(Join-Path $taskManaged $_) }
$taskReferences+=('/reference:'+(Join-Path $taskCore 'BepInEx.dll'))
$taskDll=Join-Path $taskOutput 'MaceMod.dll'
& $taskCsc /nologo /noconfig /nostdlib+ /target:library /optimize+ /warnaserror+ /langversion:5 ('/out:'+$taskDll) @taskReferences (Join-Path $PSScriptRoot 'src\MacePlugin.cs')
if($LASTEXITCODE -ne 0) { throw 'Mace retirement plugin compilation failed.' }
Write-Output ('Built inactive Mace retirement update: '+$taskDll)
