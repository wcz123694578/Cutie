param([Parameter(Mandatory=$true)][string]$Name)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$dir=(Get-ChildItem (Join-Path $root 'mg_tests') -Directory -Filter 'full_ytpmv_*'|Sort-Object Name -Descending|Select-Object -First 1).FullName
$job=Get-Content -Raw (Join-Path $dir ($Name+'.job.json'))|ConvertFrom-Json
$body=@{jsonrpc='2.0';id=33001;method='tools/call';params=@{name='get_batch_status';arguments=@{batchId=$job.batchId}}}|ConvertTo-Json -Depth 10 -Compress
$r=Invoke-RestMethod -Uri 'http://127.0.0.1:57231/' -Method Post -ContentType 'application/json' -Body $body -NoProxy -TimeoutSec 20
if($r.error -or $r.result.isError){throw ($r|ConvertTo-Json -Depth 10)}
$state=$r.result.content[0].text|ConvertFrom-Json -Depth 25
$state|ConvertTo-Json -Depth 25|Set-Content (Join-Path $dir ($Name+'.status.json')) -Encoding utf8
[pscustomobject]@{batchStatus=$state.status;render=$state.steps[0].result;error=$state.steps[0].error}|ConvertTo-Json -Depth 10
