param([string]$AudioPath='C:\Users\wzx12\Documents\ga.wav',
      [string]$VideoPath='C:\Users\wzx12\Documents\ga.mp4')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$out=Join-Path $root ('mg_tests/full_ytpmv_'+(Get-Date -Format 'yyyyMMdd_HHmmss'))
$null=New-Item -ItemType Directory -Path $out -Force
$midi=Join-Path $root 'mg_tests/cheeky_girls_fast_food_source.mid'
$script:op=0; $script:batch=0; $script:operations=[Collections.Generic.List[object]]::new()
$script:reports=[Collections.Generic.List[object]]::new()
function Call([string]$name,[hashtable]$arguments=@{}) {
    $body=@{jsonrpc='2.0';id=25000;method='tools/call';params=@{name=$name;arguments=$arguments}}|ConvertTo-Json -Depth 45 -Compress
    $r=Invoke-RestMethod -Uri 'http://127.0.0.1:57231/' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -NoProxy -TimeoutSec 60
    if($r.error){throw ($r.error|ConvertTo-Json -Compress)}
    if($r.result.isError){throw "$name : $($r.result.content[0].text)"}
    return ($r.result.content[0].text|ConvertFrom-Json -Depth 60)
}
function AddOp([string]$tool,[hashtable]$arguments=@{}) {
    $script:op++; $id='s'+$script:op
    $script:operations.Add(@{id=$id;tool=$tool;arguments=$arguments}); return $id
}
function Ref([string]$id,[string]$path) {return @{'$ref'="$id#/$path"}}
function Flush([string]$label) {
    if(!$script:operations.Count){return}
    $script:batch++; $path=Join-Path $out ('batch_{0:D3}' -f $script:batch)
    $args=@{operations=$script:operations.ToArray();label=$label}
    $args|ConvertTo-Json -Depth 45|Set-Content "$path.request.json" -Encoding utf8
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $start=Call start_batch $args
    $start|ConvertTo-Json|Set-Content "$path.job.json" -Encoding utf8
    do {
        Start-Sleep -Milliseconds 500
        $state=Call get_batch_status @{batchId=$start.batchId}
        if($clock.Elapsed.TotalMinutes -gt 10){throw "Batch still running: $($start.batchId). Query status; do not replay."}
    } while($state.status -in @('queued','running'))
    $state|ConvertTo-Json -Depth 60|Set-Content "$path.result.json" -Encoding utf8
    if($state.status -ne 'completed'){throw ($state|ConvertTo-Json -Depth 30 -Compress)}
    $script:reports.Add(@{label=$label;steps=$script:operations.Count;elapsedMs=$clock.ElapsedMilliseconds;batchId=$start.batchId})
    Write-Output "Batch $($script:batch): $label / $($script:operations.Count) steps / $($clock.ElapsedMilliseconds) ms"
    $script:operations.Clear()
}
function F([double]$n){return $n.ToString('0.######',[Globalization.CultureInfo]::InvariantCulture)}
function Fx([int]$track,$event,[string]$parameter,[string]$value,[double]$time=-1) {
    $args=@{targetType='event';trackIndex=$track;eventIndex=$event;effectIndex=0;parameterName=$parameter;value=$value}
    if($time -ge 0){$args.atMs=$time}
    $null=AddOp set_effect_parameter $args
}
$before=Call get_project_info
if($before.isModified){throw 'Save the current project first.'}
$before|ConvertTo-Json|Set-Content (Join-Path $out 'previous-project.json') -Encoding utf8
$notes=[Collections.Generic.List[object]]::new()
for($offset=0;;$offset+=2000){
    $page=Call analyze_midi @{path=$midi;offset=$offset;limit=2000}
    foreach($n in $page.notes){$notes.Add($n)}
    if($offset+2000 -ge $page.selectedNoteCount){break}
}
$notes.ToArray()|ConvertTo-Json -Depth 10|Set-Content (Join-Path $out 'midi-notes.json') -Encoding utf8
$channels=$page.channels
$songEnd=($channels|Measure-Object lastMs -Maximum).Maximum
$beat=333.333; $phrase=$beat*16; $duration=[Math]::Ceiling(($songEnd+1000)/$beat)*$beat

