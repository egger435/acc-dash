// ===========================================================================
//  AccDash - trace chart, gauges, bars, G-G disc, big readout, session panel
//  src/Ui/Charts.cs
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;


// ---------------------------------------------------------------------------
//  Centre trace chart (throttle/brake, steer/ffb, ...)
// ---------------------------------------------------------------------------
sealed class Trace : Pane
{
    public string Caption = "";
    public string L1 = "", L2 = "";
    public Color C1 = Pal.Green, C2 = Pal.Red;
    public bool Bipolar;      // draw the centre line
    public bool Signed1;      // series 1 is -1..1 instead of 0..1
    public bool Signed2;      // series 2 is -1..1 instead of 0..1
    public bool FillArea;
    public int WindowMs = 10000;
    public LapData Rev;            // when set, draw this recorded lap instead of the live ring
    public float RevPos = -1f;    // cursor position inside the lap, seconds
    public bool RevSteerFf;       // true: series are steering/FFB instead of throttle/brake
    public Ring R1 = new Ring(6000);
    public Ring R2 = new Ring(6000);

    public Trace() { TabStop = false; }

    public void Clear() { R1.Clear(); R2.Clear(); Invalidate(); }

    public void Push(double now, float a, float b)
    {
        R1.Add(now, a);
        R2.Add(now, b);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        G.Box(g, new Rectangle(0, 0, Width, Height), Pal.Panel, Pal.Line);

        // header band: title on the left, legend right aligned, time labels under
        int left = 10, headH = 32;
        Rectangle inner = new Rectangle(left, headH, Math.Max(10, Width - 20), Math.Max(10, Height - headH - 8));

        G.Text(g, Caption, F.Cap, Pal.Dim, left, 2);
        {
            float w1 = string.IsNullOrEmpty(L1) ? 0f : 16f + g.MeasureString(L1, F.CapS).Width + 12f;
            float w2 = string.IsNullOrEmpty(L2) ? 0f : 16f + g.MeasureString(L2, F.CapS).Width;
            float lx0 = inner.Right - (w1 + w2);
            float capEnd = left + g.MeasureString(Caption, F.Cap).Width + 12f;
            if (lx0 >= capEnd)      // never let the legend run into the title
            {
                if (w1 > 0f) DrawLegend(g, lx0, 3, L1, C1);
                if (w2 > 0f) DrawLegend(g, lx0 + w1, 3, L2, C2);
            }
        }

        // horizontal grid
        for (int i = 1; i <= 4; i++)
        {
            int y = inner.Y + inner.Height * i / 5;
            G.Line(g, Pal.Line, 1f, inner.Left, y, inner.Right, y + 0.5f);
        }
        // frame + centre line
        using (Pen p = new Pen(Pal.Line))
            g.DrawRectangle(p, inner.X, inner.Y, inner.Width - 1, inner.Height - 1);
        if (Bipolar)
            G.Line(g, Pal.Line, 1f, inner.Left, inner.Y + inner.Height / 2f, inner.Right, inner.Y + inner.Height / 2f);

        if (Rev != null) { DrawLap(g, inner); return; }

        // time grid + labels
        double now = Clock.Now;
        using (Font tf = new Font("Segoe UI", 7f))
        {
            for (int i = 0; i <= 5; i++)
            {
                int x = inner.Left + inner.Width * i / 5;
                if (i > 0 && i < 5)
                    G.Line(g, Color.FromArgb(44, 44, 44), 1f, x, inner.Top + 2, x, inner.Bottom - 1);

                string lab;
                int secsAgo = (WindowMs / 1000) * (5 - i) / 5;
                if (i == 5) lab = "now";
                else if (secsAgo == 0) lab = "now";
                else lab = "-" + secsAgo.ToString(Fmt.Inv) + "s";

                SizeF sz = g.MeasureString(lab, tf);
                float tx = i == 0 ? x : (i == 5 ? x - sz.Width : x - sz.Width / 2);
                G.Text(g, lab, tf, Pal.Dim, tx, 16);
            }
        }

        // series
        DrawSeries(g, inner, R1, C1, now, FillArea && !Signed1, Signed1);
        DrawSeries(g, inner, R2, C2, now, false, Signed2);
    }

