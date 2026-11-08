[CmdletBinding()]
param(
    [string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag',
    [string]$NotesFile,
    [switch]$SkipBuild,
    [switch]$Publish
)
$ErrorActionPreference='Stop'
$releaseRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $releaseRoot 'build.ps1') -GamePath $GamePath; if ($LASTEXITCODE -ne 0) { throw 'Build failed.' } }
$releaseArtifacts=Join-Path $releaseRoot 'artifacts'
$releaseDll=Join-Path $releaseArtifacts 'TotemMod.dll'
$releaseHelper=Join-Path $releaseArtifacts 'TotemMod.Updater.dll'
$releaseUtillaFix=Join-Path $releaseArtifacts 'TotemMod.UtillaFix.dll'
$releaseCompanionHelper=Join-Path $releaseArtifacts 'TotemMod.CompanionUpdater.dll'
$releaseMace=Join-Path $releaseRoot 'mace mod\artifacts\MaceMod.dll'
$releaseKey=Join-Path $env:USERPROFILE '.totemmod\update-signing-private.xml'
if (-not (Test-Path -LiteralPath $releaseKey)) { throw 'Publisher signing key is missing. It is not stored in the repository.' }
$releaseManifest=Join-Path $releaseArtifacts 'totem-update.json'
& (Join-Path $releaseArtifacts 'tests\MakeUpdateManifest.exe') $releaseDll (Join-Path $GamePath 'Gorilla Tag_Data\Managed\Assembly-CSharp.dll') $releaseKey $releaseManifest
if ($LASTEXITCODE -ne 0) { throw 'Release signing failed.' }
$releaseMaceManifest=Join-Path $releaseArtifacts 'mace-update.json'
& (Join-Path $releaseArtifacts 'tests\MakeUpdateManifest.exe') $releaseMace (Join-Path $GamePath 'Gorilla Tag_Data\Managed\Assembly-CSharp.dll') $releaseKey $releaseMaceManifest mace
if ($LASTEXITCODE -ne 0) { throw 'Mace release signing failed.' }
$releaseMetadata=Get-Content -LiteralPath $releaseManifest -Raw | ConvertFrom-Json
$releaseVersion=$releaseMetadata.Version
$releaseMaceMetadata=Get-Content -LiteralPath $releaseMaceManifest -Raw | ConvertFrom-Json
if ($releaseMaceMetadata.Version -ne $releaseVersion) { throw 'Totem and Mace must declare the same release version.' }
$releaseTag='v'+$releaseVersion
$releaseWork=Join-Path $releaseArtifacts ('publish-'+$releaseVersion)
if (Test-Path -LiteralPath $releaseWork) { throw ('Release staging already exists: '+$releaseWork+'. Use a new version or review the existing staged release.') }
$releaseRepo=Join-Path $releaseWork 'repository'
$releasePackage=Join-Path $releaseWork 'package'
$null=[IO.Directory]::CreateDirectory($releaseRepo)
$null=[IO.Directory]::CreateDirectory((Join-Path $releasePackage 'BepInEx\plugins'))
$null=[IO.Directory]::CreateDirectory((Join-Path $releasePackage 'BepInEx\patchers'))

