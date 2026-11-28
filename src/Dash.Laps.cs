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
            LapData d = LapData.Parse(File.ReadAllBytes(file), file, out err);
            if (d == null)
            {
                MessageBox.Show(this, "Could not read " + Path.GetFileName(file) + ":\n" + err, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (AddLap(d)) EnterReview(d);
            else MessageBox.Show(this, "That lap is already in the list: " + d.Name, "Import",
                                 MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not read " + Path.GetFileName(file) + ":\n" + ex.Message, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    /// Adds a lap unless an identical one is already known.  Importing a file
    /// that this session recorded itself used to give two rows for one lap.
    bool AddLap(LapData d)
    {
        foreach (LapData x in laps)
        {
            if (ReferenceEquals(x, d)) return false;
            string t0 = x.Track == null ? "" : x.Track.Trim().ToLowerInvariant();
            string t1 = d.Track == null ? "" : d.Track.Trim().ToLowerInvariant();
            if (t0 != t1) continue;
            if (x.LapNo != d.LapNo) continue;
            if (Math.Abs(x.LapMs - d.LapMs) > 5f) continue;
            return false;
        }
        laps.Add(d);
        return true;
    }

    void EnterReview(LapData d)
    {
        if (d == null || d.Samples.Count < 2) return;
        review = d;
        reviewPos = 0;
        revPlaying = false;
        revSpeed = 1.0;
        reviewOutline = BuildLapOutline(d);
        deltaPane.Outline = outline;
        refLap = FindReference(d);
        if (refLap != null && NoName(d)) d.Track = refLap.Track;   // no name on this lap: borrow the reference's
        deltaPane.Manual = false;
        deltaPane.Set(d, refLap);
        deltaPane.Visible = true;
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

    /// Delta card while driving: the lap in progress against the best lap of
    /// this track, rebuilt four times a second (cheap, but not per frame).
    void UpdateLiveDelta(double now)
    {
        if (review != null) return;                 // review drives the card itself
        if (now - liveDeltaAt < 400) return;
        liveDeltaAt = now;
        if (recLap == null || recLap.Samples.Count < 12)
        {
            deltaPane.Set(null, null);
            deltaPane.Why = "waiting for the first lap";
            deltaPane.Invalidate();
            return;
        }
        refLap = FindReference(recLap);
        deltaPane.Set(recLap, refLap);
        deltaPane.Pos = recLap.LastT;
        deltaPane.UpdateCursor();
        deltaPane.Invalidate();
    }

    /// The best lap of the same track that this session knows about: recorded
    /// laps and imported files both live in `laps`, the lap file itself carries
    /// no notion of a best time.
    void UseAutoReference()
    {
        if (review == null) return;
        refLap = FindReference(review);
        deltaPane.Manual = false;
        deltaPane.Set(review, refLap);
        deltaPane.Invalidate();
    }

    void PickCompareLap()
    {
        if (review == null) return;

        List<LapData> pick = new List<LapData>();
        foreach (LapData d in laps)
        {
            if (d == review) continue;
            if (d.LapNo == review.LapNo && Math.Abs(d.LapMs - review.LapMs) < 5f) continue;
            pick.Add(d);
        }
        if (pick.Count == 0)
        {
            MessageBox.Show(this,
                            "There is no other lap to compare with yet.\n\n" +
                            "Record another lap, or add a lap with LAPS > IMPORT...",
                            "Compare", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        LapData chosen;
        using (LapPick box = new LapPick(pick, review, refLap))
        {
            if (box.ShowDialog(this) != DialogResult.OK) return;
            chosen = box.Chosen;
        }
        if (chosen == null) return;

        if (!LapData.SameTrack(chosen.Track, review.Track))
        {
            MessageBox.Show(this,
                            "That lap is from a different track.\n\n" +
                            "This lap: " + review.Track + "\n" +
                            "Picked lap: " + chosen.Track + "\n\n" +
                            "You can only compare laps from the same track.",
                            "Different track", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        refLap = chosen;
        deltaPane.Manual = true;
        deltaPane.Set(review, refLap);
        deltaPane.Invalidate();
    }

    LapData FindReference(LapData cur)
    {
        LapData best = null;
        string key = cur.Track == null ? "" : cur.Track.Trim().ToLowerInvariant();
        if (key == "unknown" || key == "-") key = "";
        foreach (LapData d in laps)
        {
            if (ReferenceEquals(d, cur) || d.Samples == null || d.Samples.Count < 8) continue;
            if (d.LapMs < 1000f) continue;
            string k = d.Track == null ? "" : d.Track.Trim().ToLowerInvariant();
            if (k == "unknown" || k == "-") k = "";
            bool known = k.Length > 0 && key.Length > 0;
            if (known)
            {
                if (k != key) continue;
            }
            else
            {
                // one of the two laps never learned the track name (the static page
                // was still empty when it started): fall back to a lap time sanity
                // check so laps from very different tracks do not pair up
                float ratio = d.LapMs / Math.Max(1f, cur.LapMs);
                if (ratio < 0.6f || ratio > 1.6f) continue;
            }
            if (best == null || d.LapMs < best.LapMs) best = d;
        }
        return best;
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
        refLap = null;
        liveDeltaAt = -1e9;
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
        sn.Norm = s.S;
        sn.Track = review.Track;
        sn.Car = review.Car;
        sn.MaxRpm = review.MaxRpm > 500 ? review.MaxRpm : 8000;
        sn.AirTemp = review.AirTemp;
        sn.RoadTemp = review.RoadTemp;
        sn.TrackGrip = review.Grip;
        sn.CurMs = (int)(reviewPos * 1000.0);
        sn.CompletedLaps = Math.Max(1, review.LapNo) - 1;   // laps done before this one, so LAPS shows "LapNo"
        sn.TotalLaps = 0;
        sn.LastMs = (int)review.LapMs;
        sn.BestMs = 0;
        sn.DeltaMs = 0;
        sn.DeltaPositive = false;
        sn.Status = 2;
        sn.Session = -1;
    }

    // --- video taping: armed by the timing line, dropped on anything else ----

    /// Called when the lap counter moves or the lap clock restarts, which is the
    /// moment the car crosses the start/finish line.  Only from here on is the
    /// lap taped, so an out lap or a lap resumed in the middle is never recorded.
    void ArmRecording(double now)
    {
        recArmed = true;
        recArmedAt = now;
        CrossingCount++;
        pauseSince = -1;
        // deliberately no rec.End() here: the chunk writer keeps running across the
        // line, otherwise stopping and restarting it stalls the UI on every lap
        lapNote = "";
        lapNoteAt = -1e9;
    }

    /// Anything that is not a clean flying lap ends the recording and throws the
    /// chunks away: back to the menus, a pause, a restart, a session or track
    /// change.
    void CheckLapAbort(double now)
    {
        if (!recArmed) { pauseSince = -1; return; }
        if (now - recArmedAt < 1200) return;       // just crossed: let the new lap settle

        string why = null;

        if (sn.Status == 1 || sn.Status == 3)                  // replay / pause
        {
            if (pauseSince < 0) pauseSince = now;
            else if (now - pauseSince > 700) why = "session paused";
        }
        else pauseSince = -1;

        if (why == null && !sn.Live) why = "back to the menus";
        // only a real rewind: the new lap must have started running already,
        // otherwise the tiny clock right after the line looks like a jump back
        if (why == null && recLastT > 5f && sn.CurMs > 1000 && sn.CurMs / 1000f + 3f < recLastT)
            why = "lap restarted";
        if (why == null && recLap != null && recLap.Track.Length > 0 && !LapData.SameTrack(recLap.Track, sn.Track))
            why = "track changed";

        if (why != null) AbortRecording(why);
    }

    void AbortRecording(string why)
    {
        recArmed = false;
        AbortCount++;
        // nothing to delete: chunks are shared by every lap and are trimmed by time,
        // and a discarded lap never gets a clip cut out of them
        recLap = null;
        recLapNo = -1;
        recLastT = -1f;
        lapNote = "recording discarded: " + why;
        lapNoteAt = Clock.Now;
    }

    /// Restarting a session keeps ACC "live" and on the same track, so the only
    /// giveaways are the session type changing or the time left jumping back up
    /// (a restart refills the timer).  Called once per tick, always.
    bool SessionJumped()
    {
        bool jumped = false;
        if (lastSession >= 0 && sn.Session != lastSession) jumped = true;
        if (lastTimeLeft > 30f && sn.SessionTimeLeft > lastTimeLeft + 5f) jumped = true;
        lastSession = sn.Session;
        lastTimeLeft = sn.SessionTimeLeft;
        return jumped;
    }

    void RecordLap(double now)
    {
        if ((!shmOpen && !recordTest && !FakeLive) || review != null) return;
        float cur = sn.CurMs / 1000f;
        if (cur < 0f) return;
        int lapNo = sn.CompletedLaps + 1;

        // Restarting a session (or loading a new one) rewinds the lap counter and
        // the lap clock while ACC stays "live": that is not a flying lap.  Drop
        // whatever was being taped instead of carrying the old lap across, which
        // used to leave the panel "recording" with a clock that never restarted.
        bool sessionJump = SessionJumped();
        bool counterBack = recLap != null && lapNo < recLapNo;
        if (recLap != null && (counterBack || sessionJump))
        {
            AbortRecording(counterBack ? "session restarted" : "session changed");
            recLastAdd = -1e9;
        }

        // A new lap starts when the counter moves on or the clock jumps back to
        // zero - those two cases mean the car actually crossed the timing line,
        // and only then does the video recorder start taping.  Note that the
        // clock reads ~0 on the very tick of the crossing, so this must be
        // decided before any "cur is too small" bail-out.
        // ACC's iLastTime lags the crossing by a moment: once the new lap is a
        // couple of seconds old take the game's own number, so the list shows
        // exactly the time ACC shows instead of our own estimate.
        if (lastClosed != null && !lastClosedFixed && sn.LastMs > 500 && cur > 1.5f && cur < 8f)
        {
            if (Math.Abs((long)sn.LastMs - (long)lastClosed.LapMs) < 4000)
            {
                lastClosed.LapMs = sn.LastMs;
                lastClosedFixed = true;
            }
        }

        bool counterMoved = recLap != null && lapNo > recLapNo;
        // ...but only a moving car crosses: after a restart the clock restarts
        // while the car sits in the pits, which is not a lap
        bool clockRestart = recLap != null && cur + 1.5f < recLastT && sn.Speed > 10f;
        bool lineCross = counterMoved || clockRestart;

        if (recLap == null || counterMoved || clockRestart)
        {
            CloseLap(now - cur * 1000.0);
            recLap = new LapData();
            string tr = sn.Track == null ? "" : sn.Track;
            recLap.Track = tr.Length > 0 ? tr : (outline != null ? outline.Track : "");
            recLap.Car = sn.Car == null ? "" : sn.Car;
            recLap.LapNo = lapNo;
            recLap.MaxRpm = sn.MaxRpm;
            recLap.AirTemp = sn.AirTemp;
            recLap.RoadTemp = sn.RoadTemp;
            recLap.Grip = sn.TrackGrip;
            recLap.WallT0 = now - cur * 1000.0;
            recLapNo = lapNo;
            recLastT = -1f;
            recLastAdd = -1e9;
            if (lineCross) ArmRecording(now);
        }

        if (now - recLastAdd < 66.0) return;       // ~15 Hz, same as the charts
        recLastAdd = now;
        recLap.Add(cur, sn.Gas, sn.Brake, sn.SteerNorm, sn.FinalFf, sn.Speed, sn.Gear, (int)sn.Rpm, sn.LatG, sn.LongG, sn.Fuel, sn.TyreF, sn.TyreR, sn.WorldX, sn.WorldZ, sn.Head, sn.Norm);
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
            sn.Norm = (float)((t * 0.28) / (Math.PI * 2.0) % 1.0);
            RecordLap(now);
        }
        CloseLap(now);
        File.WriteAllBytes(outFile, laps.Count > 0 ? laps[laps.Count - 1].ToBytes() : new byte[0]);
    }

    void CloseLap(double wall1)
    {
        recArmed = false;
        CloseLapInner(wall1);
    }

    LapData lastClosed;      // ACC publishes iLastTime only after the line
    bool lastClosedFixed;

    void CloseLapInner(double wall1)
    {
        if (recLap == null) return;
        recLap.WallT1 = wall1;
        // the static page can still be blank when the first lap starts, and a lap
        // without a track name can never find a reference lap: backfill both this
        // lap and anything already in the list as soon as the name is known
        string liveTrack = sn.Track == null ? "" : sn.Track;
        if (NoName(recLap)) recLap.Track = liveTrack;
        if (liveTrack.Length > 0)
            for (int i = 0; i < laps.Count; i++)
                if (NoName(laps[i])) laps[i].Track = liveTrack;
        if (recLap.Samples.Count >= 20)
        {
            // ACC only publishes iLastTime a moment after the car crosses the line, so
            // on this tick it can still hold the *previous* lap's time - that used to
            // stamp lap N with lap N-1's time and cut every replay short by the
            // difference.  Trust ACC only when it agrees with our own clocks: the lap
            // sample clock (recLastT) and the wall time between the two crossings.
            long clock = (long)Math.Round(recLastT * 1000f);
            long wall = (long)Math.Round(wall1 - recLap.WallT0);
            long span = Math.Max(clock, wall);
            if (wall - clock > 2000 && clock > 500) span = clock;   // a pause inflated the wall clock
            if (span < 500) span = clock;
            recLap.LapMs = (sn.LastMs > 500 && Math.Abs((long)sn.LastMs - span) < 1500) ? sn.LastMs : span;

            // a lap that did not drive the track (cut, reset, teleport, pit bug) has a
            // distance far from the learned track length: throw it away instead of
            // letting it pollute the lap list and break the comparisons
            float dist = LapDistance(recLap);
            float trackLen = outline != null && outline.Closed ? outline.LengthM : 0f;
            bool bogus = trackLen > 300f && dist > 30f &&
                         Math.Abs(dist - trackLen) > Math.Max(0.12f * trackLen, 80f);
        if (bogus)
            {
                lapNote = "lap dropped: " + (dist / 1000f).ToString("0.00", Fmt.Inv) + " km driven, track is "
                        + (trackLen / 1000f).ToString("0.00", Fmt.Inv) + " km";
                lapNoteAt = Clock.Now;
            }
            else
            {
                laps.Add(recLap);
                lastClosed = recLap;
                lastClosedFixed = false;
                QueueClip(recLap);
            }
        }
        recLap = null;
        recLapNo = -1;
    }
    /// A lap whose track name never arrived (blank static page, or the "unknown"
    /// placeholder written by the parser) cannot be matched to a reference lap.
    static bool NoName(LapData d)
    {
        string t = d.Track == null ? "" : d.Track.Trim();
        return t.Length == 0 || t == "-" || string.Equals(t, "unknown", StringComparison.OrdinalIgnoreCase);
    }

    /// Distance the car actually covered during a lap, from its own samples.
    static float LapDistance(LapData d)
    {
        if (d == null || d.Samples.Count < 8) return 0f;
        float sum = 0f;
        for (int i = 1; i < d.Samples.Count; i++)
        {
            float dx = d.Samples[i].X - d.Samples[i - 1].X;
            float dz = d.Samples[i].Z - d.Samples[i - 1].Z;
            float step = (float)Math.Sqrt(dx * dx + dz * dz);
            if (step > 0.01f && step < 50f) sum += step;
        }
        return sum;
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
