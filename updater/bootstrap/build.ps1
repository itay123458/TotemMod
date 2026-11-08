[CmdletBinding()]
param([string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag')
$ErrorActionPreference='Stop'
$companionRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$companionOutput=Join-Path $companionRoot 'artifacts'
$companionTests=Join-Path $companionOutput 'tests'
$null=[IO.Directory]::CreateDirectory($companionTests)
$companionCore=Join-Path $GamePath 'BepInEx\core'
$companionCecil=Join-Path $companionCore 'Mono.Cecil.dll'
$companionCsc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$companionCommon=@(Get-ChildItem -LiteralPath (Join-Path $companionRoot 'updater\common') -Filter '*.cs' | ForEach-Object { $_.FullName })
$companionPreloader=Join-Path $companionRoot 'updater\preloader\CompanionPreloader.cs'
$companionDll=Join-Path $companionOutput 'TotemMod.CompanionUpdater.dll'
$companionHash=Join-Path $companionOutput 'TotemMod.CompanionUpdater.sha256'
$companionStamp=Join-Path $companionTests 'CompanionUpdater.inputs.sha256'
$companionInputs=@($companionCommon)+@($companionPreloader,$PSCommandPath,$companionCecil,(Join-Path $companionCore 'BepInEx.dll'))
$companionFingerprint=($companionInputs | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }) -join "`n"
$companionCached=(Test-Path -LiteralPath $companionDll) -and (Test-Path -LiteralPath $companionHash) -and (Test-Path -LiteralPath $companionStamp)
if($companionCached) { $companionCached=[IO.File]::ReadAllText($companionStamp) -eq $companionFingerprint -and [IO.File]::ReadAllText($companionHash).Trim() -eq (Get-FileHash -LiteralPath $companionDll).Hash }
if(-not $companionCached) {
    & $companionCsc /nologo /target:library /optimize+ /langversion:5 /warnaserror+ ('/out:'+$companionDll) ('/reference:'+$companionCecil) ('/reference:'+(Join-Path $companionCore 'BepInEx.dll')) '/reference:System.Runtime.Serialization.dll' @companionCommon $companionPreloader
    if($LASTEXITCODE -ne 0) { throw 'Companion updater compilation failed.' }
    [IO.File]::WriteAllText($companionHash,(Get-FileHash -LiteralPath $companionDll).Hash,[Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($companionStamp,$companionFingerprint,[Text.Encoding]::ASCII)
}
Copy-Item -LiteralPath $companionCecil -Destination $companionTests
$companionTest=Join-Path $companionTests 'CompanionBootstrapTests.exe'
& $companionCsc /nologo /target:exe /langversion:5 /warnaserror+ ('/out:'+$companionTest) ('/reference:'+$companionCecil) (Join-Path $PSScriptRoot 'CompanionBootstrap.cs') (Join-Path $companionRoot 'tests\CompanionBootstrapTests.cs')
if($LASTEXITCODE -ne 0) { throw 'Companion bootstrap test compilation failed.' }
& $companionTest $companionDll
if($LASTEXITCODE -ne 0) { throw 'Companion bootstrap tests failed.' }
Write-Output ('Verified shared restart updater: '+$companionDll)
