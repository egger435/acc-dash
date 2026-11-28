// ===========================================================================
//  AccDash - render pass, lamps and keyboard handling
//  src/Dash.Render.cs
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
    void Render(double now)
    {
        // centre charts follow either the live ring or the lap being reviewed
        bool rev = review != null;
        trPedals.Rev = rev ? review : null;
        trSteerFf.Rev = rev ? review : null;
        trPedals.RevSteerFf = false;
        trSteerFf.RevSteerFf = true;
        if (rev)
        {
            trPedals.RevPos = (float)reviewPos;
            trSteerFf.RevPos = (float)reviewPos;
            deltaPane.Pos = reviewPos;
            deltaPane.UpdateCursor();
            deltaPane.Invalidate();
        }

        string speedUnit = cfg.SpeedUnit == "mph" ? "MPH" : "KM/H";
        tSpeed.Caption = "SPEED " + speedUnit;
        float spd = cfg.SpeedUnit == "mph" ? sn.Speed * 0.621371f : sn.Speed;
        tSpeed.Value = Fmt.I(spd);

        tRpm.Value = Fmt.I(sn.Rpm);
        int maxRpm = sn.MaxRpm > 500 ? sn.MaxRpm : 0;
        tRpm.ValColor = maxRpm > 0 && sn.Rpm > maxRpm * 0.96f ? Pal.Red : (maxRpm > 0 && sn.Rpm > maxRpm * 0.9f ? Pal.Amber : Pal.Text);

        tGear.Value = Fmt.Gear(sn.Gear);
        tGear.ValColor = sn.Gear == 0 ? Pal.Red : Pal.Text;

        tSteer.Value = (sn.SteerNorm >= 0 ? "+" : "-") + Fmt.N(Math.Abs(sn.SteerNorm) * 240f, 1);
        tSteer.ValColor = Pal.Text;

        tLatG.Value = Fmt.N(sn.LatG, 2);
        tLongG.Value = Fmt.N(sn.LongG, 2);
        tLatG.ValColor = Math.Abs(sn.LatG) > 1.5f ? Pal.Amber : Pal.Text;
        tLongG.ValColor = Math.Abs(sn.LongG) > 1.3f ? Pal.Amber : Pal.Text;

        tTc.Value = sn.TcLevel.ToString(Fmt.Inv);
        tTc.Sub = "active " + Fmt.I(sn.TcActive * 100) + "%";
        tTc.ValColor = sn.TcActive > 0.02f ? Pal.Amber : Pal.Text;

        tAbs.Value = sn.AbsLevel.ToString(Fmt.Inv);
        tAbs.Sub = "active " + Fmt.I(sn.AbsActive * 100) + "%";
        tAbs.ValColor = sn.AbsActive > 0.02f ? Pal.Amber : Pal.Text;

        tFuel.Value = Fmt.N(sn.Fuel, 1);
        tFuel.Sub = sn.FuelXLap > 0.01f ? "est " + Fmt.N(sn.Fuel / sn.FuelXLap, 1) + " laps" : "";

        float tyre = cfg.TempUnit == "f" ? sn.TyreAvg * 9f / 5f + 32f : sn.TyreAvg;
        tTyre.Caption = cfg.TempUnit == "f" ? "TYRE F" : "TYRE C";
        tTyre.Value = Fmt.I(tyre);
        tTyre.Sub = "F " + Fmt.I(CfgT(sn.TyreF)) + "  R " + Fmt.I(CfgT(sn.TyreR));
        tTyre.ValColor = tyre > 105f && cfg.TempUnit == "c" ? Pal.Red : Pal.Text;

        // session block
        List<string[]> rows = new List<string[]>();
        List<Color> cols = new List<Color>();

        rows.Add(new string[] { "TRACK", Short(sn.Track, 18) }); cols.Add(Pal.Text);
        rows.Add(new string[] { "CAR", Short(Pretty(sn.Car), 24) }); cols.Add(Pal.Text);
        rows.Add(new string[] { "SESSION", SessionName(sn.Session) }); cols.Add(Pal.Text);
        rows.Add(new string[] { "POSITION", sn.NumCars > 0 ? "P" + sn.Position.ToString(Fmt.Inv) + "/" + sn.NumCars.ToString(Fmt.Inv) : "P-" }); cols.Add(Pal.Text);
        rows.Add(new string[] { "LAPS", sn.TotalLaps > 0 ? sn.CompletedLaps.ToString(Fmt.Inv) + "/" + sn.TotalLaps.ToString(Fmt.Inv) : sn.CompletedLaps.ToString(Fmt.Inv) }); cols.Add(Pal.Text);
        rows.Add(new string[] { "LAST", sn.LastMs > 0 && sn.LastMs < 3600000 ? Fmt.Mmss(sn.LastMs) : "-" }); cols.Add(Pal.Text);
        rows.Add(new string[] { "BEST", sn.BestMs > 0 && sn.BestMs < 3600000 ? Fmt.Mmss(sn.BestMs) : "-" }); cols.Add(Pal.Dim);
        int dm = sn.DeltaMs;
        bool dOk = sn.CurMs > 1000 && Math.Abs(dm) < 60000 && (sn.LastMs > 0 || sn.BestMs > 0) && !(review != null && dm == 0);
        rows.Add(new string[] { "DELTA", dOk ? Fmt.Delta(dm) : "--.---" }); cols.Add(dOk ? (dm > 0 ? Pal.Red : Pal.Green) : Pal.Dim);

        string[] gripNames = { "green", "fast", "optimum", "greasy", "damp", "wet", "flood" };
        string gripTxt = (sn.TrackGrip >= 0 && sn.TrackGrip <= 6) ? gripNames[sn.TrackGrip] : "-";
        string foot = "AIR " + (sn.AirTemp > 0.5f ? Fmt.I(CfgT(sn.AirTemp)) : "-") + "  ROAD " + (sn.RoadTemp > 0.5f ? Fmt.I(CfgT(sn.RoadTemp)) : "-") + "  GRIP " + gripTxt;
        if (sn.SessionTimeLeft > 1f) foot += "   LEFT " + Fmt.Clock(sn.SessionTimeLeft);
        if (lapNote.Length > 0 && now - lapNoteAt < 9000.0) foot = lapNote;
        sess.Set("SESSION", rows, cols, foot);

        // centre + right
        gauge.Steer = sn.SteerNorm;
        gauge.Ff = sn.FinalFf;
        pedals.Set(sn.Brake, sn.Gas);
        gg.Set(sn.LatG, sn.LongG, cfg.GScale);
        map.Set(sn.WorldX, sn.WorldZ, sn.Head,
                rev && reviewOutline != null ? reviewOutline : outline, rev);

        gearRead.Value = Fmt.Gear(sn.Gear);
        gearRead.ValColor = sn.Gear == 0 ? Pal.Red : Pal.Text;
        gearRead.Unit = GearWord(sn.Gear);

        rpmRead.Value = Fmt.I(sn.Rpm);
        rpmRead.ValColor = maxRpm > 0 && sn.Rpm > maxRpm * 0.96f ? Pal.Red : Pal.Text;
        rpmRead.Unit = maxRpm > 0 ? Fmt.I(Math.Min(100, sn.Rpm / maxRpm * 100f)) + "%" : "";

        // These widgets draw from plain fields, so nothing invalidates them: without
        // this the values are captured but the screen keeps the first frame.
        tSpeed.Invalidate(); tRpm.Invalidate(); tGear.Invalidate(); tSteer.Invalidate();
        tLatG.Invalidate(); tLongG.Invalidate(); tTc.Invalidate(); tAbs.Invalidate();
        tFuel.Invalidate(); tTyre.Invalidate(); sess.Invalidate();
        gauge.Invalidate(); gearRead.Invalidate(); rpmRead.Invalidate();
        map.Invalidate();

        // in review the charts no longer receive live pushes, so the scrub cursor
        // would stay put without this
        if (rev)
        {
            trPedals.Invalidate();
            trSteerFf.Invalidate();
            pedals.Invalidate();
            gg.Invalidate();
        }
    }

    float CfgT(float c) { return cfg.TempUnit == "f" ? c * 9f / 5f + 32f : c; }

    static string GearWord(int g)
    {
        if (g == 0) return "reverse";
        if (g == 1) return "neutral";
        int n = g - 1;
        if (n == 1) return "1st";
        if (n == 2) return "2nd";
        if (n == 3) return "3rd";
        return n.ToString(Fmt.Inv) + "th";
    }

    static string SessionName(int s)
    {
        switch (s)
        {
            case 0: return "PRACTICE";
            case 1: return "QUALIFY";
            case 2: return "RACE";
            case 3: return "HOTLAP";
            case 4: return "TIME ATTACK";
            case 5: return "DRIFT";
            case 6: return "DRAG";
            case 7: return "HOTSTINT";
            case 8: return "HOTSTINT SUPERPOLE";
            case 9: return "RACE";
            case -1: return "-";
            default: return "NO SESSION";
        }
    }

    static string Pretty(string s)
    {
        if (string.IsNullOrEmpty(s)) return "-";
        s = s.Replace('_', ' ');
        return s.ToUpperInvariant();
    }

    static string Short(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "-";
        if (s.Length <= n) return s.ToUpperInvariant();
        return s.Substring(0, n - 1).ToUpperInvariant() + ".";
    }

    static string GfxStatus(int st)
    {
        switch (st)
        {
            case 0: return "off";
            case 1: return "replay";
            case 2: return "live";
            case 3: return "paused";
            default: return "?";
        }
    }

    void UpdateLamps(double now)
    {
        if (review != null)
        {
            lamps[0].Set(Pal.Green, "review");
            lamps[1].Set(Pal.Green, "lap " + review.LapNo.ToString(Fmt.Inv));
            lamps[2].Set(Pal.Green, Fmt.Mmss((int)review.LapMs));
            lamps[3].Set(Pal.Green, string.IsNullOrEmpty(review.Track) ? "-" : review.Track.ToLowerInvariant());
            lamps[4].Set(Pal.Amber, review.FromFile ? "FILE" : "REPLAY");
            lamps[5].Set(Pal.Dim, "off");
            return;
        }

        if (FakeLive)
        {
            lamps[0].Set(Pal.Amber, "sim");
            lamps[1].Set(Pal.Green, Fmt.I(sn.Rpm) + " rpm");
            lamps[2].Set(Pal.Green, "live");
            lamps[3].Set(Pal.Green, SessionName(sn.Session).ToLowerInvariant() + " " + sn.CompletedLaps.ToString(Fmt.Inv));
            lamps[4].Set(Pal.Amber, "SIM");
            lamps[5].Set(Pal.Dim, "sim");
            return;
        }

        bool freshPhys = shmOpen && shm.PhysOk && now - shm.PhysStamp < 600;
        bool freshGfx = shmOpen && shm.GraphOk && now - shm.GraphStamp < 600;

        lamps[0].Set(freshPhys && freshGfx ? Pal.Green : Pal.Red, freshPhys ? "ok" : (shmOpen ? "stale" : "not found"));
        lamps[1].Set(freshPhys ? Pal.Green : Pal.Red, freshPhys ? Fmt.I(shm.PhysHz) + " Hz" : "no data");
        lamps[2].Set(freshGfx ? Pal.Green : Pal.Red, freshGfx ? GfxStatus(sn.Status) : "no data");
        lamps[3].Set(freshGfx ? Pal.Green : Pal.Red, freshGfx ? SessionName(sn.Session).ToLowerInvariant() + " " + sn.CompletedLaps.ToString(Fmt.Inv) + "/" + (sn.TotalLaps > 0 ? sn.TotalLaps.ToString(Fmt.Inv) : "-") : "no session");
        lamps[4].Set(shmOpen ? Pal.Green : Pal.Red, shmOpen ? "ACC" : "offline");

        bool taping = recArmed && rec != null && rec.On;
        if (!cfg.Video) lamps[5].Set(Pal.Dim, "video off");
        else if (taping)
        {
            // our own stopwatch since the line: ACC's lap clock keeps running when
            // a session is restarted, which made the lamp count on across restarts
            double since = (Clock.Now - recArmedAt) / 1000.0;
            if (since < 0 || since > 3600) since = 0;
            lamps[5].Set(Pal.Red, "lap " + (sn.CompletedLaps + 1).ToString(Fmt.Inv)
                                   + "  " + since.ToString("0.0", Fmt.Inv) + " s");
        }
        else lamps[5].Set(Pal.Dim, "waits for the line");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = null;   // keep the header buttons from looking "selected"
        foreach (string f in PreloadLaps)
        {
            try
            {
                string err;
                LapData d = LapData.Parse(File.ReadAllText(f), f, out err);
                if (d != null) laps.Add(d);
            }
            catch { }
        }
        if (LapsAtStart) BeginInvoke((MethodInvoker)delegate { OpenLaps(); });

        if (!string.IsNullOrEmpty(ReviewFile))
        {
            string f = ReviewFile;
            ReviewFile = "";
            BeginInvoke((MethodInvoker)delegate { OpenLapFile(f); });
        }
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5) { OpenLaps(); e.Handled = true; }
        else if (e.KeyCode == Keys.F6) { if (review != null) ExitReview(); e.Handled = true; }
        else if (e.KeyCode == Keys.Space)
        {
            if (review != null)
            {
                revPlaying = !revPlaying;
                if (revPlaying && reviewPos >= review.Span - 0.05) reviewPos = 0;
                UpdatePlayLabel();
            }
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F2) { btnPause.PerformClick(); e.Handled = true; }
        else if (e.KeyCode == Keys.F3) { ClearTraces(); e.Handled = true; }
        else if (e.KeyCode == Keys.F4) { SaveCfg(); e.Handled = true; }
        else if (e.KeyCode == Keys.F11)
        {
            FormBorderStyle = FormBorderStyle == FormBorderStyle.None ? FormBorderStyle.Sizable : FormBorderStyle.None;
            if (FormBorderStyle == FormBorderStyle.None) WindowState = FormWindowState.Maximized;
            e.Handled = true;
        }
    }
}