    float DrawLegend(Graphics g, float x, float y, string label, Color c)
    {
        if (string.IsNullOrEmpty(label)) return x;
        G.Line(g, c, 2f, x, y + 7, x + 12, y + 7);
        G.Text(g, label, F.CapS, Pal.Dim, x + 16, y - 1);
        return x + 16 + g.MeasureString(label, F.CapS).Width + 12;
    }

    /// Draws a window of a recorded lap: x = lap time, the window follows the
    /// cursor.  The window width is the same TIME setting the live charts use.
    void DrawLap(Graphics g, Rectangle inner)
    {
        float span = Math.Max(0.5f, Rev.Span);
        float win = Math.Max(2f, WindowMs / 1000f);
        if (win > span) win = span;
        float t0 = RevPos - win * 0.5f;
        if (t0 > span - win) t0 = span - win;
        if (t0 < 0f) t0 = 0f;
        float t1 = t0 + win;
        float dt = Math.Max(0.001f, t1 - t0);

        using (Font tf = new Font("Segoe UI", 7f))
        {
            for (int i = 0; i <= 5; i++)
            {
                int x = inner.Left + inner.Width * i / 5;
                if (i > 0 && i < 5)
                    G.Line(g, Color.FromArgb(44, 44, 44), 1f, x, inner.Top + 2, x, inner.Bottom - 1);

                float tv = t0 + dt * i / 5f;
                int mm = (int)(tv / 60f);
                float ss = tv - mm * 60f;
                string lab = win >= 20f
                    ? mm.ToString(Fmt.Inv) + ":" + ((int)Math.Round(ss)).ToString("00", Fmt.Inv)
                    : mm.ToString(Fmt.Inv) + ":" + ss.ToString("00.0", Fmt.Inv);
                SizeF sz = g.MeasureString(lab, tf);
                float tx = i == 0 ? x : (i == 5 ? x - sz.Width : x - sz.Width / 2f);
                G.Text(g, lab, tf, Pal.Dim, tx, 16);
            }
        }

        Region old = g.Clip;
        g.SetClip(inner);
        DrawLapSeries(g, inner, C1, RevSteerFf ? 0 : 1, RevSteerFf && Signed1, t0, t1);
        DrawLapSeries(g, inner, C2, RevSteerFf ? 3 : 2, false, t0, t1);
        g.Clip = old;

        float cur = Math.Max(t0, Math.Min(t1, RevPos));
        int cx = inner.Left + (int)(inner.Width * (cur - t0) / dt);
        G.Line(g, Color.FromArgb(150, 150, 150), 1f, cx, inner.Top + 1, cx, inner.Bottom - 1);
    }

