param([ValidateSet('Prepare','PrepareUndo','VerifyUndo','VerifyRedo','Restore')][string]$Phase='Prepare',
    [switch]$IncludeFailureInjection)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$out=Join-Path $root 'mg_tests/batch_controls'
$null=New-Item -ItemType Directory -Path $out -Force
function Call([string]$name,[hashtable]$arguments=@{},[switch]$AllowError) {
    $json=@{jsonrpc='2.0';id=21001;method='tools/call';params=@{name=$name;arguments=$arguments}} | ConvertTo-Json -Depth 30 -Compress
    $response=Invoke-RestMethod -Uri 'http://127.0.0.1:57231/' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -NoProxy -TimeoutSec 30
    if($response.error){throw ($response.error | ConvertTo-Json -Compress)}
    if($response.result.isError -and !$AllowError){throw $response.result.content[0].text}
    return ($response.result.content[0].text | ConvertFrom-Json -Depth 30)
}
function Assert($condition,[string]$message) { if(!$condition){throw $message} }
function Snapshot {
    return @{project=(Call get_project_info);tracks=@(Call list_tracks);markers=(Call list_markers);media=@(Call list_media)}
}
if($Phase -in @('Prepare','PrepareUndo')) {
  if($Phase -eq 'Prepare') {
    $original=Call get_project_info
    Assert (!$original.isModified) 'Save the original project first.'
    $original | ConvertTo-Json | Set-Content (Join-Path $out 'original.json') -Encoding utf8
    $null=Call new_project
    $operations=@(0..999 | ForEach-Object { @{id="m$_";tool='add_marker';arguments=@{positionMs=($_*10);label="cancel $_"}} })
    $started=Call start_batch @{operations=$operations;label='Cancellation test'}
    $cancel=Call cancel_batch @{batchId=$started.batchId}
    $timer=[Diagnostics.Stopwatch]::StartNew()
    do {
        $state=Call get_batch_status @{batchId=$started.batchId}
        if($state.status -notin @('queued','running')){break}
        Start-Sleep -Milliseconds 100
    } while($timer.Elapsed.TotalSeconds -lt 30)
    $state | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $out 'cancellation.json') -Encoding utf8
    Assert ($state.status -eq 'cancelled') ('Expected cancellation, got '+$state.status)
    $succeeded=@($state.steps | Where-Object status -eq 'succeeded').Count
    Assert ($succeeded -lt 1000) 'Cancellation did not stop execution between steps.'
    Assert (@((Call list_markers).markers).Count -eq $succeeded) 'Partial edits disagree with reported successful steps.'
    $null=Call save_project @{path=(Join-Path $out 'cancelled.veg')}
    $null=Call new_project
    if($IncludeFailureInjection) {
    Write-Output 'EXPECTED FAILURE TEST: update_track(trackIndex=999). If the debugger breaks on thrown exceptions, continue with F5; the batch must catch this error.'
    $failureId='failure_' + [Guid]::NewGuid().ToString('N')
    @{batchId=$failureId} | ConvertTo-Json | Set-Content (Join-Path $out 'failure-request.json') -Encoding utf8
    $failure=Call execute_batch @{batchId=$failureId;onError='continue';operations=@(
        @{id='good';tool='create_track';arguments=@{type='video';name='Retained'}},
        @{id='bad';tool='update_track';arguments=@{trackIndex=999;name='bad'}},
        @{id='dependent';tool='delete_track';arguments=@{trackIndex=@{'$ref'='bad#/index'}}},
        @{id='independent';tool='add_marker';arguments=@{positionMs=0;label='retained'}}
    )} -AllowError
    $failure | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $out 'continue.json') -Encoding utf8
    Assert ($failure.status -eq 'failed') 'Expected failed overall status.'
    Assert (($failure.steps.status -join ',') -eq 'succeeded,failed,skipped,succeeded') 'Continue/dependency semantics failed.'
    Assert ((Call get_project_info).trackCount -eq 1) 'Failure unexpectedly discarded preceding edits.'
    }
  }
    # PrepareUndo resumes after a debugger pause without replaying the invalid-index probe.
    if((Call get_project_info).isModified) {$null=Call save_project @{path=(Join-Path $out 'partial-failure.veg')}}
    $null=Call new_project
    $null=Call save_project @{path=(Join-Path $out 'undo-baseline.veg')}
    Snapshot | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $out 'before.json') -Encoding utf8
    $result=Call execute_batch @{label='Cutie undo verification';operations=@(
        @{id='v';tool='create_track';arguments=@{type='video';name='Undo video';index=0}},
        @{id='g';tool='create_generated_event';arguments=@{trackIndex=0;pluginId='{Svfx:com.vegascreativesoftware:solidcolor}';startMs=0;lengthMs=1000}},
        @{id='fx';tool='add_effect';arguments=@{targetType='event';trackIndex=0;eventIndex=0;pluginId='{Svfx:com.vegascreativesoftware:pictureinpicture}'}},
        @{id='motion';tool='set_track_motion_keyframe';arguments=@{trackIndex=0;atMs=0;interpolation='Smooth';x=100}},
        @{id='a';tool='create_track';arguments=@{type='audio';name='Undo audio';index=1}},
        @{id='m';tool='add_marker';arguments=@{positionMs=100;label='Undo marker'}},
        @{id='r';tool='add_region';arguments=@{startMs=0;lengthMs=1000;label='Undo region'}}
    )}
    $result | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $out 'undo-batch.json') -Encoding utf8
    Snapshot | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $out 'after.json') -Encoding utf8
    Assert ((Call get_project_info).trackCount -eq 2) 'Undo probe did not create two tracks.'
    Write-Output 'READY: press Ctrl+Z once in VEGAS, then run -Phase VerifyUndo. See cancellation.json and optional continue.json for the preceding control tests.'
} elseif($Phase -eq 'VerifyUndo') {
    $state=Snapshot
    $state | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $out 'after-undo.json') -Encoding utf8
    Assert ($state.project.trackCount -eq 0) 'One Undo did not remove both tracks.'
    Assert (@($state.markers.markers).Count -eq 0 -and @($state.markers.regions).Count -eq 0) 'One Undo did not remove the marker and region.'
    Assert ($state.media.Count -eq 0) 'One Undo did not remove the generated media.'
    Write-Output 'PASS: one Undo restored the empty baseline (tracks, events, effects, media, markers and regions).'
} elseif($Phase -eq 'VerifyRedo') {
    $state=Snapshot
    $state | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $out 'after-redo.json') -Encoding utf8
    Assert ($state.project.trackCount -eq 2) 'One Redo did not restore both tracks.'
    Assert (@($state.markers.markers).Count -eq 1 -and @($state.markers.regions).Count -eq 1) 'One Redo did not restore marker and region.'
    Assert (@(Call list_events @{trackIndex=0}).Count -eq 1) 'Redo did not restore the event.'
    Assert (@(Call list_effects @{targetType='event';trackIndex=0;eventIndex=0}).Count -eq 1) 'Redo did not restore the effect.'
    Write-Output 'PASS: one Redo restored the batch.'
} else {
    $original=Get-Content -Raw (Join-Path $out 'original.json') | ConvertFrom-Json
    if((Call get_project_info).isModified){$null=Call save_project @{path=(Join-Path $out 'undo-final.veg')}}
    if($original.filePath){$null=Call open_project @{path=$original.filePath}}else{$null=Call new_project}
    Write-Output 'Original project reopen requested.'
}