# Read the supplied PCM16 sample; derive quiet stereo variants from the same source.
# No external music or drum samples are introduced.
$wave=[IO.File]::ReadAllBytes($AudioPath)
if([Text.Encoding]::ASCII.GetString($wave,0,4) -ne 'RIFF' -or [BitConverter]::ToInt16($wave,20) -ne 1 -or [BitConverter]::ToInt16($wave,34) -ne 16){throw 'Expected PCM16 WAV source.'}
$sampleRate=[BitConverter]::ToInt32($wave,24); $nChannels=[BitConverter]::ToInt16($wave,22)
$dataOffset=12
while([Text.Encoding]::ASCII.GetString($wave,$dataOffset,4) -ne 'data'){$size=[BitConverter]::ToInt32($wave,$dataOffset+4);$dataOffset+=8+$size+($size%2)}
$dataLength=[BitConverter]::ToInt32($wave,$dataOffset+4);$dataOffset+=8
$frames=$dataLength/(2*$nChannels);$sourceLength=$frames*1000/$sampleRate
$mono=[double[]]::new($frames);$peak=0.0
for($i=0;$i -lt $frames;$i++){
    for($c=0;$c -lt $nChannels;$c++){$sample=[BitConverter]::ToInt16($wave,$dataOffset+($i*$nChannels+$c)*2)/32768.0;$mono[$i]+=$sample/$nChannels;$peak=[Math]::Max($peak,[Math]::Abs($sample))}
}
# Autocorrelation across the voiced center of the 200 ms sample. Estimate only; record confidence.
$best=0.0;$bestLag=0;$lagMin=[int]($sampleRate/650);$lagMax=[int]($sampleRate/80)
for($lag=$lagMin;$lag -le $lagMax;$lag++){
    $sum=0.0;$a=0.0;$b=0.0
    for($i=[int]($sampleRate*0.025);$i -lt [int]($sampleRate*0.135);$i+=3){$x=$mono[$i];$y=$mono[$i+$lag];$sum+=$x*$y;$a+=$x*$x;$b+=$y*$y}
    $score=$sum/[Math]::Sqrt([Math]::Max(1e-12,$a*$b))
    if($score -gt $best){$best=$score;$bestLag=$lag}
}
$frequency=$sampleRate/$bestLag;$baseNote=[int][Math]::Round(69+12*[Math]::Log($frequency/440,2))
if($best -lt 0.45){$baseNote=55}
$weights=@{1=0.50;2=0.27;3=0.25;5=0.85;7=0.62;8=0.3;9=0.3;10=0.52;12=0.38;13=0.3}
$sweep=[Collections.Generic.List[object]]::new()
foreach($n in $notes){$len=[Math]::Max(33.4,[Math]::Min(195,$n.lengthMs));$w=$weights[[int]$n.channel];$sweep.Add(@{t=$n.startMs;v=$w});$sweep.Add(@{t=($n.startMs+$len);v=-$w})}
$active=0.0;$maxWeight=0.0
foreach($point in ($sweep|Sort-Object t,v)){$active+=$point.v;$maxWeight=[Math]::Max($maxWeight,$active)}
$headroom=0.82/([Math]::Max(0.01,$peak)*$maxWeight)
$audioFiles=@{}
foreach($channel in $channels){
    $ch=[int]$channel.channel;$copy=[byte[]]$wave.Clone();$gain=$weights[$ch]*$headroom
    $pan=if($ch -in @(2,8,12)){-0.22}elseif($ch -in @(3,9,13)){0.22}else{0}
    for($i=0;$i -lt $frames;$i++){
        $fade=[Math]::Min(1,[Math]::Min($i/([double]$sampleRate*0.002),($frames-1-$i)/([double]$sampleRate*0.009)))
        for($c=0;$c -lt $nChannels;$c++){
            $at=$dataOffset+($i*$nChannels+$c)*2;$side=if($c -eq 0){1-[Math]::Max(0,$pan)}else{1+[Math]::Min(0,$pan)}
            $value=[int16]([BitConverter]::ToInt16($wave,$at)*$gain*$fade*$side)
            $bytes=[BitConverter]::GetBytes($value);$copy[$at]=$bytes[0];$copy[$at+1]=$bytes[1]
        }
    }
    $file=Join-Path $out "ga_channel_$ch.wav";[IO.File]::WriteAllBytes($file,$copy);$audioFiles[$ch]=$file
}
@{sourcePeak=$peak;estimatedFrequency=$frequency;correlation=$best;baseNote=$baseNote;maximumWeightedOverlap=$maxWeight;gainScale=$headroom;durationMs=$duration;noteCount=$notes.Count}|ConvertTo-Json|Set-Content (Join-Path $out 'audio-plan.json') -Encoding utf8
Write-Output "Full song: $($notes.Count) notes / $([Math]::Round($duration/1000,2)) s / estimated sample MIDI $baseNote (correlation $([Math]::Round($best,3)))"

