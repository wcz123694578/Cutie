param([Parameter(Mandatory=$true)][string]$Path,[string]$Times='250,370,540,710,1040')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase
function Pump([int]$Milliseconds){
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do{
        $frame=[Windows.Threading.DispatcherFrame]::new()
        $null=[Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke([Windows.Threading.DispatcherPriority]::Background,
            [Windows.Threading.DispatcherOperationCallback]{param($f)$f.Continue=$false;return $null},$frame)
        [Windows.Threading.Dispatcher]::PushFrame($frame)
        Start-Sleep -Milliseconds 15
    }while($watch.ElapsedMilliseconds -lt $Milliseconds)
}
$player=[Windows.Media.MediaPlayer]::new();$player.ScrubbingEnabled=$true;$player.Volume=0
$player.Open([Uri]::new($Path));Pump 1800
if(!$player.NaturalVideoWidth){throw 'Video did not open.'}
$info=@{path=$Path;width=$player.NaturalVideoWidth;height=$player.NaturalVideoHeight;hasAudio=$player.HasAudio;hasVideo=$player.HasVideo;durationMs=$player.NaturalDuration.TimeSpan.TotalMilliseconds}
$dir=Split-Path $Path;$prefix=[IO.Path]::GetFileNameWithoutExtension($Path)
$info|ConvertTo-Json|Set-Content (Join-Path $dir ($prefix+'.media.json')) -Encoding utf8
foreach($ms in ($Times -split ','|ForEach-Object {[double]$_})){
    $player.Position=[TimeSpan]::FromMilliseconds($ms);Pump 500
    $visual=[Windows.Media.DrawingVisual]::new();$drawing=$visual.RenderOpen()
    $drawing.DrawVideo($player,[Windows.Rect]::new(0,0,960,540));$drawing.Close()
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(960,540,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($visual)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $file=Join-Path $dir ($prefix+'_'+[int]$ms+'.png');$stream=[IO.File]::Create($file)
    try{$encoder.Save($stream)}finally{$stream.Dispose()};Write-Output $file
}
$player.Close();$info|ConvertTo-Json
