namespace Debarr.Detecting;

/// <summary>The picture area cropdetect found, in square pixels.</summary>
public readonly record struct CropBox(int Width, int Height)
{
    public double AspectRatio => (double)Width / Height;
}
