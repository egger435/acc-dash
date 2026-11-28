// ===========================================================================
//  AccDash - entry point and the --dump/--watch tools
//  src/Program.cs
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
//  --dump : write page sizes and a few decoded fields to shm-dump.txt
// ---------------------------------------------------------------------------
static class DumpTool
{
    public static void Run()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("AccDash shared memory dump  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv));
        sb.AppendLine();

        int sPhys = Marshal.SizeOf(typeof(Shm.Physics));
        int sGraph = Marshal.SizeOf(typeof(Shm.Graphics));
        int sStat = Marshal.SizeOf(typeof(Shm.StaticInfo));
        sb.AppendLine("managed struct size: physics=" + sPhys + "  graphics=" + sGraph + "  static=" + sStat);

        using (AccShm shm = new AccShm())
        {
            shm.Open();
            sb.AppendLine("open error: " + (shm.LastError.Length == 0 ? "(none)" : shm.LastError));
            sb.AppendLine("page bytes : physics=" + shm.PhysLen + "  graphics=" + shm.GraphLen + "  static=" + shm.StatLen);
            sb.AppendLine();

            for (int i = 0; i < 3; i++)
            {
                shm.Poll();
                shm.ReadStatic();
                sb.AppendLine("--- sample " + i + "  t=+" + (i * 400) + "ms");
                sb.AppendLine("  packet   phys=" + shm.PhysPacket + " graph=" + shm.GraphPacket);
                sb.AppendLine("  speed=" + shm.P.SpeedKmh + "  rpm=" + shm.P.Rpms + "  gear=" + shm.P.Gear
                              + "  gas=" + shm.P.Gas + "  brake=" + shm.P.Brake + "  steer=" + shm.P.SteerAngle);
                sb.AppendLine("  status=" + shm.G.Status + "  session=" + shm.G.Session + "  laps=" + shm.G.CompletedLaps
                              + "  pos=" + shm.G.Position + "  cur=" + AccShm.Clean(shm.G.CurrentTime)
                              + "  best=" + AccShm.Clean(shm.G.BestTime) + "  delta=" + shm.G.IDeltaLapTime);
                sb.AppendLine("  track='" + AccShm.Clean(shm.S.Track) + "'  car='" + AccShm.Clean(shm.S.CarModel)
                              + "'  maxRpm=" + shm.S.MaxRpm + "  cars=" + shm.S.NumCars);
                System.Threading.Thread.Sleep(400);
            }
        }

        string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shm-dump.txt");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    /// Raw word map of the first 128 bytes of each page: lets us find which offset
    /// really holds a value (rpm / speed / gear) when a field looks wrong.
    /// <summary>Appends the raw word map of both pages (values as int and as float).</summary>
    static void AppendHexMap(AccShm shm, StringBuilder sb)
    {
        sb.AppendLine("--- physics, offsets 0..160 (int | float)");
        for (int off = 0; off <= 160; off += 4)
        {
            int iv = BitConverter.ToInt32(shm.RawPhys, off);
            float fv = BitConverter.ToSingle(shm.RawPhys, off);
            sb.AppendLine(string.Format(Fmt.Inv, "{0,4}  {1,12}  {2}", off, iv, fv.ToString("0.0000", Fmt.Inv)));
        }
        sb.AppendLine("--- graphics, offsets 0..160 (int | float)");
        for (int off = 0; off <= 160; off += 4)
        {
            int iv = BitConverter.ToInt32(shm.RawGraph, off);
            float fv = BitConverter.ToSingle(shm.RawGraph, off);
            sb.AppendLine(string.Format(Fmt.Inv, "{0,4}  {1,12}  {2}", off, iv, fv.ToString("0.0000", Fmt.Inv)));
        }
    }

    public static void RunHex()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("AccDash raw page map  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv));
        using (AccShm shm = new AccShm())
        {
            shm.Open();
            sb.AppendLine("open error: " + (shm.LastError.Length == 0 ? "(none)" : shm.LastError));
            if (shm.LastError.Length == 0)
            {
                shm.Poll();
                AppendHexMap(shm, sb);
            }
        }
        File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shm-hex.txt"), sb.ToString(), Encoding.UTF8);
    }

    /// Waits until the car is actually moving, then samples 20 s of live data.
    public static void RunWaitDrive(int seconds)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("AccDash wait-and-sample  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv));
        sb.AppendLine("t,speed,rpm,gear,gas,brake,steer,latG,accG1,accG2,ff,fuel,status,laps,pos,cur,best,delta");
        bool got = false;
        using (AccShm shm = new AccShm())
        {
            double t0 = Clock.Now;
            int moving = 0;
            while (Clock.Now - t0 < seconds * 1000)
            {
                if (!shm.PhysOk) shm.Open();
                if (shm.PhysOk)
                {
                    shm.Poll();
                    shm.ReadStatic();
                    if (shm.P.SpeedKmh > 2f || shm.P.Rpms > 800) moving++;
                    else moving = 0;
                    if (moving >= 5) { got = true; break; }
                }
                System.Threading.Thread.Sleep(200);
            }

            if (!got)
            {
                sb.AppendLine("never saw the car move (speed/rpm stayed idle)");
            }
            else
            {
                AppendHexMap(shm, sb);
                double t1 = Clock.Now;
                while (Clock.Now - t1 < 20000)
                {
                    shm.Poll();
                    shm.ReadStatic();
                    double t = (Clock.Now - t1) / 1000.0;
                    sb.AppendLine(string.Format(Fmt.Inv, "{0:0.0},{1:0.0},{2},{3},{4:0.00},{5:0.00},{6:0.000},{7:0.00},{8:0.00},{9:0.00},{10:0.00},{11:0.0},{12},{13},{14},{15},{16},{17}",
                        t, shm.P.SpeedKmh, shm.P.Rpms, shm.P.Gear, shm.P.Gas, shm.P.Brake, shm.P.SteerAngle,
                        shm.P.AccG[0], shm.P.AccG[1], shm.P.AccG[2], shm.P.FinalFF, shm.P.Fuel,
                        shm.G.Status, shm.G.CompletedLaps, shm.G.Position,
                        AccShm.Clean(shm.G.CurrentTime), AccShm.Clean(shm.G.BestTime), shm.G.IDeltaLapTime));
                    System.Threading.Thread.Sleep(200);
                }
            }
        }
        File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shm-watch.txt"), sb.ToString(), Encoding.UTF8);
    }

    /// Samples the live pages once per 200 ms for a while: proves that the fields
    /// follow the car while driving.
    public static void RunWatch(int seconds)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("AccDash live sample  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv));
        sb.AppendLine("t,speed,rpm,gear,gas,brake,steer,latG,accG1,accG2,ff,fuel,status,laps,pos,cur,best,delta");
        using (AccShm shm = new AccShm())
        {
            shm.Open();
            if (shm.LastError.Length > 0)
            {
                sb.AppendLine("open error: " + shm.LastError);
            }
            else
            {
                double t0 = Clock.Now;
                while (Clock.Now - t0 < seconds * 1000)
                {
                    shm.Poll();
                    shm.ReadStatic();
                    double t = (Clock.Now - t0) / 1000.0;
                    sb.AppendLine(string.Format(Fmt.Inv, "{0:0.0},{1:0.0},{2},{3},{4:0.00},{5:0.00},{6:0.000},{7:0.00},{8:0.00},{9:0.00},{10:0.00},{11:0.0},{12},{13},{14},{15},{16},{17}",
                        t, shm.P.SpeedKmh, shm.P.Rpms, shm.P.Gear, shm.P.Gas, shm.P.Brake, shm.P.SteerAngle,
                        shm.P.AccG[0], shm.P.AccG[1], shm.P.AccG[2], shm.P.FinalFF, shm.P.Fuel,
                        shm.G.Status, shm.G.CompletedLaps, shm.G.Position,
                        AccShm.Clean(shm.G.CurrentTime), AccShm.Clean(shm.G.BestTime), shm.G.IDeltaLapTime));
                    System.Threading.Thread.Sleep(200);
                }
            }
        }
        File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shm-watch.txt"), sb.ToString(), Encoding.UTF8);
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        foreach (string a in args)
        {
            if (a == "--dump") { DumpTool.Run(); return; }
        }

        if (args.Length >= 2 && args[0] == "--hex")
        {
            DumpTool.RunHex();
            return;
        }

        if (args.Length >= 2 && args[0] == "--waitdrive")
        {
            DumpTool.RunWaitDrive(args.Length > 1 ? int.Parse(args[1]) : 600);
            return;
        }

        if (args.Length >= 2 && args[0] == "--watch")
        {
            DumpTool.RunWatch(args.Length > 1 ? int.Parse(args[1]) : 30);
            return;
        }

        try { Native.SetProcessDPIAware(); } catch { }
        // keep a crash note: this is a winexe, an unhandled exception only
        // shows a dialog nobody can copy the text out of
        Application.ThreadException += delegate(object s2, System.Threading.ThreadExceptionEventArgs e2)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv) + Environment.NewLine + e2.Exception + Environment.NewLine + Environment.NewLine); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s2, UnhandledExceptionEventArgs e2)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.txt"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv) + " [domain]" + Environment.NewLine + e2.ExceptionObject + Environment.NewLine + Environment.NewLine); } catch { }
        };
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length >= 2 && args[0] == "--info")
        {
            using (Dash d = new Dash()) d.InfoMode(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--shot")
        {
            int w = args.Length > 2 ? int.Parse(args[2]) : 1500;
            int h = args.Length > 3 ? int.Parse(args[3]) : 900;
            int warm = args.Length > 4 ? int.Parse(args[4]) : 9000;
            using (Dash d = new Dash())
            {

                if (args.Length > 5) d.ShotLapFile = args[5];
                if (args.Length > 6) d.ShotScrubAt = float.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture);
                if (args.Length > 7 && args[7] == "play") d.ShotPlay = true;
                if (args.Length > 7 && args[7] == "fast") { d.ShotPlay = true; d.ShotFast = true; }
                int firstRef = 8;
                if (args.Length > 7 && args[7] != "play" && args[7] != "fast") firstRef = 7;
                for (int i = firstRef; i < args.Length; i++) d.PreloadLaps.Add(args[i]);   // reference laps for the delta
                d.ShotMode(args[1], w, h, warm);
            }
            return;
        }

        if (args.Length >= 2 && args[0] == "--shotsim")
        {
            int w = args.Length > 2 ? int.Parse(args[2]) : 1500;
            int h = args.Length > 3 ? int.Parse(args[3]) : 900;
            int warm = args.Length > 4 ? int.Parse(args[4]) : 9000;
            using (Dash d = new Dash())
            {
                d.FakeLive = true;
                d.SimSave = Array.IndexOf(args, "--simsave") >= 0;
                d.SimRestart = Array.IndexOf(args, "--simrestart") >= 0;
                d.ShotMode(args[1], w, h, warm);
            }
            return;
        }

        if (args.Length >= 1 && args[0] == "--sim")
        {
            using (Dash d = new Dash())
            {
                d.FakeLive = true;
                Application.Run(d);
            }
            return;
        }

        if (args.Length >= 3 && args[0] == "--rectest")
        {
            using (Dash d = new Dash())
            {
                d.RecordTest(args[2], double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));
            }
            return;
        }

        if (args.Length >= 2 && args[0] == "--windump")
        {
            File.WriteAllText(args[1], Ff.WindowDump(), Encoding.UTF8);
            return;
        }

        if (args.Length >= 2 && args[0] == "--grabinfo")
        {
            GrabInfo(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--pwtest")
        {
            PwTest(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--captest")
        {
            CaptureTest(args.Length > 1 ? int.Parse(args[1]) : 6, args.Length > 2 ? args[2] : "captest.accdash");
            return;
        }
        if (args.Length >= 3 && args[0] == "--laptest")
        {
            string err0;
            LapData a = LapData.Parse(File.ReadAllBytes(args[1]), args[1], out err0);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (a == null)
            {
                sb.AppendLine("parse failed: " + err0);
            }
            else
            {
                string txt = a.ToText();
                File.WriteAllBytes(args[2], a.ToBytes());
                string err1;
                LapData b = LapData.Parse(txt, args[2], out err1);
                sb.AppendLine("track=" + a.Track + " car=" + a.Car + " lap=" + a.LapNo + " ms=" + a.LapMs + " samples=" + a.Samples.Count + " span=" + a.Span.ToString("0.000", Fmt.Inv));
                sb.AppendLine("reparse ok=" + (b != null) + " err=" + err1);
                if (b != null)
                {
                    double worst = 0;
                    int n = Math.Min(a.Samples.Count, b.Samples.Count);
                    for (int i = 0; i < n; i++)
                    {
                        LapSample x = a.Samples[i], y = b.Samples[i];
                        worst = Math.Max(worst, Math.Abs(x.Gas - y.Gas));
                        worst = Math.Max(worst, Math.Abs(x.Brake - y.Brake));
                        worst = Math.Max(worst, Math.Abs(x.Steer - y.Steer));
                        worst = Math.Max(worst, Math.Abs(x.Ff - y.Ff));
                        worst = Math.Max(worst, Math.Abs(x.Speed - y.Speed) / 200.0);
                        worst = Math.Max(worst, Math.Abs(x.Lat - y.Lat));
                        worst = Math.Max(worst, Math.Abs(x.Long - y.Long));
                        worst = Math.Max(worst, Math.Abs(x.Fuel - y.Fuel) / 40.0);
                        worst = Math.Max(worst, Math.Abs(x.TyreF - y.TyreF) / 100.0);
                        if (x.Gear != y.Gear || x.Rpm != y.Rpm) worst = Math.Max(worst, 1);
                    }
                    sb.AppendLine("resamples=" + b.Samples.Count + " worst normalized diff=" + worst.ToString("0.000000", Fmt.Inv));
                    sb.AppendLine("first=" + a.At(0f).Speed.ToString("0.0", Fmt.Inv) + " mid=" + a.At((float)a.Span * 0.5f).Speed.ToString("0.0", Fmt.Inv));
                }
            }
            File.WriteAllText(Path.ChangeExtension(args[2], ".report.txt"), sb.ToString());
            return;
        }

        if (args.Length >= 2 && args[0] == "--shotlaps")
        {
            List<LapData> ls = new List<LapData>();
            for (int i = 2; i < args.Length; i++)
            {
                try
                {
                    string err;
                    LapData d = LapData.Parse(File.ReadAllBytes(args[i]), args[i], out err);
                    if (d != null) ls.Add(d);
                }
                catch { }
            }
            using (LapBrowser lb = new LapBrowser(ls)) lb.ShotTo(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--shotpick")
        {
            List<LapData> ls = new List<LapData>();
            for (int i = 2; i < args.Length; i++)
            {
                try
                {
                    string err;
                    LapData d = LapData.Parse(File.ReadAllBytes(args[i]), args[i], out err);
                    if (d != null) ls.Add(d);
                }
                catch { }
            }
            LapData cur = ls.Count > 0 ? ls[0] : null;
            using (LapPick lp = new LapPick(ls, cur, ls.Count > 1 ? ls[1] : null)) lp.ShotTo(args[1]);
            return;
        }

        if (args.Length >= 2 && args[0] == "--shotsettings")
        {
            Cfg c = Cfg.Load(AppDomain.CurrentDomain.BaseDirectory);
            using (SettingsBox box = new SettingsBox(c, null)) box.ShotTo(args[1]);
            return;
        }

        // write a fresh ini template (does not touch the one next to the exe)
        if (args.Length >= 2 && args[0] == "--iniout")
        {
            Cfg t = Cfg.Load(null);
            t.Path = args[1];
            t.Save();
            return;
        }

        if (args.Length >= 1 && args[0] == "--laps")
        {
            Dash d = new Dash();
            d.LapsAtStart = true;
            for (int i = 1; i < args.Length; i++) d.PreloadLaps.Add(args[i]);
            Application.Run(d);
            return;
        }

        if (args.Length >= 2 && args[0] == "--review")
        {
            Dash d = new Dash();
            d.ReviewFile = args[1];
            Application.Run(d);
            return;
        }

        Application.Run(new Dash());
    }

    /// Records a few seconds of whatever is on screen and cuts a lap file with
    /// the clip embedded, so the whole capture -> clip -> container path can be
    /// checked without ACC running.
    /// <summary>
    /// Writes what the capture path sees: screens, detected game window, the
    /// resulting ddagrab source, and whether a frame can actually be grabbed.
    /// </summary>
    /// Diagnostic: tries PrintWindow under several DPI contexts and flags, so we
    /// can see which combination returns real pixels inside this process.
    static void PwTest(string file)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        bool found;
        Ff.GameArea(out found);
        IntPtr h = Ff.LastWindow;
        sb.AppendLine("hwnd=" + ((long)h).ToString(Fmt.Inv) + " found=" + found);
        IntPtr[] ctx = { IntPtr.Zero, new IntPtr(-1), new IntPtr(-2), new IntPtr(-3), new IntPtr(-4) };
        string[] names = { "leave", "unaware", "system", "pm1", "pm2" };
        for (int ci = 0; ci < ctx.Length; ci++)
        {
            IntPtr prev = IntPtr.Zero;
            if (ctx[ci] != IntPtr.Zero) { try { prev = Native.SetThreadDpiAwarenessContext(ctx[ci]); } catch { } }
            for (int flag = 0; flag <= 2; flag += 2)
            {
                Native.RECT r;
                Native.GetWindowRect(h, out r);
                int w = r.Right - r.Left, hh = r.Bottom - r.Top;
                double m = -1;
                try
                {
                    using (Bitmap b = new Bitmap(w, hh, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                    {
                        using (Graphics g = Graphics.FromImage(b))
                        {
                            IntPtr dc = g.GetHdc();
                            bool ok = Native.PrintWindow(h, dc, flag);
                            g.ReleaseHdc(dc);
                        }
                        long sum = 0; int n = 0;
                        for (int y = 0; y < hh; y += 9) for (int x = 0; x < w; x += 9) { Color c = b.GetPixel(x, y); sum += c.R + c.G + c.B; n++; }
                        m = n == 0 ? 0 : sum / (double)n;
                    }
                }
                catch (Exception ex) { sb.AppendLine("  " + names[ci] + " flag" + flag + " error " + ex.Message); continue; }
                sb.AppendLine("  " + names[ci] + " flag" + flag + " size=" + w + "x" + hh + " mean=" + m.ToString("0.0"));
            }
            if (prev != IntPtr.Zero) { try { Native.SetThreadDpiAwarenessContext(prev); } catch { } }
        }
        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
    }
    static void GrabInfo(string file)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        try
        {
            Cfg cfg = Cfg.Load(AppDomain.CurrentDomain.BaseDirectory);
            Ff.Locate(cfg.FfmpegPath);
            Ff.ForceArea = cfg.VideoArea;
            Ff.ForceOutput = cfg.VideoOutput;
            sb.AppendLine("ffmpeg=" + Ff.Exe + " ready=" + Ff.Ready);
            if (Ff.Ready) sb.AppendLine("version=" + Ff.Probe());

            Screen[] all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++)
                sb.AppendLine("screen[" + i + "] " + all[i].DeviceName + " " + Fmt.Str(all[i].Bounds)
                              + (all[i].Primary ? " primary" : ""));

            bool found;
            Rectangle area = Ff.GameArea(out found);
            sb.AppendLine("--- detection ---");
            sb.AppendLine(Ff.Diag.TrimEnd());
            sb.AppendLine("found=" + found + " area=" + Fmt.Str(area) + " hwnd=" + ((long)Ff.LastWindow).ToString(Fmt.Inv));

            if (found && Ff.LastWindow != IntPtr.Zero)
            {
                FrameGrabber grab = new FrameGrabber();
                grab.Area = area;
                grab.Start(Ff.LastWindow);
                for (int i = 0; i < 18; i++) System.Threading.Thread.Sleep(100);   // let the fallback kick in
                int seq = grab.Seq;
                string png = System.IO.Path.ChangeExtension(file, ".png");
                int lit = 0;
                using (Bitmap b = new Bitmap(Vid.W, Vid.H, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        if (!grab.Draw(g, new Rectangle(0, 0, Vid.W, Vid.H)))
                            sb.AppendLine("frame test: no frame drawn  err=" + grab.Error);
                    }
                    b.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    for (int y = 0; y < b.Height; y += 4)
                        for (int x = 0; x < b.Width; x += 4)
                        {
                            Color c = b.GetPixel(x, y);
                            if (c.R + c.G + c.B > 24) lit++;
                        }
                }
                grab.Stop();
                sb.AppendLine("frame test: seq=" + seq + " shot=" + grab.ShotW + "x" + grab.ShotH
                              + " mean=" + grab.Mean.ToString("0.0", Fmt.Inv)
                              + " png=" + png + " lit samples=" + lit
                              + "  err=" + grab.Error);
                sb.AppendLine("grab rate=" + grab.RateFps + " fps  ms=" + grab.Ms.ToString("0.0", Fmt.Inv)
                              + "  fastblit=" + grab.FastBlit + "  screen=" + grab.UseScreen);
            }
        }
        catch (Exception ex) { sb.AppendLine("error: " + ex.Message); }
        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
    }
    static void CaptureTest(int seconds, string lapOut)
    {
        StringBuilder sb = new StringBuilder();
        Ff.Locate("");
        sb.AppendLine("ffmpeg=" + Ff.Exe + "  ready=" + Ff.Ready + "  " + Ff.Error);
        if (!Ff.Ready) { File.WriteAllText("captest.txt", sb.ToString()); return; }

        bool found;
        Rectangle r = Ff.GameArea(out found);
        sb.AppendLine("game window=" + Fmt.Str(r) + "  game=" + found + "  hwnd=" + ((long)Ff.LastWindow).ToString(Fmt.Inv));
        if (!found || Ff.LastWindow == IntPtr.Zero)
        {
            sb.AppendLine("no game window - start ACC first");
            File.WriteAllText("captest.txt", sb.ToString());
            return;
        }

        Recorder rec = new Recorder();
        FrameGrabber grab = new FrameGrabber();
        grab.Area = r;
        grab.Sink = delegate(byte[] buf, int off) { rec.Push(buf, off); };
        grab.Start(Ff.LastWindow);

        double t0 = Clock.Now;
        rec.Begin();
        for (int i = 0; i < seconds * 10; i++) { System.Threading.Thread.Sleep(100); rec.Tick(true); }
        double t1 = Clock.Now;
        System.Threading.Thread.Sleep(400);        // let the writer drain
        rec.End();
        grab.Stop();
        int chunks = 0;
        try { chunks = Directory.GetFiles(Recorder.Root, "seg_*.mkv", SearchOption.AllDirectories).Length; } catch { }
        sb.AppendLine("captured " + ((t1 - t0) / 1000.0).ToString("0.0", Fmt.Inv) + " s  frames=" + grab.Seq
                      + "  chunks=" + chunks + "  dropped=" + rec.Dropped + "  frame mean=" + rec.LastFrameMean.ToString("0.0", Fmt.Inv)
                      + "  grab mean=" + grab.Mean.ToString("0.0", Fmt.Inv) + " screen=" + grab.UseScreen
                      + "  err=" + rec.Error);

        float vt0, vms;
        byte[] b = rec.Clip(t0 + 600, t1 - 200, out vt0, out vms);
        sb.AppendLine("clip=" + (b == null ? 0 : b.Length) + " bytes  vt0=" + vt0.ToString("0.000", Fmt.Inv) + "  dur=" + vms.ToString("0.000", Fmt.Inv) + "  err=" + rec.Error);
        if (b != null)
        {
            LapData d = new LapData();
            d.Track = "test"; d.Car = "test"; d.LapNo = 1;
            d.LapMs = (float)(t1 - t0);
            for (int i = 0; i < 20; i++) d.Add(i * 1f, 0.5f, 0.2f, 0.1f, 0.3f, 100f, 3, 6000, 0.5f, 0.2f, 20f, 80f, 82f, 10f * i, 0f, i * 0.1f, i / 20f);
            d.VideoBytes = b; d.VideoT0 = vt0; d.VideoMs = vms * 1000f;
            byte[] file = d.ToBytes();
            File.WriteAllBytes(lapOut, file);
            string err;
            LapData back = LapData.Parse(file, lapOut, out err);
            sb.AppendLine("file=" + file.Length + " bytes  reparse=" + (back != null) + "  hasVideo=" + (back != null && back.HasVideo)
                + "  samples=" + (back == null ? 0 : back.Samples.Count) + "  vt0=" + (back == null ? 0f : back.VideoT0).ToString("0.000", Fmt.Inv) + "  err=" + err);
        }
        File.WriteAllText("captest.txt", sb.ToString());
    }
}
