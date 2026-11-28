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
    List<LapSample> curS, refS;         // samples up to the lap line, nothing past it

    float[] pTime;                      // reference time at 0, 1/N, 2/N .. 1
    bool byProg;
    bool byDist;                    // aligned on the odometer, not on progress
    float[] curDistCut, refDistCut; // distances up to the lap line

    public float Delta;                 // seconds, + means slower than the reference
    public bool HasRef;
    public bool ByProg;                 // alignment uses track progress, not odometer
    public bool Approx;                 // ... and that progress came from distance, not the track
    public TrackOutline Outline;         // learned track, used when ACC gives no progress
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
        curS = null; refS = null;
        Approx = false;
        curDist = Distances(cur);
        refDist = Distances(refLap);
        HasRef = false;
        Why = "";
        gridDist = null;
        gridTime = null;
        Delta = 0f;

        if (cur == null) { Why = "no lap"; return; }
        if (curDist == null) { Why = "this lap has no position data"; return; }
        if (refLap == null || refDist == null || refLap == cur) { Why = "no other lap of this track yet"; return; }

        int cut;
        curProg = Progress(cur, out cut);
        curS = Cut(cur, cut);
        if (curProg == null) { curProg = ProgressFromOutline(cur, Outline, out cut); curS = Cut(cur, cut); }
        refProg = Progress(refLap, out cut);
        refS = Cut(refLap, cut);
        if (refProg == null) { refProg = ProgressFromOutline(refLap, Outline, out cut); refS = Cut(refLap, cut); }

        // Still nothing: project both laps onto the line the reference lap itself
        // drove.  That works even when this track has no saved map yet, and it keeps
        // both laps on exactly the same yardstick.
        if (curProg == null || refProg == null)
        {
            TrackOutline path = PathOf(refLap);
            if (path != null)
            {
                if (curProg == null) { curProg = ProgressFromOutline(cur, path, out cut); curS = Cut(cur, cut); }
                if (refProg == null) { refProg = ProgressFromOutline(refLap, path, out cut); refS = Cut(refLap, cut); }
            }
        }

        // Neither lap carried usable lap progress (ACC's normalized position reads
        // zero on some builds) and no track map was available either: aligning by
        // the fraction of the lap distance each lap had covered still gives a
        // usable curve, so do that instead of showing nothing at all.
        if (curProg == null || refProg == null)
        {
            if (curDist != null && refDist != null
                && curDist[curDist.Length - 1] > 100f && refDist[refDist.Length - 1] > 100f)
            {
                curProg = Normalised(curDist); curS = All(cur);
                refProg = Normalised(refDist); refS = All(refLap);
                Approx = true;
            }
        }

        byProg = curProg != null && refProg != null && curS != null && refS != null;
        pTime = null;
        byDist = false;
        curDistCut = null;
        refDistCut = null;

        // Distance is the smoother yardstick.  ACC's normalized position is quantized
        // to a ten-thousandth of a lap (about a metre here) and wobbles by a little
        // more, so two laps compared through it give two staircases and the delta
        // curve looks like a saw-tooth.  The odometers are continuous, so align on
        // distance whenever both laps cover the same lap.
        if (curDist != null && refDist != null)
        {
            int nc = curS != null ? curS.Count - 1 : curDist.Length - 1;
            int nr = refS != null ? refS.Count - 1 : refDist.Length - 1;
            curDistCut = Slice(curDist, nc);
            refDistCut = Slice(refDist, nr);
            float lc = curDistCut[curDistCut.Length - 1];
            float lr = refDistCut[refDistCut.Length - 1];
            if (lc > 100f && lr > 100f && Math.Abs(lc - lr) < 0.12f * Math.Max(lc, lr))
            {
                if (curS == null) curS = Cut(cur, nc);
                if (refS == null) refS = Cut(refLap, nr);
                if (curS != null && refS != null && curS[curS.Count - 1].T > 5f
                    && refS[refS.Count - 1].T > 5f)
                {
                    byDist = true;
                    byProg = false;
                    ByProg = false;
                    HasRef = true;
                    Approx = false;
                    return;
                }
                curDistCut = null;
                refDistCut = null;
            }
        }

        ByProg = byProg;
        if (byProg)
        {
            // Both runs must actually cover the lap, otherwise the lookup would keep
            // returning the other lap's first or last sample and the curve would ramp
            // or dive for no reason.
            float curEnd = curS[curS.Count - 1].T, refEnd = refS[refS.Count - 1].T;
            if (curEnd < 5f || refEnd < 5f || Math.Abs(curProg[curProg.Length - 1] - 1f) > 0.02f
                || Math.Abs(refProg[refProg.Length - 1] - 1f) > 0.02f)
            {
                byProg = false;
                HasRef = false;
                Why = "these laps cannot be aligned - drive one full flying lap";
                return;
            }
            HasRef = true;
            return;
        }

        Why = curProg == null && refProg == null
            ? "no lap progress for either lap - drive a full flying lap"
            : (curProg == null ? "no lap progress for this lap - drive a full flying lap"
                               : "the reference lap has no lap progress");
        return;
    }

    /// Mean change of the delta between points half a second apart: how jagged the
    /// curve is.  Used by the layout dump to check the smoothing.
    public float Roughness()
    {
        if (!HasRef || Cur == null) return 0f;
        float span = curS != null ? curS[curS.Count - 1].T : Cur.Span;
        if (span < 5f) return 0f;
        float sum = 0f, prev = 0f;
        int n = 0;
        bool have = false;
        for (float tt = 0f; tt <= span; tt += 0.5f)
        {
            float d = tt - RefTimeAt(DistanceAtTime(tt));
            if (have) { sum += Math.Abs(d - prev); n++; }
            prev = d; have = true;
        }
        return n > 0 ? sum / n : 0f;
    }

    /// Debug only: what the alignment ended up with (0 = current lap, 1 = reference).
    public string SampleInfo(int which)
    {
        List<LapSample> s = which == 0 ? curS : refS;
        float[] p = which == 0 ? curProg : refProg;
        if (s == null || p == null) return "none";
        return s.Count.ToString(Fmt.Inv) + " samples, prog " + p[0].ToString("0.000", Fmt.Inv)
               + "->" + p[p.Length - 1].ToString("0.000", Fmt.Inv)
               + ", t " + s[0].T.ToString("0.0", Fmt.Inv) + "->" + s[s.Count - 1].T.ToString("0.0", Fmt.Inv);
    }

    /// The first n+1 entries of an array (n may exceed the length).
    static float[] Slice(float[] a, int n)
    {
        if (a == null || a.Length == 0) return null;
        if (n < 0) n = 0;
        if (n > a.Length - 1) n = a.Length - 1;
        float[] r = new float[n + 1];
        Array.Copy(a, r, n + 1);
        return r;
    }

    /// The distance the current lap had covered at the lap line.
    float LastDistance()
    {
        if (byDist && curDistCut != null) return curDistCut[curDistCut.Length - 1];
        return 1f;
    }

    /// The samples up to the lap line; everything the game reported after the line
    /// belongs to the next lap and must not stretch this lap's own time span.
    static List<LapSample> Cut(LapData d, int cut)
    {
        if (d == null || d.Samples == null) return null;
        if (cut < 0 || cut >= d.Samples.Count) return null;
        return d.Samples.GetRange(0, cut + 1);
    }

    const int ProgSteps = 1000;         // 0.1 % of the lap per grid step

    /// ACC's normalized position per sample, or null when the lap predates it.
    static float[] Progress(LapData d, out int cut)
    {
        cut = -1;
        if (d == null || d.Samples == null || d.Samples.Count < 8) return null;
        float[] r = new float[d.Samples.Count];
        for (int i = 0; i < r.Length; i++) r[i] = d.Samples[i].S;
        return Finish(r, out cut, true);
    }

    /// Progress taken from the learned track outline: every sample is matched to
    /// the nearest outline point.  ACC's own normalized position is zero on this
    /// build often enough that the delta card must not depend on it.
    static float[] ProgressFromOutline(LapData d, TrackOutline o, out int cut)
    {
        cut = -1;
        if (d == null || d.Samples == null || d.Samples.Count < 8) return null;
        if (o == null || o.Points == null || o.Points.Count < 8 || o.LengthM < 200f) return null;
        int n = o.Points.Count;
        float[] r = new float[d.Samples.Count];

        int hint = 0;
        float best = float.MaxValue;
        for (int j = 0; j < n; j++)                       // find the first sample
        {
            float dx = o.Points[j].X - d.Samples[0].X, dz = o.Points[j].Y - d.Samples[0].Z;
            float m = dx * dx + dz * dz;
            if (m < best) { best = m; hint = j; }
        }

        for (int i = 0; i < r.Length; i++)
        {
            LapSample s = d.Samples[i];
            best = float.MaxValue;
            int at = hint;
            for (int j = hint - 80; j <= hint + 80; j++)  // the car only moves a little per sample
            {
                int k = ((j % n) + n) % n;
                float dx = o.Points[k].X - s.X, dz = o.Points[k].Y - s.Z;
                float m = dx * dx + dz * dz;
                if (m < best) { best = m; at = k; }
            }
            hint = at;
            r[i] = at / (float)n;
        }
        return Finish(r, out cut, true);
    }

    /// Unwraps the lap-line jump, clamps jitter and normalises the covered range to
    /// 0..1.  `cut` is the sample that sits on the line: samples after it belong to
    /// the next lap, and keeping them made the reference's time at the line 10 s too
    /// big (the delta curve then dived off the chart at the end of the lap).
    ///
    /// `allowEnd` handles the usual case where the recording simply stops just
    /// before the line - the lap file never contains the wrapped sample, so without
    /// it the whole card claimed "no track progress for this lap".
    static float[] Finish(float[] r, out int cut, bool allowEnd)
    {
        cut = -1;
        float prev = -1f, add = 0f;
        for (int i = 0; i < r.Length; i++)
        {
            float s0 = r[i];
            if (prev >= 0f && s0 < prev - 0.5f) add += 1f;   // lap line: 0.999 -> 0.001
            prev = s0;
            r[i] = s0 + add;
            if (r[i] >= 1f) { cut = i; break; }
        }
        if (cut < 0 && allowEnd) cut = r.Length - 1;         // the last sample is the line
        if (cut < 4) return null;                            // no lap line inside this lap

        // ACC's normalized position has four decimals (about a metre on a 7 km lap)
        // and wobbles a little; clamping it alone turns the signal into a staircase,
        // and two independent staircases make the delta curve saw-tooth instead of
        // the smooth line the game shows.  A short moving average first, then the
        // monotone clamp, gives a smooth position again.
        float[] sm = new float[cut + 1];
        for (int i = 0; i <= cut; i++)
        {
            float sum = 0f; int n = 0;
            for (int k = i - 2; k <= i + 2; k++)
            {
                if (k < 0 || k > cut) continue;
                sum += r[k]; n++;
            }
            sm[i] = n > 0 ? sum / n : r[i];
        }
        sm[0] = r[0];
        sm[cut] = r[cut];
        for (int i = 1; i <= cut; i++) if (sm[i] < sm[i - 1]) sm[i] = sm[i - 1];

        float baseS = sm[0], span = sm[cut] - baseS;
        if (span < 0.5f) return null;
        float[] o = new float[cut + 1];
        for (int i = 0; i <= cut; i++) o[i] = (sm[i] - baseS) / span;
        o[cut] = 1f;
        return o;
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

    /// A throw-away outline built from a lap's own positions, so that a lap can be
    /// projected onto the line another lap drove when no track map is available.
    static TrackOutline PathOf(LapData d)
    {
        if (d == null || d.Samples == null || d.Samples.Count < 8) return null;
        TrackOutline o = new TrackOutline();
        o.Track = d.Track;
        o.Closed = true;
        for (int i = 0; i < d.Samples.Count; i++)
        {
            LapSample s = d.Samples[i];
            o.Points.Add(new PointF(s.X, s.Z));
        }
        o.RecomputeLength();
        return o.LengthM >= 200f ? o : null;
    }

    /// Progress 0..1 out of a cumulative distance array.
    static float[] Normalised(float[] d)
    {
        float last = d[d.Length - 1];
        float[] r = new float[d.Length];
        for (int i = 0; i < d.Length; i++) r[i] = last > 1f ? d[i] / last : 0f;
        return r;
    }

    /// Every sample of a lap, used by the approximate (distance) alignment.
    static List<LapSample> All(LapData d)
    {
        if (d == null || d.Samples == null || d.Samples.Count == 0) return null;
        return d.Samples.GetRange(0, d.Samples.Count);
    }

    /// Time (seconds into the reference lap) at a distance.
    /// Time the reference lap needed to reach a given point of the lap, given as
    /// track progress (0..1).  Binary search over that lap's own progress array,
    /// so nothing has to be resampled and a bad grid cannot spoil the result.
    float RefTimeAtProg(float p)
    {
        if (Ref == null || refProg == null || refS == null || refProg.Length < 2) return 0f;
        int lo = 0, hi = refProg.Length - 1;
        if (p <= refProg[lo]) return refS[lo].T;
        if (p >= refProg[hi]) return refS[hi].T;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (refProg[mid] <= p) lo = mid; else hi = mid;
        }
        float s0 = refProg[lo], s1 = refProg[hi];
        float f = s1 > s0 ? (p - s0) / (s1 - s0) : 0f;
        return refS[lo].T + (refS[hi].T - refS[lo].T) * f;
    }

    float RefTimeAt(float dist)
    {
        if (byDist) return RefTimeAtDist(dist);
        if (byProg) return RefTimeAtProg(dist);
        if (gridTime == null || gridTime.Length == 0) return 0f;
        int i = (int)(dist / Step);
        if (i < 0) i = 0;
        if (i >= gridTime.Length - 1) return gridTime[gridTime.Length - 1] + (dist - gridDist[gridTime.Length - 1]) / 40f;
        float d0 = gridDist[i], d1 = gridDist[i + 1];
        float f = d1 > d0 ? (dist - d0) / (d1 - d0) : 0f;
        return gridTime[i] + (gridTime[i + 1] - gridTime[i]) * f;
    }

    /// Time the reference lap needed to cover a distance (odometer alignment).
    float RefTimeAtDist(float dist)
    {
        if (Ref == null || refS == null || refDistCut == null || refDistCut.Length < 2) return 0f;
        int lo = 0, hi = refDistCut.Length - 1;
        if (dist <= refDistCut[lo]) return refS[lo].T;
        if (dist >= refDistCut[hi]) return refS[hi].T;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (refDistCut[mid] <= dist) lo = mid; else hi = mid;
        }
        float d0 = refDistCut[lo], d1 = refDistCut[hi];
        float f = d1 > d0 ? (dist - d0) / (d1 - d0) : 0f;
        return refS[lo].T + (refS[hi].T - refS[lo].T) * f;
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
        float end = curS != null ? curS[curS.Count - 1].T : Cur.Span;
        if (t > end) t = end;
        Delta = t - RefTimeAt(DistanceAtTime(t));
        // at the line the delta must read exactly the lap time difference
        if (Cur.LapMs > 1000f && Ref.LapMs > 1000f && t >= end - 0.12f)
            Delta = end - RefTimeAt(LastDistance());
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
        int n = byProg ? curS.Count : Cur.Samples.Count;
        for (int i = 0; i < n; i++)
        {
            float at = byProg ? curProg[i] : curDist[Math.Min(i, curDist.Length - 1)];
            float tt = byProg ? curS[i].T : Cur.Samples[i].T;
            float dd = Math.Abs(tt - RefTimeAt(at));
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
            lapDiff = (byProg && curS != null ? curS[curS.Count - 1].T : Cur.Span)
                      - (byProg && refS != null ? refS[refS.Count - 1].T : Ref.Span);
            if (Math.Abs(lapDiff) > maxAbs) maxAbs = (float)Math.Ceiling(Math.Abs(lapDiff) * 2f) / 2f;
        }
        float span = Math.Max(0.1f, byProg && curS != null ? curS[curS.Count - 1].T : Math.Min(Cur.Span, Cur.LastT > 0.5f ? Cur.LastT : Cur.Span));
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

        if (Approx)
            G.TextMidR(g, "aligned by distance", F.CapS, Color.FromArgb(110, 110, 110),
                       new Rectangle(plot.X, plot.Bottom - 14, plot.Width - 2, 14), 0f);
    }

    /// Distance travelled at a lap time; linear between samples.
    float DistanceAtTime(float t)
    {
        if (byDist && curS != null && curDistCut != null)
        {
            List<LapSample> cs = curS;
            if (t <= cs[0].T) return curDistCut[0];
            int hi0 = cs.Count - 1;
            if (t >= cs[hi0].T) return curDistCut[hi0];
            int lo0 = 0, hi1 = hi0;
            while (hi1 - lo0 > 1)
            {
                int mid = (lo0 + hi1) / 2;
                if (cs[mid].T <= t) lo0 = mid; else hi1 = mid;
            }
            float sp = cs[hi1].T - cs[lo0].T;
            float u = sp > 0.0001f ? (t - cs[lo0].T) / sp : 0f;
            return curDistCut[lo0] + (curDistCut[hi1] - curDistCut[lo0]) * u;
        }
        if (byProg)
        {
            List<LapSample> cs = curS;
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