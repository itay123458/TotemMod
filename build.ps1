[CmdletBinding()]
param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag',[switch]$UseTestUpdateKey)
$ErrorActionPreference = 'Stop'
$taskCsc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskManaged = Join-Path $GamePath 'Gorilla Tag_Data\Managed'
$taskCore = Join-Path $GamePath 'BepInEx\core'
$taskOutput = Join-Path $PSScriptRoot 'artifacts'
$taskTests = Join-Path $taskOutput 'tests'
$null = [IO.Directory]::CreateDirectory($taskTests)
if (-not (Test-Path (Join-Path $PSScriptRoot 'assets\pop.pcm'))) {
    & python (Join-Path $PSScriptRoot 'tools\extract-assets.py')
    if ($LASTEXITCODE -ne 0) { throw 'Local Minecraft asset extraction failed.' }
}
$taskReferences = @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.AudioModule.dll','UnityEngine.XRModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.ScreenCaptureModule.dll','UnityEngine.AssetBundleModule.dll','UnityEngine.UIModule.dll','UnityEngine.UI.dll','UnityEngine.TextRenderingModule.dll','PhotonUnityNetworking.dll','PhotonRealtime.dll','Photon3Unity3D.dll') | ForEach-Object { '/reference:' + (Join-Path $taskManaged $_) }
$taskReferences += ('/reference:' + (Join-Path $taskCore 'BepInEx.dll'))
$taskReferences += ('/reference:' + (Join-Path $taskCore '0Harmony.dll'))
$taskReferences += ('/reference:' + (Join-Path $taskManaged 'com.rlabrecque.steamworks.net.dll'))
$taskReferences += ('/reference:' + (Join-Path $taskManaged 'System.Runtime.Serialization.dll'))
$taskReferences += ('/reference:' + (Join-Path $taskManaged 'System.Xml.dll'))
$taskReferences += ('/reference:' + (Join-Path $taskCore 'Mono.Cecil.dll'))
$taskReferences += ('/reference:' + (Join-Path $GamePath 'BepInEx\plugins\Utilla.dll'))
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$taskUpdaterCommon = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'updater\common') -Filter '*.cs' | ForEach-Object { $_.FullName })
$taskSources += $taskUpdaterCommon
$taskSources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'updater\runtime') -Filter '*.cs' | ForEach-Object { $_.FullName })
& (Join-Path $PSScriptRoot 'updater\utilla\build.ps1') -GamePath $GamePath
if ($LASTEXITCODE -ne 0) { throw 'Utilla repair helper build failed.' }
$taskSources += (Join-Path $PSScriptRoot 'updater\utilla\UtillaRepairBootstrap.cs')
$taskRepairResources = @(
    ('/resource:' + (Join-Path $taskOutput 'TotemMod.UtillaFix.dll') + ',TotemMod.UtillaFix.dll'),
    ('/resource:' + (Join-Path $taskOutput 'TotemMod.UtillaFix.sha256') + ',TotemMod.UtillaFix.sha256')
)
if (-not (Test-Path (Join-Path $PSScriptRoot 'assets\menu\click.pcm'))) {
    & python (Join-Path $PSScriptRoot 'tools\extract-menu-assets.py')
    if ($LASTEXITCODE -ne 0) { throw 'Local Minecraft menu asset extraction failed.' }
}
$taskMenuResources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets\menu') -File | Where-Object { $_.Extension -in '.png','.pcm' } | ForEach-Object { '/resource:' + $_.FullName + ',TotemMod.Menu.' + $_.Name })
$taskDll = Join-Path $taskOutput 'TotemMod.dll'
# Photon targets netstandard 2.0; this Unity installation supplies its compatible
# netstandard 2.1 facade. Suppress only that legacy-csc identity warning.
& $taskCsc /nologo /noconfig /nostdlib+ /target:library /optimize+ /warnaserror+ /nowarn:1701 /langversion:5 ("/out:" + $taskDll) @taskReferences ("/resource:" + (Join-Path $PSScriptRoot 'assets\totem.png') + ',TotemMod.totem.png') ("/resource:" + (Join-Path $PSScriptRoot 'assets\pop.pcm') + ',TotemMod.pop.pcm') ("/resource:" + (Join-Path $PSScriptRoot 'third_party\GorillaNotifications\gnotifications') + ',TotemMod.gnotifications') @taskMenuResources @taskRepairResources @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Plugin compilation failed.' }
$taskCecil = Join-Path $taskCore 'Mono.Cecil.dll'
Copy-Item -LiteralPath $taskCecil -Destination $taskTests
$taskVerifier = Join-Path $taskTests 'VerifyAssembly.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskVerifier) ("/reference:" + $taskCecil) (Join-Path $PSScriptRoot 'tests\VerifyAssembly.cs')
if ($LASTEXITCODE -ne 0) { throw 'Assembly verifier compilation failed.' }
& $taskVerifier $taskDll
if ($LASTEXITCODE -ne 0) { throw 'Assembly verification failed.' }
$taskStateTests = Join-Path $taskTests 'EquipStateTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskStateTests) (Join-Path $PSScriptRoot 'src\EquipState.cs') (Join-Path $PSScriptRoot 'tests\EquipStateTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Equip tests compilation failed.' }
& $taskStateTests
if ($LASTEXITCODE -ne 0) { throw 'Equip-state tests failed.' }
$taskInventoryTests = Join-Path $taskTests 'InventoryTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskInventoryTests) (Join-Path $PSScriptRoot 'src\EquipState.cs') (Join-Path $PSScriptRoot 'src\TotemInventory.cs') (Join-Path $PSScriptRoot 'tests\InventoryTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Inventory tests compilation failed.' }
& $taskInventoryTests
if ($LASTEXITCODE -ne 0) { throw 'Inventory tests failed.' }
$taskPalmTests = Join-Path $taskTests 'PalmAnchorTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskPalmTests) (Join-Path $PSScriptRoot 'src\PalmAnchor.cs') (Join-Path $PSScriptRoot 'tests\PalmAnchorTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Palm tests compilation failed.' }
& $taskPalmTests
if ($LASTEXITCODE -ne 0) { throw 'Palm attachment tests failed.' }
$taskTagTests = Join-Path $taskTests 'TagObserverTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskTagTests) (Join-Path $PSScriptRoot 'src\EquipState.cs') (Join-Path $PSScriptRoot 'src\TagObserver.cs') (Join-Path $PSScriptRoot 'tests\TagObserverTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Tag tests compilation failed.' }
& $taskTagTests
if ($LASTEXITCODE -ne 0) { throw 'Tag observer tests failed.' }
$taskSyncTests = Join-Path $taskTests 'SyncTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskSyncTests) (Join-Path $PSScriptRoot 'src\SyncModel.cs') (Join-Path $PSScriptRoot 'src\RoomRules.cs') (Join-Path $PSScriptRoot 'src\DeveloperCommand.cs') (Join-Path $PSScriptRoot 'src\PhotonSync.cs') (Join-Path $PSScriptRoot 'tests\DeveloperCommandTests.cs') (Join-Path $PSScriptRoot 'tests\SyncTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Sync tests compilation failed.' }
& $taskSyncTests
if ($LASTEXITCODE -ne 0) { throw 'Friend sync tests failed.' }
$taskHostTests = Join-Path $taskTests 'HostProtectionTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskHostTests) (Join-Path $PSScriptRoot 'src\HostProtection.cs') (Join-Path $PSScriptRoot 'tests\HostProtectionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Host tests compilation failed.' }
& $taskHostTests
if ($LASTEXITCODE -ne 0) { throw 'Host protection tests failed.' }
$taskRulesTests = Join-Path $taskTests 'RulesTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskRulesTests) (Join-Path $PSScriptRoot 'src\RoomRules.cs') (Join-Path $PSScriptRoot 'src\TotemInventory.cs') (Join-Path $PSScriptRoot 'tests\RulesTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Rules tests compilation failed.' }
& $taskRulesTests
if ($LASTEXITCODE -ne 0) { throw 'Rules tests failed.' }
$taskMenuInputTests = Join-Path $taskTests 'MenuInputTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskMenuInputTests) (Join-Path $PSScriptRoot 'src\MenuHoldGesture.cs') (Join-Path $PSScriptRoot 'tests\MenuInputTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Menu input tests compilation failed.' }
& $taskMenuInputTests
if ($LASTEXITCODE -ne 0) { throw 'Menu input tests failed.' }
$taskPointerTests=Join-Path $taskTests 'MenuPointerTests.exe'
& $taskCsc /nologo /target:exe /langversion:5 /warnaserror+ ("/out:"+$taskPointerTests) ("/reference:"+$taskCecil) (Join-Path $PSScriptRoot 'src\MenuPointerState.cs') (Join-Path $PSScriptRoot 'tests\MenuPointerTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Menu pointer tests compilation failed.' }
& $taskPointerTests $taskDll
if ($LASTEXITCODE -ne 0) { throw 'Menu pointer tests failed.' }
Write-Output "Verified: $taskDll"
$taskDeveloperTests = Join-Path $taskTests 'DeveloperAccessTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskDeveloperTests) (Join-Path $PSScriptRoot 'src\DeveloperAccess.cs') (Join-Path $PSScriptRoot 'tests\DeveloperAccessTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Developer access tests compilation failed.' }
& $taskDeveloperTests
if ($LASTEXITCODE -ne 0) { throw 'Developer access tests failed.' }
$taskIdentityTests = Join-Path $taskTests 'DeveloperIdentityTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskIdentityTests) (Join-Path $PSScriptRoot 'src\DeveloperIdentity.cs') (Join-Path $PSScriptRoot 'tests\DeveloperIdentityTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Developer identity tests compilation failed.' }
& $taskIdentityTests
if ($LASTEXITCODE -ne 0) { throw 'Developer identity tests failed.' }
$taskParticleTests = Join-Path $taskTests 'PopParticleStyleTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskParticleTests) (Join-Path $PSScriptRoot 'src\PopParticleStyle.cs') (Join-Path $PSScriptRoot 'tests\PopParticleStyleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Particle style tests compilation failed.' }
& $taskParticleTests
if ($LASTEXITCODE -ne 0) { throw 'Particle style tests failed.' }
$taskPopAnimationTests=Join-Path $taskTests 'PopAnimationStyleTests.exe'
& $taskCsc /nologo /target:exe /langversion:5 /warnaserror+ ("/out:"+$taskPopAnimationTests) (Join-Path $PSScriptRoot 'tests\PopAnimationStyleTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Pop animation tests compilation failed.' }
& $taskPopAnimationTests $taskDll
if ($LASTEXITCODE -ne 0) { throw 'Pop animation tests failed.' }
$taskQueueTests = Join-Path $taskTests 'RoomCommandQueueTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskQueueTests) (Join-Path $PSScriptRoot 'src\DeveloperCommand.cs') (Join-Path $PSScriptRoot 'src\RoomCommandQueue.cs') (Join-Path $PSScriptRoot 'tests\RoomCommandQueueTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Room command queue tests compilation failed.' }
& $taskQueueTests
if ($LASTEXITCODE -ne 0) { throw 'Room command queue tests failed.' }
$taskNavigationTests = Join-Path $taskTests 'MenuNavigationTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskNavigationTests) (Join-Path $PSScriptRoot 'src\MenuNavigation.cs') (Join-Path $PSScriptRoot 'tests\MenuNavigationTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Category navigation tests compilation failed.' }
& $taskNavigationTests
if ($LASTEXITCODE -ne 0) { throw 'Category navigation tests failed.' }
$taskBetaTests = Join-Path $taskTests 'BetaVisualSafetyTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:" + $taskBetaTests) ("/reference:" + $taskCecil) (Join-Path $PSScriptRoot 'tests\BetaVisualSafetyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Beta visual safety tests compilation failed.' }
& $taskBetaTests $taskDll
if ($LASTEXITCODE -ne 0) { throw 'Beta visual safety tests failed.' }
$taskPixelTests=Join-Path $taskTests 'PixelMeshTests.exe'
& $taskCsc /nologo /target:exe /langversion:5 /warnaserror+ ("/out:"+$taskPixelTests) '/reference:System.Drawing.dll' (Join-Path $PSScriptRoot 'src\PixelMeshBuilder.cs') (Join-Path $PSScriptRoot 'tests\PixelMeshTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Pixel mesh tests compilation failed.' }
& $taskPixelTests (Join-Path $PSScriptRoot 'assets\totem.png')
if ($LASTEXITCODE -ne 0) { throw 'Pixel mesh tests failed.' }
$taskBatchTests=Join-Path $taskTests 'PopParticleBatchTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:"+$taskBatchTests) ("/reference:"+$taskCecil) (Join-Path $PSScriptRoot 'tests\PopParticleBatchTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Particle batch tests compilation failed.' }
& $taskBatchTests $taskDll
if ($LASTEXITCODE -ne 0) { throw 'Particle batch tests failed.' }
$taskBetaSyncTests=Join-Path $taskTests 'BetaSyncTests.exe'
& $taskCsc /nologo /target:exe /langversion:5 /warnaserror+ ("/out:"+$taskBetaSyncTests) (Join-Path $PSScriptRoot 'src\BetaAppearance.cs') (Join-Path $PSScriptRoot 'src\BetaRoomSync.cs') (Join-Path $PSScriptRoot 'tests\BetaSyncTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Beta sync tests compilation failed.' }
& $taskBetaSyncTests
if ($LASTEXITCODE -ne 0) { throw 'Beta sync tests failed.' }
$taskBetaAccountTests=Join-Path $taskTests 'BetaAccountAccessTests.exe'
& $taskCsc /nologo /target:exe /warnaserror+ ("/out:"+$taskBetaAccountTests) (Join-Path $PSScriptRoot 'src\BetaAccountAccess.cs') (Join-Path $PSScriptRoot 'tests\BetaAccountAccessTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Beta account tests compilation failed.' }
& $taskBetaAccountTests
if ($LASTEXITCODE -ne 0) { throw 'Beta account tests failed.' }
$taskUpdaterDll=Join-Path $taskOutput 'TotemMod.Updater.dll'
$taskUpdaterReferences=@(('/reference:'+(Join-Path $taskCore 'BepInEx.dll')),('/reference:'+$taskCecil),'/reference:System.Runtime.Serialization.dll')
& $taskCsc /nologo /target:library /optimize+ /langversion:5 /warnaserror+ ("/out:"+$taskUpdaterDll) @taskUpdaterReferences @taskUpdaterCommon (Join-Path $PSScriptRoot 'updater\preloader\Preloader.cs')
if ($LASTEXITCODE -ne 0) { throw 'Updater preloader compilation failed.' }
$taskUpdaterKey=Join-Path $env:USERPROFILE '.totemmod\update-signing-private.xml'
$taskUpdaterTestCommon=@($taskUpdaterCommon)
$taskTemporaryTrust=$null
if ($UseTestUpdateKey -or -not (Test-Path -LiteralPath $taskUpdaterKey)) {
    # Public source builds use an isolated test key. Production DLLs retain the
    # fixed publisher public key compiled above; no signing secret is required.
    $taskTemporaryTrust=Join-Path ([IO.Path]::GetTempPath()) ('TotemMod-tests-'+[Guid]::NewGuid().ToString('N'))
    $null=[IO.Directory]::CreateDirectory($taskTemporaryTrust)
    $taskUpdaterKey=Join-Path $taskTemporaryTrust 'test-private.xml'
    $taskTestRsa=New-Object Security.Cryptography.RSACryptoServiceProvider 2048
    $taskTestRsa.PersistKeyInCsp=$false
    try {
        [IO.File]::WriteAllText($taskUpdaterKey,$taskTestRsa.ToXmlString($true))
        $taskTestTrustText=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'updater\common\UpdateTrust.cs'))
        $taskTestTrustText=[regex]::Replace($taskTestTrustText,'PublicKey="[^"]+";',('PublicKey="'+$taskTestRsa.ToXmlString($false)+'";'))
        $taskTestTrustSource=Join-Path $taskTemporaryTrust 'UpdateTrust.cs'
        [IO.File]::WriteAllText($taskTestTrustSource,$taskTestTrustText)
    } finally { $taskTestRsa.Dispose() }
    $taskUpdaterTestCommon=@($taskUpdaterCommon | Where-Object { [IO.Path]::GetFileName($_) -ne 'UpdateTrust.cs' })
    $taskUpdaterTestCommon+=$taskTestTrustSource
}
try {
foreach ($taskUpdaterTestName in @('UpdateManifestTests','UpdateClientTests','UpdateInstallerTests')) {
    $taskUpdaterTest=Join-Path $taskTests ($taskUpdaterTestName+'.exe')
    $taskUpdaterTestSources=@($taskUpdaterTestCommon)
    if ($taskUpdaterTestName -eq 'UpdateClientTests') { $taskUpdaterTestSources+=(Join-Path $PSScriptRoot 'updater\runtime\UpdateClient.cs') }
    $taskUpdaterTestSources+=(Join-Path $PSScriptRoot ('tests\'+$taskUpdaterTestName+'.cs'))
    & $taskCsc /nologo /target:exe /langversion:5 /warnaserror+ ("/out:"+$taskUpdaterTest) '/reference:System.Runtime.Serialization.dll' ("/reference:"+$taskCecil) @taskUpdaterTestSources
    if ($LASTEXITCODE -ne 0) { throw ($taskUpdaterTestName+' compilation failed.') }
    if ($taskUpdaterTestName -eq 'UpdateInstallerTests') { & $taskUpdaterTest $taskDll $taskUpdaterKey }
    else { & $taskUpdaterTest $taskUpdaterKey }
    if ($LASTEXITCODE -ne 0) { throw ($taskUpdaterTestName+' failed.') }
}
} finally {
    if ($taskTemporaryTrust) {
        [IO.File]::Delete((Join-Path $taskTemporaryTrust 'test-private.xml'))
        [IO.File]::Delete((Join-Path $taskTemporaryTrust 'UpdateTrust.cs'))
        [IO.Directory]::Delete($taskTemporaryTrust,$false)
    }
}
$taskManifestTool=Join-Path $taskTests 'MakeUpdateManifest.exe'
& $taskCsc /nologo /target:exe /langversion:5 /warnaserror+ ("/out:"+$taskManifestTool) '/reference:System.Runtime.Serialization.dll' ("/reference:"+$taskCecil) @taskUpdaterCommon (Join-Path $PSScriptRoot 'tools\MakeUpdateManifest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Release signing tool compilation failed.' }
Write-Output ("Verified updater helper: "+$taskUpdaterDll)