# Copy an explicit source allowlist. Recovered files, installed game DLLs,
# local backups, asset caches, compiled tests and private keys never enter git.
foreach ($releaseDirectory in @('src','tests','updater','tools','third_party')) {
    Copy-Item -LiteralPath (Join-Path $releaseRoot $releaseDirectory) -Destination (Join-Path $releaseRepo $releaseDirectory) -Recurse
}
$releaseMaceSource=Join-Path $releaseRepo 'mace mod'
$null=[IO.Directory]::CreateDirectory($releaseMaceSource)
foreach ($releaseDirectory in @('shared','src','tools','tests')) {
    $releaseSourceDirectory=Join-Path $releaseRoot ('mace mod\'+$releaseDirectory)
    $releaseDestinationDirectory=Join-Path $releaseMaceSource $releaseDirectory
    foreach ($releaseSourceFile in (Get-ChildItem -LiteralPath $releaseSourceDirectory -Recurse -File | Where-Object { $_.Extension -in '.cs','.py','.ps1','.md','.txt' })) {
        $releaseRelative=$releaseSourceFile.FullName.Substring($releaseSourceDirectory.Length+1)
        $releaseDestination=Join-Path $releaseDestinationDirectory $releaseRelative
        $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($releaseDestination))
        Copy-Item -LiteralPath $releaseSourceFile.FullName -Destination $releaseDestination
    }
}
foreach ($releaseFile in @('build.ps1','README.md','INSTALL.txt')) {
    Copy-Item -LiteralPath (Join-Path $releaseRoot ('mace mod\'+$releaseFile)) -Destination (Join-Path $releaseMaceSource $releaseFile)
}
$null=[IO.Directory]::CreateDirectory((Join-Path $releaseRepo 'compat\utilla'))
Get-ChildItem -LiteralPath (Join-Path $releaseRoot 'compat\utilla') -File | Where-Object { $_.Extension -in '.cs','.ps1' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseRepo 'compat\utilla') }
foreach ($releaseFile in @('build.ps1','README.md','.gitignore')) { Copy-Item -LiteralPath (Join-Path $releaseRoot $releaseFile) -Destination (Join-Path $releaseRepo $releaseFile) }
Copy-Item -LiteralPath (Join-Path $releaseRoot 'third_party\GorillaNotifications\LICENSE') -Destination (Join-Path $releaseRepo 'LICENSE')
Copy-Item -LiteralPath $releaseDll -Destination (Join-Path $releasePackage 'BepInEx\plugins\TotemMod.dll')
Copy-Item -LiteralPath $releaseHelper -Destination (Join-Path $releasePackage 'BepInEx\patchers\TotemMod.Updater.dll')
Copy-Item -LiteralPath $releaseUtillaFix -Destination (Join-Path $releasePackage 'BepInEx\patchers\TotemMod.UtillaFix.dll')
Copy-Item -LiteralPath $releaseCompanionHelper -Destination (Join-Path $releasePackage 'BepInEx\patchers\TotemMod.CompanionUpdater.dll')
Copy-Item -LiteralPath (Join-Path $releaseRoot 'README.md') -Destination (Join-Path $releasePackage 'README.md')
Copy-Item -LiteralPath (Join-Path $releaseRepo 'LICENSE') -Destination (Join-Path $releasePackage 'LICENSE')
$releaseUnexpected=@(Get-ChildItem -LiteralPath $releaseRepo -Recurse -File | Where-Object { $_.Extension -in '.dll','.exe','.pfx','.pem','.key','.cfg','.pyc' })
if ($releaseUnexpected.Count -gt 0) { throw ('Unexpected binary or private configuration in source package: '+$releaseUnexpected[0].FullName) }
foreach ($releaseTextFile in (Get-ChildItem -LiteralPath $releaseRepo -Recurse -File | Where-Object { $_.Extension -in '.cs','.ps1','.py','.md','.json','.txt','.yml','.yaml' })) {
    $releaseText=[IO.File]::ReadAllText($releaseTextFile.FullName)
    if ($releaseText -match '(?s)<RSAKeyValue>[^"\r\n]*<D>' -or $releaseText -match '-----BEGIN (RSA |OPENSSH )?PRIVATE KEY-----' -or $releaseText -match 'gh[pousr]_[A-Za-z0-9]{25,}') { throw ('Secret-like material found in '+$releaseTextFile.FullName) }
}
$releaseZip=Join-Path $releaseArtifacts ('PrimalCraft-'+$releaseVersion+'.zip')
Compress-Archive -Path (Join-Path $releasePackage '*') -DestinationPath $releaseZip -Force
$releaseMacePackage=Join-Path $releaseWork 'mace-package'
$null=[IO.Directory]::CreateDirectory((Join-Path $releaseMacePackage 'BepInEx\plugins'))
$null=[IO.Directory]::CreateDirectory((Join-Path $releaseMacePackage 'BepInEx\patchers'))
Copy-Item -LiteralPath $releaseMace -Destination (Join-Path $releaseMacePackage 'BepInEx\plugins\MaceMod.dll')
Copy-Item -LiteralPath $releaseUtillaFix -Destination (Join-Path $releaseMacePackage 'BepInEx\patchers\TotemMod.UtillaFix.dll')
Copy-Item -LiteralPath $releaseCompanionHelper -Destination (Join-Path $releaseMacePackage 'BepInEx\patchers\TotemMod.CompanionUpdater.dll')
Copy-Item -LiteralPath (Join-Path $releaseMaceSource 'README.md') -Destination (Join-Path $releaseMacePackage 'README.md')
Copy-Item -LiteralPath (Join-Path $releaseRepo 'LICENSE') -Destination (Join-Path $releaseMacePackage 'LICENSE')
$releaseMaceZip=Join-Path $releaseArtifacts ('MaceMod-for-'+$releaseVersion+'.zip')
Compress-Archive -Path (Join-Path $releaseMacePackage '*') -DestinationPath $releaseMaceZip -Force
$releaseNotes=Join-Path $releaseWork 'release-notes.md'
if ($NotesFile) { Copy-Item -LiteralPath $NotesFile -Destination $releaseNotes }
else { @"
PrimalCraft $releaseVersion. Changes and controls are described in README.

For the first install, extract PrimalCraft-$releaseVersion.zip into the Gorilla Tag folder while the game is closed. The ZIP installs the plugin and its updater helper; BepInEx and Utilla are required separately.

The mod checks on startup and periodically, stages compatible signed updates, and installs them at the next game launch with a backup. Settings are under Interface / Updates. Developer keys and local configuration are not included.

Source and GPL v2 license are in this repository at tag $releaseTag. Minecraft assets belong to Mojang/Microsoft; notification template attribution is in README.
"@ | Set-Content -LiteralPath $releaseNotes -Encoding UTF8 }
Write-Output ('Prepared signed release: '+$releaseZip)
Write-Output ('Public source allowlist: '+$releaseRepo)
if (-not $Publish) { Write-Output 'Nothing uploaded. Use the prepared repository and release files to publish.'; return }

& git -C $releaseRepo init -b main
if ($LASTEXITCODE -ne 0) { throw 'Git init failed.' }
& git -C $releaseRepo config core.autocrlf false
$releasePreviousPreference=$ErrorActionPreference
try {
    $ErrorActionPreference='Continue'
    $null=& gh repo view itay123458/TotemMod --json name 2>$null
    $releaseRepositoryExists=$LASTEXITCODE -eq 0
} finally { $ErrorActionPreference=$releasePreviousPreference }
if ($releaseRepositoryExists) {
    & git -C $releaseRepo remote add origin https://github.com/itay123458/TotemMod.git
    & git -C $releaseRepo fetch origin main
    if ($LASTEXITCODE -ne 0) { throw 'Existing repository history could not be fetched.' }
    # Keep the prepared worktree while basing the new commit on existing history.
    & git -C $releaseRepo reset --mixed origin/main
    if ($LASTEXITCODE -ne 0) { throw 'Existing repository history could not be selected.' }
}
$releaseAccountId=& gh api user --jq .id
if ($LASTEXITCODE -ne 0) { throw 'GitHub account lookup failed.' }
& git -C $releaseRepo config user.name itay123458
& git -C $releaseRepo config user.email ($releaseAccountId+'+itay123458@users.noreply.github.com')
& git -C $releaseRepo add -A -- src tests updater tools third_party compat 'mace mod' build.ps1 README.md LICENSE .gitignore
& git -C $releaseRepo commit --quiet -m ('Release PrimalCraft '+$releaseVersion)
if ($LASTEXITCODE -ne 0) { throw 'Git commit failed.' }
if ($releaseRepositoryExists) { & git -C $releaseRepo push origin main }
else { & gh repo create itay123458/TotemMod --public --description 'PrimalCraft: Minecraft items, VR settings and signed updates for Gorilla Tag.' --source $releaseRepo --remote origin --push }
if ($LASTEXITCODE -ne 0) { throw 'Repository creation or push failed. No release was published.' }
& gh release create $releaseTag $releaseDll $releaseHelper $releaseUtillaFix $releaseCompanionHelper $releaseMace $releaseManifest $releaseMaceManifest $releaseZip $releaseMaceZip --repo itay123458/TotemMod --title ('PrimalCraft '+$releaseVersion) --notes-file $releaseNotes --latest
if ($LASTEXITCODE -ne 0) { throw 'Release publication failed.' }
Write-Output ('Published https://github.com/itay123458/TotemMod/releases/tag/'+$releaseTag)
