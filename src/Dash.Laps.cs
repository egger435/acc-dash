// ===========================================================================
//  AccDash - lap list, recording and review
//  src/Dash.Laps.cs
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
    // ------------------------------------------------------------------ laps
    void OpenLaps()
    {
        using (LapBrowser b = new LapBrowser(laps))
        {
            DialogResult r = b.ShowDialog(this);
            if (r == DialogResult.OK && b.Chosen != null) EnterReview(b.Chosen);
        }
        ActiveControl = null;
    }

    public void OpenLapFile(string file)
    {
        try
        {
            string err;
            LapData d = LapData.Parse(File.ReadAllText(file), file, out err);
            if (d == null)
            {
                MessageBox.Show(this, "Could not read " + Path.GetFileName(file) + ":\n" + err, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            laps.Add(d);
            EnterReview(d);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not read " + Path.GetFileName(file) + ":\n" + ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    void EnterReview(LapData d)
    {
        if (d == null || d.Samples.Count < 2) return;
        review = d;
        reviewPos = 0;
        revPlaying = false;
        revSpeed = 1.0;
        reviewOutline = BuildLapOutline(d);
        scrub.Max = d.Span;
        scrub.Pos = 0;
        scrub.Title = "LAP " + d.LapNo.ToString(Fmt.Inv) + "   " + Fmt.Mmss((int)d.LapMs)
            + (string.IsNullOrEmpty(d.Track) ? "" : "   " + d.Track)
            + (d.FromFile ? "   [imported]" : "");
        scrub.Visible = true;
        btnPlay.Visible = true;
        btnLive.Visible = true;
        gg.ResetPeaks = true;           // peak g belongs to the lap, not to the live session
        UpdatePlayLabel();
        LayoutAll();
        FillReviewSnap();
        Render(Clock.Now);
    }

    void UpdatePlayLabel()
    {
        if (btnPlay == null) return;
        string t = "PLAY";
        if (review != null)
        {
            if (holdFired) t = ">> 8x";
            else if (revPlaying) t = "PAUSE";
        }
        if (btnPlay.Text != t) btnPlay.Text = t;
    }

    void ExitReview()
    {
        review = null;
        reviewOutline = null;
        revPlaying = false;
        revSpeed = 1.0;
        playHeld = false;
        holdFired = false;
        if (btnPlay != null) { btnPlay.Visible = false; btnLive.Visible = false; UpdatePlayLabel(); }
        scrub.Visible = false;
        ClearTraces();
        LayoutAll();
        ActiveControl = null;
    }

    void FillReviewSnap()
    {
        if (review == null) return;
        LapSample s = review.At((float)reviewPos);
        if (s == null) return;
        sn.Speed = s.Speed;
        sn.Rpm = s.Rpm;
        sn.Gear = s.Gear;
        sn.SteerNorm = s.Steer;
        sn.Gas = s.Gas;
        sn.Brake = s.Brake;
        sn.LatG = s.Lat;
        sn.LongG = s.Long;
        sn.FinalFf = s.Ff;
        sn.Fuel = s.Fuel;
        sn.TyreF = s.TyreF;
        sn.TyreR = s.TyreR;
        sn.TyreAvg = (s.TyreF + s.TyreR) * 0.5f;
        sn.WorldX = s.X;
        sn.WorldZ = s.Z;
        sn.Head = s.Head;
        sn.Track = review.Track;
        sn.Car = review.Car;
        sn.MaxRpm = review.MaxRpm > 500 ? review.MaxRpm : 8000;
        sn.AirTemp = review.AirTemp;
        sn.RoadTemp = review.RoadTemp;
        sn.TrackGrip = review.Grip;
        sn.CurMs = (int)(reviewPos * 1000.0);
        sn.CompletedLaps = Math.Max(1, review.LapNo);   // the lap being replayed
        sn.TotalLaps = 0;
        sn.LastMs = (int)review.LapMs;
        sn.BestMs = 0;
        sn.DeltaMs = 0;
        sn.DeltaPositive = false;
        sn.Status = 2;
        sn.Session = -1;
    }

    void RecordLap(double now)
    {
        if ((!shmOpen && !recordTest && !FakeLive) || review != null) return;
        float cur = sn.CurMs / 1000f;
        if (cur <= 0.05f) return;
        int lapNo = sn.CompletedLaps + 1;

        // a new lap starts when the counter moves on or the clock jumps back
        if (recLap == null || lapNo != recLapNo || cur + 1.5f < recLastT)
        {
            CloseLap();
            recLap = new LapData();
            recLap.Track = sn.Track;
            recLap.Car = sn.Car;
            recLap.LapNo = lapNo;
            recLap.MaxRpm = sn.MaxRpm;
            recLap.AirTemp = sn.AirTemp;
            recLap.RoadTemp = sn.RoadTemp;
            recLap.Grip = sn.TrackGrip;
            recLapNo = lapNo;
            recLastT = -1f;
            recLastAdd = -1e9;
        }

        if (now - recLastAdd < 66.0) return;       // ~15 Hz, same as the charts
        recLastAdd = now;
        recLap.Add(cur, sn.Gas, sn.Brake, sn.SteerNorm, sn.FinalFf, sn.Speed, sn.Gear, (int)sn.Rpm, sn.LatG, sn.LongG, sn.Fuel, sn.TyreF, sn.TyreR, sn.WorldX, sn.WorldZ, sn.Head);
        recLastT = cur;
    }

    /// Feeds a synthetic stream through the recorder without touching ACC, so the
    /// record -> close -> file path can be checked offline.
    public void RecordTest(string outFile, double seconds)
    {
        recordTest = true;
        sn = new Snap();
        sn.MaxRpm = 7700;
        sn.Track = "Spa";
        sn.Car = "mclaren_720s_gt3_evo";
        sn.CompletedLaps = 0;
        sn.NumCars = 1;
        double now = Clock.Now;
        int steps = (int)(seconds * 30);
        for (int i = 0; i < steps; i++)
        {
            double t = i / 30.0;
            now += 33;
            sn.CurMs = (int)(t * 1000);
            sn.LastMs = 0;
            sn.Gas = (float)(0.5 + 0.5 * Math.Sin(t * 0.9));
            sn.Brake = (float)Math.Max(0, Math.Sin(t * 0.9 + 3.0));
            sn.SteerNorm = (float)(0.6 * Math.Sin(t * 0.45));
            sn.FinalFf = (float)(0.5 * Math.Sin(t * 1.3));
            sn.Speed = (float)(160 + 60 * Math.Sin(t * 0.3));
            sn.Gear = 4;
            sn.Rpm = (float)(6000 + 800 * Math.Sin(t * 0.7));
            sn.LatG = (float)(1.3 * Math.Sin(t * 0.45));
            sn.LongG = 1.15f * sn.Brake - 0.55f * sn.Gas;   // braking positive, as in live data
            sn.Fuel = (float)(30 - t * 0.05);
            sn.TyreF = (float)(80 + 5 * Math.Sin(t * 0.1));
            sn.TyreR = sn.TyreF - 2;

            // a lap-shaped racing line, so exported test files carry x/z/heading
            sn.WorldX = (float)(300.0 * Math.Cos(t * 0.28));
            sn.WorldZ = (float)(190.0 * Math.Sin(t * 0.28));
            sn.Head = (float)((t * 0.28 + Math.PI * 0.5) % (Math.PI * 2.0));
            RecordLap(now);
        }
        CloseLap();
        File.WriteAllText(outFile, laps.Count > 0 ? laps[laps.Count - 1].ToText() : "# no laps", new System.Text.UTF8Encoding(false));
    }

    void CloseLap()
    {
        if (recLap == null) return;
        if (recLap.Samples.Count >= 20)
        {
            recLap.LapMs = sn.LastMs > 500 ? sn.LastMs : recLastT * 1000f;
            laps.Add(recLap);
        }
        recLap = null;
        recLapNo = -1;
    }
    /// The racing line the reviewed lap drove, decimated to a drawable polyline.
    static TrackOutline BuildLapOutline(LapData d)
    {
        if (d == null || d.Samples.Count < 8) return null;
        float sum = 0f;
        for (int i = 0; i < d.Samples.Count; i++) sum += Math.Abs(d.Samples[i].X) + Math.Abs(d.Samples[i].Z);
        if (sum < 1f) return null;                 // lap file without positions

        TrackOutline o = new TrackOutline();
        o.Track = d.Track;
        o.Closed = true;
        int step = Math.Max(1, d.Samples.Count / 1500);
        for (int i = 0; i < d.Samples.Count; i += step) o.AddPoint(d.Samples[i].X, d.Samples[i].Z);
        return o.Points.Count >= 8 ? o : null;
    }
}
