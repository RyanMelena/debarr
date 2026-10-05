using Debarr.Detecting;

namespace Debarr.Tests.Detecting;

public class CropDetectParserTests
{
    [Fact]
    public void The_last_crop_line_in_a_letterboxed_capture_gives_the_picture_box()
    {
        const string stderr = """
            Input #0, matroska,webm, from 'letterbox-239.mkv':
              Duration: 00:01:00.00, start: 0.000000, bitrate: 3374 kb/s
              Stream #0:0: Video: h264 (Constrained Baseline), yuv420p(tv, progressive), 1920x1080 [SAR 1:1 DAR 16:9], 5 fps, 5 tbr, 1k tbn (default)
            Stream mapping:
              Stream #0:0 -> #0:0 (h264 (native) -> wrapped_avframe (native))
            Output #0, null, to 'pipe:':
              Stream #0:0: Video: wrapped_avframe, yuv420p(tv, progressive), 1920x1080 [SAR 1:1 DAR 16:9], q=2-31, 200 kb/s, 5 fps, 5 tbn
            [Parsed_cropdetect_2 @ 0000021d0be0c200] x1:0 x2:1919 y1:138 y2:941 w:1920 h:804 x:0 y:138 pts:150 t:0.150000 limit:24.000000 crop=1920:804:0:138
            [Parsed_cropdetect_2 @ 0000021d0be0c200] x1:0 x2:1919 y1:138 y2:941 w:1920 h:804 x:0 y:138 pts:350 t:0.350000 limit:24.000000 crop=1920:804:0:138
            [Parsed_cropdetect_2 @ 0000021d0be0c200] x1:0 x2:1919 y1:136 y2:941 w:1920 h:806 x:0 y:136 pts:550 t:0.550000 limit:24.000000 crop=1920:806:0:136
            [out#0/null @ 0000021d0be0ccc0] video:2KiB audio:0KiB subtitle:0KiB other streams:0KiB global headers:0KiB muxing overhead: unknown
            """;

        var box = CropDetectParser.ParseLast(stderr.Split('\n'));

        Assert.Equal(new CropBox(1920, 806), box);
    }

    [Fact]
    public void An_all_black_capture_gives_no_box()
    {
        const string stderr = """
            [Parsed_cropdetect_0 @ 000001fa6c5ed240] x1:319 x2:0 y1:239 y2:0 w:-318 h:-238 x:320 y:240 pts:3 t:0.600000 limit:24.000000 crop=-318:-238:320:240
            [Parsed_cropdetect_0 @ 000001fa6c5ed240] x1:319 x2:0 y1:239 y2:0 w:-318 h:-238 x:320 y:240 pts:4 t:0.800000 limit:24.000000 crop=-318:-238:320:240
            """;

        Assert.Null(CropDetectParser.ParseLast(stderr.Split('\n')));
    }
}