    /// which: 0 = steering, 1 = throttle, 2 = brake, 3 = |FFB|
    void DrawLapSeries(Graphics g, Rectangle inner, Color c, int which, bool signed, float t0, float t1)
    {
        int n = Rev.Samples.Count;
        if (n < 2) return;

        float dt = Math.Max(0.001f, t1 - t0);
        List<PointF> pts = new List<PointF>(n);
        for (int i = 0; i < n; i++)
        {
            LapSample s = Rev.Samples[i];
            if (s.T < t0 - 0.5f || s.T > t1 + 0.5f) continue;
            float val;
            if (which == 0) val = s.Steer;
            else if (which == 1) val = s.Gas;
            else if (which == 2) val = s.Brake;
            else val = Math.Min(1f, Math.Abs(s.Ff));
            float y = signed
                ? inner.Y + inner.Height * (0.5f - 0.5f * Math.Max(-1f, Math.Min(1f, val)))
                : inner.Y + inner.Height * (1f - Math.Max(-0.2f, Math.Min(1f, val)));
            if (y < inner.Y + 1.5f) y = inner.Y + 1.5f;
            if (y > inner.Bottom - 1.5f) y = inner.Bottom - 1.5f;
            float x = inner.Left + inner.Width * (s.T - t0) / dt;
            pts.Add(new PointF(x, y));
        }
        if (pts.Count < 2) return;

        using (Pen p = new Pen(c, 1.6f))
        {
            p.LineJoin = LineJoin.Round;
            g.DrawLines(p, pts.ToArray());
        }
    }
    void DrawSeries(Graphics g, Rectangle inner, Ring r, Color c, double now, bool fill, bool signed)
    {
        if (r.Count < 2) return;
        double t0 = now - WindowMs;
        if (t0 < 0) t0 = 0;

        List<PointF> pts = new List<PointF>(256);
        for (int i = r.Count - 1; i >= 0; i--)
        {
            double t = r.Time(i);
            if (t < t0) break;
            float val = r[i];
            float y = signed
                ? inner.Y + inner.Height * (0.5f - 0.5f * Math.Max(-1f, Math.Min(1f, val)))
                : inner.Y + inner.Height * (1f - Math.Max(-0.2f, Math.Min(1f, val)));
            if (y < inner.Y + 1.5f) y = inner.Y + 1.5f;
            if (y > inner.Bottom - 1.5f) y = inner.Bottom - 1.5f;
            float x = inner.Right - (float)((now - t) / WindowMs * inner.Width);
            pts.Add(new PointF(x, y));
        }
        if (pts.Count < 2) return;

        using (Pen p = new Pen(c, 1.8f))
        {
            p.LineJoin = LineJoin.Round;
            g.DrawLines(p, pts.ToArray());
        }
        if (fill && pts.Count > 2)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(26, c)))
            {
                PointF[] poly = new PointF[pts.Count + 2];
                pts.CopyTo(poly, 0);
                poly[pts.Count] = new PointF(pts[pts.Count - 1].X, inner.Bottom - 1);
                poly[pts.Count + 1] = new PointF(pts[0].X, inner.Bottom - 1);
                g.FillPolygon(b, poly);
            }
        }
    }
}

// ---------------------------------------------------------------------------
//  Steering wheel + FFB force
// ---------------------------------------------------------------------------
sealed class SteerGauge : Pane
{
    public float Steer;   // -1..1
    public float Ff;      // 0..1
    public int FfPct;

    public SteerGauge() { TabStop = false; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        G.Box(g, new Rectangle(0, 0, Width, Height), Pal.Panel, Pal.Line);

float barH = 16f;
        Rectangle bar = new Rectangle(12, Height - (int)barH - 6, Width - 24, 6);

        G.Text(g, "STEERING", F.Cap, Pal.Dim, 10, 5);
        G.Text(g, "FFB " + Fmt.I(Ff * 100) + "%", F.Cap, Pal.Amber,
               Width - 10 - g.MeasureString("FFB 100%", F.Cap).Width, 5);

        // dial on the left, numeric readout on the right: a wide, short card
        // cannot hold a big dial with the number underneath it
        float top = 26f;
        float rad = Math.Min(Width * 0.44f - 10f, (bar.Y - 6f - top) / 2f);
        if (rad < 14f) rad = 14f;
        float cx = 10f + rad + 4f, cy = top + rad;
        RectangleF circle = new RectangleF(cx - rad, cy - rad, rad * 2, rad * 2);

        using (Pen p = new Pen(Pal.Line, 1f)) g.DrawEllipse(p, circle);
        using (Pen p = new Pen(Color.FromArgb(40, 40, 40), 1f))
            g.DrawEllipse(p, new RectangleF(cx - rad * 0.62f, cy - rad * 0.62f, rad * 1.24f, rad * 1.24f));

        // ticks every 15 degrees over the +/-135 sweep
        for (int a = -135; a <= 135; a += 15)
        {
            double r = (a - 90) * Math.PI / 180.0;
            float x1 = cx + (float)Math.Cos(r) * (rad - 2);
            float y1 = cy + (float)Math.Sin(r) * (rad - 2);
            bool major = a % 45 == 0;
            float len = major ? Math.Min(9f, rad * 0.3f) : Math.Min(5f, rad * 0.18f);
            float x2 = cx + (float)Math.Cos(r) * (rad - 2 - len);
            float y2 = cy + (float)Math.Sin(r) * (rad - 2 - len);
            G.Line(g, major ? Pal.Dim : Color.FromArgb(70, 70, 70), major ? 1.4f : 1f, x1, y1, x2, y2);
        }

        float sa = Steer * 135f;
        double na = (sa - 90) * Math.PI / 180.0;
        float nx = cx + (float)Math.Cos(na) * (rad - 10);
        float ny = cy + (float)Math.Sin(na) * (rad - 10);
        G.Line(g, Pal.Text, 2.2f, cx, cy, nx, ny);
        using (SolidBrush b = new SolidBrush(Pal.Text)) g.FillEllipse(b, cx - 3.5f, cy - 3.5f, 7, 7);
        using (SolidBrush b = new SolidBrush(Pal.Panel)) g.FillEllipse(b, cx - 1.5f, cy - 1.5f, 3, 3);

        string txt = (Steer >= 0 ? "+" : "-") + Fmt.N(Math.Abs(Steer * 100), 0) + "%";
        float tx0 = cx + rad + 10f;
        int tw = (int)Math.Max(20f, Width - tx0 - 8f);
        G.TextMidL(g, txt, F.ValS, Steer >= 0 ? Pal.Text : Pal.Amber,
                   new Rectangle((int)tx0, (int)(cy - 14f), tw, 28), 0f);

        // FFB bar: magnitude from the force, direction from the steering
        G.Fill(g, bar, Pal.PanelHi);
        int mid = bar.X + bar.Width / 2;
        G.Line(g, Pal.Line, 1f, mid, bar.Y - 2, mid, bar.Bottom + 2);
        float hw = Math.Max(0.02f, Math.Min(1f, Ff)) * (bar.Width / 2f - 1);
        Rectangle f = Steer >= 0
            ? new Rectangle(mid, bar.Y, (int)Math.Max(1, hw), bar.Height)
            : new Rectangle(mid - (int)Math.Max(1, hw), bar.Y, (int)Math.Max(1, hw), bar.Height);
        G.Fill(g, f, Pal.Amber);
    }
}

