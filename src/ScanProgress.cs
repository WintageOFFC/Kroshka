using System.Drawing.Drawing2D;

namespace Kroshka;

internal sealed class ScanProgress : Control
{
    readonly System.Windows.Forms.Timer animation = new() { Interval = 150 };
    bool running, paused;
    double? fraction;
    float phase = .35f;
    string heading = "", detail = "";
    public ScanProgress()
    {
        DoubleBuffered = true; Height = 72; Margin = new Padding(0, 3, 0, 9);
        AccessibleRole = AccessibleRole.ProgressBar;
        animation.Tick += (_, _) => { phase = (phase + .045f) % 1; Invalidate(BarBounds()); };
        VisibleChanged += (_, _) => UpdateAnimation();
    }
    public void UpdateState(bool isRunning, bool isPaused, long count, long estimate, string stage, bool windowVisible)
    {
        running = isRunning && windowVisible; paused = isPaused;
        fraction = estimate > 0 && stage != "Сверяю удалённые файлы" ? Math.Min(.99, Math.Max(0, count / (double)estimate)) : null;
        heading = paused ? "Сканирование на паузе" : stage == "Сверяю удалённые файлы" ? "Завершаю сверку карты" : "Строю карту диска";
        detail = count.ToString("N0") + " файлов обработано";
        detail += estimate > 0 ? "  ·  " + (fraction.HasValue ? $"≈ {fraction.Value:P0} по прошлой карте" : "финальная проверка")
            : "  ·  первый обход: общий объём пока неизвестен";
        if (!isRunning && paused) detail = "Нажми «Продолжить», чтобы начать или продолжить построение карты";
        AccessibleName = heading + ". " + detail;
        UpdateAnimation(); Invalidate();
    }
    void UpdateAnimation() { animation.Enabled = Visible && running && !paused && !fraction.HasValue; }
    Rectangle BarBounds() => new(16, Height - 17, Math.Max(1, Width - 32), 7);
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Bg);
        float scale = DeviceDpi / 96f;
        using var bg = new SolidBrush(Theme.Panel); using var outline = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 12 * scale); g.FillPath(bg, outline);
        using var headFont = Theme.Font(10, true); using var detailFont = Theme.Font(8.5f);
        TextRenderer.DrawText(g, heading, headFont, new Rectangle((int)(16 * scale), (int)(9 * scale), Width - (int)(32 * scale), (int)(20 * scale)), paused ? Theme.Lilac : Theme.Mint, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, detail, detailFont, new Rectangle((int)(16 * scale), (int)(31 * scale), Width - (int)(32 * scale), (int)(20 * scale)), Theme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        Rectangle bar = BarBounds(); using var track = new SolidBrush(Theme.Line); g.FillRectangle(track, bar);
        using var fill = new SolidBrush(paused ? Theme.Lilac : Theme.Mint);
        if (fraction.HasValue) g.FillRectangle(fill, bar.Left, bar.Top, Math.Max(3, (float)(bar.Width * fraction.Value)), bar.Height);
        else
        {
            var saved = g.Save(); g.SetClip(bar);
            float width = bar.Width * .24f; float x = bar.Left + phase * (bar.Width + width) - width;
            g.FillRectangle(fill, x, bar.Top, width, bar.Height); g.Restore(saved);
        }
        using var gap = new SolidBrush(Theme.Panel);
        for (int x = bar.Left + 7; x < bar.Right; x += 10) g.FillRectangle(gap, x, bar.Top, 3, bar.Height);
        using var border = new Pen(Theme.Line); g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
