param([string]$Directory)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
if(!$Directory){$Directory=(Get-ChildItem (Join-Path $root 'mg_tests') -Directory -Filter 'full_ytpmv_*'|Sort-Object Name -Descending|Select-Object -First 1).FullName}
function Call([string]$name,[hashtable]$arguments=@{}){
    $json=@{jsonrpc='2.0';id=26000;method='tools/call';params=@{name=$name;arguments=$arguments}}|ConvertTo-Json -Depth 30 -Compress
    $r=Invoke-RestMethod -Uri 'http://127.0.0.1:57231/' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -NoProxy -TimeoutSec 60
    if($r.error -or $r.result.isError){throw ($r|ConvertTo-Json -Depth 15 -Compress)}
    return ($r.result.content[0].text|ConvertFrom-Json -Depth 40)
}
$tracks=@(Call list_tracks)
$hero=$tracks|Where-Object name -eq '06 / HERO CAMERA'|Select-Object -First 1
$cut=$tracks|Where-Object name -eq '03 / LEAD NOTE CUTS'|Select-Object -First 1
if(!$hero -or !$cut){throw 'Full YTPMV tracks are not present.'}
$fps=(Call get_project_info).frameRate
$notes=@(Get-Content -Raw (Join-Path $Directory 'midi-notes.json')|ConvertFrom-Json|Where-Object channel -eq 5|Sort-Object startMs|Group-Object startTick|ForEach-Object{$_.Group|Sort-Object velocity -Descending|Select-Object -First 1})
$ops=[Collections.Generic.List[object]]::new();$keyframes=[Collections.Generic.List[object]]::new();$count=0;$batch=0
function Flush {
    if(!$ops.Count){return}
    $script:batch++
    $job=Call start_batch @{label='Note-synced left-right snap';operations=$ops.ToArray()}
    do {Start-Sleep -Milliseconds 400;$state=Call get_batch_status @{batchId=$job.batchId}}while($state.status -in @('queued','running'))
    $state|ConvertTo-Json -Depth 40|Set-Content (Join-Path $Directory ('jitter_{0:D3}.json' -f $script:batch)) -Encoding utf8
    if($state.status -ne 'completed'){throw ($state|ConvertTo-Json -Depth 20 -Compress)}
    Write-Output "Jitter batch $script:batch / $($ops.Count) keys"
    $ops.Clear()
}
for($i=0;$i -lt $notes.Count;$i++){
    $note=$notes[$i];$frame=[int][Math]::Round($note.startMs*$fps/1000)
    $direction=if($i%2 -eq 0){-1}else{1}
    $amplitude=[Math]::Round(30+34*$note.velocity/127.0)
    foreach($target in @(@{index=$hero.index;factor=1.0},@{index=$cut.index;factor=-1.3})){
        foreach($point in @(@{frame=($frame-1);x=0;type='Hold'},@{frame=$frame;x=($direction*$amplitude*$target.factor);type='Hold'},@{frame=($frame+2);x=0;type='Smooth'})){
            $count++;$time=[Math]::Max(0,$point.frame)*1000/$fps
            $args=@{trackIndex=$target.index;atMs=$time;interpolation=$point.type;x=$point.x;y=0}
            $ops.Add(@{id="j$count";tool='set_track_motion_keyframe';arguments=$args})
            $keyframes.Add(@{trackIndex=$target.index;atMs=$time;x=$point.x;type=$point.type})
        }
    }
    if($ops.Count -ge 240){Flush}
}
Flush
$readback=@(Call list_track_motion_keyframes @{trackIndex=$hero.index})
if(!($readback|Where-Object x -gt 25) -or !($readback|Where-Object x -lt -25)){throw 'Left/right jitter was not written.'}
@{noteChannel=5;noteOnsets=$notes.Count;requestedKeyframes=$count;heroKeyframes=$readback.Count;frameRate=$fps;pattern='center one frame before onset, alternating left/right on onset, center two frames later';keyframes=$keyframes.ToArray()}|
    ConvertTo-Json -Depth 10|Set-Content (Join-Path $Directory 'note-jitter.json') -Encoding utf8
Write-Output "JITTER COMPLETE: $($notes.Count) note onsets, $count keys across two opposing layers."
