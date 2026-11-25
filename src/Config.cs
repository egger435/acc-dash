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
    public int LongGSign = -1;           // +1 / -1 : which way the LONG G tile reads

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
                    case "longgsign": c.LongGSign = ToInt(v, -1) >= 0 ? 1 : -1; break;
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
            sb.AppendLine("; sign of the LONG G tile and the G-G vertical axis");
            sb.AppendLine(";   -1 = braking reads negative (default), +1 = braking reads positive");
            sb.AppendLine("LongGSign=" + LongGSign.ToString(Fmt.Inv));
            sb.AppendLine("[source]");
            File.WriteAllText(Path, sb.ToString(), Encoding.UTF8);
        }
        catch { }
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
