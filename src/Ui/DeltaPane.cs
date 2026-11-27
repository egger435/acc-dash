//  DeltaPane - the card right of the video player.
//
//  It answers one question: on this lap, where am I gaining and where am I
//  losing time against the best lap of the same track that this session knows
//  about.  The reference lap is picked from the lap list (recorded or imported),
//  never from the lap file itself, so the curve is empty until there are at
//  least two laps of the same track.
//
//  Both laps are compared at equal distance, not at equal time: for every point
//  of the reference lap we look up when the current lap got there, and the
//  difference is the delta.  That makes the curve independent of where the
//  driver was faster.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

sealed class DeltaPane : Pane
{
    const int Step = 2;                 // reference lap resampled every 2 m

    public LapData Cur, Ref;
    public double Pos;

    float[] curDist;                    // cumulative distance of each sample, metres
    float[] refDist;
    float[] gridDist, gridTime;         // reference lap resampled on a fixed grid

    // ACC gives the car's position along the track (0..1).  When both laps have
    // it we align by that instead of by driven distance, so a wider line on one
    // lap does not shift the comparison.
    float[] curProg, refProg;

    float[] pTime;                      // reference time at 0, 1/N, 2/N .. 1
    bool byProg;

    public float Delta;                 // seconds, + means slower than the reference
    public bool HasRef;
    public bool Manual;                       // reference was picked by hand, not the fastest
    public event EventHandler PickRef;        // "compare..." clicked
    public event EventHandler UseAuto;        // "auto" clicked
    Rectangle autoRect;
    public string Why = "";             // why there is no delta

    public DeltaPane()
    {
        BackColor = Pal.Panel;
        Cursor = Cursors.Default;
    }

    Rectangle pickRect;
    bool pickHot;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hot = pickRect.Contains(e.Location);
        if (hot != pickHot)
        {
            pickHot = hot;
            Cursor = hot ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    public void Set(LapData cur, LapData refLap)
    {
        Cur = cur;
        Ref = refLap;
        curDist = Distances(cur);
        refDist = Distances(refLap);
        HasRef = false;
        Why = "";
        gridDist = null;
        gridTime = null;
        Delta = 0f;

        if (cur == null) { Why = "no lap"; return; }
        if (curDist == null) { Why = "this lap has no position data"; return; }
        if (refLap == null || refDist == null) { Why = "no other lap of this track yet"; return; }

        curProg = Progress(cur);
        refProg = Progress(refLap);
        byProg = curProg != null && refProg != null;
        pTime = null;
        if (byProg)
        {
            int np = ProgSteps + 1;
            pTime = new float[np];
            int pj = 0;
            for (int i = 0; i < np; i++)
            {
                float want = i / (float)ProgSteps;
                while (pj < refProg.Length - 2 && refProg[pj + 1] < want) pj++;
                float s0 = refProg[pj], s1 = refProg[pj + 1];
                float t0 = Ref.Samples[pj].T, t1 = Ref.Samples[pj + 1].T;
                float f = s1 > s0 ? (want - s0) / (s1 - s0) : 0f;
                pTime[i] = t0 + (t1 - t0) * f;
            }
            HasRef = true;
            return;
        }

        // no progress data (old files): fall back to distance, resampled on a
        // grid so a lookup is a binary search
        float total = refDist[refDist.Length - 1];
        int n = (int)(total / Step) + 2;
        gridDist = new float[n];
        gridTime = new float[n];
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            float want = i * Step;
            while (j < refDist.Length - 2 && refDist[j + 1] < want) j++;
            float d0 = refDist[j], d1 = refDist[j + 1];
            float t0 = Ref.Samples[j].T, t1 = Ref.Samples[j + 1].T;
            float f = d1 > d0 ? (want - d0) / (d1 - d0) : 0f;
            gridDist[i] = want;
            gridTime[i] = t0 + (t1 - t0) * f;
        }
        float totCur = curDist[curDist.Length - 1];
        float totRef = refDist[refDist.Length - 1];
        bool partial = Cur.LapMs < 1000f;               // lap in progress: shorter is expected
        if (totRef < 100f || totCur > 1.15f * totRef || (!partial && totCur < 0.85f * totRef))
        {
            Why = "these two laps do not match (" + (totCur / 1000f).ToString("0.00", Fmt.Inv)
                + " km vs " + (totRef / 1000f).ToString("0.00", Fmt.Inv) + " km)";
            return;
        }
        if (Ref.LapMs > 1000f && Cur.LapMs > 1000f)
        {
            float ratio = Cur.LapMs / Ref.LapMs;
            if (ratio < 0.7f || ratio > 1.4f)
            {
                Why = "lap times are too far apart to compare";
                return;
            }
        }
        HasRef = true;

    }

