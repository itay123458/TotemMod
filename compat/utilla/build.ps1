[CmdletBinding()]
param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag')
$ErrorActionPreference = 'Stop'
$utillaRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$utillaOutput = Join-Path $utillaRoot 'artifacts\utilla'
$utillaTools = Join-Path $utillaOutput 'tools'
$utillaManaged = Join-Path $GamePath 'Gorilla Tag_Data\Managed'
$utillaCore = Join-Path $GamePath 'BepInEx\core'
$utillaCsc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$utillaOriginal = Join-Path $PSScriptRoot 'backups\Utilla.original.dll'
$utillaInstalled = Join-Path $GamePath 'BepInEx\plugins\Utilla.dll'
$null = [IO.Directory]::CreateDirectory($utillaTools)
$null = [IO.Directory]::CreateDirectory((Join-Path $PSScriptRoot 'backups'))
if (-not (Test-Path -LiteralPath $utillaOriginal)) { Copy-Item -LiteralPath $utillaInstalled -Destination $utillaOriginal }
if ((Get-FileHash -LiteralPath $utillaOriginal -Algorithm SHA256).Hash -ne '4C64753E6F3609A40CA5D86E6033B134F915474EC7387D3FD517A95F255DE813') { throw 'Unsupported original Utilla build.' }
if ((Get-FileHash -LiteralPath (Join-Path $utillaManaged 'Assembly-CSharp.dll') -Algorithm SHA256).Hash -ne 'FF7337A447B80B0FCC1738C79A98FB9DE2C72497CC68C8588FD822359E06E4A7') { throw 'Game build changed; recheck the wire parser and manager validation before rebuilding.' }
$utillaRefs = @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','PhotonUnityNetworking.dll','PhotonRealtime.dll','Photon3Unity3D.dll') | ForEach-Object { '/reference:' + (Join-Path $utillaManaged $_) }
$utillaRefs += '/reference:' + (Join-Path $utillaCore '0Harmony.dll')
$utillaPayload = Join-Path $utillaTools 'Utilla.Compat.Payload.dll'
& $utillaCsc /nologo /noconfig /nostdlib+ /target:library /optimize+ /warnaserror+ /nowarn:1701 /langversion:5 ("/out:" + $utillaPayload) @utillaRefs (Join-Path $PSScriptRoot 'InfectionWireFormat.cs') (Join-Path $PSScriptRoot 'InfectionCompatibility.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compatibility payload compilation failed.' }
$utillaCecil = Join-Path $utillaCore 'Mono.Cecil.dll'
Copy-Item -LiteralPath $utillaCecil -Destination $utillaTools
$utillaPatcher = Join-Path $utillaTools 'PatchUtilla.exe'
& $utillaCsc /nologo /target:exe /optimize+ /warnaserror+ ("/out:" + $utillaPatcher) ("/reference:" + $utillaCecil) (Join-Path $PSScriptRoot 'PatchUtilla.cs')
if ($LASTEXITCODE -ne 0) { throw 'Patcher compilation failed.' }
$utillaDll = Join-Path $utillaOutput 'Utilla.dll'
& $utillaPatcher $utillaOriginal $utillaPayload $utillaDll $utillaManaged
if ($LASTEXITCODE -ne 0) { throw 'Patch failed.' }
$utillaFixtures = Join-Path $utillaTools 'WireFormatTests.exe'
& $utillaCsc /nologo /target:exe /warnaserror+ ("/out:" + $utillaFixtures) (Join-Path $PSScriptRoot 'InfectionWireFormat.cs') (Join-Path $PSScriptRoot 'WireFormatTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed.' }
& $utillaFixtures
if ($LASTEXITCODE -ne 0) { throw 'Wire-format fixtures failed.' }
$utillaIntegration = Join-Path $utillaTools 'CompatibilityTests.exe'
& $utillaCsc /nologo /target:exe /warnaserror+ ("/out:" + $utillaIntegration) (Join-Path $PSScriptRoot 'InfectionWireFormat.cs') (Join-Path $PSScriptRoot 'InfectionCompatibility.cs') (Join-Path $PSScriptRoot 'CompatibilityTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Integration fixture compilation failed.' }
& $utillaIntegration
if ($LASTEXITCODE -ne 0) { throw 'Integration fixtures failed.' }
$utillaVerifier = Join-Path $utillaTools 'VerifyPatch.exe'
& $utillaCsc /nologo /target:exe /warnaserror+ ("/out:" + $utillaVerifier) ("/reference:" + $utillaCecil) (Join-Path $PSScriptRoot 'VerifyPatch.cs')
if ($LASTEXITCODE -ne 0) { throw 'Verifier compilation failed.' }
& $utillaVerifier $utillaOriginal $utillaDll
if ($LASTEXITCODE -ne 0) { throw 'Assembly verification failed.' }
$utillaRuntimeTests = Join-Path $utillaTools 'PatchedRuntimeTests.exe'
& $utillaCsc /nologo /target:exe /warnaserror+ ("/out:" + $utillaRuntimeTests) (Join-Path $PSScriptRoot 'PatchedRuntimeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Patched-runtime tests compilation failed.' }
'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}' | Set-Content -LiteralPath (Join-Path $utillaTools 'PatchedRuntimeTests.runtimeconfig.json') -Encoding UTF8
& dotnet $utillaRuntimeTests $utillaDll (Join-Path $utillaManaged 'Assembly-CSharp.dll')
if ($LASTEXITCODE -ne 0) { throw 'Patched-runtime pure-method tests failed.' }
Write-Output ('Built local artifact: ' + $utillaDll)
