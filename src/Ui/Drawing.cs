// ===========================================================================
//  AccDash - fonts, drawing helpers and the control base class
//  src/Ui/Drawing.cs
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
//  Fonts
// ---------------------------------------------------------------------------
static class F
{
    public static readonly Font Cap = new Font("Segoe UI", 8.5f, FontStyle.Bold);
    public static readonly Font CapS = new Font("Segoe UI", 7.5f, FontStyle.Bold);
    public static readonly Font Small = new Font("Segoe UI", 8f);
    public static readonly Font SmallB = new Font("Segoe UI", 8f, FontStyle.Bold);
    public static readonly Font Title = new Font("Segoe UI", 11f, FontStyle.Bold);
    public static readonly Font Val = new Font("Consolas", 19f, FontStyle.Bold);
    public static readonly Font ValM = new Font("Consolas", 15f, FontStyle.Bold);
    public static readonly Font ValS = new Font("Consolas", 12f, FontStyle.Bold);
    public static readonly Font Huge = new Font("Consolas", 34f, FontStyle.Bold);
    public static readonly Font Huge2 = new Font("Consolas", 40f, FontStyle.Bold);
}

// ---------------------------------------------------------------------------
//  Drawing helpers - everything is axis aligned, no rounded corners
// ---------------------------------------------------------------------------
static class G
{
    public static void Fill(Graphics g, Rectangle r, Color c)
    {
        using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, r);
    }

    public static float Clamp(float v, float lo, float hi)
    {
        return v < lo ? lo : (v > hi ? hi : v);
    }

    public static void Box(Graphics g, Rectangle r, Color fill, Color border)
    {
        Fill(g, r, fill);
        using (Pen p = new Pen(border)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    public static void Line(Graphics g, Color c, float w, float x1, float y1, float x2, float y2)
    {
        using (Pen p = new Pen(c, w)) g.DrawLine(p, x1, y1, x2, y2);
    }

    public static void Text(Graphics g, string s, Font f, Color c, float x, float y)
    {
        using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, x, y);
    }

    public static void TextR(Graphics g, string s, Font f, Color c, Rectangle r, float pad)
    {
        SizeF sz = g.MeasureString(s, f);
        using (SolidBrush b = new SolidBrush(c))
            g.DrawString(s, f, b, r.Right - pad - sz.Width, r.Y + (r.Height - sz.Height) * 0.5f);
    }

    public static void TextC(Graphics g, string s, Font f, Color c, Rectangle r)
    {
        SizeF sz = g.MeasureString(s, f);
        using (SolidBrush b = new SolidBrush(c))
            g.DrawString(s, f, b, r.X + (r.Width - sz.Width) * 0.5f, r.Y + (r.Height - sz.Height) * 0.5f);
    }

    /// Right aligned text that shrinks to fit "maxW".
    public static void TextFit(Graphics g, string s, Font f, Color c, Rectangle r, float pad, float minPt)
    {
        SizeF sz = g.MeasureString(s, f);
        float maxW = r.Width - pad * 2;
        if (sz.Width <= maxW)
        {
            TextR(g, s, f, c, r, pad);
            return;
        }
        float pt = f.Size;
        while (pt > minPt)
        {
            pt -= 1f;
            using (Font nf = new Font(f.FontFamily, pt, f.Style))
            {
                sz = g.MeasureString(s, nf);
                if (sz.Width <= maxW)
                {
                    TextR(g, s, nf, c, r, pad);
                    return;
                }
            }
        }
        TextR(g, s, f, c, r, pad);
    }

    // ---- vertically centred variants -------------------------------------
    // DrawString with LineAlignment.Center puts the optical centre of the text
    // box on the centre of the rectangle, so a Segoe key and a Consolas value in
    // the same row share a baseline no matter how their metrics differ (the old
    // MeasureString based centring left the value ~4px above its key).

    static readonly StringFormat SfMidL = MakeSf(StringAlignment.Near);
    static readonly StringFormat SfMidR = MakeSf(StringAlignment.Far);
    static readonly StringFormat SfMidC = MakeSf(StringAlignment.Center);

    static StringFormat MakeSf(StringAlignment a)
    {
        StringFormat s = new StringFormat(StringFormatFlags.NoWrap);
        s.Alignment = a;
        s.LineAlignment = StringAlignment.Center;
        s.Trimming = StringTrimming.None;
        return s;
    }

    public static void TextMidL(Graphics g, string s, Font f, Color c, Rectangle r, float x)
    {
        RectangleF rf = new RectangleF(r.X + x, r.Y, Math.Max(8f, r.Width - x), r.Height);
        using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, rf, SfMidL);
    }

    public static void TextMidR(Graphics g, string s, Font f, Color c, Rectangle r, float pad)
    {
        RectangleF rf = new RectangleF(r.X, r.Y, Math.Max(8f, r.Width - pad), r.Height);
        using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, rf, SfMidR);
    }

    public static void TextMidC(Graphics g, string s, Font f, Color c, Rectangle r)
    {
        RectangleF rf = new RectangleF(r.X, r.Y, Math.Max(8f, r.Width), Math.Max(8f, r.Height));
        using (SolidBrush b = new SolidBrush(c)) g.DrawString(s, f, b, rf, SfMidC);
    }

    public static void TextFitMidR(Graphics g, string s, Font f, Color c, Rectangle r, float pad, float minPt)
    {
        SizeF sz = g.MeasureString(s, f);
        float maxW = r.Width - pad * 2;
        if (sz.Width <= maxW) { TextMidR(g, s, f, c, r, pad); return; }
        float pt = f.Size;
        while (pt > minPt)
        {
            pt -= 0.5f;
            using (Font nf = new Font(f.FontFamily, pt, f.Style))
            {
                sz = g.MeasureString(s, nf);
                if (sz.Width <= maxW) { TextMidR(g, s, nf, c, r, pad); return; }
            }
        }
        TextMidR(g, s, f, c, r, pad);
    }
}

// ---------------------------------------------------------------------------
//  Base control
// ---------------------------------------------------------------------------
abstract class Pane : Control
{
    protected Pane()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Pal.Panel;
    }
}
