// ===========================================================================
//  AccDash - ffmpeg discovery, process helpers and the game window rect
//  src/Video/Ff.cs
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;


/// <summary>
/// Everything that talks to ffmpeg lives behind this class: where the binary is,
/// how to start it without a console window, and which screen area the game owns.
/// </summary>
static class Ff
{
    public static string Exe = "";
    public static string Error = "";
    public static string Version = "";
    static bool located;

    /// Resolve ffmpeg: the ini hint, then next to the exe (ffmpeg.exe or
    /// ffmpeg\bin\ffmpeg.exe - where the folder usually ships), then the old
    /// tools\ffmpeg\bin spots, then PATH.
    public static bool Locate(string hint)
    {
        located = true;
        Error = "";
        List<string> tries = new List<string>();
        if (!string.IsNullOrEmpty(hint)) tries.Add(hint);

        string b = AppDomain.CurrentDomain.BaseDirectory;
        tries.Add(Path.Combine(b, "ffmpeg.exe"));
        tries.Add(Path.Combine(b, @"ffmpeg\bin\ffmpeg.exe"));
        tries.Add(Path.Combine(b, @"tools\ffmpeg\bin\ffmpeg.exe"));
        tries.Add(Path.Combine(b, @"..\tools\ffmpeg\bin\ffmpeg.exe"));
        tries.Add(Path.Combine(b, @"..\..\tools\ffmpeg\bin\ffmpeg.exe"));

        string pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (string dir in pathVar.Split(';'))
            {
                if (dir.Trim().Length == 0) continue;
                try { tries.Add(Path.Combine(dir.Trim(), "ffmpeg.exe")); }
                catch { }
            }
        }

