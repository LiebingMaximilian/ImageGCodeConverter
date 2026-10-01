using System.Numerics;

namespace ImageConverter.Core;

/// <summary>One continuous line (the result of every drawing mode: TSP art, spiral, …).</summary>
/// <param name="Path">Points in drawing order, in working-image pixel coordinates.</param>
/// <param name="Width">Working image width (pixels) – the coordinate space of <see cref="Path"/>.</param>
/// <param name="Height">Working image height (pixels).</param>
/// <param name="LengthPx">Total line length in working-image pixels.</param>
public sealed record LineArtResult(Vector2[] Path, int Width, int Height, double LengthPx);
