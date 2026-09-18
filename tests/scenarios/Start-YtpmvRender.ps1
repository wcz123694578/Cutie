param([Parameter(Mandatory=$true)][string]$Name,[double]$StartMs=0,[double]$LengthMs=2000,[int]$Height=1080,[switch]$AudioOnly)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$dir=(Get-ChildItem (Join-Path $root 'mg_tests') -Directory -Filter 'full_ytpmv_*'|Sort-Object Name -Descending|Select-Object -First 1).FullName
function Call([string]$name,[hashtable]$arguments=@{}){
    $body=@{jsonrpc='2.0';id=33000;method='tools/call';params=@{name=$name;arguments=$arguments}}|ConvertTo-Json -Depth 25 -Compress
    $r=Invoke-RestMethod -Uri 'http://127.0.0.1:57231/' -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -NoProxy -TimeoutSec 60
    if($r.error -or $r.result.isError){throw ($r|ConvertTo-Json -Depth 20 -Compress)}
    return ($r.result.content[0].text|ConvertFrom-Json -Depth 40)
}
$project=Call get_project_info
if($project.trackCount -ne 17){throw 'Expected full YTPMV project.'}
$null=Call save_project @{path=(Join-Path $dir 'Cheeky_Girls_Fast_Food_FULL.veg')}
$templates=(Call list_render_templates @{limit=500}).items
if($AudioOnly){$template=$templates|Where-Object {$_.extension -match 'wav' -and $_.templateName -match '16'}|Select-Object -First 1;if(!$template){$template=$templates|Where-Object extension -Match 'wav'|Select-Object -First 1};$extension='.wav'}
else{
    $choices=@($templates|Where-Object {$_.extension -eq '*.mp4' -and $_.height -eq $Height -and [Math]::Abs($_.frameRate-29.97) -lt 0.1})
    $template=$choices|Where-Object {$_.rendererName -match 'MainConcept' -and $_.templateName -match 'Internet'}|Select-Object -First 1
    if(!$template){$template=$choices|Where-Object {$_.rendererName -match 'MAGIX AVC' -and $_.templateName -match 'Internet'}|Select-Object -First 1}
    if(!$template){$template=$choices|Select-Object -First 1};$extension='.mp4'
}
if(!$template){throw 'No matching render template.'}
$output=Join-Path $dir ($Name+$extension)
if(Test-Path -LiteralPath $output){throw "Output already exists: $output"}
$job=Call start_batch @{label="Render $Name";undoMode='staged';completionTimeoutSeconds=3600;operations=@(
    @{id='render';tool='render_project';arguments=@{outputPath=$output;rendererId=$template.rendererId;templateId=$template.templateId;startMs=$StartMs;lengthMs=$LengthMs}},
    @{id='status';tool='get_render_status';arguments=@{}})}
@{batchId=$job.batchId;outputPath=$output;template=$template;startMs=$StartMs;durationMs=$LengthMs;started=(Get-Date).ToString('o')}|
    ConvertTo-Json -Depth 12|Set-Content (Join-Path $dir ($Name+'.job.json')) -Encoding utf8
Write-Output "Started $($job.batchId): $output / $($template.rendererName) / $($template.templateName)"