        foreach (string t in tries)
        {
            try
            {
                if (File.Exists(t)) { Exe = Path.GetFullPath(t); break; }
            }
            catch { }
        }
        if (Exe.Length == 0)
        {
            Error = "ffmpeg.exe not found - put it next to AccDash.exe (ffmpeg.exe or ffmpeg\\bin\\ffmpeg.exe) or set FfmpegPath in AccDash.ini";
            return false;
        }
        return true;
    }

    public static bool Ready
    {
        get
        {
            if (!located) Locate("");
            return Exe.Length > 0;
        }
    }

    /// Runs ffmpeg to completion and returns stdout+stderr.  Used for probing only.
    public static string Run(string args, int timeoutMs)
    {
        if (!Ready) return Error;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(Exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                return (o + e).Trim();
            }
        }
        catch (Exception ex) { Error = ex.Message; return ex.Message; }
    }

    /// ffprobe next to ffmpeg, when the build ships one.
    public static string ProbeExe
    {
        get
        {
            try
            {
                string d = Path.GetDirectoryName(Exe);
                if (string.IsNullOrEmpty(d)) return "";
                string p = Path.Combine(d, "ffprobe.exe");
                return File.Exists(p) ? p : "";
            }
            catch { return ""; }
        }
    }

    /// Real duration and frame count of a video file.  The clip cutter asks for
    /// `t1 - t0` seconds, but chunks may be missing at either end, so the length
    /// written into the lap file has to come from the file itself.
    public static bool Measure(string file, out double dur, out int frames)
    {
        dur = ProbeNumber(file, "format=duration", false);
        frames = (int)ProbeNumber(file, "stream=nb_read_frames", true);
        return dur > 0.05 || frames > 0;
    }

    static double ProbeNumber(string file, string entry, bool countFrames)
    {
        string exe = ProbeExe;
        if (exe.Length == 0 || !File.Exists(file)) return 0;
        try
        {
            string args = "-v error " + (countFrames ? "-count_frames -select_streams v:0 " : "")
                        + "-show_entries " + entry + " -of default=nw=1:nk=1 \"" + file + "\"";
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd().Trim();
                p.StandardError.ReadToEnd();
                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } }
                int nl = o.IndexOfAny(new char[] { '\r', '\n' });
                if (nl > 0) o = o.Substring(0, nl);
                double d;
                if (double.TryParse(o.Trim(), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            }
        }
        catch { }
        return 0;
    }

    /// The first line of `ffmpeg -version`, cached.
    public static string Probe()
    {
        if (Version.Length == 0)
        {
            string s = Run("-hide_banner -version", 4000);
            int nl = s.IndexOf('\n');
            Version = nl > 0 ? s.Substring(0, nl).Trim() : s.Trim();
        }
        return Version;
    }

    /// Start ffmpeg hidden.  `keepStdin` lets the caller send 'q'; `pipeOut`
    /// hands stdout to a reader thread (raw video frames).
    public static Process Start(string args, bool keepStdin, bool pipeOut)
    {
        if (!Ready) return null;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(Exe, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = keepStdin;
            psi.RedirectStandardOutput = pipeOut;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = Path.GetDirectoryName(Exe);
            Process p = Process.Start(psi);
            // drain stderr so a full pipe can never wedge ffmpeg
            try { p.BeginErrorReadLine(); } catch { }
            return p;
        }
        catch (Exception ex) { Error = ex.Message; return null; }
    }

    /// Ask ffmpeg to stop politely (flushes the current mkv chunk), then kill it.
    public static void Stop(Process p, int graceMs)
    {
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                if (p.StartInfo.RedirectStandardInput)
                {
                    try { p.StandardInput.WriteLine("q"); p.StandardInput.Flush(); } catch { }
                }
                if (!p.WaitForExit(graceMs)) { try { p.Kill(); } catch { } }
            }
        }
        catch { }
        try { p.Dispose(); } catch { }
    }

    public static string Diag = "";          // human readable trace of the last GameArea call
    public static Rectangle ForceArea = Rectangle.Empty;
    public static int ForceOutput = -1;
    public static IntPtr LastWindow = IntPtr.Zero;   // hwnd matched by the last GameArea call

    static readonly string[] GameProcs =
    {
        "acc", "ac2-win64-shipping", "assettocorsacompetizione", "accsteam", "ac2"
    };

    /// Set ACCDASH_FAKE_GAME=<process name> to point the detector at another
    /// window - handy for testing the capture path without ACC.
    static readonly string FakeGame = Environment.GetEnvironmentVariable("ACCDASH_FAKE_GAME");

    static bool IsGameProc(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string n = name.ToLowerInvariant();
        if (!string.IsNullOrEmpty(FakeGame) && n == FakeGame.ToLowerInvariant()) return true;
        for (int i = 0; i < GameProcs.Length; i++) if (n == GameProcs[i]) return true;
        return false;
    }

    static Rectangle WindowRect(IntPtr h)
    {
        Native.RECT r;
        if (Native.DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(Native.RECT))) == 0)
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        if (Native.GetWindowRect(h, out r))
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return Rectangle.Empty;
    }

    static long Overlap(Rectangle a, Rectangle b)
    {
        int x1 = Math.Max(a.Left, b.Left), x2 = Math.Min(a.Right, b.Right);
        int y1 = Math.Max(a.Top, b.Top), y2 = Math.Min(a.Bottom, b.Bottom);
        if (x2 <= x1 || y2 <= y1) return 0;
        return (long)(x2 - x1) * (y2 - y1);
    }

    /// Dumps every top level window with pid, process name, class and rect.
    /// Used by --windump to work out why the game window is (not) matched.
    public static string WindowDump()
    {
        StringBuilder sb = new StringBuilder();
        try
        {
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                string proc = "?";
                string perr = "";
                try { using (Process q = Process.GetProcessById((int)pid)) proc = q.ProcessName; }
                catch (Exception e) { perr = e.GetType().Name; }
                StringBuilder cls = new StringBuilder(64);
                try { Native.GetClassName(h, cls, cls.Capacity); } catch { }
                StringBuilder title = new StringBuilder(160);
                try { Native.GetWindowText(h, title, title.Capacity); } catch { }
                Rectangle r = WindowRect(h);
                sb.Append("hwnd=").Append(((long)h).ToString(Fmt.Inv))
                  .Append(" pid=").Append(pid.ToString(Fmt.Inv))
                  .Append(" proc=").Append(proc).Append(perr.Length > 0 ? "(" + perr + ")" : "")
                  .Append(" cls=").Append(cls.ToString())
                  .Append(" vis=").Append(Native.IsWindowVisible(h) ? 1 : 0)
                  .Append(" min=").Append(Native.IsIconic(h) ? 1 : 0)
                  .Append(" rect=").Append(Fmt.Str(r))
                  .Append(" title=").Append(title.ToString())
                  .Append('\n');
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { sb.Append("error: " + ex.Message + "\n"); }
        return sb.ToString();
    }
    /// The monitor the game is on, in physical pixels.  The game is located by
    /// process name and by the Unreal window class, so borderless and exclusive
    /// fullscreen both work.  found=false means "no game running": callers must
    /// then capture nothing, because falling back to the primary screen would
    /// record the dashboard instead of the game.
    static bool IsGameTitle(string t)
    {
        if (string.IsNullOrEmpty(t)) return false;
        string s = t.ToLowerInvariant();
        return s == "ac2" || s.Contains("assetto corsa") || s.Contains("competizione");
    }

    /// The monitor the game is on, in physical pixels.
    ///
    /// The game is matched three ways, because the process list of an elevated
    /// game is often unreadable from here: by process name (acc,
    /// AC2-Win64-Shipping, ...), by the Unreal window class, and by the window
    /// title.  found=false means "no game running": callers must then capture
    /// nothing, because falling back to the primary screen would record the
    /// dashboard instead of the game.
    public static Rectangle GameArea(out bool found)
    {
        found = false;
        Diag = "";
        LastWindow = IntPtr.Zero;
        if (ForceArea.Width > 64 && ForceArea.Height > 64) { found = true; Diag = "forced " + Fmt.Str(ForceArea); return ForceArea; }
        // (the window rect is not used as the capture region: taping the whole
        //  monitor the game sits on keeps the full picture, while a window rect
        //  crops the render as soon as DWM or the game window reports borders)

        StringBuilder log = new StringBuilder();
        Screen[] all = Screen.AllScreens;
        for (int i = 0; i < all.Length; i++)
            log.Append("screen ").Append(i).Append(' ').Append(all[i].DeviceName).Append(' ')
               .Append(Fmt.Str(all[i].Bounds)).Append(all[i].Primary ? " primary" : "").Append('\n');

        uint self = (uint)Process.GetCurrentProcess().Id;

        // one snapshot of the process table, so the window pass needs no lookups
        Dictionary<int, string> names = new Dictionary<int, string>();
        List<Process> snapshot = new List<Process>();
        try
        {
            foreach (Process q in Process.GetProcesses())
            {
                snapshot.Add(q);
                int id = -1; string nm = "";
                try { id = q.Id; } catch { }
                try { nm = q.ProcessName; } catch { }
                if (id > 0) names[id] = nm;
            }
        }
        catch { }

        Rectangle best = Rectangle.Empty;
        string bestHow = "";

        // pass 1: the game process owns a visible main window
        foreach (Process q in snapshot)
        {
            string nm = "";
            int id = -1;
            try { nm = q.ProcessName; id = q.Id; } catch { }
            if (!IsGameProc(nm)) continue;
            IntPtr h = IntPtr.Zero;
            try { h = q.MainWindowHandle; } catch { }
            if (h == IntPtr.Zero) continue;
            if (!Native.IsWindowVisible(h)) continue;
            Rectangle r = WindowRect(h);
            if (r.Width < 320 || r.Height < 200) continue;
            if ((long)r.Width * r.Height <= (long)best.Width * best.Height) continue;
            best = r; bestHow = "process " + nm; LastWindow = h;
        }

        // pass 2: any top level Unreal / AC2 window, even if its process is opaque
        if (best.Width == 0)
        {
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == self) return true;

                StringBuilder cls = new StringBuilder(64);
                try { Native.GetClassName(h, cls, cls.Capacity); } catch { }
                StringBuilder title = new StringBuilder(160);
                try { Native.GetWindowText(h, title, title.Capacity); } catch { }

                string nm;
                if (!names.TryGetValue((int)pid, out nm)) nm = "";
                bool hit = cls.ToString() == "UnrealWindow" || IsGameTitle(title.ToString()) || IsGameProc(nm);
                if (!hit) return true;

                Rectangle r = WindowRect(h);
                if (r.Width < 320 || r.Height < 200) return true;
                if ((long)r.Width * r.Height <= (long)best.Width * best.Height) return true;
                best = r;
                bestHow = "window class=" + cls + " title=" + title.ToString().Trim();
                LastWindow = h;
                return true;
            }, IntPtr.Zero);
        }

        foreach (Process q in snapshot) { try { q.Dispose(); } catch { } }

        if (best.Width == 0)
        {
            log.Append("game window: none\n");
            Diag = log.ToString();
            return Rectangle.Empty;
        }
        log.Append("game window: ").Append(bestHow).Append(' ').Append(Fmt.Str(best)).Append('\n');

        // Tape exactly the game window: Windows gives us its frame bounds in real
        // pixels (DwmGetWindowAttribute above), so a windowed game fills the clip
        // instead of sitting in the middle of a desktop copy.  Only a window that
        // covers the whole monitor is snapped to the monitor, to drop its 1 px
        // border.
        Rectangle area = best;
        for (int i = 0; i < all.Length; i++)
        {
            long mon = (long)all[i].Bounds.Width * all[i].Bounds.Height;
            if (mon > 0 && Overlap(best, all[i].Bounds) > 0.99 * mon)
            {
                area = all[i].Bounds;
                log.Append("snapped to screen ").Append(i).Append(' ').Append(Fmt.Str(area)).Append('\n');
                break;
            }
        }
        found = true;
        log.Append("capture area ").Append(Fmt.Str(area)).Append('\n');
        Diag = log.ToString();
        return area;
    }
    /// ddagrab source string for a screen rectangle.  ddagrab wants a region
    /// inside one output, so pick the output with the largest overlap.
    public static string GrabSource(Rectangle area, int fps, out int outputIndex)
    {
        Screen[] all = Screen.AllScreens;
        int idx = 0;
        if (ForceOutput >= 0 && ForceOutput < all.Length) idx = ForceOutput;
        else
        {
            long bestOv = -1;
            for (int i = 0; i < all.Length; i++)
            {
                long ov = Overlap(area, all[i].Bounds);
                if (ov > bestOv) { bestOv = ov; idx = i; }
            }
        }
        Rectangle b = all.Length > idx ? all[idx].Bounds : Screen.PrimaryScreen.Bounds;

        int x = Math.Max(b.Left, Math.Min(area.Left, b.Right - 64));
        int y = Math.Max(b.Top, Math.Min(area.Top, b.Bottom - 64));
        int w = Math.Min(area.Width, b.Right - x);
        int h = Math.Min(area.Height, b.Bottom - y);
        if (w < 64) w = 64;
        if (h < 64) h = 64;
        w -= w % 2;
        h -= h % 2;

        outputIndex = idx;
        return "ddagrab=output_idx=" + idx.ToString(Fmt.Inv)
            + ":draw_mouse=0:framerate=" + fps.ToString(Fmt.Inv)
            + ":video_size=" + w.ToString(Fmt.Inv) + "x" + h.ToString(Fmt.Inv)
            + ":offset_x=" + x.ToString(Fmt.Inv) + ":offset_y=" + y.ToString(Fmt.Inv);
    }
    /// 640x360 letterboxed, so the aspect ratio never distorts.
    public static string FitChain
    {
        get
        {
            return "scale=" + Vid.W + ":" + Vid.H + ":force_original_aspect_ratio=decrease,"
                 + "pad=" + Vid.W + ":" + Vid.H + ":(ow-iw)/2:(oh-ih)/2:black,setsar=1";
        }
    }

    /// ddagrab hands out d3d11 frames, so they have to come back to system
    /// memory before the ordinary scale/pad filters can touch them.
    public static string ScaleChain { get { return "hwdownload,format=bgra," + FitChain; } }

    /// Same chain, ending in the planar format x264 wants.
    public static string EncodeChain { get { return ScaleChain + ",format=yuv420p"; } }
}