// ---------------------------------------------------------------------------
//  Brake / throttle vertical bars
// ---------------------------------------------------------------------------
sealed class PedalBars : Pane
{
    public float Brake, Gas;
    public float PeakBrake, PeakGas;
    public bool ResetPeaks;

    public PedalBars() { TabStop = false; }

    public void Set(float brake, float gas)
    {
        if (brake > PeakBrake) PeakBrake = brake;
        if (gas > PeakGas) PeakGas = gas;
        if (ResetPeaks) { PeakBrake = brake; PeakGas = gas; ResetPeaks = false; }
        Brake = brake; Gas = gas;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        G.Box(g, new Rectangle(0, 0, Width, Height), Pal.Panel, Pal.Line);

        G.Text(g, "BRAKE", F.Cap, Pal.Dim, 10, 5);
        G.Text(g, "THROTTLE", F.Cap, Pal.Dim, Width - 10 - g.MeasureString("THROTTLE", F.Cap).Width, 5);

        int top = 26, bot = Height - 26;
        int h = Math.Max(20, bot - top);
        int gap = 24;
        int bw = Math.Min(48, Math.Max(14, (Width - gap) / 4));
        int bx1 = Width / 2 - gap / 2 - bw;
        int bx2 = Width / 2 + gap / 2;

        RenderBar(g, new Rectangle(bx1, top, bw, h), Brake, PeakBrake, Pal.Red);
        RenderBar(g, new Rectangle(bx2, top, bw, h), Gas, PeakGas, Pal.Green);

        G.TextC(g, Fmt.I(Brake * 100) + "%", F.ValS, Pal.Red, new Rectangle(bx1 - 20, bot + 2, bw + 40, 20));
        G.TextC(g, Fmt.I(Gas * 100) + "%", F.ValS, Pal.Green, new Rectangle(bx2 - 20, bot + 2, bw + 40, 20));
    }

    void RenderBar(Graphics g, Rectangle r, float v, float peak, Color c)
    {
        G.Fill(g, r, Pal.PanelHi);
        using (Pen p = new Pen(Pal.Line)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);

        int fh = (int)(r.Height * Math.Max(0, Math.Min(1, v)));
        if (fh > 0) G.Fill(g, new Rectangle(r.X + 1, r.Bottom - fh, r.Width - 2, fh), c);

        for (int i = 1; i <= 3; i++)
        {
            int y = r.Bottom - r.Height * i / 4;
            G.Line(g, Color.FromArgb(70, 70, 70), 1f, r.X + 1, y, r.Right - 1, y);
        }
        if (peak > 0.01f)
        {
            int py = r.Bottom - (int)(r.Height * Math.Min(1, peak));
            G.Line(g, Pal.Text, 1.4f, r.X, py, r.Right - 1, py);
        }
    }
}

