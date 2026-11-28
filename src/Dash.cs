// ===========================================================================
//  AccDash - main window: fields, constructor and control building
//  src/Dash.cs
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
//  Main window
// ---------------------------------------------------------------------------
sealed partial class Dash : Form
{
    const int HeaderH = 52;
    const int LeftW = 322;
    const int RightW = 240;

    readonly Cfg cfg;
    readonly AccShm shm = new AccShm();
    Snap sn = new Snap();
    bool paused;
    bool shmOpen;
    double lastOpenTry = -1e9;
    double lastPoll = -1e9;
    double liveLostAt = -1e9;
    double lastTrace = -1e9;
    double lastTick = -1;
    int tick;
    int laidOutW = -1, laidOutH = -1;
    double lastStat = -1e9;
    double lastPacketMove = -1e9;
    int lastPktSeen = -1;
    string lastWhy = "";
    double lastGood = -1e9;

    // --- lap recording / review
    readonly List<LapData> laps = new List<LapData>();
    LapData recLap;              // lap being recorded right now
    int recLapNo = -1;
    string lapNote = "";
    bool calibAsked;
    double lapNoteAt = -1e9;
    float recLastT = -1f;
    double recLastAdd = -1e9;
    bool recArmed;               // set when the car crosses the timing line
    public int CrossingCount;
    public int AbortCount;
    double recArmedAt = -1e9;
    double pauseSince = -1;      // when the session went to pause/replay
    int lastSession = -1;        // session type seen last, to notice a restart
    float lastTimeLeft = -1f;    // session time left, to notice a restart
    public bool SimRestart;      // --simrestart: let the sim restart the session mid drive
    bool recordTest;
    LapData review;              // non-null: the centre charts show this lap
    double reviewPos;            // scrub cursor inside `review`, seconds
    Scrub scrub;
    public string ShotLapFile = "";   // --shot <png> w h warm <lapfile>
    public string ReviewFile = "";    // --review <lapfile>
    public float ShotScrubAt = -1f;   // --shot ... <lapfile> <seconds>
    public bool ShotPlay;             // --shot ... <lapfile> <seconds> play
    public bool ShotFast;             // --shot ... play fast  : also hold PLAY (8x)
    public bool LapsAtStart;          // --laps <file>...  : import then show the lap list
    public readonly List<string> PreloadLaps = new List<string>();

    // --- playback of a reviewed lap
    bool revPlaying;
    double revSpeed = 1.0;
    bool playHeld;
    bool holdFired;
    double playDownAt = -1e9;
    public bool FakeLive;
    public bool SimSave;      // test hook: let simulated laps write tracks\*.map             // --shotsim : synthetic live data, no ACC needed

    // --- track map
    TrackMap map;
    TrackOutline outline;
    string outlineKey = "";
    double mapLastSample = -1e9;
    float mapLastX, mapLastZ;
    bool mapHasLast;
    int mapLastLap = -1;
    int lapAnchor = -1;             // index in outline.Points where the current lap began
    double mapLastSave = -1e9;      // last time the unfinished outline was parked on disk
    TrackOutline reviewOutline;      // racing line of the lap being reviewed
    DeltaPane deltaPane;             // delta card right of the video
    LapData refLap;                  // best lap of this track, for the delta
    double liveDeltaAt = -1e9;       // throttle for the live delta rebuild


    /// Called when ACC has been silent for a while: clear the snapshot so the
    /// panel shows blanks instead of freezing on the last numbers it saw.
    /// Synthetic live data for --shotsim: needs no ACC, and it completes a lap every
    /// 22 s so the record -> list -> review path can be exercised without the game.
    void FakeSnap(double now)
    {
        sn.Live = true;          // the sim drives a real session, gate included
        FakeSnapBody(now);
    }

