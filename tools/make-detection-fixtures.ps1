# Writes the detection test fixtures into .dev/fixtures with ffmpeg from PATH.
# Each file is 60 s of testsrc2 at 5 fps. Existing files are overwritten.

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$fixtures = Join-Path (Join-Path $repoRoot '.dev') 'fixtures'
New-Item -ItemType Directory -Force -Path $fixtures | Out-Null

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw 'ffmpeg is not on PATH.'
}

$fixtureSpecs = @(
    @{ Name = 'letterbox-239.mkv'; Size = '1920x804'; Filter = 'pad=1920:1080:(ow-iw)/2:(oh-ih)/2:black' },
    @{ Name = 'letterbox-185.mkv'; Size = '1920x1038'; Filter = 'pad=1920:1080:(ow-iw)/2:(oh-ih)/2:black' },
    @{ Name = 'full-178.mkv'; Size = '1920x1080'; Filter = 'null' },
    @{ Name = 'anamorphic-133.mkv'; Size = '540x480'; Filter = 'pad=720:480:(ow-iw)/2:(oh-ih)/2:black,setsar=32/27' },
    @{ Name = 'cropped-240.mkv'; Size = '1920x800'; Filter = 'null' },
    # 1.82 sits exactly 0.04 from 1.78.
    @{ Name = 'full-182.mkv'; Size = '1820x1000'; Filter = 'null' },
    # The white bar sits in the bottom letterbox for the first sample window only.
    @{ Name = 'burned-bar-239.mkv'; Size = '1920x804'; Filter = "pad=1920:1080:(ow-iw)/2:(oh-ih)/2:black,drawbox=x=0:y=1000:w=iw:h=40:color=white:t=fill:enable='between(t,4,8)'" },
    # 10-bit PQ, where black sits at code 64 of 1023 rather than 16 of 255.
    @{
        Name = 'letterbox-239-hdr10.mkv'
        Size = '1920x804'
        Filter = 'pad=1920:1080:(ow-iw)/2:(oh-ih)/2:black'
        PixelFormat = 'yuv420p10le'
        ExtraArgs = @('-profile:v', 'high10', '-x264-params', 'colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc')
    }
)

foreach ($spec in $fixtureSpecs) {
    $output = Join-Path $fixtures $spec.Name
    $source = 'testsrc2=size={0}:rate=5:duration=60' -f $spec.Size
    $pixelFormat = if ($spec.ContainsKey('PixelFormat')) { $spec.PixelFormat } else { 'yuv420p' }
    $extraArgs = if ($spec.ContainsKey('ExtraArgs')) { $spec.ExtraArgs } else { @() }

    & ffmpeg -hide_banner -loglevel error -y `
        -f lavfi -i $source `
        -vf $spec.Filter `
        -c:v libx264 -preset ultrafast -g 5 -pix_fmt $pixelFormat `
        $extraArgs `
        $output

    if ($LASTEXITCODE -ne 0) {
        throw ('ffmpeg exited {0} writing {1}' -f $LASTEXITCODE, $output)
    }

    Write-Output ('Wrote {0}' -f $output)
}
