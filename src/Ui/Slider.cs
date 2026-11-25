// ===========================================================================
//  AccDash - flat slider
//  src/Ui/Slider.cs
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
//  Flat slider used for the trace window
// ---------------------------------------------------------------------------
sealed class Slider : Control
{
    public int Min = 1;
    public int Max = 60;
    public int Value = 10;
    public event EventHandler ValueChanged;

    bool drag;

    public Slider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Pal.Bg;
        Height = 22;
        TabStop = false;
        Cursor = Cursors.Hand;
    }

    float T()
    {
        if (Max <= Min) return 0f;
        return (Value - Min) / (float)(Max - Min);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        G.Fill(g, ClientRectangle, BackColor);
        int mid = Height / 2;
        G.Fill(g, new Rectangle(0, mid - 1, Width, 3), Pal.PanelHi);
        int hx = (int)(T() * (Width - 8));
        G.Fill(g, new Rectangle(0, mid - 1, hx + 4, 3), Pal.Accent);
        G.Fill(g, new Rectangle(hx, mid - 7, 7, 15), Pal.Text);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        drag = true;
        SetFromX(e.X);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (drag) SetFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        drag = false;
        base.OnMouseUp(e);
    }

    void SetFromX(int x)
    {
        float t = Width <= 8 ? 0 : (x - 4f) / (Width - 8);
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        int nv = Min + (int)Math.Round(t * (Max - Min));
        if (nv != Value)
        {
            Value = nv;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }
}
