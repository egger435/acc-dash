// ===========================================================================
//  AccDash - timer, tick loop, shared memory open/close, CLI modes
//  src/Dash.Runtime.cs
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
    // ------------------------------------------------------------------ logic
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Application.Idle += delegate
        {
            if (IsHandleCreated && !timerStarted)
            {
                timerStarted = true;
                StartTimer();
            }
        };
    }

    bool timerStarted;
    Timer ui;

    void StartTimer()
    {
        ui = new Timer();
        ui.Interval = 33;
        ui.Tick += delegate { Tick(); };
        ui.Start();
        LayoutAll();
    }

    void ClearTraces()
    {
        trPedals.Clear();
        trSteerFf.Clear();
        pedals.ResetPeaks = true;
        gg.ResetPeaks = true;
    }

    void SaveCfg()
    {
        cfg.TraceSeconds = timeSlider.Value;
        cfg.Save();
    }

    void UpdateTimeLabel()
    {
        timeLbl.Text = "TIME " + timeSlider.Value.ToString(Fmt.Inv) + "s";
    }

    void ApplyTraceWindow()
    {
        int ms = timeSlider.Value * 1000;
        if (trPedals != null) trPedals.WindowMs = ms;
        if (trSteerFf != null) trSteerFf.WindowMs = ms;
    }

    void Tick()
    {
        tick++;
        double now = Clock.Now;

        // Windows can clamp the window to the work area before any Resize reaches us
        if (ClientSize.Width != laidOutW || ClientSize.Height != laidOutH) LayoutAll();

        double dt = lastTick <= 0 ? 0.033 : Math.Min(0.10, Math.Max(0.001, (now - lastTick) / 1000.0));
        lastTick = now;

        // --- source: ACC shared memory only
        if (FakeLive) FakeSnap(now);
        else
        {
            // ACC recreates its shared pages when a session (re)starts: a page that
            // stops advancing is a stale mapping, so drop it and map the page again.
            if (shmOpen && now - lastPacketMove > 2000)
            {
                CloseShm();
                lastOpenTry = -1e9;
                liveLostAt = now - 5000;
            }
            if (!shmOpen && now - lastOpenTry > 2000) OpenShm();
            if (shmOpen && now - lastPoll > 12)
            {
                lastPoll = now;
                shm.Poll();
                if (shm.PhysOk && shm.PhysPacket != lastPktSeen)
                {
                    lastPktSeen = shm.PhysPacket;
                    lastPacketMove = now;
                }
            }

            string why = "no shm";
            if (shmOpen && shm.Fill(sn, out why))
            {
                liveLostAt = -1e9;
                lastGood = now;
                lastWhy = "";
            }
            else
            {
                lastWhy = why;
                if (now - lastGood > 1500) BlankSnap();
            }
        }

        UpdateTrackMap(now);


        if (review != null)
        {
            // hold PLAY for a moment to scrub forward at 8x
            if (playHeld && !holdFired && now - playDownAt > 350)
            {
                holdFired = true;
                if (!revPlaying) { revPlaying = true; }
                revSpeed = 8.0;
                UpdatePlayLabel();
            }
            if (revPlaying)
            {
                reviewPos += dt * revSpeed;
                if (reviewPos >= review.Span)
                {
                    reviewPos = review.Span;
                    revPlaying = false;
                    revSpeed = 1.0;
                    holdFired = false;
                    UpdatePlayLabel();
                }
                scrub.Pos = (float)reviewPos;
                scrub.Invalidate();
            }
            FillReviewSnap();
        }
        else RecordLap(now);

        if (now - lastStat > 1000) { paintsPerSec = (int)((paints - lastPaints) * 1000.0 / Math.Max(1.0, now - lastStat)); lastPaints = paints; lastStat = now; WriteStatus(now); }

        Render(now);

        // --- traces at ~15 Hz
        if (!paused && review == null && now - lastTrace > 66)
        {
            lastTrace = now;
            trPedals.Push(now, sn.Gas, sn.Brake);
            trSteerFf.Push(now, sn.SteerNorm, Math.Min(1f, Math.Abs(sn.FinalFf)));
            trPedals.Invalidate();
            trSteerFf.Invalidate();
        }

        UpdateLamps(now);
    }

    void CloseShm()
    {
        shm.Close();
        shmOpen = false;
        lastPktSeen = -1;
    }

    void OpenShm()
    {
        lastOpenTry = Clock.Now;
        lastPacketMove = Clock.Now;
        lastPktSeen = -1;
        shm.Open();
        shmOpen = shm.PhysLen > 0;
        if (shmOpen)
        {
            lastPoll = Clock.Now;
            shm.Poll();
            shm.ReadStatic();
        }
    }

    /// Windows can clamp or scale the real window; dump what the form actually sees.
    public void InfoMode(string path)
    {
        Show();
        Application.DoEvents();
        System.Threading.Thread.Sleep(1200);
        Application.DoEvents();
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("screen bounds   " + Screen.PrimaryScreen.Bounds);
        sb.AppendLine("work area       " + Screen.PrimaryScreen.WorkingArea);
        sb.AppendLine("window bounds   " + Bounds);
        sb.AppendLine("client size     " + ClientSize);
        sb.AppendLine("client rect scr " + RectangleToScreen(ClientRectangle));
        sb.AppendLine("device dpi      " + CreateGraphics().DpiX);
        sb.AppendLine("autoscale       " + AutoScaleDimensions + " factor " + AutoScaleFactor);
        sb.AppendLine("laid out        " + laidOutW + "x" + laidOutH);
        File.WriteAllText(path, sb.ToString());
        Close();
    }

    /// Off-screen self render used to verify the layout at any size.
    public void ShotMode(string path, int w, int h, int warmMs)
    {
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(w, h);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(0, 0);
        Show();
        LayoutAll();
        if (!string.IsNullOrEmpty(ShotLapFile)) OpenLapFile(ShotLapFile);
        if (ShotScrubAt >= 0f && review != null)
        {
            reviewPos = Math.Min((double)ShotScrubAt, review.Span);
            scrub.Pos = (float)reviewPos;
            scrub.Invalidate();
            FillReviewSnap();
            Render(Clock.Now);
        }
        if (ShotPlay && review != null) { revPlaying = true; UpdatePlayLabel(); }
        if (ShotFast && review != null) { playHeld = true; holdFired = false; playDownAt = Clock.Now - 1000; revPlaying = true; UpdatePlayLabel(); }
        double t0 = Clock.Now;
        while (Clock.Now - t0 < warmMs)
        {
            Tick();
            Application.DoEvents();
            System.Threading.Thread.Sleep(8);
        }
        LayoutAll();
        Application.DoEvents();
        using (Bitmap bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
        {
            DrawToBitmap(bmp, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("client " + ClientSize.Width + "x" + ClientSize.Height);
        sb.AppendLine("last tile  " + tTyre.Bounds);
        sb.AppendLine("session    " + sess.Bounds + "  rows=" + sess.Rows.Count + " rowH=" + sess.LastRowH + " top=" + sess.LastTop + " footH=" + sess.LastFootH + " h=" + sess.Height);
        sb.AppendLine("time label " + timeLbl.Bounds);
        sb.AppendLine("map        " + map.Bounds + "  pts=" + (outline != null ? outline.Points.Count.ToString(Fmt.Inv) : "-") + " key=" + outlineKey);        sb.AppendLine("time slider" + timeSlider.Bounds);
        sb.AppendLine("ini label  " + iniLbl.Bounds);
        sb.AppendLine("gauge      " + gauge.Bounds);
        sb.AppendLine("pedals     " + pedals.Bounds);
        sb.AppendLine("gg         " + gg.Bounds);
        sb.AppendLine("gear       " + gearRead.Bounds);
        sb.AppendLine("rpm        " + rpmRead.Bounds);
        sb.AppendLine("trace1     " + trPedals.Bounds);
        sb.AppendLine("trace2     " + trSteerFf.Bounds);
        foreach (Button b in headButtons) sb.AppendLine("button     " + b.Text + " " + b.Bounds + " vis=" + b.Visible);
        sb.AppendLine("laps       " + laps.Count + "  rec=" + (recLap != null ? recLap.Samples.Count.ToString() : "-"));
        sb.AppendLine("gg         lat=" + gg.Lat.ToString("0.00", Fmt.Inv) + " lon=" + gg.Lon.ToString("0.00", Fmt.Inv) + " scale=" + gg.GScale.ToString("0.0", Fmt.Inv));
        sb.AppendLine("review     pos=" + reviewPos.ToString("0.00", Fmt.Inv) + " span=" + (review != null ? review.Span.ToString("0.00", Fmt.Inv) : "-") + " playing=" + revPlaying + " speed=" + revSpeed.ToString("0", Fmt.Inv));
        File.WriteAllText(path + ".layout.txt", sb.ToString());
        Close();
    }
}
