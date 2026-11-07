[CmdletBinding()]
param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag')
$ErrorActionPreference = 'Stop'
$repairRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$repairOutput = Join-Path $repairRoot 'artifacts'
$repairTools = Join-Path $repairOutput 'utilla\repair'
$repairManaged = Join-Path $GamePath 'Gorilla Tag_Data\Managed'
$repairCore = Join-Path $GamePath 'BepInEx\core'
$repairCompat = Join-Path $repairRoot 'compat\utilla'
$repairCsc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$null = [IO.Directory]::CreateDirectory($repairTools)
$repairDll = Join-Path $repairOutput 'TotemMod.UtillaFix.dll'
$repairHash = Join-Path $repairOutput 'TotemMod.UtillaFix.sha256'
$repairPayload = Join-Path $repairTools 'Utilla.Compat.Payload.dll'
$repairStamp = Join-Path $repairTools 'inputs.sha256'
$repairCecil = Join-Path $repairCore 'Mono.Cecil.dll'
$repairInputs = @(
    (Join-Path $repairCompat 'InfectionWireFormat.cs'),
    (Join-Path $repairCompat 'InfectionCompatibility.cs'),
    (Join-Path $repairCompat 'PatchUtilla.cs'),
    (Join-Path $PSScriptRoot 'UtillaRepair.cs'),
    (Join-Path $PSScriptRoot 'UtillaFixPreloader.cs'),
    $PSCommandPath,
    $repairCecil,
    (Join-Path $repairCore 'BepInEx.dll'),
    (Join-Path $repairCore '0Harmony.dll')
)
$repairReferenceNames = @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','PhotonUnityNetworking.dll','PhotonRealtime.dll','Photon3Unity3D.dll')
$repairInputs += @($repairReferenceNames | ForEach-Object { Join-Path $repairManaged $_ })
$repairFingerprint = (($repairInputs | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }) -join "`n")
$repairCached = (Test-Path -LiteralPath $repairDll) -and (Test-Path -LiteralPath $repairHash) -and (Test-Path -LiteralPath $repairPayload) -and (Test-Path -LiteralPath $repairStamp)
if ($repairCached) {
    $repairCached = [IO.File]::ReadAllText($repairStamp) -eq $repairFingerprint -and
        [IO.File]::ReadAllText($repairHash).Trim() -eq (Get-FileHash -LiteralPath $repairDll -Algorithm SHA256).Hash
}
if (-not $repairCached) {
    $repairRefs = @($repairReferenceNames | ForEach-Object { '/reference:' + (Join-Path $repairManaged $_) })
    $repairRefs += '/reference:' + (Join-Path $repairCore '0Harmony.dll')
    & $repairCsc /nologo /noconfig /nostdlib+ /target:library /optimize+ /warnaserror+ /nowarn:1701 /langversion:5 ("/out:" + $repairPayload) @repairRefs (Join-Path $repairCompat 'InfectionWireFormat.cs') (Join-Path $repairCompat 'InfectionCompatibility.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Utilla compatibility payload compilation failed.' }
    & $repairCsc /nologo /target:library /optimize+ /warnaserror+ /langversion:5 ("/out:" + $repairDll) ("/reference:" + $repairCecil) ("/reference:" + (Join-Path $repairCore 'BepInEx.dll')) ("/resource:" + $repairPayload + ',TotemMod.UtillaCompat.Payload.dll') (Join-Path $repairCompat 'PatchUtilla.cs') (Join-Path $PSScriptRoot 'UtillaRepair.cs') (Join-Path $PSScriptRoot 'UtillaFixPreloader.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Utilla preloader repair compilation failed.' }
    [IO.File]::WriteAllText($repairHash,(Get-FileHash -LiteralPath $repairDll -Algorithm SHA256).Hash,[Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($repairStamp,$repairFingerprint,[Text.Encoding]::ASCII)
}
Copy-Item -LiteralPath $repairCecil -Destination (Join-Path $repairTools 'Mono.Cecil.dll')
$repairTests = Join-Path $repairTools 'UtillaRepairTests.exe'
& $repairCsc /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 /main:UtillaRepairTests ("/out:" + $repairTests) ("/reference:" + $repairCecil) ("/resource:" + $repairDll + ',TotemMod.UtillaFix.dll') ("/resource:" + $repairHash + ',TotemMod.UtillaFix.sha256') (Join-Path $repairCompat 'PatchUtilla.cs') (Join-Path $PSScriptRoot 'UtillaRepair.cs') (Join-Path $PSScriptRoot 'UtillaRepairBootstrap.cs') (Join-Path $repairRoot 'tests\UtillaRepairTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Utilla repair tests compilation failed.' }
& $repairTests (Join-Path $repairCompat 'backups\Utilla.original.dll') $repairPayload $repairManaged $repairDll (Join-Path $GamePath 'BepInEx\plugins\Utilla.dll')
if ($LASTEXITCODE -ne 0) { throw 'Utilla repair/bootstrap tests failed.' }
Write-Output ('Verified Utilla startup repair: ' + $repairDll)