// ---------------------------------------------------------------------------
//  G-G friction circle
// ---------------------------------------------------------------------------
sealed class GGDisc : Pane
{
    public float Lat, Lon;          // g
    public float GScale = 1.6f;
    public float Peak = 1f;
    float smoothM;
    public bool ResetPeaks;
    readonly List<PointF> trail = new List<PointF>(120);

    public GGDisc() { TabStop = false; }

    public void Set(float lat, float lon, float gscale)
    {
        Lat = lat; Lon = lon; GScale = Math.Max(0.5f, gscale);
        float m = (float)Math.Sqrt(lat * lat + lon * lon);
        smoothM += (m - smoothM) * 0.18f;      // peak from the smoothed vector, not one-frame spikes
        if (smoothM > Peak) Peak = smoothM;
        if (ResetPeaks) { Peak = Math.Max(0.2f, smoothM); ResetPeaks = false; trail.Clear(); }
        trail.Add(new PointF(lat, lon));
        if (trail.Count > 90) trail.RemoveAt(0);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        G.Box(g, new Rectangle(0, 0, Width, Height), Pal.Panel, Pal.Line);

        G.Text(g, "G-G", F.Cap, Pal.Dim, 10, 5);

        int pad = 22, foot = 26;
        int d = Math.Min(Width - pad * 2, Height - foot - 12);
        if (d < 20) d = 20;
        float cx = Width / 2f;
        float cy = 8 + (Height - foot - 8) / 2f;
        float rad = d / 2f;

        using (Pen p = new Pen(Pal.Line)) g.DrawEllipse(p, cx - rad, cy - rad, rad * 2, rad * 2);
        using (Pen p = new Pen(Color.FromArgb(42, 42, 42)))
            g.DrawEllipse(p, cx - rad * 0.5f, cy - rad * 0.5f, rad, rad);
        G.Line(g, Color.FromArgb(50, 50, 50), 1f, cx - rad, cy, cx + rad, cy);
        G.Line(g, Color.FromArgb(50, 50, 50), 1f, cx, cy - rad, cx, cy + rad);

        float k = rad / GScale;
        for (int i = 0; i < trail.Count; i++)
        {
            float a = 0.10f + 0.55f * (i / (float)Math.Max(1, trail.Count - 1));
            Color c = Color.FromArgb((int)(a * 255), Pal.Amber);
            using (SolidBrush b = new SolidBrush(c))
            {
                float px = cx + G.Clamp(trail[i].X / GScale, -0.98f, 0.98f) * rad;
                float py = cy - G.Clamp(trail[i].Y / GScale, -0.98f, 0.98f) * rad;
                g.FillRectangle(b, px - 1f, py - 1f, 2.2f, 2.2f);
            }
        }

        float x = cx + G.Clamp(Lat / GScale, -0.98f, 0.98f) * rad;
        float y = cy - G.Clamp(Lon / GScale, -0.98f, 0.98f) * rad;
        using (SolidBrush b = new SolidBrush(Pal.Amber)) g.FillEllipse(b, x - 3.5f, y - 3.5f, 7, 7);

        // cross labels
        using (Font f = new Font("Segoe UI", 6.5f))
        {
            G.Text(g, "LAT", f, Pal.Dim, cx + rad - 20, cy + 3);
            G.Text(g, "LON", f, Pal.Dim, cx + 3, cy - rad + 1);
        }

        G.TextC(g, "peak " + Fmt.N(Peak, 2) + " g", F.Small, Pal.Dim, new Rectangle(0, Height - foot + 2, Width, 18));
    }
}

// ---------------------------------------------------------------------------
//  Big single readout (gear / rpm)
// ---------------------------------------------------------------------------
sealed class BigRead : Pane
{
    public string Caption = "";
    public string Value = "";
    public string Unit = "";
    public Color ValColor = Pal.Text;
    public float ValPt = 40f;

