// ===========================================================================
//  AccDash - data tile, status lamp and flat button
//  src/Ui/Widgets.cs
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
//  Left column data tile
// ---------------------------------------------------------------------------
sealed class Tile : Pane
{
    public string Caption = "";
    public string Value = "";
    public string Sub = "";
    public Color ValColor = Pal.Text;
    public float ValPt = 19f;
    public bool Warning;

    public Tile(string caption) { Caption = caption; TabStop = false; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        G.Box(g, new Rectangle(0, 0, Width, Height), Pal.Panel, Warning ? Pal.Amber : Pal.Line);

        bool compact = Height < 46;
        // The value should never fill the whole tile: keep a margin above and
        // below, and only grow past the design size on very tall cards.
        float pt = Math.Min(26f, Math.Max(11f, (Height - 4) * 0.38f));

        if (compact)
        {
            // one line: caption on the left, number on the right
            G.Text(g, Caption, F.CapS, Pal.Dim, 10, (Height - F.CapS.Height) / 2f + 1f);
            using (Font f = new Font("Consolas", pt, FontStyle.Bold))
                G.TextFit(g, Value, f, ValColor, new Rectangle(8, 0, Width - 16, Height), 2f, 10f);
            return;
        }

        // caption and sub stack on the left, the number owns the full height on the right
        G.Text(g, Caption, F.Cap, Pal.Dim, 10, 5);
        if (Sub.Length > 0)
            G.Text(g, Sub, F.Small, SubColor, 10, 5 + F.Cap.Height + 1);

        using (Font f = new Font("Consolas", pt, FontStyle.Bold))
            G.TextFit(g, Value, f, ValColor, new Rectangle(8, 4, Width - 16, Height - 8), 2f, 10f);
    }

    public Color SubColor = Pal.Dim;
}

// ---------------------------------------------------------------------------
//  Top bar status lamp
// ---------------------------------------------------------------------------
sealed class StatusLamp : Pane
{
    public string Title = "";
    public string Detail = "";
    public Color C = Pal.Red;

    public StatusLamp(string title) { Title = title; TabStop = false; }

    public void Set(Color c, string detail)
    {
        bool dirty = c != C || detail != Detail;
        C = c; Detail = detail;
        if (dirty) Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        G.Fill(g, ClientRectangle, Pal.Panel);
        // Two explicit bands: the caption and the detail line each get a fixed
        // height, so neither can push the other out of the 10px LED row.
        int led = 9;
        G.Fill(g, new Rectangle(0, 8, led, led), C);
        int tx = led + 6;
        G.TextMidL(g, Title, F.CapS, C, new Rectangle(tx, 3, Math.Max(20, Width - tx - 2), 18), 0f);
        G.TextMidL(g, Detail, F.Small, Pal.Dim, new Rectangle(tx, 21, Math.Max(20, Width - tx - 2), 20), 0f);
    }
}

// ---------------------------------------------------------------------------
//  Flat button, drawn like the rest of the panel (no native button chrome)
// ---------------------------------------------------------------------------
sealed class FlatBtn : Pane
{
    public string Caption = "";
    public bool Dimmed;          // drawn in the dim ink but still clickable
    public event EventHandler Hit;

    bool hover, down;

    public FlatBtn(string caption)
    {
        Caption = caption;
        TabStop = false;
        Cursor = Cursors.Hand;
        BackColor = Pal.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Color fill = (hover || down) ? Pal.PanelHi : Pal.Bg;
        G.Box(g, new Rectangle(0, 0, Width, Height), fill, hover ? Pal.Dim : Pal.Line);
        if (down)
            G.Box(g, new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2)), fill, Pal.Line);
        G.TextMidC(g, Caption, F.SmallB, Dimmed ? Pal.Dim : Pal.Text, new Rectangle(0, 0, Width, Height));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = false; down = false; Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        down = true; Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        bool was = down;
        down = false;
        Invalidate();
        if (!was) return;
        if (e.X < 0 || e.Y < 0 || e.X >= Width || e.Y >= Height) return;
        if (Hit != null) Hit(this, EventArgs.Empty);
    }
}
