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
        bool revRow = review != null;

        // dial stays a sane size, the pedals and the G-G disc share what is left,
        // and gear + rpm keep a readable strip at the bottom
        int stripH = Math.Max(62, Math.Min(112, rh / 9));
        int gaugeH = Math.Max(96, mapH);          // same height as the delta card / track map
        int afterGauge = rh - 18 - gaugeH - stripH;
        int pedH = afterGauge / 2;
        int ggH = afterGauge - pedH;

        gauge.Bounds = new Rectangle(rx, ry, RightW, gaugeH);
        pedals.Bounds = new Rectangle(rx, ry + gaugeH + 6, RightW, pedH);
        gg.Bounds = new Rectangle(rx, ry + gaugeH + pedH + 12, RightW, ggH);
        int gy = ry + rh - stripH;
        gearRead.Bounds = new Rectangle(rx, gy, RightW, stripH);
        rpmRead.Visible = false;

        // ---- centre column
        int cx = x + LeftW + 10;
        int cw = rx - 10 - cx;
        if (cw < 100) cw = 100;
        int ch = rh;
        int scrubH = review != null ? 56 : 0;

        // the video card sits on top of both charts, left aligned, and keeps the
        // exact size of the track map.  The space to its right stays empty for now.
        // video + delta only exist while reviewing a lap; live gets the whole
        // centre column for the charts
        int vh = revRow ? Math.Max(96, mapH) : 0;
        int dh = revRow ? vh + 46 : 0;                    // the row is as tall as the delta card
        vh = dh;                                          // ... and the video grows with it, keeping 16:9
        int vw = revRow ? Math.Max(160, (int)Math.Round(vh * 16.0 / 9.0)) : 0;
        if (vw > cw - 170) vw = Math.Max(160, cw - 170);   // never squeeze the delta card away
        if (vw > cw) vw = cw;
        video.Visible = revRow;
        deltaPane.Visible = revRow;
        if (revRow)
        {
            video.Bounds = new Rectangle(cx, ry, vw, vh);
            deltaPane.Bounds = new Rectangle(cx + vw + 6, ry, Math.Max(120, cw - vw - 6), dh);
        }

        int rowH = Math.Max(vh, dh);
        int top = ry + rowH + (revRow ? 6 : 0);
        int th = (ch - rowH - (revRow ? 12 : 6) - scrubH) / 2;
        if (th < 70) th = 70;
        trPedals.Bounds = new Rectangle(cx, top, cw, th);
        trSteerFf.Bounds = new Rectangle(cx, top + th + 6, cw, Math.Max(60, ch - rowH - (revRow ? 12 : 6) - th - scrubH));
        if (scrub != null)
        {
            scrub.Visible = review != null;
            if (review != null) scrub.Bounds = new Rectangle(cx, ry + ch - 50, cw, 50);
        }

        // fonts that depend on card height
        gearRead.ValPt = Math.Max(16f, Math.Min(52f, gearRead.Height * 0.56f));
        rpmRead.ValPt = Math.Max(14f, Math.Min(44f, rpmRead.Height * 0.50f));
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
