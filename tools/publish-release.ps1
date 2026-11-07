[CmdletBinding()]
param(
    [string]$GamePath='C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag',
    [switch]$SkipBuild,
    [switch]$Publish
)
$ErrorActionPreference='Stop'
$releaseRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $releaseRoot 'build.ps1') -GamePath $GamePath; if ($LASTEXITCODE -ne 0) { throw 'Build failed.' } }
$releaseArtifacts=Join-Path $releaseRoot 'artifacts'
$releaseDll=Join-Path $releaseArtifacts 'TotemMod.dll'
$releaseHelper=Join-Path $releaseArtifacts 'TotemMod.Updater.dll'
$releaseKey=Join-Path $env:USERPROFILE '.totemmod\update-signing-private.xml'
if (-not (Test-Path -LiteralPath $releaseKey)) { throw 'Publisher signing key is missing. It is not stored in the repository.' }
$releaseManifest=Join-Path $releaseArtifacts 'totem-update.json'
& (Join-Path $releaseArtifacts 'tests\MakeUpdateManifest.exe') $releaseDll (Join-Path $GamePath 'Gorilla Tag_Data\Managed\Assembly-CSharp.dll') $releaseKey $releaseManifest
if ($LASTEXITCODE -ne 0) { throw 'Release signing failed.' }
$releaseMetadata=Get-Content -LiteralPath $releaseManifest -Raw | ConvertFrom-Json
$releaseVersion=$releaseMetadata.Version
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
$null=[IO.Directory]::CreateDirectory((Join-Path $releaseRepo 'compat\utilla'))
Get-ChildItem -LiteralPath (Join-Path $releaseRoot 'compat\utilla') -File | Where-Object { $_.Extension -in '.cs','.ps1' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $releaseRepo 'compat\utilla') }
foreach ($releaseFile in @('build.ps1','README.md','.gitignore')) { Copy-Item -LiteralPath (Join-Path $releaseRoot $releaseFile) -Destination (Join-Path $releaseRepo $releaseFile) }
Copy-Item -LiteralPath (Join-Path $releaseRoot 'third_party\GorillaNotifications\LICENSE') -Destination (Join-Path $releaseRepo 'LICENSE')
Copy-Item -LiteralPath $releaseDll -Destination (Join-Path $releasePackage 'BepInEx\plugins\TotemMod.dll')
Copy-Item -LiteralPath $releaseHelper -Destination (Join-Path $releasePackage 'BepInEx\patchers\TotemMod.Updater.dll')
Copy-Item -LiteralPath (Join-Path $releaseRoot 'README.md') -Destination (Join-Path $releasePackage 'README.md')
Copy-Item -LiteralPath (Join-Path $releaseRepo 'LICENSE') -Destination (Join-Path $releasePackage 'LICENSE')
$releaseUnexpected=@(Get-ChildItem -LiteralPath $releaseRepo -Recurse -File | Where-Object { $_.Extension -in '.dll','.exe','.pfx','.pem','.key','.cfg' })
if ($releaseUnexpected.Count -gt 0) { throw ('Unexpected binary or private configuration in source package: '+$releaseUnexpected[0].FullName) }
foreach ($releaseTextFile in (Get-ChildItem -LiteralPath $releaseRepo -Recurse -File | Where-Object { $_.Extension -in '.cs','.ps1','.py','.md','.json','.txt','.yml','.yaml' })) {
    $releaseText=[IO.File]::ReadAllText($releaseTextFile.FullName)
    if ($releaseText -match '(?s)<RSAKeyValue>[^"\r\n]*<D>' -or $releaseText -match '-----BEGIN (RSA |OPENSSH )?PRIVATE KEY-----' -or $releaseText -match 'gh[pousr]_[A-Za-z0-9]{25,}') { throw ('Secret-like material found in '+$releaseTextFile.FullName) }
}
$releaseZip=Join-Path $releaseArtifacts ('TotemMod-'+$releaseVersion+'.zip')
Compress-Archive -Path (Join-Path $releasePackage '*') -DestinationPath $releaseZip -Force
$releaseNotes=Join-Path $releaseWork 'release-notes.md'
@"
Totem Mod $releaseVersion adds signed automatic updates from this repository.

For the first install, extract TotemMod-$releaseVersion.zip into the Gorilla Tag folder while the game is closed. The ZIP installs the plugin and its updater helper; BepInEx and Utilla are required separately.

The mod checks on startup and periodically, stages compatible signed updates, and installs them at the next game launch with a backup. Settings are under Interface / Updates. Developer keys and local configuration are not included.

Source and GPL v2 license are in this repository at tag $releaseTag. Minecraft assets belong to Mojang/Microsoft; notification template attribution is in README.
"@ | Set-Content -LiteralPath $releaseNotes -Encoding UTF8
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
& git -C $releaseRepo add -A -- src tests updater tools third_party compat build.ps1 README.md LICENSE .gitignore
& git -C $releaseRepo commit --quiet -m ('Release Totem Mod '+$releaseVersion)
if ($LASTEXITCODE -ne 0) { throw 'Git commit failed.' }
if ($releaseRepositoryExists) { & git -C $releaseRepo push origin main }
else { & gh repo create itay123458/TotemMod --public --description 'Gorilla Tag Totem Mod: presets, VR settings, Beta avatar totems and signed automatic updates.' --source $releaseRepo --remote origin --push }
if ($LASTEXITCODE -ne 0) { throw 'Repository creation or push failed. No release was published.' }
& gh release create $releaseTag $releaseDll $releaseHelper $releaseManifest $releaseZip --repo itay123458/TotemMod --title ('Totem Mod '+$releaseVersion) --notes-file $releaseNotes --latest
if ($LASTEXITCODE -ne 0) { throw 'Release publication failed.' }
Write-Output ('Published https://github.com/itay123458/TotemMod/releases/tag/'+$releaseTag)
