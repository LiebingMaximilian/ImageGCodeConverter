# ImageConverter

Drag JPG/PNG images into the window. Two modes:

- **TSP art – single line + G-code**: the image becomes one continuous, non-crossing line, so a pen
  plotter / CNC can draw it without ever lifting the pen. Output: `<name>_tsp.png` (preview) and
  `<name>_tsp.gcode`.
- **Sobel edges**: `<name>_sobel.png` edge image.

Everything is written to **Desktop\ImageConverter Output**. Settings (right panel) are saved to
`%LOCALAPPDATA%\ImageConverter\settings.json`.

## How the single line is made (TSP art)

1. **Density map** – downscale to *Working size*, darkness → `darkness^Gamma`, optionally blended with
   Sobel edge strength (*Edge weight*); everything below *White cutoff* gets no ink.
2. **Weighted Voronoi stippling** – scatter *Point count* dots by density, then *Relaxation iterations*
   of Lloyd relaxation move every dot to the density-weighted centre of its Voronoi cell → evenly
   spaced dots whose density follows the image.
3. **Travelling salesman tour** – nearest-neighbour tour, then 2-opt (neighbour lists + don't-look
   bits), then a pass that finds and removes every remaining self-crossing. The longest edge is cut
   so the result is an open path.
4. **G-code** – scaled to fit *Max width × Max height* mm, Y flipped, pen up → travel to start →
   pen down **once** → `G1` through all points → pen up → footer.

## Structure

```
src/
  ImageConverter.Algorithms/
    Tsp/    TspArtSettings, DensityMap, Stippler, PointGrid, TourSolver, TspArtGenerator(.Drawing)
    GCode/  GCodeSettings, GCodeWriter
  ImageConverter.App/   WinForms UI (drag & drop, previews, settings grid, log)
```

## Machine setup (G-code settings)

| Machine                    | Pen up       | Pen down         | Pen delay |
|----------------------------|--------------|------------------|-----------|
| Z-axis pen (default)       | `G0 Z5`      | `G1 Z0 F1000`    | 0         |
| GRBL servo pen             | `M3 S0`/`M5` | `M3 S90`         | 200–300   |

Check the drawing area and feed rate before the first run, and do a dry run with the pen raised.

## Run

Requires .NET 10 SDK (or Visual Studio).

- WinForms app (Windows): `dotnet run --project src/ImageConverter.App`
- Web app (any OS): `dotnet run --project src/ImageConverter.Web` → http://localhost:5080
  Drag & drop an image, set points / drawing size / pen commands (all parameters are under
  "All parameters"), preview with zoom & pan, download G-code, SVG or PNG.
  Settings are remembered in the browser.

## Projects

- `ImageConverter.Algorithms` – platform-independent: stippling, TSP tour, G-code and SVG writers
- `ImageConverter.Core` – Windows/System.Drawing parts (image loading, Sobel, preview PNG)
- `ImageConverter.App` – WinForms UI
- `ImageConverter.Web` – ASP.NET Core minimal API + static page (`wwwroot/`)
