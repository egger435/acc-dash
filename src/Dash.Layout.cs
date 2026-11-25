// ===========================================================================
//  AccDash - layout of the three columns
//  src/Dash.Layout.cs
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


sealed partial class Dash
{
    void LayoutAll()
    {
        if (lamps == null || ClientSize.Width < 200) return;
        laidOutW = ClientSize.Width;
        laidOutH = ClientSize.Height;

        int W = ClientSize.Width, H = ClientSize.Height;

        // ---- header
        int bx = W - 12;
        for (int i = headButtons.Length - 1; i >= 0; i--)
        {
            Button b = headButtons[i];
            if (!b.Visible) continue;
            b.Bounds = new Rectangle(bx - b.Width, 11, b.Width, 30);
            bx -= b.Width + 8;
        }
        int btnLeft = bx;

        int lampW = 150;
        int room = btnLeft - 12 - 12;
        if (5 * (lampW + 8) > room) lampW = Math.Max(84, (room - 5 * 8) / 5);
        else if (W > 1700) lampW = Math.Min(260, (room - 5 * 8) / 5);   // wide: spread the lamps out
        for (int i = 0; i < 5; i++)
        {
            lamps[i].Bounds = new Rectangle(12 + i * (lampW + 8), 4, lampW, 44);
        }

        // ---- left column
        int x = 10, y = HeaderH + 10, w = LeftW;
        int bottom = H - 10;

        Tile[] tiles = { tSpeed, tRpm, tGear, tSteer, tLatG, tLongG, tTc, tAbs, tFuel, tTyre };
        int nT = tiles.Length, gap = 3;
        int timeH = 26, iniH = 20, spacer = 12;

        int free = bottom - y - ((nT - 1) * gap + spacer + 4 + timeH + iniH + 2);

        // the track map owns the top of the column; the ten cards shrink to fit.
        // The session panel needs eight 11px rows plus a caption, and the cards
        // need 28px, so the map is the one that gives way on a short screen.
        int sessMin = 126, tileMin = 28;
        int mapH = Math.Max(110, Math.Min(320, (int)(free * 0.26)));
        int mapRoom = free - nT * tileMin - sessMin - 6;
        if (mapH > mapRoom) mapH = Math.Max(90, mapRoom);
        int rest = free - mapH - 6;
        int sessTarget = Math.Min(240, Math.Max(sessMin, (int)(rest * 0.34)));
        int tileH = (rest - sessTarget) / nT;
        if (tileH < tileMin)
        {
            tileH = tileMin;
            sessTarget = rest - tileH * nT;
        }
        sessTarget = rest - tileH * nT;          // absorb the rounding remainder
        if (sessTarget < sessMin) sessTarget = sessMin;

        map.Bounds = new Rectangle(x, y, w, mapH);
        y += mapH + 6;

        foreach (Tile t in tiles)
        {
            t.Bounds = new Rectangle(x, y, w, tileH);
            y += tileH + gap;
        }

        y += spacer - gap;
        sess.Bounds = new Rectangle(x, y, w, sessTarget);
        y += sessTarget + 4;

        timeLbl.Bounds = new Rectangle(x, y, 74, timeH);
        timeSlider.Bounds = new Rectangle(x + 76, y + 1, w - 76, 22);
        y += timeH;

        iniLbl.Bounds = new Rectangle(x, bottom - iniH, w, iniH);

        // ---- right column
        int rx = W - 10 - RightW;
        int ry = HeaderH + 10;
        int rh = H - 10 - ry;
        double[] wts = { 1.60, 1.70, 2.15, 1.15, 1.15 };
        double sum = 0;
        foreach (double d in wts) sum += d;
        int avail = rh - 4 * 6;

        int[] hs = new int[5];
        int used = 0;
        for (int i = 0; i < 5; i++)
        {
            hs[i] = (int)(avail * wts[i] / sum);
            used += hs[i];
        }
        hs[2] += avail - used;      // give rounding leftovers to the G-G card

        Control[] comps = { gauge, pedals, gg, gearRead, rpmRead };
        int cy = ry;
        for (int i = 0; i < 5; i++)
        {
            comps[i].Bounds = new Rectangle(rx, cy, RightW, hs[i]);
            cy += hs[i] + 6;
        }

        // ---- centre column
        int cx = x + LeftW + 10;
        int cw = rx - 10 - cx;
        if (cw < 100) cw = 100;
        int ch = rh;
        int scrubH = review != null ? 56 : 0;
        int th = (ch - 6 - scrubH) / 2;
        trPedals.Bounds = new Rectangle(cx, ry, cw, th);
        trSteerFf.Bounds = new Rectangle(cx, ry + th + 6, cw, ch - th - 6 - scrubH);
        if (scrub != null)
        {
            scrub.Visible = review != null;
            if (review != null) scrub.Bounds = new Rectangle(cx, ry + ch - 50, cw, 50);
        }

        // fonts that depend on tile height
        gearRead.ValPt = Math.Max(18f, Math.Min(52f, hs[3] * 0.62f));
        rpmRead.ValPt = Math.Max(16f, Math.Min(44f, hs[4] * 0.52f));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int one = 1;
            Native.DwmSetWindowAttribute(Handle, 20, ref one, 4);   // dark mode
            Native.DwmSetWindowAttribute(Handle, 19, ref one, 4);
            int caption = 0x000000;                                 // COLORREF
            Native.DwmSetWindowAttribute(Handle, 35, ref caption, 4);
            int textCol = 0xE8E8E8;
            Native.DwmSetWindowAttribute(Handle, 36, ref textCol, 4);
            int border = 0x1A1A1A;
            Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);
        }
        catch { }
    }
}