    public BigRead() { TabStop = false; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        G.Box(g, new Rectangle(0, 0, Width, Height), Pal.Panel, Pal.Line);

        G.Text(g, Caption, F.Cap, Pal.Dim, 10, 5);

        bool showUnit = Unit.Length > 0 && Height >= 84;
        float capH = 19f;                                   // caption strip at the top
        float unitH = showUnit ? 17f : 0f;                  // unit strip at the bottom
        Rectangle valRect = new Rectangle(0, (int)capH, Width, (int)Math.Max(12f, Height - capH - unitH));
        // font points are not pixels: at 125% DPI a 39pt digit is ~60px tall and
        // used to run over the unit strip and out of the card, so measure the
        // real string and shrink it into the box.
        float pt = ValPt;
        using (Font probe = new Font("Consolas", pt, FontStyle.Bold))
        {
            SizeF sz = g.MeasureString(Value, probe);
            if (sz.Height > valRect.Height - 2 && sz.Height > 0)
                pt = Math.Max(11f, pt * (valRect.Height - 2) / sz.Height);
        }
        using (Font f = new Font("Consolas", pt, FontStyle.Bold))
            G.TextC(g, Value, f, ValColor, valRect);

        if (showUnit)
            G.TextC(g, Unit, F.Small, Pal.Dim, new Rectangle(0, Height - (int)unitH, Width, 14));
    }
}

// ---------------------------------------------------------------------------
//  Session info block (replaces the FFB tuning sliders)
// ---------------------------------------------------------------------------
sealed class SessionPanel : Pane
{
    public string Title = "SESSION";
    public List<string[]> Rows = new List<string[]>();    // { key, value }
    public List<Color> RowColors = new List<Color>();
    public string Foot = "";
    public int LastRowH, LastTop, LastFootH;

    public SessionPanel() { TabStop = false; }

    public void Set(string title, List<string[]> rows, List<Color> colors, string foot)
    {
        Title = title; Rows = rows; RowColors = colors; Foot = foot;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        G.Box(g, new Rectangle(0, 0, Width, Height), Pal.Panel, Pal.Line);

        G.Text(g, Title, F.Cap, Pal.Dim, 10, 4);

        // The panel is stretched by the column layout, so the row pitch is not a
        // constant: derive the font size from the pitch and clip every value to
        // its own row band.  Without the clip a long value (car name) bleeds into
        // the row below and the foot.
        bool showFoot = Foot.Length > 0 && Height >= 148;
        int footH = showFoot ? F.Small.Height + 7 : 2;
        int top = 4 + F.Cap.Height + 8;
        int bottom = Height - 4 - footH;
        int n = Math.Max(1, Rows.Count);
        int avail = Math.Max(n * 11, bottom - top);
        int rowH = Math.Max(11, avail / n);
        LastRowH = rowH; LastTop = top; LastFootH = footH;
        float rowPt = G.Clamp((rowH - 3.5f) * 0.62f, 6.5f, 11f);
        // "POSITION" is the widest key; the value column has to start after it or
        // the two overlap (that was the text collision in the session panel).
        const int KeyCol = 78;
        float keyPt = Math.Max(6.5f, Math.Min(8.5f, rowPt - 1f));

        using (Font kf = new Font("Segoe UI", keyPt))
        using (Font vf = new Font("Consolas", rowPt, FontStyle.Bold))
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                int ry = top + i * rowH;
                Rectangle band = new Rectangle(0, ry, Width, rowH);
                G.TextMidL(g, Rows[i][0], kf, Pal.Dim, band, 10f);
                Color vc = i < RowColors.Count ? RowColors[i] : Pal.Text;
                Rectangle vr = new Rectangle(KeyCol, ry, Math.Max(24, Width - KeyCol - 6), rowH);
                GraphicsState st = g.Save();
                g.SetClip(vr, CombineMode.Intersect);
                G.TextFitMidR(g, Rows[i][1], vf, vc, vr, 4f, 7f);
                g.Restore(st);
            }
        }

        if (showFoot)
            G.Text(g, Foot, F.Small, Pal.Dim, 10, Height - footH);
    }
}