# Graphic plates: original typography and geometry, with transparent cutout space for the ga video.
Add-Type -AssemblyName System.Drawing
$palette=@('#C9FA72','#FF8EAB','#79DBEA','#EBD7AB','#B4A4FA','#FFAC76')
$plates=@();$titles=@()
for($p=0;$p -lt 6;$p++){
    $bmp=[Drawing.Bitmap]::new(1920,1080);$g=[Drawing.Graphics]::FromImage($bmp);$g.SmoothingMode='AntiAlias';$g.Clear([Drawing.Color]::Transparent)
    $accent=[Drawing.ColorTranslator]::FromHtml($palette[$p]);$brush=[Drawing.SolidBrush]::new($accent);$white=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(240,244,239));$pen=[Drawing.Pen]::new($accent,2)
    $font=[Drawing.Font]::new('Arial',24,[Drawing.FontStyle]::Bold);$small=[Drawing.Font]::new('Consolas',15)
    $g.DrawString('CHEEKY GIRLS / FAST FOOD',$font,$white,70,42);$g.DrawString('GA!',$font,$brush,1740,42)
    $g.DrawLine($pen,70,98,1850,98);$g.DrawLine($pen,70,986,1850,986)
    $g.DrawString(('SIDE {0:00} / {1}' -f ($p+1),@('CUT & REPEAT','DOUBLE TAKE','SUGAR RUSH','AFTER HOURS','STACK ATTACK','ONE MORE!')[$p]),$small,$white,70,1010)
    for($q=0;$q -lt 12;$q++){$g.FillRectangle($brush,1470+$q*30,1012,18,8+($q%4)*4)}
    $g.DrawRectangle($pen,55,130,1810,820)
    $file=Join-Path $out "frame_$p.png";$bmp.Save($file,[Drawing.Imaging.ImageFormat]::Png);$plates+=$file
    $g.Clear([Drawing.Color]::Transparent)
    $big=[Drawing.Font]::new('Arial',115,[Drawing.FontStyle]::Bold)
    $g.DrawString(@('GA!','FAST','FOOD','CHEEKY','REPEAT','AGAIN!')[$p],$big,$brush,92,770)
    $file=Join-Path $out "title_$p.png";$bmp.Save($file,[Drawing.Imaging.ImageFormat]::Png);$titles+=$file
    $big.Dispose();$font.Dispose();$small.Dispose();$pen.Dispose();$brush.Dispose();$white.Dispose();$g.Dispose();$bmp.Dispose()
}
$null=Call new_project
$video=(Call import_media @{path=$VideoPath}).mediaId
$plateIds=@($plates|ForEach-Object{(Call import_media @{path=$_}).mediaId})
$titleIds=@($titles|ForEach-Object{(Call import_media @{path=$_}).mediaId})
$audioIds=@{};foreach($ch in $channels){$c=[int]$ch.channel;$audioIds[$c]=(Call import_media @{path=$audioFiles[$c]}).mediaId}
$videoTracks=@('01 / FRAME & TYPE','02 / CHAPTER PUNCH','03 / LEAD NOTE CUTS','04 / BASS NOTE CUTS','05 / SECOND CAMERA','06 / HERO CAMERA','07 / COLOR FIELD')
for($i=0;$i -lt $videoTracks.Count;$i++){$null=AddOp create_track @{type='video';name=$videoTracks[$i];index=$i}}
$audioTracks=@{};$index=$videoTracks.Count
foreach($ch in $channels){$c=[int]$ch.channel;$audioTracks[$c]=$index;$null=AddOp create_track @{type='audio';name="GA / MIDI $c";index=$index};$index++}
Flush 'Full arrangement / tracks'

