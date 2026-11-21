using SkiaSharp;

namespace MusicVisualizer.Visuals;

/// <summary>
/// 3D "synthwave" landscape: every few milliseconds the spectrum becomes a new ridge line that scrolls
/// away toward the horizon. Bass raises mountains at the sides, treble shapes the valley in the middle.
/// </summary>
public sealed class TerrainVisualizer : IVisualizer
{
    private const int Rows = 56;
    private const int Cols = 96;
    private const float PushInterval = 1f / 40f;
    private const float ZNear = 1.6f;
    private const float RowDepth = 0.22f;
    private const float CameraHeight = 1.0f;
    private const float MountainHeight = 1.25f;
    private const int GridEvery = 8;          // a "road" grid line every N columns

    private static readonly SKColor Ground = new(0x08, 0x08, 0x0D);

    private readonly float[][] _rows = Enumerable.Range(0, Rows).Select(_ => new float[Cols]).ToArray();
    private readonly SKPoint[][] _proj = Enumerable.Range(0, Rows).Select(_ => new SKPoint[Cols]).ToArray();
    private int _head;              // index of the newest row
    private float _accumulator;

    private readonly SKPath _path = new();
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = Ground };
    private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Bevel };
    private readonly SKPaint _lineGlow = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Bevel };
    private readonly SKPaint _grid = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly SKPaint _shape = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    public string Name => "Terrain";

    public void Render(in RenderContext ctx)
    {
        var frame = ctx.Frame;
        var canvas = ctx.Canvas;
        float w = ctx.Width, h = ctx.Height, cx = w / 2;

        // Scroll: push a fresh ridge at a fixed rate; frac smooths the motion between pushes.
        _accumulator += ctx.Dt;
        while (_accumulator >= PushInterval)
        {
            _accumulator -= PushInterval;
            PushRow(frame.Bands);
        }
        float frac = _accumulator / PushInterval;

        float horizon = ctx.Overlay ? h * 0.12f : h * 0.45f;
        float bottom = h * 0.98f;
        float focal = (bottom - horizon) * ZNear / CameraHeight;
        float halfWidth = w * 0.58f * ZNear / focal; // nearest ridge spans just past the screen edges
        float farZ = ZNear + Rows * RowDepth;

        if (!ctx.Overlay) DrawSkyAndSun(ctx, horizon);

        // Project every vertex once.
        for (int k = 0; k < Rows; k++)
        {
            var row = _rows[(_head + k) % Rows];
            var pts = _proj[k];
            float z = ZNear + (k + frac) * RowDepth;
            float s = focal / z;
            for (int c = 0; c < Cols; c++)
            {
                float x = (c / (float)(Cols - 1) * 2f - 1f) * halfWidth;
                float y = row[c] * MountainHeight;
                pts[c] = new SKPoint(cx + x * s, horizon + (CameraHeight - y) * s);
            }
        }

        float lineW = 1.4f * ctx.Scale;
        // Glow for the nearest ridges goes in the low-res layer underneath (see RenderContext.GlowCanvas).
        SKCanvas? glowCanvas = ctx.GlowCanvas;

        // Painter's algorithm: far to near, each ridge's fill hides what's behind it.
        for (int k = Rows - 1; k >= 0; k--)
        {
            float z = ZNear + (k + frac) * RowDepth;
            float d = (z - ZNear) / (farZ - ZNear);              // 0 near .. 1 far
            float fade = MathF.Pow(1f - d, 1.1f);
            if (fade <= 0.01f) continue;
            var pts = _proj[k];
            var color = ctx.Palette.Sample(1f - d * 0.85f);

            // Grid lines running from this ridge back to the one behind it.
            if (k < Rows - 1)
            {
                var behind = _proj[k + 1];
                _grid.Color = color.WithAlpha((byte)(110 * fade));
                _grid.StrokeWidth = d > 0.3f ? 0 : lineW * 0.8f;
                for (int c = GridEvery / 2; c < Cols; c += GridEvery)
                    canvas.DrawLine(behind[c], pts[c], _grid);
            }

            _path.Reset();
            _path.MoveTo(pts[0]);
            for (int c = 1; c < Cols; c++) _path.LineTo(pts[c]);

            if (!ctx.Overlay)
            {
                // Close the ridge down to the ground at this depth and fill it to occlude farther ridges.
                // Nothing behind this ridge reaches below the ground line of the ridge directly behind it,
                // so clip the fill there — it saves filling most of the screen 56 times over.
                float groundY = horizon + CameraHeight * focal / z + 2f;
                float behindGroundY = horizon + CameraHeight * focal / (z + RowDepth) + 2f;
                _path.LineTo(pts[Cols - 1].X, groundY);
                _path.LineTo(pts[0].X, groundY);
                _path.Close();
                canvas.Save();
                canvas.ClipRect(new SKRect(0, 0, w, behindGroundY + lineW * 2));
                canvas.DrawPath(_path, _fill);
                canvas.Restore();
                // Re-trace just the ridge for the stroke.
                _path.Reset();
                _path.MoveTo(pts[0]);
                for (int c = 1; c < Cols; c++) _path.LineTo(pts[c]);
            }

            if (glowCanvas != null && k < 14)
            {
                _lineGlow.Color = color.WithAlpha((byte)(150 * fade));
                _lineGlow.StrokeWidth = lineW * 2.5f;
                glowCanvas.DrawPath(_path, _lineGlow);
            }
            _line.Color = color.WithAlpha((byte)(255 * fade));
            // Distant ridges use hairlines (StrokeWidth 0): visually the same at that size and far cheaper.
            _line.StrokeWidth = d > 0.3f ? 0 : lineW * (0.6f + 0.8f * (1f - d));
            canvas.DrawPath(_path, _line);
        }
    }

    private void PushRow(float[] bands)
    {
        _head = (_head + Rows - 1) % Rows;
        var row = _rows[_head];
        int n = bands.Length;
        for (int c = 0; c < Cols; c++)
        {
            float t = MathF.Abs(c / (float)(Cols - 1) * 2f - 1f); // 0 centre .. 1 edges
            float bf = (1f - t) * (n - 1) * 0.85f;                // bass at the edges, treble in the middle
            int b = (int)bf;
            float v = bands[b] + (bands[Math.Min(b + 1, n - 1)] - bands[b]) * (bf - b);
            row[c] = v * (0.3f + 0.7f * t);                        // keep a valley down the middle
        }
    }

    private void DrawSkyAndSun(in RenderContext ctx, float horizon)
    {
        var canvas = ctx.Canvas;
        var frame = ctx.Frame;
        float w = ctx.Width, h = ctx.Height, cx = w / 2;

        // Sky glow rising from the horizon.
        var skyGlow = ctx.Palette.Sample(1f);
        using (var sky = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, horizon),
                   new[] { SKColors.Transparent, SKColors.Transparent, skyGlow.WithAlpha(70) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp))
        {
            _shape.Shader = sky;
            // A soft full-width gradient: on the low-res glow layer when available (far fewer pixels).
            (ctx.GlowCanvas ?? canvas).DrawRect(0, 0, w, horizon, _shape);
        }

        // Striped sun sitting on the horizon, pulsing with the bass.
        float r = MathF.Min(w, h) * 0.2f * (1f + 0.07f * frame.Bass + 0.05f * frame.Pulse);
        float sy = horizon - r * 0.35f;
        using (var sun = SKShader.CreateLinearGradient(new SKPoint(0, sy - r), new SKPoint(0, sy + r),
                   new[] { ctx.Palette.Sample(0f), ctx.Palette.Sample(1f) }, null, SKShaderTileMode.Clamp))
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, w, horizon));
            _shape.Shader = sun;
            canvas.DrawCircle(cx, sy, r, _shape);
            _shape.Shader = null;

            // Bands cut through the lower half, thicker toward the bottom.
            _shape.Color = Ground;
            for (int i = 0; i < 6; i++)
            {
                float y = sy + r * (0.05f + i * 0.17f);
                float thickness = r * (0.025f + i * 0.018f);
                canvas.DrawRect(cx - r, y, r * 2, thickness, _shape);
            }
            canvas.Restore();
        }
        _shape.Shader = null;
    }

}