    const int ProgSteps = 1000;         // 0.1 % of the lap per grid step

    /// ACC's normalized position per sample, or null when the lap predates it.
    static float[] Progress(LapData d)
    {
        if (d == null || d.Samples == null || d.Samples.Count < 8) return null;
        float[] r = new float[d.Samples.Count];
        float max = 0f, prev = -1f, add = 0f;
        for (int i = 0; i < r.Length; i++)
        {
            float s0 = d.Samples[i].S;
            if (prev >= 0f && s0 < prev - 0.5f) add += 1f;   // lap line: 0.999 -> 0.001
            prev = s0;
            r[i] = s0 + add;
            if (r[i] > max) max = r[i];
        }
        float baseS = r[0], span = max - baseS;
        if (max - baseS < 0.9f) return null;                 // never reached the end of the lap
        if (span > 1.0001f)
            for (int i = 0; i < r.Length; i++) r[i] = (r[i] - baseS) / span;
        return r;
    }

    /// Cumulative distance of every sample, or null when the lap has no
    /// position at all (old files written before x/z were recorded).
    static float[] Distances(LapData d)
    {
        if (d == null || d.Samples == null || d.Samples.Count < 8) return null;
        float[] r = new float[d.Samples.Count];
        float sum = 0f;
        bool any = false;
        for (int i = 1; i < r.Length; i++)
        {
            LapSample a = d.Samples[i - 1], b = d.Samples[i];
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float step = (float)Math.Sqrt(dx * dx + dz * dz);
            if (step > 0.01f && step < 50f) { sum += step; any = true; }
            r[i] = sum;
        }
        if (!any || sum < 100f) return null;
        return r;
    }

    /// Time (seconds into the reference lap) at a distance.
    float RefTimeAt(float dist)
    {
        if (byProg)
        {
            float x = dist * ProgSteps;
            if (x <= 0f) return pTime[0];
            int pi = (int)x;
            if (pi >= ProgSteps) return pTime[ProgSteps];
            return pTime[pi] + (pTime[pi + 1] - pTime[pi]) * (x - pi);
        }
        if (gridTime == null || gridTime.Length == 0) return 0f;
        int i = (int)(dist / Step);
        if (i < 0) i = 0;
        if (i >= gridTime.Length - 1) return gridTime[gridTime.Length - 1] + (dist - gridDist[gridTime.Length - 1]) / 40f;
        float d0 = gridDist[i], d1 = gridDist[i + 1];
        float f = d1 > d0 ? (dist - d0) / (d1 - d0) : 0f;
        return gridTime[i] + (gridTime[i + 1] - gridTime[i]) * f;
    }