    void FakeSnapBody(double now)
    {
        double t = now / 1000.0;
        // --simrestart: pretend the session is restarted at 60 s (counter back to
        // zero, clock back to zero) so the abort path can be checked offline
        if (SimRestart && t >= 60.0) t -= 60.0;
        // two laps of different lengths (22 s then 25 s, repeating) so one lap can
        // never be mistaken for its predecessor.  LastMs lags one lap on purpose,
        // exactly the way ACC publishes iLastTime a moment after the line.
        double cyc = t % 47.0;
        int pair = (int)(t / 47.0);
        bool second = cyc >= 22.0;
        double lapLen = second ? 25.0 : 22.0;
        double u = second ? cyc - 22.0 : cyc;
        int lapNo = pair * 2 + (second ? 1 : 0);
        double ph = u / lapLen * Math.PI * 2.0;
        int prevMs = lapNo <= 0 ? 0 : (((lapNo - 1) % 2 == 0) ? 22000 : 25000);

        double a = Math.Sin(ph * 6.0);
        sn.Speed = (float)Math.Abs(120 + 70 * Math.Sin(ph * 3.0));
        sn.Rpm = (float)(5200 + 2200 * Math.Abs(Math.Sin(ph * 6.0)));
        sn.Gear = 2 + (int)(Math.Abs(Math.Sin(ph * 3.0)) * 4);
        sn.SteerNorm = (float)(0.75 * Math.Sin(ph * 5.0));
        sn.Gas = (float)Math.Min(1.0, Math.Max(0.0, a * 1.6));
        sn.Brake = (float)Math.Min(1.0, Math.Max(0.0, -a * 1.6));
        sn.LatG = (float)(1.5 * Math.Sin(ph * 5.0));
        // same sense as live data: braking reads positive, accelerating negative
        sn.LongG = (float)(-1.2 * a);
        sn.FinalFf = (float)(0.6 * Math.Sin(ph * 7.0));
        sn.Fuel = 24f;
        sn.TyreF = 88f; sn.TyreR = 90f; sn.TyreAvg = 89f;
        sn.BrakeTempAvg = 420f; sn.WaterTemp = 84f;
        sn.AirTemp = 26f; sn.RoadTemp = 33f;
        sn.TcLevel = 4; sn.AbsLevel = 3;
        sn.TcActive = (float)Math.Max(0.0, -a);
        sn.AbsActive = (float)Math.Max(0.0, -a) * 0.5f;
        sn.MaxRpm = 7700;
        sn.PitLimiter = 0; sn.EngineRunning = 1;
        sn.BrakeBias = 0.56f;

        sn.Track = "SimTrack"; sn.Car = "mclaren_720s_gt3_evo";   // never saved: FakeLive keeps tracks\ clean
        sn.SectorCount = 3; sn.MaxFuel = 120f;
        sn.NumCars = 1; sn.Position = 1;
        sn.Session = 3; sn.Status = 2; sn.TotalLaps = 0; sn.TrackGrip = 2;
        sn.CompletedLaps = lapNo;
        sn.CurMs = (int)(u * 1000.0);
        sn.LastMs = prevMs;
        sn.BestMs = prevMs;
        sn.DeltaMs = 0;
        sn.SessionTimeLeft = 0f;
        sn.FuelXLap = 2.6f;

        // synthetic track position: an oval with a wobble, roughly 1.4 km around
        double rr = 1.0 + 0.18 * Math.Sin(ph * 3.0);
        sn.WorldX = (float)(260.0 * rr * Math.Cos(ph));
        sn.WorldZ = (float)(170.0 * rr * Math.Sin(ph));
        sn.Head = (float)((ph + Math.PI * 0.5) % (Math.PI * 2.0));

        sn.Stamp = now;
        sn.Live = true;
    }

    void BlankSnap()
    {
        sn = new Snap();
        sn.MaxRpm = 0;
        sn.NumCars = 0;
        sn.Position = 0;
    }

