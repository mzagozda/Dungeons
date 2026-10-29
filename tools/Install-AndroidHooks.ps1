[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'Common.ps1')
$buildDirectory = Join-Path $script:RepoRoot 'game/android/build'
$sourceDirectory = Join-Path $buildDirectory 'src/main/java/com/godot/game'
$activityPath = Join-Path $sourceDirectory 'GodotApp.java'
$manifestPath = Join-Path $buildDirectory 'src/main/AndroidManifest.xml'
if (!(Test-Path -LiteralPath $activityPath)) { throw 'Install the pinned Android build template before applying application hooks.' }
$activity = [IO.File]::ReadAllText($activityPath)
$activity = $activity.Replace("super.onCreate(savedInstanceState);`n        LevelIntentInbox.enqueue(this, getIntent());", "LevelIntentInbox.enqueue(this, getIntent());`n        super.onCreate(savedInstanceState);")
if (!$activity.Contains('LevelIntentInbox.enqueue')) {
    $anchor = 'super.onCreate(savedInstanceState);'
    if ([regex]::Matches($activity, [regex]::Escape($anchor)).Count -ne 1) { throw 'Unexpected Android activity template; application hooks were not applied.' }
    $activity = $activity.Replace($anchor, "LevelIntentInbox.enqueue(this, getIntent());`n        " + $anchor)
    $lastBrace = $activity.LastIndexOf('}')
    $override = "`n    @Override`n    public void onNewIntent(android.content.Intent intent) {`n        super.onNewIntent(intent);`n        setIntent(intent);`n        LevelIntentInbox.enqueue(this, intent);`n    }`n"
    $activity = $activity.Insert($lastBrace, $override)
    [IO.File]::WriteAllText($activityPath, $activity, [Text.UTF8Encoding]::new($false))
}
[IO.File]::WriteAllText($activityPath, $activity, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'platform/android/LevelIntentInbox.java') -Destination (Join-Path $sourceDirectory 'LevelIntentInbox.java') -Force
Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'platform/android/import.gradle') -Destination (Join-Path $buildDirectory 'dungeons-import.gradle') -Force
$gradlePath = Join-Path $buildDirectory 'build.gradle'
$gradle = [IO.File]::ReadAllText($gradlePath)
if (!$gradle.Contains('dungeons-import.gradle')) { [IO.File]::AppendAllText($gradlePath, "`napply from: 'dungeons-import.gradle'`n", [Text.UTF8Encoding]::new($false)) }
$manifest = [xml][IO.File]::ReadAllText($manifestPath)
$androidNamespace = 'http://schemas.android.com/apk/res/android'
$activityNode = @($manifest.manifest.application.activity | Where-Object { $_.GetAttribute('name', $androidNamespace) -eq '.GodotApp' })
if ($activityNode.Count -ne 1) { throw 'Expected one GodotApp manifest activity.' }
[void]$activityNode[0].SetAttribute('exported', $androidNamespace, 'true')
if (!$manifest.OuterXml.Contains('application/x-block-dungeons-level')) {
    foreach ($mime in @('application/octet-stream', 'application/x-block-dungeons-level')) {
        $filter = $manifest.CreateElement('intent-filter')
        foreach ($entry in @(@('action','name','android.intent.action.VIEW'), @('category','name','android.intent.category.DEFAULT'), @('category','name','android.intent.category.BROWSABLE'))) {
            $node = $manifest.CreateElement($entry[0]); [void]$node.SetAttribute($entry[1], $androidNamespace, $entry[2]); [void]$filter.AppendChild($node)
        }
        $data = $manifest.CreateElement('data'); [void]$data.SetAttribute('scheme', $androidNamespace, 'content'); [void]$data.SetAttribute('mimeType', $androidNamespace, $mime)
        if ($mime -eq 'application/octet-stream') { [void]$data.SetAttribute('host', $androidNamespace, '*'); [void]$data.SetAttribute('pathPattern', $androidNamespace, '.*\\.dlvl') }
        [void]$filter.AppendChild($data); [void]$activityNode[0].AppendChild($filter)
    }
}
foreach ($filter in $activityNode[0].'intent-filter') {
    foreach ($data in $filter.SelectNodes('data')) {
        if ($data.GetAttribute('mimeType', $androidNamespace) -eq 'application/octet-stream') { [void]$data.SetAttribute('pathPattern', $androidNamespace, '.*\\.dlvl') }
    }
}
$manifest.Save($manifestPath)
Write-Host 'Android application import hooks installed (no storage permissions).'
