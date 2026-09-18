param([string]$Directory)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
if(!$Directory){$Directory=(Get-ChildItem (Join-Path $root 'mg_tests') -Directory -Filter 'full_ytpmv_*'|Sort-Object Name -Descending|Select-Object -First 1).FullName}
function Call([string]$name,[hashtable]$arguments=@{}){
    $json=@{jsonrpc='2.0';id=28000;method='tools/call';params=@{name=$name;arguments=$arguments}}|ConvertTo-Json -Depth 40 -Compress
    $r=Invoke-RestMethod -Uri 'http://127.0.0.1:57231/' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -NoProxy -TimeoutSec 60
    if($r.error -or $r.result.isError){throw ($r|ConvertTo-Json -Depth 15 -Compress)}
    return ($r.result.content[0].text|ConvertFrom-Json -Depth 50)
}
$notes=@(Get-Content -Raw (Join-Path $Directory 'midi-notes.json')|ConvertFrom-Json)
$audioPlan=Get-Content -Raw (Join-Path $Directory 'audio-plan.json')|ConvertFrom-Json
$duration=$audioPlan.durationMs
$tracks=@(Call list_tracks)
if($tracks.Count -ne 17 -or $tracks[3].name -ne '04 / BASS NOTE CUTS'){throw 'Unexpected active project; refusing to edit.'}
$video=(Call list_media|Where-Object path -eq 'C:\Users\wzx12\Documents\ga.mp4'|Select-Object -First 1).mediaId
if($null -eq $video){throw 'Source video not found in project media.'}
$existing=@(Call list_events @{trackIndex=3})
$selected=@($notes|Where-Object channel -eq 7|Sort-Object startMs|Group-Object startTick|ForEach-Object{$_.Group|Select-Object -First 1})
$ops=[Collections.Generic.List[object]]::new();$id=0;$added=0
function AddOp([string]$name,[hashtable]$arguments){$script:id++;$key="finish$script:id";$ops.Add(@{id=$key;tool=$name;arguments=$arguments});return $key}
for($i=0;$i -lt $selected.Count;$i+=5){
    $n=$selected[$i]
    if($existing|Where-Object {[Math]::Abs($_.startMs-$n.startMs) -lt 0.01}){continue}
    $phase=[int]([Math]::Floor($i/5))%4
    $key=AddOp add_event @{trackIndex=3;mediaId=$video;startMs=$n.startMs;lengthMs=[Math]::Min(160,[Math]::Max(90,$n.lengthMs))}
    $event=@{'$ref'="$key#/eventIndex"};$added++
    $null=AddOp update_event @{trackIndex=3;eventIndex=$event;name="NOTE $($n.note)";sourceOffsetMs=($phase*120)}
    $null=AddOp add_effect @{targetType='event';trackIndex=3;eventIndex=$event;pluginId='{Svfx:com.vegascreativesoftware:pictureinpicture}'}
    foreach($v in @(@{name='Scale';value='0.168';time=0},@{name='Scale';value='0.14';time=80},@{name='Location';value=@('0.17,0.24','0.83,0.24','0.17,0.77','0.83,0.77')[$phase];time=-1})){
        $a=@{targetType='event';trackIndex=3;eventIndex=$event;effectIndex=0;parameterName=$v.name;value=$v.value}
        if($v.time -ge 0){$a.atMs=$v.time};$null=AddOp set_effect_parameter $a
    }
    $null=AddOp set_event_fade @{trackIndex=3;eventIndex=$event;side='out';lengthMs=30}
}
if($ops.Count){
    $r=Call execute_batch @{label='Complete remaining bass note cutouts';operations=$ops.ToArray()}
    $r|ConvertTo-Json -Depth 40|Set-Content (Join-Path $Directory 'finish-cutouts.json') -Encoding utf8
}
$tracks=@(Call list_tracks)
$validation=[Collections.Generic.List[object]]::new()
foreach($channel in ($notes|Group-Object channel)){
    $track=$tracks|Where-Object name -eq "GA / MIDI $($channel.Name)"|Select-Object -First 1
    $events=@(Call list_events @{trackIndex=$track.index})
    if($events.Count -ne $channel.Count){throw "Audio note mismatch on channel $($channel.Name)"}
    $validation.Add(@{channel=[int]$channel.Name;expected=$channel.Count;actual=$events.Count;minimumPitch=($events|Measure-Object pitchSemis -Minimum).Minimum;maximumPitch=($events|Measure-Object pitchSemis -Maximum).Maximum})
}
$jitter=@(Call list_track_motion_keyframes @{trackIndex=5})
if(!($jitter|Where-Object x -gt 25) -or !($jitter|Where-Object x -lt -25)){throw 'Note-triggered left/right motion missing.'}
$project=Join-Path $Directory 'Cheeky_Girls_Fast_Food_FULL.veg'
$null=Call save_project @{path=$project}
$templates=Call list_render_templates @{limit=500}
$templates|ConvertTo-Json -Depth 12|Set-Content (Join-Path $Directory 'render-templates.json') -Encoding utf8
$candidates=@($templates.items|Where-Object {$_.extension -match 'mp4' -and $_.width -eq 1920 -and $_.height -eq 1080 -and [Math]::Abs($_.frameRate-29.97) -lt 0.1})
$template=$candidates|Where-Object {$_.rendererName -match 'MAGIX AVC' -and $_.templateName -match 'Internet|互联网|Internet HD'}|Select-Object -First 1
if(!$template){$template=$candidates|Select-Object -First 1}
if(!$template){throw 'No 1080p30 MP4 template found.'}
$renderPath=Join-Path $Directory 'Cheeky_Girls_Fast_Food_FULL.mp4'
$job=Call start_batch @{label='Render full YTPMV with note jitter';undoMode='staged';completionTimeoutSeconds=3600;operations=@(
    @{id='render';tool='render_project';arguments=@{outputPath=$renderPath;rendererId=$template.rendererId;templateId=$template.templateId;startMs=0;lengthMs=$duration}},
    @{id='status';tool='get_render_status';arguments=@{}})}
@{batchId=$job.batchId;outputPath=$renderPath;projectPath=$project;durationMs=$duration;template=$template}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $Directory 'render-job.json') -Encoding utf8
@{project=$project;durationMs=$duration;trackCount=$tracks.Count;eventCount=($tracks|Measure-Object eventCount -Sum).Sum;shotCount=$tracks[5].eventCount;addedBassCutouts=$added;audioValidation=$validation.ToArray();heroMotionKeyframes=$jitter.Count;tracks=$tracks}|
    ConvertTo-Json -Depth 12|Set-Content (Join-Path $Directory 'arrangement.json') -Encoding utf8
Write-Output "COMPLETE: 5454 notes; $($tracks.Count) tracks; $($tracks[5].eventCount) shots; $($jitter.Count) hero motion keys. Added $added missing cutouts. Render job $($job.batchId)."
Write-Output "Output: $renderPath"
