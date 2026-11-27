// ===========================================================================
//  AccDash - AccDash.ini load/save
//  src/Config.cs
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
//  Config (AccDash.ini)
// ---------------------------------------------------------------------------
sealed class Cfg
{
    public int TraceSeconds = 10;
    public string SpeedUnit = "kmh";     // kmh | mph
    public string TempUnit = "c";        // c | f
    public float GScale = 2.0f;

    // --- lap video
    public bool Video = true;            // record one clip per lap
    public int VideoCrf = 26;            // x264 quality, lower = bigger
    public int VideoW = 320;             // clip size and frame rate, see [video] below
    public int VideoH = 180;
    public int VideoFps = 24;
    public string FfmpegPath = "";       // empty = next to the exe, then in PATH
    public Rectangle VideoArea = Rectangle.Empty;  // force the capture region (x,y,w,h)
    public int VideoOutput = -1;                   // force the ddagrab output index

    public string Path = null;

    public static Cfg Load(string nearExe)
    {
        Cfg c = new Cfg();
        try
        {
            string p = System.IO.Path.Combine(nearExe, "AccDash.ini");
            c.Path = p;
            if (!File.Exists(p)) { c.Save(); return c; }
            foreach (string raw in File.ReadAllLines(p))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "traceseconds": c.TraceSeconds = Math.Max(3, Math.Min(60, ToInt(v, 10))); break;
                            case "speedunit": c.SpeedUnit = v.ToLowerInvariant() == "mph" ? "mph" : "kmh"; break;
                    case "tempunit": c.TempUnit = v.ToLowerInvariant() == "f" ? "f" : "c"; break;
                    case "gscale": c.GScale = (float)Math.Max(0.5, Math.Min(4.0, ToDouble(v, 2.0))); break;
                    case "video": c.Video = ToBool(v, true); break;
                    case "videocrf": c.VideoCrf = Math.Max(16, Math.Min(34, ToInt(v, 26))); break;
                    case "videow": c.VideoW = Math.Max(160, Math.Min(1280, ToInt(v, 320))); break;
                    case "videoh": c.VideoH = Math.Max(90, Math.Min(720, ToInt(v, 180))); break;
                    case "videofps": c.VideoFps = Math.Max(5, Math.Min(60, ToInt(v, 24))); break;
                    case "ffmpegpath": c.FfmpegPath = v; break;
                    case "videoarea": c.VideoArea = ToRect(v); break;
                    case "videooutput": c.VideoOutput = ToInt(v, -1); break;
                }
            }
        }
        catch { }
        return c;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(Path)) return;
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("; AccDash.ini - ACC Telemetry Dashboard");
            sb.AppendLine("; Numbers only, no units.  Changes apply after restart.");
            sb.AppendLine();
            sb.AppendLine("[display]");
            sb.AppendLine("; seconds of history drawn in the two centre charts (3..60)");
            sb.AppendLine("TraceSeconds=" + TraceSeconds.ToString(Fmt.Inv));
            sb.AppendLine("; speed tile unit: kmh | mph");
            sb.AppendLine("SpeedUnit=" + SpeedUnit);
            sb.AppendLine("; temperature unit: c | f");
            sb.AppendLine("TempUnit=" + TempUnit);
            sb.AppendLine("; G-G circle full scale (g)");
            sb.AppendLine("GScale=" + GScale.ToString("0.0", Fmt.Inv));
            sb.AppendLine();
            sb.AppendLine("[video]");
            sb.AppendLine("; record one clip per lap and store it inside the lap file");
            sb.AppendLine("Video=" + (Video ? "1" : "0"));
            sb.AppendLine("; clip size in pixels and frame rate (160..1280 x 90..720, 5..60 fps)");
            sb.AppendLine("VideoW=" + VideoW.ToString(Fmt.Inv));
            sb.AppendLine("VideoH=" + VideoH.ToString(Fmt.Inv));
            sb.AppendLine("VideoFps=" + VideoFps.ToString(Fmt.Inv));
            sb.AppendLine("; x264 quality, 16 (best, big) .. 34 (small)");
            sb.AppendLine("VideoCrf=" + VideoCrf.ToString(Fmt.Inv));
            sb.AppendLine("; ffmpeg.exe to use; empty = ffmpeg.exe or ffmpeg\\bin\\ffmpeg.exe next to");
            sb.AppendLine("; AccDash.exe (default), then PATH");
            sb.AppendLine("FfmpegPath=" + FfmpegPath);
            sb.AppendLine("; force the capture region, x,y,w,h; empty = follow the game window");
            sb.AppendLine("VideoArea=" + (VideoArea.Width > 0
                ? VideoArea.X.ToString(Fmt.Inv) + "," + VideoArea.Y.ToString(Fmt.Inv) + ","
                  + VideoArea.Width.ToString(Fmt.Inv) + "," + VideoArea.Height.ToString(Fmt.Inv)
                : ""));
            sb.AppendLine("; force the ddagrab output (screen) index; -1 = follow the game window");
            sb.AppendLine("VideoOutput=" + VideoOutput.ToString(Fmt.Inv));
            File.WriteAllText(Path, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }

    static Rectangle ToRect(string s)
    {
        string[] f = s.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 4) return Rectangle.Empty;
        return new Rectangle(ToInt(f[0], 0), ToInt(f[1], 0), ToInt(f[2], 0), ToInt(f[3], 0));
    }

    static int ToInt(string s, int d) { int v; return int.TryParse(s, NumberStyles.Integer, Fmt.Inv, out v) ? v : d; }
    static double ToDouble(string s, double d) { double v; return double.TryParse(s, NumberStyles.Float, Fmt.Inv, out v) ? v : d; }
    static bool ToBool(string s, bool d)
    {
        s = s.ToLowerInvariant();
        if (s == "1" || s == "true" || s == "yes" || s == "on") return true;
        if (s == "0" || s == "false" || s == "no" || s == "off") return false;
        return d;
    }
}
