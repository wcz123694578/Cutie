param([string]$Endpoint = 'http://127.0.0.1:57231/')
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$out = Join-Path $root ('mg_tests/batch_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
$null = New-Item -ItemType Directory -Path $out -Force
$script:id = 19000
$script:covered = [Collections.Generic.HashSet[string]]::new()
function Call([string]$name, [hashtable]$arguments = @{}, [switch]$AllowError) {
    $script:id++
    $json = @{jsonrpc='2.0';id=$script:id;method='tools/call';params=@{name=$name;arguments=$arguments}} | ConvertTo-Json -Depth 60 -Compress
    $response = Invoke-RestMethod -Uri $Endpoint -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -NoProxy -TimeoutSec 60
    if ($response.error) { throw ($response.error | ConvertTo-Json -Compress) }
    if ($response.result.isError -and !$AllowError) { throw $response.result.content[0].text }
    $text = $response.result.content[0].text
    try { return ($text | ConvertFrom-Json -Depth 60) } catch { if ($AllowError) { return $text }; throw }
}
function Batch([object[]]$operations, [string]$file, [string]$mode='single', [string]$onError='stop') {
    $result = Call execute_batch @{operations=$operations;label="Batch test $file";undoMode=$mode;onError=$onError} -AllowError
    $result | ConvertTo-Json -Depth 60 | Set-Content (Join-Path $out "$file.json") -Encoding utf8
    if ($result.status -ne 'completed') { throw "Batch $file failed: $($result | ConvertTo-Json -Depth 30 -Compress)" }
    foreach ($step in $result.steps) { $null = $script:covered.Add($step.tool) }
    return $result
}
function Op([string]$id,[string]$tool,[hashtable]$arguments=@{},[object[]]$capture=@()) { return @{id=$id;tool=$tool;arguments=$arguments;capture=$capture} }
function Ref([string]$path) { return @{'$ref'=$path} }
function Handle([string]$name,[string]$property) { return @{'$handle'=$name;property=$property} }
function Capture([string]$name,[string]$kind,[hashtable]$selector) { return @{name=$name;kind=$kind;selector=$selector} }
function Assert($condition,[string]$message) { if (!$condition) { throw $message } }
function Step($result,[string]$id) { return ($result.steps | Where-Object id -eq $id).result }

$original = Call get_project_info
Assert (!$original.isModified) 'Save the current project before this isolated integration test.'
$original | ConvertTo-Json | Set-Content (Join-Path $out 'original-project.json') -Encoding utf8
$switched = $false
try {
    # Rejection must happen before the first edit, even when the invalid operation is last.
    $rejected = Call execute_batch @{operations=@((Op a create_track @{type='video';name='Must not exist'}),(Op b save_project @{path=(Join-Path $out 'must_not_exist.veg')}))} -AllowError
    Assert ($rejected -is [string] -and $rejected -match 'staged') 'Expected single-undo preflight rejection.'
    Assert ((Call get_project_info).trackCount -eq $original.trackCount) 'Preflight changed the original project.'
    $null = Batch @((Op fresh new_project)) 'new-project' 'staged'
    $switched = $true

    $wav = Join-Path $out 'tone.wav'
    $stream = [IO.File]::Create($wav); $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([int](36+48000))
        $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $writer.Write([int]16)
        $writer.Write([int16]1); $writer.Write([int16]1); $writer.Write([int]48000); $writer.Write([int]96000)
        $writer.Write([int16]2); $writer.Write([int16]16); $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([int]48000)
        for($i=0;$i -lt 24000;$i++) { $writer.Write([int16](2000*[Math]::Sin(2*[Math]::PI*440*$i/48000))) }
    } finally { $writer.Dispose() }
    $midi = Join-Path $out 'notes.mid'
    [IO.File]::WriteAllBytes($midi,[byte[]]@(
        0x4D,0x54,0x68,0x64,0,0,0,6,0,0,0,1,1,0xE0,
        0x4D,0x54,0x72,0x6B,0,0,0,0x0D,0,0x90,60,100,0x83,0x60,0x80,60,0,0,0xFF,0x2F,0))

    $ops = [Collections.Generic.List[object]]::new()
    $ops.Add((Op video create_track @{type='video';name='Batch main';index=0} @((Capture main track @{trackIndex=(Ref 'video#/index')}))))
    $ops.Add((Op audio create_track @{type='audio';name='Batch audio';index=0} @((Capture audio track @{trackIndex=(Ref 'audio#/index')}))))
    $ops.Add((Op insert create_track @{type='video';name='Index shifter';index=0} @((Capture shift track @{trackIndex=(Ref 'insert#/index')}))))
    $ops.Add((Op rename update_track @{trackIndex=(Handle main trackIndex);name='Resolved main';mute=$false;solo=$false}))
    $ops.Add((Op generator create_generated_event @{trackIndex=(Handle main trackIndex);pluginId='{Svfx:com.vegascreativesoftware:solidcolor}';startMs=0;lengthMs=9000} @(
        (Capture clip event @{trackIndex=(Handle main trackIndex);eventIndex=(Ref 'generator#/eventInfo/eventIndex')}))))
    $clip = @{trackIndex=(Handle clip trackIndex);eventIndex=(Handle clip eventIndex)}
    $fx = $clip + @{targetType='event';effectIndex=(Handle fx effectIndex)}
    $ops.Add((Op color set_generator_parameter ($clip + @{parameterName='Color';value='0.2,0.5,0.8,1'})))
    $ops.Add((Op fx add_effect ($clip + @{targetType='event';pluginId='{Svfx:com.vegascreativesoftware:pictureinpicture}'}) @(
        (Capture fx effect ($clip + @{targetType='event';effectIndex=(Ref 'fx#/index')})))))
    $ops.Add((Op bypass update_effect ($fx + @{bypass=$false})))
    $videoTypes = @('Linear','Hold','Slow','Fast','Smooth','Sharp')
    $ofxTypes = @('Linear','Hold','Slow','Fast','Smooth','Sharp','Manual','Split')
    for($i=0;$i -lt 8;$i++) {
        $ops.Add((Op "scale$i" set_effect_parameter ($fx + @{parameterName='Scale';value=([string](0.3+$i*0.05));atMs=($i*1001)})))
        if($i -lt 6) {
            $ops.Add((Op "pan$i" set_pan_crop_keyframe ($clip + @{atMs=($i*1001);interpolation=$videoTypes[$i];moveX=($i*10)})))
            $ops.Add((Op "motion$i" set_track_motion_keyframe @{trackIndex=(Handle main trackIndex);atMs=($i*1001);interpolation=$videoTypes[$i];x=($i*10);y=0}))
        }
    }
    for($i=0;$i -lt 8;$i++) { $ops.Add((Op "interpolation$i" set_ofx_keyframe_interpolation_by_index ($fx + @{parameterName='Scale';keyframeIndex=$i;interpolation=$ofxTypes[$i]}))) }
    $ops.Add((Op timeInterpolation set_ofx_keyframe_interpolation ($fx + @{parameterName='Scale';atMs=1001;interpolation='Hold'})))
    $ops.Add((Op mirror set_pan_crop_keyframe ($clip + @{atMs=0;interpolation='Linear';scaleX=-1;scaleY=1})))
    $ops.Add((Op unmirror set_pan_crop_keyframe ($clip + @{atMs=0;interpolation='Linear';scaleX=-1;scaleY=1})))
    $ops.Add((Op panRead list_pan_crop_keyframes $clip))
    $ops.Add((Op motionRead list_track_motion_keyframes @{trackIndex=(Handle main trackIndex)}))
    $ops.Add((Op ofxRead list_ofx_keyframes ($fx + @{parameterName='Scale'})))
    $ops.Add((Op import import_media @{path=$wav}))
    $ops.Add((Op media get_media @{mediaId=(Ref 'import#/mediaId')}))
    $ops.Add((Op event add_event @{trackIndex=(Handle audio trackIndex);mediaId=(Ref 'import#/mediaId');startMs=1000;lengthMs=400} @(
        (Capture sound event @{trackIndex=(Handle audio trackIndex);eventIndex=(Ref 'event#/eventIndex')}))))
    $sound = @{trackIndex=(Handle sound trackIndex);eventIndex=(Handle sound eventIndex)}
    $ops.Add((Op pitch update_event ($sound + @{pitchSemis=3;name='Pitch +3';sourceOffsetMs=0;playbackRate=1.0})))
    $ops.Add((Op fade set_event_fade ($sound + @{side='in';lengthMs=10})))
    $ops.Add((Op take set_active_take ($sound + @{takeIndex=0})))
    $ops.Add((Op copy copy_event ($sound + @{destinationTrackIndex=(Handle audio trackIndex);startMs=2000}) @(
        (Capture copy event @{trackIndex=(Handle audio trackIndex);eventIndex=(Ref 'copy#/eventIndex')}))))
    $ops.Add((Op split split_event @{trackIndex=(Handle copy trackIndex);eventIndex=(Handle copy eventIndex);offsetMs=200} @(
        (Capture split event @{trackIndex=(Handle audio trackIndex);eventIndex=(Ref 'split#/eventIndex')}))))
    $ops.Add((Op deleteEvent delete_event @{trackIndex=(Handle split trackIndex);eventIndex=(Handle split eventIndex)}))
    $ops.Add((Op analyze analyze_midi @{path=$midi}))
    $ops.Add((Op notes place_midi_notes @{midiPath=$midi;mediaId=(Ref 'import#/mediaId');trackIndex=(Handle audio trackIndex);channel=1;fromMs=0;toMs=500;baseNote=60}))
    $ops.Add((Op marker add_marker @{positionMs=700;label='marker'} @((Capture marker marker @{index=(Ref 'marker#/index')}))))
    $ops.Add((Op earlier add_marker @{positionMs=100;label='earlier'}))
    $ops.Add((Op markerUpdate update_marker @{index=(Handle marker index);positionMs=800;label='moved'}))
    $ops.Add((Op markerDelete delete_marker @{index=(Handle marker index)}))
    $ops.Add((Op region add_region @{startMs=0;lengthMs=1000;label='region'} @((Capture region region @{index=(Ref 'region#/index')}))))
    $ops.Add((Op regionUpdate update_region @{index=(Handle region index);lengthMs=500;label='updated'}))
    $ops.Add((Op regionDelete delete_region @{index=(Handle region index)}))
    foreach($name in @('get_vegas_info','get_project_info','list_tracks','get_timeline_state','list_keyframe_types','list_media','get_selection','list_markers','list_generators','get_render_status')) { $ops.Add((Op $name $name)) }
    $ops.Add((Op plugins list_plugins @{type='video'}))
    $ops.Add((Op presets list_generator_presets @{pluginId='{Svfx:com.vegascreativesoftware:solidcolor}'}))
    $ops.Add((Op generatorParameters list_generator_parameters $clip))
    $ops.Add((Op effectParameters list_effect_parameters $fx))
    $ops.Add((Op effects list_effects ($clip + @{targetType='event'})))
    $ops.Add((Op eventRead get_event $sound))
    $ops.Add((Op events list_events @{trackIndex=(Handle audio trackIndex)}))
    $ops.Add((Op secondFx add_effect ($clip + @{targetType='event';pluginId='{Svfx:com.vegascreativesoftware:pictureinpicture}'})))
    $ops.Add((Op removeFx remove_effect ($clip + @{targetType='event';effectIndex=(Ref 'secondFx#/index')})))
    $ops.Add((Op removeTrack delete_track @{trackIndex=(Handle shift trackIndex)}))
    $ops.Add((Op finalTrack update_track @{trackIndex=(Handle main trackIndex);name='Resolved after deletion'}))
    $result = Batch $ops.ToArray() 'edit-matrix'
    $mirrored = (Step $result mirror).bounds
    $restored = (Step $result unmirror).bounds
    Assert ($mirrored.topLeft.x -gt $mirrored.topRight.x) 'Native horizontal flip did not reverse X bounds.'
    Assert ($restored.topLeft.x -lt $restored.topRight.x) 'Second native flip did not restore X bounds.'
    $pan = Step $result panRead; $motion = Step $result motionRead; $ofx = Step $result ofxRead
    for($i=0;$i -lt 6;$i++) { Assert ($pan[$i].type -eq $videoTypes[$i]) "Pan/Crop type $i"; Assert ($motion[$i].type -eq $videoTypes[$i]) "TrackMotion type $i" }
    for($i=0;$i -lt 8;$i++) { Assert ($ofx.keyframes[$i].interpolation -eq $ofxTypes[$i]) "OFX type $i" }
    Assert ((Step $result eventRead).pitchSemis -eq 3) 'Audio pitch readback failed.'
    Assert ((Step $result notes).count -eq 1) 'MIDI placement failed.'
    Assert ((Step $result finalTrack).name -eq 'Resolved after deletion') 'Stable track handle failed.'

    $transport = Batch @((Op cursor set_cursor @{positionMs=0}),(Op selection set_selection @{startMs=0;lengthMs=500}),
        (Op loop set_loop @{startMs=0;lengthMs=500;enabled=$false}),(Op play play),(Op stop stop_playback)) 'transport' 'staged'
    $saved = Join-Path $out 'batch-matrix.veg'
    $null = Batch @((Op save save_project @{path=$saved}),(Op open open_project @{path=$saved}),(Op read get_project_info)) 'save-open' 'staged'
    $templates = Batch @((Op templates list_render_templates @{limit=500})) 'templates'
    $wave = (Step $templates templates).items | Where-Object extension -Match 'wav' | Select-Object -First 1
    Assert ($null -ne $wave) 'No installed WAV render template is available.'
    $renderPath = Join-Path $out 'batch-render.wav'
    $render = Batch @((Op render render_project @{outputPath=$renderPath;rendererId=$wave.rendererId;templateId=$wave.templateId;startMs=0;lengthMs=500}),
        (Op status get_render_status)) 'render' 'staged'
    Assert ((Step $render status).status -eq 'Complete') 'Render completion barrier failed.'
    Assert ((Get-Item $renderPath).Length -gt 44) 'Rendered output is empty.'
    $capabilities = Call list_batch_capabilities
    $missing = @($capabilities | Where-Object { $_.staged -and !$script:covered.Contains($_.name) } | Select-Object -ExpandProperty name)
    Assert ($missing.Count -eq 0) ('Missing batch coverage: ' + ($missing -join ', '))
    @{status='passed';covered=@($script:covered | Sort-Object);operationCount=$ops.Count;outputDirectory=$out;undoVerified=$false} |
        ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out 'summary.json') -Encoding utf8
    Write-Output "PASS: all $($script:covered.Count) non-control tools exercised through batches. Results: $out"
} finally {
    if($switched) {
        $current = Call get_project_info
        if($current.isModified) { $null = Call save_project @{path=(Join-Path $out 'final-state.veg')} }
        if($original.filePath) { $null = Call open_project @{path=$original.filePath} }
        else { $null = Call new_project }
    }
}