    /// Writes a one-line heartbeat so an outside observer can see exactly what the
    /// dashboard is showing (source, freshness and the values it holds).
    void WriteStatus(double now)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("t=" + DateTime.Now.ToString("HH:mm:ss", Fmt.Inv) + " open=" + shmOpen);
        sb.AppendLine("paints/s=" + paintsPerSec + " rawPaints=" + paints);
        sb.AppendLine("physOk=" + shm.PhysOk + " graphOk=" + shm.GraphOk + " why=" + (lastWhy.Length == 0 ? "-" : lastWhy) + " pkt=" + shm.PhysPacket + " hz=" + Fmt.I(shm.PhysHz));
        sb.AppendLine(string.Format(Fmt.Inv, "live={0} speed={1:0.0} rpm={2} gear={3} steer={4:0.000} lat={5:0.00} lon={6:0.00} gas={7:0.00} brake={8:0.00} ff={9:0.00}",
            sn.Live, sn.Speed, sn.Rpm, sn.Gear, sn.SteerNorm, sn.LatG, sn.LongG, sn.Gas, sn.Brake, sn.FinalFf));
        sb.AppendLine("track='" + sn.Track + "' cfg='" + sn.TrackConfig + "' mapkey=" + outlineKey +
                      " pts=" + (outline == null ? 0 : outline.Points.Count) +
                      " closed=" + (outline != null && outline.Closed) + " lap=" + sn.CompletedLaps + " anchor=" + lapAnchor);
        sb.AppendLine("norm=" + sn.Norm.ToString("0.000", Fmt.Inv) + " normRaw=" + shm.GraphNorm.ToString("0.000", Fmt.Inv)
                      + " cur=" + sn.CurMs + " last=" + sn.LastMs + " lap=" + sn.CompletedLaps);
        sb.AppendLine("grab fps=" + (grab == null ? 0 : grab.RateFps) + " want=" + cfg.GrabFps
                      + " ms=" + (grab == null ? 0.0 : grab.Ms).ToString("0.0", Fmt.Inv)
                      + " seq=" + (grab == null ? 0 : grab.Seq) + " screen=" + (grab != null && grab.UseScreen)
                      + " fast=" + (grab != null && grab.FastBlit));
        sb.AppendLine("armed=" + recArmed + " crossings=" + CrossingCount + " aborts=" + AbortCount
                      + " recLapNo=" + recLapNo + " samples=" + (recLap == null ? 0 : recLap.Samples.Count)
                      + " recOn=" + (rec != null && rec.On) + " laps=" + laps.Count);

