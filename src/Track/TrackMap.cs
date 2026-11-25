// ===========================================================================
//  AccDash - track map control
//  src/Track/TrackMap.cs
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


/// Left-column track map: the learned outline plus the car's live position.
sealed class TrackMap : Pane
{
    public TrackOutline Outline;
    public float X, Z, Head;
    public bool Review;

    public TrackMap()
    {
        TabStop = false;
    }

    public void Set(float x, float z, float head, TrackOutline o, bool review)
    {
        X = x; Z = z; Head = head; Outline = o; Review = review;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        G.Box(g, new Rectangle(0, 0, Width, Height), Pal.Panel, Pal.Line);

        Rectangle head = new Rectangle(10, 0, Math.Max(20, Width - 20), 24);
        G.TextMidL(g, "TRACK MAP", F.Cap, Pal.Dim, head, 2f);
        if (Outline != null && !string.IsNullOrEmpty(Outline.Track))
        {
            string right = Outline.Track.ToUpperInvariant();
            if (Outline.Closed && Outline.LengthM > 50f)
                right += "  " + (Outline.LengthM / 1000f).ToString("0.000", Fmt.Inv) + " km";
            G.TextMidR(g, right, F.CapS, Review ? Pal.Amber : Pal.Dim, head, 2f);
        }

        Rectangle inner = new Rectangle(10, 26, Math.Max(10, Width - 20), Math.Max(10, Height - 40));
        List<PointF> pts = Outline == null ? null : Outline.Points;
        if (pts == null || pts.Count < 8)
        {
            G.TextMidC(g, "none", F.Small, Color.FromArgb(110, 110, 110), inner);
            return;
        }

        float minX = pts[0].X, maxX = pts[0].X, minZ = pts[0].Y, maxZ = pts[0].Y;
        for (int i = 1; i < pts.Count; i++)
        {
            if (pts[i].X < minX) minX = pts[i].X;
            if (pts[i].X > maxX) maxX = pts[i].X;
            if (pts[i].Y < minZ) minZ = pts[i].Y;
            if (pts[i].Y > maxZ) maxZ = pts[i].Y;
        }
        float w = Math.Max(1f, maxX - minX), h = Math.Max(1f, maxZ - minZ);
        float sc = Math.Min((inner.Width - 10f) / w, (inner.Height - 10f) / h);
        float ox = inner.X + (inner.Width - w * sc) * 0.5f;
        float oz = inner.Y + (inner.Height - h * sc) * 0.5f;

        // Top-down projection: screen right = +x, screen down = +z.  The previous
        // version mirrored z, which flipped every corner left<->right.
        PointF[] sp = new PointF[pts.Count];
        for (int i = 0; i < pts.Count; i++)
            sp[i] = new PointF(ox + (pts[i].X - minX) * sc, oz + (pts[i].Y - minZ) * sc);

        using (Pen p = new Pen(Color.FromArgb(150, 150, 150), 3.5f))
        {
            p.LineJoin = LineJoin.Round;
            p.StartCap = LineCap.Round;
            p.EndCap = LineCap.Round;
            for (int i = 1; i < sp.Length; i++) g.DrawLine(p, sp[i - 1], sp[i]);
            if (Outline.Closed) g.DrawLine(p, sp[sp.Length - 1], sp[0]);
        }

        // start / finish tick: perpendicular to the first segment
        if (sp.Length > 2)
        {
            float dx = sp[1].X - sp[0].X, dy = sp[1].Y - sp[0].Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len > 0.01f)
            {
                float nx = -dy / len * 6f, ny = dx / len * 6f;
                G.Line(g, Pal.Green, 2f, sp[0].X - nx, sp[0].Y - ny, sp[0].X + nx, sp[0].Y + ny);
            }
        }

        if (Math.Abs(X) + Math.Abs(Z) < 0.01f) return;

        float cx = ox + (X - minX) * sc;
        float cy = oz + (Z - minZ) * sc;
        cx = Math.Max(inner.X + 4f, Math.Min(inner.Right - 4f, cx));
        cy = Math.Max(inner.Y + 4f, Math.Min(inner.Bottom - 4f, cy));

        Color dot = Review ? Pal.Amber : Pal.Text;
        using (SolidBrush b = new SolidBrush(dot))
            g.FillEllipse(b, cx - 4.5f, cy - 4.5f, 9f, 9f);
    }
}
