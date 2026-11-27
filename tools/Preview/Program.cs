// Dev tool: renders visualizers offscreen with a synthetic 120 BPM track, mimicking the app's glow layer
// (above/below), RenderScale and overlay mode, and saves PNGs (window, overlay strip, full-screen overlay)
// plus average ms/frame. Use it to check how a visualizer looks and how fast it is without running the app.
//
//   dotnet run -c Release --project tools/Preview -- <outDir> [name|all] [palette] [glow|noglow] [seconds]
//   e.g.  dotnet run -c Release --project tools/Preview -- out/preview Highway Sunset glow 4
using MusicVisualizer.Audio;
using MusicVisualizer.Visuals;
using SkiaSharp;
using System.Diagnostics;
using System.IO;

string outDir = args.Length > 0 ? args[0] : ".";
string? only = args.Length > 1 && args[1] != "all" ? args[1] : null;
string paletteName = args.Length > 2 ? args[2] : "Neon";
bool glowOn = args.Length <= 3 || args[3] != "noglow";
double seconds = args.Length > 4 ? double.Parse(args[4]) : 3.27;
Directory.CreateDirectory(outDir);

const int SR = 48000;
const float Dt = 1f / 60f;
var palette = Palette.All.First(p => p.Name == paletteName);
var rng = new Random(1);
var blurPaint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(10f, 10f) };

// Synthetic track: 120 BPM kick, bass line, chord pad, lead, hi-hats.
float[] history = new float[2048];
long sampleIndex = 0;
float NextSample()
{
    double t = sampleIndex++ / (double)SR;
    double beat = t % 0.5;
    double kick = Math.Sin(2 * Math.PI * (50 + 80 * Math.Exp(-beat * 30)) * beat) * Math.Exp(-beat * 9) * 0.8;
    double bass = Math.Sin(2 * Math.PI * 82.4 * t) * 0.25 * (0.6 + 0.4 * Math.Sin(t * 3));
    double pad = (Math.Sin(2 * Math.PI * 220 * t) + Math.Sin(2 * Math.PI * 277.2 * t) + Math.Sin(2 * Math.PI * 329.6 * t)) * 0.06;
    double lead = Math.Sin(2 * Math.PI * (880 + 220 * Math.Floor(t * 4 % 4)) * t) * 0.05;
    double hb = t % 0.25;
    double hat = (rng.NextDouble() * 2 - 1) * Math.Exp(-hb * 60) * 0.25;
    return (float)(kick + bass + pad + lead + hat);
}

void Render(IVisualizer vis, int outW, int outH, bool overlay, string file)
{
    int w = (int)Math.Round(outW * vis.RenderScale), h = (int)Math.Round(outH * vis.RenderScale);
    var analyzer = new SpectrumAnalyzer();
    sampleIndex = 0;
    using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
    int gw = (w + 3) / 4, gh = (h + 3) / 4;
    using var glowSurface = SKSurface.Create(new SKImageInfo(gw, gh, SKColorType.Bgra8888, SKAlphaType.Premul));
    int frames = (int)(seconds * 60);
    var sw = new Stopwatch();
    for (int f = 0; f < frames; f++)
    {
        Array.Copy(history, 800, history, 0, history.Length - 800);
        for (int i = history.Length - 800; i < history.Length; i++) history[i] = NextSample();
        var frame = analyzer.Process(history, SR, Dt);
        sw.Start();
        var c = surface.Canvas;
        c.Clear(SKColors.Transparent);
        SKCanvas? g = null;
        if (glowOn)
        {
            g = glowSurface.Canvas;
            g.Clear(SKColors.Transparent);
            g.Save();
            g.Scale(gw / (float)w, gh / (float)h);
            g.SaveLayer(blurPaint);
        }
        vis.Render(new RenderContext(c, w, h, vis.RenderScale, frame, palette, Dt, g, overlay));
        if (g != null) { g.Restore(); g.Restore(); }
        sw.Stop();
    }
    using var snap = surface.Snapshot();
    using var glowSnap = glowSurface.Snapshot();
    // Composite like the app: background → glow (GPU-upscaled in the app) → main. Overlays sit on a blue-grey "desktop".
    using var outSurface = SKSurface.Create(new SKImageInfo(outW, outH));
    var oc = outSurface.Canvas;
    oc.Clear(overlay ? new SKColor(70, 90, 120) : new SKColor(8, 8, 13));
    var linear = new SKSamplingOptions(SKFilterMode.Linear);
    if (glowOn && !vis.GlowAbove) oc.DrawImage(glowSnap, new SKRect(0, 0, outW, outH), linear, null);
    oc.DrawImage(snap, new SKRect(0, 0, outW, outH), linear, null);
    if (glowOn && vis.GlowAbove) oc.DrawImage(glowSnap, new SKRect(0, 0, outW, outH), linear, null);
    using var data = outSurface.Snapshot().Encode(SKEncodedImageFormat.Png, 90);
    File.WriteAllBytes(Path.Combine(outDir, file), data.ToArray());
    Console.WriteLine($"wrote {file}  avg {sw.Elapsed.TotalMilliseconds / frames:F2} ms/frame");
}

// Fresh instances per render, straight from the app's catalog so new visualizers show up here automatically.
var all = Enumerable.Range(0, VisualizerCatalog.CreateAll().Count)
    .Select(i => (Func<IVisualizer>)(() => VisualizerCatalog.CreateAll()[i]))
    .ToArray();
foreach (var make in all)
{
    var probe = make();
    if (only != null && !probe.Name.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
    Render(probe, 1920, 1080, false, $"{probe.Name}-window.png");
    Render(make(), 1600, 220, true, $"{probe.Name}-strip.png");
    Render(make(), 1920, 1080, true, $"{probe.Name}-overlay-full.png");
}