        try
        {
            File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dash-status.txt"), sb.ToString(), Encoding.UTF8);
        }
        catch (Exception) { }
    }

    // header
    StatusLamp[] lamps;                // 6: link, physics, graphics, session, source, REC
    Button[] headButtons;
    Button btnLaps, btnPause, btnClear, btnSave, btnPlay, btnLive;

    // left column
    Tile tSpeed, tRpm, tGear, tSteer, tLatG, tLongG, tTc, tAbs, tFuel, tTyre;
    SessionPanel sess;
    Slider timeSlider;
    Label timeLbl;
    Label iniLbl;

    // centre
    Trace trPedals, trSteerFf;

    // right column
    SteerGauge gauge;
    PedalBars pedals;
    GGDisc gg;
    BigRead gearRead, rpmRead;

    public Dash()
    {
        Text = "ACC Telemetry Dashboard 1.6.1";
        ClientSize = new Size(1500, 900);
        MinimumSize = new Size(1180, 700);

        // the work area can be smaller than the design size (laptops, RDP sessions)
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        int cw = Math.Min(ClientSize.Width, Math.Max(900, wa.Width - 40));
        int ch = Math.Min(ClientSize.Height, Math.Max(560, wa.Height - 40));
        if (cw != ClientSize.Width || ch != ClientSize.Height) ClientSize = new Size(cw, ch);
        BackColor = Pal.Bg;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        // title bar / taskbar icon: the gauge we embed with /win32icon
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { }

        cfg = Cfg.Load(AppDomain.CurrentDomain.BaseDirectory);
        Vid.Set(cfg.VideoW, cfg.VideoH, cfg.VideoFps);

        BuildHeader();
        BuildLeft();
        BuildCentre();
        BuildRight();

        map = new TrackMap();
        Controls.Add(map);

        video = new VideoPane();
        Controls.Add(video);

        deltaPane = new DeltaPane();
        deltaPane.Visible = false;
        deltaPane.PickRef += delegate { PickCompareLap(); };
        deltaPane.UseAuto += delegate { UseAutoReference(); };
        Controls.Add(deltaPane);

        scrub = new Scrub();
        scrub.Visible = false;
        scrub.Moved += delegate
        {
            if (review == null) return;
            reviewPos = scrub.Pos;
            revPlaying = false;
            revSpeed = 1.0;
            UpdatePlayLabel();
            FillReviewSnap();
            Render(Clock.Now);
        };
        Controls.Add(scrub);

        timeSlider.Value = cfg.TraceSeconds;
        UpdateTimeLabel();
        ApplyTraceWindow();

        Shown += delegate { LayoutAll(); Refresh(); StartVideo(); };
        Resize += delegate { LayoutAll(); };
        FormClosing += delegate { SaveCfg(); SavePartialMap(); StopGrabber(); StopClipPlayer(); if (rec != null) rec.End(); };
        KeyDown += OnKey;
    }

    // ------------------------------------------------------------------ build
    void BuildHeader()
    {
        lamps = new StatusLamp[6];
        string[] names = { "ACC SHM", "PHYSICS", "GRAPHICS", "SESSION", "SOURCE", "REC" };
        for (int i = 0; i < lamps.Length; i++)
        {
            lamps[i] = new StatusLamp(names[i]);
            lamps[i].Set(Pal.Red, "-");
            Controls.Add(lamps[i]);
        }

        btnSave = MkBtn("SETTINGS", 104);
        btnClear = MkBtn("CLEAR", 84);
        btnPause = MkBtn("FREEZE", 88);
        btnLaps = MkBtn("LAPS", 76);
        btnPlay = MkBtn("PLAY", 76);
        btnLive = MkBtn("LIVE", 76);
        btnPlay.Visible = false;
        btnLive.Visible = false;

        Controls.Add(btnSave);
        Controls.Add(btnClear);
        Controls.Add(btnPause);
        Controls.Add(btnLaps);
        Controls.Add(btnPlay);
        Controls.Add(btnLive);
        headButtons = new Button[] { btnLive, btnPlay, btnLaps, btnPause, btnClear, btnSave };

        btnSave.Click += delegate { OpenSettings(); };
        btnLaps.Click += delegate { OpenLaps(); };
        btnClear.Click += delegate { ClearTraces(); };
        btnPause.Click += delegate { paused = !paused; btnPause.Text = paused ? "RESUME" : "FREEZE"; };

        // playback: click = play/pause, hold = fast forward
        btnLive.Click += delegate { ExitReview(); };
        btnPlay.MouseDown += delegate
        {
            playHeld = true;
            holdFired = false;
            playDownAt = Clock.Now;
        };
        btnPlay.MouseUp += delegate
        {
            if (!holdFired)
            {
                revPlaying = !revPlaying;
                if (revPlaying && review != null && reviewPos >= review.Span - 0.05) reviewPos = 0;
            }
            playHeld = false;
            holdFired = false;
            revSpeed = 1.0;
            UpdatePlayLabel();
        };
        btnPlay.MouseLeave += delegate
        {
            playHeld = false;
            holdFired = false;
            revSpeed = 1.0;
            UpdatePlayLabel();
        };
    }

    // Settings dialog: edits the ini graphically, shows the cache and the credit
    void OpenSettings()
    {
        using (SettingsBox box = new SettingsBox(cfg, delegate
        {
            // the cache lives under <exe>\cache and may be open in ffmpeg right now
            StopClipPlayer();
            StopGrabber();
            if (rec != null) rec.End();
        }))
        {
            if (box.ShowDialog(this) != DialogResult.OK) return;
        }

        Ff.Locate(cfg.FfmpegPath);
        Ff.ForceArea = cfg.VideoArea;
        Ff.ForceOutput = cfg.VideoOutput;
        Vid.Set(cfg.VideoW, cfg.VideoH, cfg.VideoFps);

        timeSlider.Value = Math.Max(timeSlider.Min, Math.Min(timeSlider.Max, cfg.TraceSeconds));
        UpdateTimeLabel();
        ApplyTraceWindow();
        Invalidate(true);
        Render(Clock.Now);
    }

    Button MkBtn(string text, int w)
    {
        Button b = new Button();
        b.Text = text;
        b.Width = w;
        b.Height = 30;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Pal.Line;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Pal.PanelHi;
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(48, 48, 48);
        b.BackColor = Pal.Bg;
        b.ForeColor = Pal.Text;
        b.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
        b.Margin = new Padding(8, 0, 0, 0);
        b.TabStop = false;
        b.TextAlign = ContentAlignment.MiddleCenter;
        b.AutoEllipsis = true;
        return b;
    }

    void BuildLeft()
    {
        tSpeed = AddTile("SPEED KM/H");
        tRpm = AddTile("ENGINE RPM");
        tGear = AddTile("GEAR");
        tSteer = AddTile("STEER DEG");
        tLatG = AddTile("LAT G");
        tLongG = AddTile("LONG G");
        tTc = AddTile("TC");
        tAbs = AddTile("ABS");
        tFuel = AddTile("FUEL L");
        tTyre = AddTile("TYRE C");

        sess = new SessionPanel();
        Controls.Add(sess);

        timeLbl = new Label();
        timeLbl.AutoSize = false;
        timeLbl.Text = "TIME 10s";
        timeLbl.ForeColor = Pal.Dim;
        timeLbl.BackColor = Pal.Bg;
        timeLbl.Font = F.CapS;
        timeLbl.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(timeLbl);

        timeSlider = new Slider();
        timeSlider.Min = 3;
        timeSlider.Max = 60;
        timeSlider.ValueChanged += delegate
        {
            cfg.TraceSeconds = timeSlider.Value;
            UpdateTimeLabel();
            ApplyTraceWindow();
        };
        Controls.Add(timeSlider);

        iniLbl = new Label();
        iniLbl.AutoSize = false;
        iniLbl.Text = "ini: AccDash.ini";
        iniLbl.ForeColor = Color.FromArgb(105, 105, 105);
        iniLbl.BackColor = Pal.Bg;
        iniLbl.Font = F.Small;
        iniLbl.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(iniLbl);
    }

    int paints;
    int paintsPerSec;
    int lastPaints;

    void Hook(Control c) { c.Paint += delegate { paints++; }; }

    Tile AddTile(string cap)
    {
        Tile t = new Tile(cap);
        Controls.Add(t);
        Hook(t);
        return t;
    }

    Trace MakeTrace(string cap, string l1, Color c1, string l2, Color c2, bool bipolar)
    {
        Trace t = new Trace();
        t.Caption = cap; t.L1 = l1; t.L2 = l2; t.C1 = c1; t.C2 = c2; t.Bipolar = bipolar;
        Controls.Add(t);
        return t;
    }

    void BuildCentre()
    {
        trPedals = MakeTrace("THROTTLE / BRAKE   (%)", "THR", Pal.Green, "BRK", Pal.Red, false);
        trPedals.FillArea = true;
        trSteerFf = MakeTrace("STEERING / FFB FORCE   (%)", "STEER", Pal.Violet, "FFB", Pal.Amber, true);
        trSteerFf.Signed1 = true;   // steering is -100..+100, FFB stays 0..100
    }

    void BuildRight()
    {
        gauge = new SteerGauge(); Controls.Add(gauge);
        pedals = new PedalBars(); Controls.Add(pedals);
        gg = new GGDisc(); Controls.Add(gg);
        gearRead = new BigRead(); gearRead.Caption = "GEAR"; Controls.Add(gearRead);
        rpmRead = new BigRead(); rpmRead.Caption = "ENGINE RPM"; Controls.Add(rpmRead);
    }

    // ----------------------------------------------------------------- layout
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        G.Fill(g, ClientRectangle, Pal.Bg);
        G.Fill(g, new Rectangle(0, 0, Width, HeaderH), Pal.Panel);
        G.Fill(g, new Rectangle(0, HeaderH - 1, Width, 1), Pal.Line);
    }
}