# All note-on events, without truncating channels at the 1000-event tool limit.
foreach($channel in $channels){
    $ch=[int]$channel.channel
    for($from=0;$from -lt $duration;$from+=32000){
        $null=AddOp place_midi_notes @{midiPath=$midi;mediaId=$audioIds[$ch];trackIndex=$audioTracks[$ch];channel=$ch;fromMs=$from;toMs=[Math]::Min($duration,$from+32000);baseNote=$baseNote;selection='all';maxEvents=1000;maxLengthMs=195}
    }
    Flush "Audio / MIDI channel $ch"
}
# Very short fades remove cut discontinuities without changing note timing or pitch.
foreach($channel in $channels){
    $ch=[int]$channel.channel;$events=@(Call list_events @{trackIndex=$audioTracks[$ch]})
    if($events.Count -ne $channel.noteCount){throw "Channel $ch was truncated: $($events.Count) / $($channel.noteCount)"}
    foreach($event in $events){
        $null=AddOp set_event_fade @{trackIndex=$audioTracks[$ch];eventIndex=$event.eventIndex;side='out';lengthMs=5}
        if($script:operations.Count -ge 240){Flush "Audio gates / channel $ch"}
    }
    Flush "Audio gates / channel $ch"
}
$pip='{Svfx:com.vegascreativesoftware:pictureinpicture}';$solid='{Svfx:com.vegascreativesoftware:solidcolor}'
$layouts=@(
    @{scale=0.78;x=0.50;y=0.51;echo=0.21;ex=0.80;ey=0.77},
    @{scale=0.59;x=0.33;y=0.50;echo=0.36;ex=0.79;ey=0.49},
    @{scale=0.68;x=0.62;y=0.48;echo=0.27;ex=0.20;ey=0.70},
    @{scale=0.87;x=0.50;y=0.48;echo=0.20;ex=0.20;ey=0.76},
    @{scale=0.53;x=0.32;y=0.42;echo=0.47;ex=0.72;ey=0.60},
    @{scale=0.76;x=0.50;y=0.55;echo=0.24;ex=0.80;ey=0.27})
$shots=[Collections.Generic.List[object]]::new();$shotCount=[int][Math]::Ceiling($duration/$phrase)
for($shot=0;$shot -lt $shotCount;$shot++){
    $start=$shot*$phrase;$length=[Math]::Min($phrase,$duration-$start);$p=$shot%6;$layout=$layouts[$p]
    $shots.Add(@{shot=$shot+1;startMs=$start;lengthMs=$length;layout=$p})
    $null=AddOp add_region @{startMs=$start;lengthMs=$length;label=('SHOT {0:00} / {1}' -f ($shot+1),@('HERO','DUET','OFFSET','PUSH','SPLIT','REPRISE')[$p])}
    $bg=AddOp create_generated_event @{trackIndex=6;pluginId=$solid;startMs=$start;lengthMs=$length}
    $null=AddOp set_generator_parameter @{trackIndex=6;eventIndex=(Ref $bg 'eventInfo/eventIndex');parameterName='Color';value=@('0.035,0.055,0.075,1','0.085,0.035,0.055,1','0.035,0.070,0.080,1','0.075,0.060,0.045,1','0.055,0.040,0.080,1','0.085,0.045,0.030,1')[$p]}
    $null=AddOp add_event @{trackIndex=0;mediaId=$plateIds[$p];startMs=$start;lengthMs=$length}
    $title=AddOp add_event @{trackIndex=1;mediaId=$titleIds[$p];startMs=$start;lengthMs=[Math]::Min(1000,$length)}
    $null=AddOp set_event_fade @{trackIndex=1;eventIndex=(Ref $title 'eventIndex');side='out';lengthMs=250}
    foreach($track in @(5,4)){
        $ev=AddOp add_event @{trackIndex=$track;mediaId=$video;startMs=$start;lengthMs=$length};$ei=Ref $ev 'eventIndex'
        $null=AddOp update_event @{trackIndex=$track;eventIndex=$ei;name="SHOT $($shot+1) / CAM $track";loop=$true;sourceOffsetMs=(($shot%4)*180)}
        $null=AddOp add_effect @{targetType='event';trackIndex=$track;eventIndex=$ei;pluginId=$pip}
        $scale=if($track -eq 5){$layout.scale}else{$layout.echo};$x=if($track -eq 5){$layout.x}else{$layout.ex};$y=if($track -eq 5){$layout.y}else{$layout.ey}
        Fx $track $ei Scale (F ($scale*0.92)) 0
        Fx $track $ei Scale (F $scale) 180
        Fx $track $ei Scale (F ($scale*1.06)) ([Math]::Max(200,$length-200))
        Fx $track $ei Location ((F ($x-0.025))+','+(F $y)) 0
        Fx $track $ei Location ((F ($x+0.025))+','+(F $y)) ([Math]::Max(200,$length-200))
        $null=AddOp set_ofx_keyframe_interpolation_by_index @{targetType='event';trackIndex=$track;eventIndex=$ei;effectIndex=0;parameterName='Scale';keyframeIndex=0;interpolation='Fast'}
        $null=AddOp set_pan_crop_keyframe @{trackIndex=$track;eventIndex=$ei;atMs=0;interpolation='Smooth';moveX=((-1+[int]($shot%2)*2)*25)}
        $null=AddOp set_pan_crop_keyframe @{trackIndex=$track;eventIndex=$ei;atMs=([Math]::Max(200,$length-200));interpolation='Smooth';moveX=([int]($shot%3)*25)}
    }
    $null=AddOp set_track_motion_keyframe @{trackIndex=4;atMs=$start;interpolation='Smooth';x=([Math]::Sin($shot)*12);y=0}
    if($script:operations.Count -ge 200){Flush 'Picture / phrase cameras'}
}
Flush 'Picture / phrase cameras'

