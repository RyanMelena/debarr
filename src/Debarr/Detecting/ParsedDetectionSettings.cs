namespace Debarr.Detecting;

/// <summary>The picture measurement, the standard ratios and the recheck scope of a valid <see cref="ChangeDetectionSettings"/>, which <c>Validate</c> hands to the handler's later methods.</summary>
public sealed record ParsedDetectionSettings(PictureMeasurement PictureMeasurement, StandardRatios StandardRatios, RecheckScope RecheckScope);
