param([string]$Directory,[double]$UntilMs=2000)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
if(!$Directory){$Directory=(Get-ChildItem (Join-Path $root 'mg_tests') -Directory -Filter 'full_ytpmv_*'|Sort-Object Name -Descending|Select-Object -First 1).FullName}
function Call([string]$name,[hashtable]$arguments=@{}){
    $json=@{jsonrpc='2.0';id=30001;method='tools/call';params=@{name=$name;arguments=$arguments}}|ConvertTo-Json -Depth 35 -Compress
    $r=Invoke-RestMethod -Uri 'http://127.0.0.1:57231/' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -NoProxy -TimeoutSec 60
    if($r.error -or $r.result.isError){throw ($r|ConvertTo-Json -Depth 20 -Compress)}
    return ($r.result.content[0].text|ConvertFrom-Json -Depth 40)
}
$listBody=@{jsonrpc='2.0';id=30000;method='tools/list'}|ConvertTo-Json -Compress
$catalog=Invoke-RestMethod -Uri 'http://127.0.0.1:57231/' -Method Post -ContentType 'application/json' -Body $listBody -NoProxy -TimeoutSec 15
$panTool=$catalog.result.tools|Where-Object name -eq 'set_pan_crop_keyframe'
if(!$panTool.inputSchema.properties.scaleX){throw 'Load the updated KeyframeTools and refresh tool registration before running this script.'}
$tracks=@(Call list_tracks)
if($tracks[5].name -ne '06 / HERO CAMERA'){throw 'Expected full YTPMV project.'}
$fps=(Call get_project_info).frameRate;$halfFrame=500/$fps
$notes=@(Get-Content -Raw (Join-Path $Directory 'midi-notes.json')|ConvertFrom-Json|Where-Object channel -eq 5|Sort-Object startMs|Group-Object startTick|ForEach-Object{$_.Group|Select-Object -First 1})
$ops=[Collections.Generic.List[object]]::new();$id=0;$batch=0;$flippedCount=0
$report=[Collections.Generic.List[object]]::new()
function AddOp([string]$name,[hashtable]$arguments){$script:id++;$ops.Add(@{id="native$script:id";tool=$name;arguments=$arguments})}
function Flush([string]$label){
    if(!$ops.Count){return};$script:batch++
    $job=Call start_batch @{label=$label;operations=$ops.ToArray()}
    do {Start-Sleep -Milliseconds 300;$state=Call get_batch_status @{batchId=$job.batchId}}while($state.status -in @('queued','running'))
    $state|ConvertTo-Json -Depth 40|Set-Content (Join-Path $Directory ('native_flip_{0}_{1:D3}.json' -f [int]$UntilMs,$script:batch)) -Encoding utf8
    if($state.status -ne 'completed'){throw ($state|ConvertTo-Json -Depth 20 -Compress)}
    $ops.Clear()
}
function DesiredFlip([double]$globalMs,[int]$track){
    $last=-1
    for($j=0;$j -lt $notes.Count -and $notes[$j].startMs -le $globalMs+$halfFrame;$j++){$last=$j}
    if($last -lt 0){return $false}
    if($track -in @(4,2)){return ($last%2 -ne 0)}
    return ($last%2 -eq 0)
}
foreach($track in @(5,4,2,3)){
    $events=@(Call list_events @{trackIndex=$track})
    foreach($event in $events){
        if($event.startMs -ge $UntilMs){continue}
        $frames=@(Call list_pan_crop_keyframes @{trackIndex=$track;eventIndex=$event.eventIndex})
# All keyframe properties are supplied by this external MIDI arrangement.
        foreach($frame in $frames){
            if($event.startMs+$frame.positionMs -lt $UntilMs){AddOp set_pan_crop_keyframe @{trackIndex=$track;eventIndex=$event.eventIndex;atMs=$frame.positionMs;interpolation='Hold'}}
        }
        if($track -in @(5,4)){
            foreach($note in $notes){
                if($note.startMs -lt $event.startMs -or $note.startMs -ge $event.endMs -or $note.startMs -ge $UntilMs){continue}
                $at=[Math]::Round(($note.startMs-$event.startMs)*$fps/1000)*1000/$fps
                if(!($frames|Where-Object {[Math]::Abs($_.positionMs-$at) -le $halfFrame})){
                    AddOp set_pan_crop_keyframe @{trackIndex=$track;eventIndex=$event.eventIndex;atMs=$at;interpolation='Hold'}
                }
            }
        }
        Flush 'Native flip / create hold keyframes'
        $frames=@(Call list_pan_crop_keyframes @{trackIndex=$track;eventIndex=$event.eventIndex})
        foreach($frame in $frames){
            $global=$event.startMs+$frame.positionMs
            if($global -ge $UntilMs){continue}
            $desired=DesiredFlip $global $track
            $isFlipped=$frame.bounds.topRight.x -lt $frame.bounds.topLeft.x
            if($desired -ne $isFlipped){
                AddOp set_pan_crop_keyframe @{trackIndex=$track;eventIndex=$event.eventIndex;atMs=$frame.positionMs;interpolation='Hold';scaleX=-1;scaleY=1};$flippedCount++
            }
            $report.Add(@{trackIndex=$track;eventIndex=$event.eventIndex;atMs=$frame.positionMs;globalMs=$global;desiredFlipped=$desired})
        }
        Flush 'Native flip / scale X only'
        Write-Output "Native mirror track $track / event $($event.eventIndex)"
    }
}
$check=@(Call list_pan_crop_keyframes @{trackIndex=5;eventIndex=0})
$check|ConvertTo-Json -Depth 20|Set-Content (Join-Path $Directory 'native-flip-first-event.json') -Encoding utf8
if(!($check|Where-Object {$_.bounds.topRight.x -lt $_.bounds.topLeft.x})){throw 'Native ScaleBy did not reverse horizontal bounds.'}
@{untilMs=$UntilMs;appliedScales=$flippedCount;nativeMethod='VideoMotionKeyframe.ScaleBy(-1,1)';schedule=$report.ToArray()}|ConvertTo-Json -Depth 12|
    Set-Content (Join-Path $Directory ('native-flip-{0}.json' -f [int]$UntilMs)) -Encoding utf8
Write-Output "NATIVE MIRROR COMPLETE: $flippedCount scale operations, $($report.Count) orientation checks. No displacement, save or render performed."
