using SkiaSharp;

namespace LightsaberCursor;

/// `LightsaberCursor.exe --render-sheet out.png` draws every preset, hilt and state for visual review.
internal static class ContactSheet
{
    public static void Render(string path)
    {
        const int cols = 6, cellW = 380, cellH = 400, hiltRowH = 140, stateRowH = 400;
        const float scale = 2.5f;
        var presets = Presets.All;
        int saberRows = (presets.Count + cols - 1) / cols;
        var hilts = Enum.GetValues<HiltStyle>();
        int hiltRows = (hilts.Length + cols - 1) / cols;
        int width = cols * cellW, height = saberRows * cellH + hiltRows * hiltRowH + stateRowH;

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(13, 15, 23));
        using var font = new SKFont(SKTypeface.Default, 20);
        using var text = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 205) };

        void Place(SKImage img, SKRect cell) =>
            canvas.DrawImage(img, cell.MidX - img.Width / 2f, cell.MidY - img.Height / 2f - 12);
        void Label(string s, SKRect cell) => canvas.DrawText(s, cell.Left + 12, cell.Bottom - 12, font, text);

        for (int i = 0; i < presets.Count; i++)
        {
            var cell = SKRect.Create(i % cols * cellW, i / cols * cellH, cellW, cellH);
            using var r = SaberRenderer.Render(presets[i], new SaberState(1, 0.3), scale);
            Place(r.Image, cell);
            Label(presets[i].Name, cell);
        }

        int hiltTop = saberRows * cellH;
        var sample = Presets.Default;
        var finishes = Enum.GetValues<HiltFinish>();
        for (int i = 0; i < hilts.Length; i++)
        {
            sample.Hilt = hilts[i];
            sample.Finish = finishes[i % finishes.Length];
            var cell = SKRect.Create(i % cols * cellW, hiltTop + i / cols * hiltRowH, cellW, hiltRowH);
            using var icon = SaberRenderer.RenderHiltIcon(sample, 72);
            Place(icon, cell);
            Label($"{hilts[i]} / {sample.Finish}", cell);
        }

        int stateTop = hiltTop + hiltRows * hiltRowH;
        var states = new[] { ("Extended", 1.0), ("Retracting", 0.45), ("Retracted", 0.0) };
        for (int i = 0; i < states.Length; i++)
        {
            var cell = SKRect.Create(i * cellW, stateTop, cellW, stateRowH);
            using var r = SaberRenderer.Render(Presets.Vader, new SaberState(states[i].Item2), scale);
            Place(r.Image, cell);
            Label("Vader - " + states[i].Item1, cell);
        }
        var sparkCell = SKRect.Create(3 * cellW, stateTop, cellW, stateRowH);
        SaberRenderer.DrawSpark(canvas, new SKPoint(sparkCell.MidX, sparkCell.MidY), 0.3, Presets.ObiWan.Blade, 3);
        Label("Click spark", sparkCell);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