# Note-triggered overlays use two sparse layers, with independent positions and punch-in motion.
foreach($layer in @(@{track=2;channel=5;every=3;scale=0.19},@{track=3;channel=7;every=5;scale=0.14})){
    $selected=@($notes|Where-Object channel -eq $layer.channel|Sort-Object startMs|Group-Object startTick|ForEach-Object{$_.Group|Select-Object -First 1})
    for($i=0;$i -lt $selected.Count;$i+=$layer.every){
        $note=$selected[$i];$phase=[int]([Math]::Floor($i/$layer.every))%4
        $ev=AddOp add_event @{trackIndex=$layer.track;mediaId=$video;startMs=$note.startMs;lengthMs=[Math]::Min(160,[Math]::Max(90,$note.lengthMs))};$ei=Ref $ev 'eventIndex'
        $null=AddOp update_event @{trackIndex=$layer.track;eventIndex=$ei;name="NOTE $($note.note)";sourceOffsetMs=($phase*120)}
        $null=AddOp add_effect @{targetType='event';trackIndex=$layer.track;eventIndex=$ei;pluginId=$pip}
        Fx $layer.track $ei Scale (F ($layer.scale*1.20)) 0
        Fx $layer.track $ei Scale (F $layer.scale) 80
        Fx $layer.track $ei Location @('0.17,0.24','0.83,0.24','0.17,0.77','0.83,0.77')[$phase]
        $null=AddOp set_event_fade @{trackIndex=$layer.track;eventIndex=$ei;side='out';lengthMs=30}
        if($script:operations.Count -ge 210){Flush 'Picture / MIDI-triggered cutouts'}
    }
    Flush 'Picture / MIDI-triggered cutouts'
}
$project=Join-Path $out 'Cheeky_Girls_Fast_Food_FULL.veg'
$null=Call save_project @{path=$project}
$tracks=@(Call list_tracks)
$eventCount=($tracks|Measure-Object eventCount -Sum).Sum
$summary=@{project=$project;outputDirectory=$out;durationMs=$duration;sourceMidi=$midi;sourceAudio=$AudioPath;sourceVideo=$VideoPath;noteCount=$notes.Count;trackCount=$tracks.Count;eventCount=$eventCount;shots=$shots.ToArray();audioBaseNote=$baseNote;audioPlan='All note-on events, 33.4-195 ms gated ga samples; channel-weighted gain and stereo placement';batches=$script:reports.ToArray();tracks=$tracks}
$summary|ConvertTo-Json -Depth 15|Set-Content (Join-Path $out 'arrangement.json') -Encoding utf8
$templates=Call list_render_templates @{limit=500}
$templates|ConvertTo-Json -Depth 12|Set-Content (Join-Path $out 'render-templates.json') -Encoding utf8
$template=$templates.items|Where-Object { $_.extension -match 'mp4' -and $_.width -eq 1920 -and $_.height -eq 1080 -and [Math]::Abs($_.frameRate-29.97) -lt 0.1 }|Select-Object -First 1
if(!$template){throw 'No matching 1080p30 MP4 template; project saved for manual template selection.'}
$renderPath=Join-Path $out 'Cheeky_Girls_Fast_Food_FULL.mp4'
$job=Call start_batch @{label='Render full YTPMV';undoMode='staged';completionTimeoutSeconds=3600;operations=@(
    @{id='render';tool='render_project';arguments=@{outputPath=$renderPath;rendererId=$template.rendererId;templateId=$template.templateId;startMs=0;lengthMs=$duration}},
    @{id='status';tool='get_render_status';arguments=@{}})}
@{batchId=$job.batchId;outputPath=$renderPath;projectPath=$project;durationMs=$duration;template=$template}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $out 'render-job.json') -Encoding utf8
Write-Output "ARRANGEMENT COMPLETE: $($tracks.Count) tracks, $eventCount events, $shotCount shots. Rendering job $($job.batchId). Folder: $out"
