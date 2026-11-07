[CmdletBinding()]
param([string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag')
$ErrorActionPreference='Stop'
$taskCsc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskManaged=Join-Path $GamePath 'Gorilla Tag_Data\Managed'
$taskCore=Join-Path $GamePath 'BepInEx\core'
$taskOutput=Join-Path $PSScriptRoot 'artifacts'
$taskTests=Join-Path $taskOutput 'tests'
$taskWorkspace=Split-Path $PSScriptRoot -Parent
$null=[IO.Directory]::CreateDirectory($taskTests)
& (Join-Path $taskWorkspace 'updater\utilla\build.ps1') -GamePath $GamePath
if($LASTEXITCODE -ne 0) { throw 'Utilla repair helper build failed.' }
foreach($taskAsset in @('mace.png','critical_hit.png','glint.png','gust.png','hit.pcm','smash.pcm','burst.pcm')) {
    if(-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot ('assets\'+$taskAsset)))) {
        & python (Join-Path $PSScriptRoot 'tools\extract-assets.py')
        if($LASTEXITCODE -ne 0) { throw 'Offline Minecraft mace asset extraction failed.' }
        break
    }
}
& python (Join-Path $PSScriptRoot 'tests\test_assets.py')
if($LASTEXITCODE -ne 0) { throw 'Asset integrity tests failed.' }
$taskReferences=@('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.AudioModule.dll','UnityEngine.XRModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.UIModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll','PhotonUnityNetworking.dll','PhotonRealtime.dll','Photon3Unity3D.dll','System.Runtime.Serialization.dll','System.Xml.dll') | ForEach-Object { '/reference:'+(Join-Path $taskManaged $_) }
$taskReferences+=('/reference:'+(Join-Path $taskCore 'BepInEx.dll'))
$taskReferences+=('/reference:'+(Join-Path $taskCore '0Harmony.dll'))
$taskReferences+=('/reference:'+(Join-Path $taskCore 'Mono.Cecil.dll'))
$taskReferences+=('/reference:'+(Join-Path $GamePath 'BepInEx\plugins\Utilla.dll'))
$taskSources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'shared') -Filter '*.cs' | ForEach-Object { $_.FullName })
$taskSources+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$taskSources+=(Join-Path $taskWorkspace 'src\PixelMeshBuilder.cs')
$taskSources+=(Join-Path $taskWorkspace 'src\DeveloperIdentity.cs')
$taskSources+=(Join-Path $taskWorkspace 'updater\utilla\UtillaRepairBootstrap.cs')
$taskResources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets') -File | Where-Object { $_.Extension -in '.png','.pcm' } | ForEach-Object { '/resource:'+ $_.FullName+',MaceMod.'+$_.Name })
$taskResources+=('/resource:'+(Join-Path $taskWorkspace 'artifacts\TotemMod.UtillaFix.dll')+',TotemMod.UtillaFix.dll')
$taskResources+=('/resource:'+(Join-Path $taskWorkspace 'artifacts\TotemMod.UtillaFix.sha256')+',TotemMod.UtillaFix.sha256')
$taskDll=Join-Path $taskOutput 'MaceMod.dll'
& $taskCsc /nologo /noconfig /nostdlib+ /target:library /optimize+ /warnaserror+ /nowarn:1701 /langversion:5 ('/out:'+$taskDll) @taskReferences @taskResources @taskSources
if($LASTEXITCODE -ne 0) { throw 'Mace plugin compilation failed.' }
$taskCecil=Join-Path $taskCore 'Mono.Cecil.dll'
Copy-Item -LiteralPath $taskCecil -Destination $taskTests
$taskVisualTests=Join-Path $taskTests 'MaceVisualTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ /langversion:5 ('/out:'+$taskVisualTests) ('/reference:'+$taskCecil) (Join-Path $PSScriptRoot 'tests\MaceVisualTests.cs')
if($LASTEXITCODE -ne 0) { throw 'Visual safety test compilation failed.' }
& $taskVisualTests $taskDll
if($LASTEXITCODE -ne 0) { throw 'Mace visual safety tests failed.' }
$taskMeshTests=Join-Path $taskTests 'MacePixelMeshTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ /langversion:5 ('/out:'+$taskMeshTests) '/reference:System.Drawing.dll' (Join-Path $taskWorkspace 'src\PixelMeshBuilder.cs') (Join-Path $taskWorkspace 'tests\PixelMeshTests.cs')
if($LASTEXITCODE -ne 0) { throw 'Mace mesh tests compilation failed.' }
& $taskMeshTests (Join-Path $PSScriptRoot 'assets\mace.png')
if($LASTEXITCODE -ne 0) { throw 'Mace closed solid mesh tests failed.' }
$taskProtocolTests=Join-Path $taskTests 'MaceProtocolTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ /langversion:5 ('/out:'+$taskProtocolTests) (Join-Path $PSScriptRoot 'shared\MaceModel.cs') (Join-Path $PSScriptRoot 'shared\MaceTransport.cs') (Join-Path $PSScriptRoot 'tests\MaceProtocolTests.cs')
if($LASTEXITCODE -ne 0) { throw 'Mace protocol test compilation failed.' }
& $taskProtocolTests
if($LASTEXITCODE -ne 0) { throw 'Mace protocol tests failed.' }
$taskMechanicsTests=Join-Path $taskTests 'MaceMechanicsTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ /langversion:5 ('/out:'+$taskMechanicsTests) (Join-Path $PSScriptRoot 'shared\MaceMechanics.cs') (Join-Path $PSScriptRoot 'tests\MaceMechanicsTests.cs')
if($LASTEXITCODE -ne 0) { throw 'Mace mechanics test compilation failed.' }
& $taskMechanicsTests
if($LASTEXITCODE -ne 0) { throw 'Mace mechanics tests failed.' }
Write-Output ('Verified standalone mace: '+$taskDll)
$taskIntegrationTests=Join-Path $taskTests 'MaceIntegrationTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ /langversion:5 ('/out:'+$taskIntegrationTests) ('/reference:'+$taskCecil) (Join-Path $PSScriptRoot 'shared\MaceCombatRules.cs') (Join-Path $PSScriptRoot 'tests\MaceIntegrationTests.cs')
if($LASTEXITCODE -ne 0) { throw 'Mace integration test compilation failed.' }
& $taskIntegrationTests $taskDll
if($LASTEXITCODE -ne 0) { throw 'Mace integration tests failed.' }

