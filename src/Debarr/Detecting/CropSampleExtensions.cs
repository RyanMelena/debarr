namespace Debarr.Detecting;

public static class CropSampleExtensions
{
    /// <summary>The largest width or height difference, in pixels, between a box and its bucket's first box.</summary>
    private const int CropBoxBucketTolerance = 4;

    /// <summary>
    /// The mean aspect ratio of the agreeing samples' boxes, and their share of all samples.
    /// Null when no sample has a box.
    /// </summary>
    public static (double AspectRatio, double Confidence)? AggregateAspectRatio(this IEnumerable<CropSample> samples)
    {
        var all = samples as IReadOnlyCollection<CropSample> ?? samples.ToList();
        var agreeing = all.AgreeingSamples();
        if (agreeing.Count == 0)
        {
            return null;
        }

        var aspectRatio = agreeing.Average(sample => sample.Box!.Value.AspectRatio);
        var confidence = Math.Round((double)agreeing.Count / all.Count, 3);

        return (aspectRatio, confidence);
    }

    /// <summary>
    /// The samples in the largest bucket of boxes, where each box is within four pixels of its bucket's first box.
    /// A tie goes to the bucket that formed first, and the list is empty when no sample has a box.
    /// </summary>
    public static IReadOnlyList<CropSample> AgreeingSamples(this IEnumerable<CropSample> samples)
    {
        var buckets = new List<List<CropSample>>();

        foreach (var sample in samples)
        {
            if (sample.Box is not { } box)
            {
                continue;
            }

            var bucket = buckets.FirstOrDefault(members =>
                Math.Abs(members[0].Box!.Value.Width - box.Width) <= CropBoxBucketTolerance
                && Math.Abs(members[0].Box!.Value.Height - box.Height) <= CropBoxBucketTolerance);

            if (bucket is null)
            {
                buckets.Add([sample]);
            }
            else
            {
                bucket.Add(sample);
            }
        }

        return buckets.MaxBy(bucket => bucket.Count) ?? [];
    }
}