    /// Time (seconds into the current lap) when it reached a distance.
    float CurTimeAt(float dist)
    {
        if (curDist == null) return 0f;
        int lo = 0, hi = curDist.Length - 1;
        if (dist <= curDist[0]) return Cur.Samples[0].T;
        if (dist >= curDist[hi]) return Cur.Samples[hi].T;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (curDist[mid] < dist) lo = mid; else hi = mid;
        }
        float d0 = curDist[lo], d1 = curDist[hi];
        float t0 = Cur.Samples[lo].T, t1 = Cur.Samples[hi].T;
        float f = d1 > d0 ? (dist - d0) / (d1 - d0) : 0f;
        return t0 + (t1 - t0) * f;
    }

    /// Delta at the play head: how much later (or earlier) this lap reached the
    /// play head's distance than the reference did.
    public void UpdateCursor()
    {
        if (!HasRef || Cur == null) { Delta = 0f; return; }
        float t = (float)Pos;
        if (t < 0f) t = 0f;
        if (t > Cur.Span) t = Cur.Span;
        Delta = t - RefTimeAt(DistanceAtTime(t));
        // at the line the delta must read exactly the lap time difference
        if (Cur.LapMs > 1000f && Ref.LapMs > 1000f && t >= Cur.Span - 0.12f)
            Delta = Cur.Span - Ref.Span;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (pickRect.Contains(e.Location) && PickRef != null) { PickRef(this, EventArgs.Empty); return; }
        if (autoRect.Contains(e.Location) && UseAuto != null) UseAuto(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        G.Box(g, ClientRectangle, BackColor, Pal.Line);

        Rectangle r = ClientRectangle;
        int pad = 10;
        int top = r.Y + 6;

        // title + reference
        G.TextMidL(g, "DELTA", F.Cap, Pal.Dim, new Rectangle(r.X + pad, top, 56, 20), 0f);
        // "COMPARE" is plain text like the rest of the card; hover turns it bright
        string cmp = "COMPARE";
        SizeF csz = g.MeasureString(cmp, F.CapS);
        float cxr = r.Right - pad - csz.Width;

        // the reference label only gets whatever is left between the title and
        // COMPARE; on a narrow card it drops the time, then it disappears
        int lx = r.X + pad + 50;
        int lw = (int)cxr - 8 - lx;
        if (lw >= 70)
        {
            string rightTxt = Ref != null ? "vs L" + Ref.LapNo.ToString(Fmt.Inv) + "  " + Fmt.Mmss((int)Ref.LapMs) : "no reference";
            if (Ref != null && lw < 130) rightTxt = "vs L" + Ref.LapNo.ToString(Fmt.Inv);
            G.TextMidL(g, rightTxt, F.CapS, HasRef ? Pal.Text : Pal.Dim, new Rectangle(lx, top, lw, 20), 0f);
        }
        G.Text(g, cmp, F.CapS, pickHot ? Pal.Text : Pal.Dim, cxr, top + 3);
        pickRect = new Rectangle((int)cxr - 6, top, (int)csz.Width + 12, 20);

        // "auto" link, only after a lap was picked by hand
        if (Manual && Ref != null)
        {
            string au = "auto";
            SizeF asz = g.MeasureString(au, F.CapS);
            float ax = cxr - 14 - asz.Width;
            G.Text(g, au, F.CapS, Pal.Amber, ax, top + 3);
            autoRect = new Rectangle((int)ax - 6, top, (int)asz.Width + 12, 20);
        }
        else autoRect = Rectangle.Empty;
        top += 30;

        if (Cur == null || !HasRef)
        {
            G.TextMidC(g, Why, F.Small, Color.FromArgb(120, 120, 120), new Rectangle(r.X, top, r.Width, r.Height - top));
            return;
        }

        top += 6;

        try { DrawCurve(g, r, top, pad); } catch (Exception ex)
        { try { File.AppendAllText("paint-err.txt", ex.ToString() + "  size=" + r + " top=" + top + Environment.NewLine); } catch { } }
    }

    void DrawCurve(Graphics g, Rectangle r, int top, int pad)
    {
        Rectangle plot = new Rectangle(r.X + pad + 30, top, r.Width - pad * 2 - 34, r.Height - top - pad - 2);
        if (plot.Height < 24 || plot.Width < 40) return;

        float maxAbs = 0.5f;
        for (int i = 0; i < Cur.Samples.Count; i++)
        {
            float at = byProg ? curProg[i] : curDist[Math.Min(i, curDist.Length - 1)];
            float dd = Math.Abs(Cur.Samples[i].T - RefTimeAt(at));
            if (dd > maxAbs) maxAbs = dd;
        }
        maxAbs = (float)Math.Ceiling(maxAbs * 2f) / 2f;

        G.Box(g, plot, BackColor, Pal.Line);
        // zero line, dead centre so gains and losses get the same room
        int zero = plot.Y + plot.Height / 2;
        using (Pen axis = new Pen(Pal.Line, 1f)) g.DrawLine(axis, plot.X, zero, plot.Right, zero);

        float lapDiff = 0f;
        bool atLine = Cur.LapMs > 1000f && Ref.LapMs > 1000f;
        if (atLine)
        {
            lapDiff = Cur.Span - Ref.Span;
            if (Math.Abs(lapDiff) > maxAbs) maxAbs = (float)Math.Ceiling(Math.Abs(lapDiff) * 2f) / 2f;
        }
        float span = Math.Max(0.1f, Math.Min(Cur.Span, Cur.LastT > 0.5f ? Cur.LastT : Cur.Span));
        PointF prev = PointF.Empty;
        bool hasPrev = false;
        using (Pen pen = new Pen(Pal.Amber, 2.8f))
        {
            for (float x = 0; x <= plot.Width; x += 1f)
            {
                float t = x / plot.Width * span;
                float dist = DistanceAtTime(t);
                float dd = t - RefTimeAt(dist);
                float yy = zero - dd / maxAbs * (plot.Height * 0.46f);
                if (yy < plot.Y + 1) yy = plot.Y + 1;
                if (yy > plot.Bottom - 1) yy = plot.Bottom - 1;
                PointF p = new PointF(plot.X + x, yy);
                if (hasPrev) g.DrawLine(pen, prev, p);
                prev = p;
                hasPrev = true;
            }
            // the line ends exactly on the lap time difference, like ACC's counter
            if (atLine && hasPrev)
            {
                float yy = zero - lapDiff / maxAbs * (plot.Height * 0.46f);
                if (yy < plot.Y + 1) yy = plot.Y + 1;
                if (yy > plot.Bottom - 1) yy = plot.Bottom - 1;
                g.DrawLine(pen, prev, new PointF(plot.Right, yy));
            }
        }

        // axis labels
        G.TextMidR(g, "+" + maxAbs.ToString("0.0", Fmt.Inv), F.CapS, Pal.Dim, new Rectangle(r.X + 2, plot.Y - 2, 28, 14), 0f);
        G.TextMidR(g, "-" + maxAbs.ToString("0.0", Fmt.Inv), F.CapS, Pal.Dim, new Rectangle(r.X + 2, plot.Bottom - 14, 28, 14), 0f);
        G.TextMidR(g, "0", F.CapS, Pal.Dim, new Rectangle(r.X + 2, zero - 7, 28, 14), 0f);

        // play head
        float px2 = plot.X + (float)(Pos / span) * plot.Width;
        if (px2 >= plot.X && px2 <= plot.Right)
        {
            using (Pen pen = new Pen(Pal.Text, 1f)) g.DrawLine(pen, px2, plot.Y, px2, plot.Bottom);
            string dtxt = (Delta >= 0 ? "+" : "") + Delta.ToString("0.000", Fmt.Inv);
            Color dc = Math.Abs(Delta) < 0.005f ? Pal.Dim : (Delta < 0 ? Pal.Green : Pal.Red);
            // sit next to the curve, not against the frame
            int ly = (int)(zero - Delta / maxAbs * (plot.Height * 0.46f)) - 18;
            if (ly < plot.Y + 2) ly = plot.Y + 2;
            if (ly > plot.Bottom - 18) ly = plot.Bottom - 18;
            int lx = (int)px2 + 4;
            if (lx > plot.Right - 74) lx = (int)px2 - 74;
            G.TextMidL(g, dtxt, F.Cap, dc, new Rectangle(lx, ly, 70, 16), 0f);
        }
    }

    /// Distance travelled at a lap time; linear between samples.
    float DistanceAtTime(float t)
    {
        if (byProg)
        {
            List<LapSample> cs = Cur.Samples;
            if (t <= cs[0].T) return curProg[0];
            int hi0 = cs.Count - 1;
            if (t >= cs[hi0].T) return curProg[hi0];
            int lo0 = 0, hi1 = hi0;
            while (hi1 - lo0 > 1)
            {
                int mid = (lo0 + hi1) / 2;
                if (cs[mid].T <= t) lo0 = mid; else hi1 = mid;
            }
            float sp = cs[hi1].T - cs[lo0].T;
            float u = sp > 0.0001f ? (t - cs[lo0].T) / sp : 0f;
            return curProg[lo0] + (curProg[hi1] - curProg[lo0]) * u;
        }
        List<LapSample> s = Cur.Samples;
        if (t <= s[0].T) return 0f;
        if (t >= s[s.Count - 1].T) return curDist[curDist.Length - 1];
        int lo = 0, hi = s.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (s[mid].T < t) lo = mid; else hi = mid;
        }
        float t0 = s[lo].T, t1 = s[hi].T;
        float f = t1 > t0 ? (t - t0) / (t1 - t0) : 0f;
        return curDist[lo] + (curDist[hi] - curDist[lo]) * f;
    }
}