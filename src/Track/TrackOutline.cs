// ===========================================================================
//  AccDash - learned track outline and its file format
//  src/Track/TrackOutline.cs
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
//  Track outline: world-space polyline, learned while driving and cached on disk
//  as  tracks\<track>[-<config>].map
// ---------------------------------------------------------------------------
sealed class TrackOutline
{
    public string Track = "", Config = "";

    public float LengthM;
    public DateTime When = DateTime.Now;
    public bool Closed;
    public List<PointF> Points = new List<PointF>();

    public static string Dir
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tracks"); }
    }

    /// File-name safe key, so "Spa" + "" becomes "spa".
    public static string Key(string track, string config)
    {
        string t = string.IsNullOrEmpty(track) ? "unknown" : track.Trim();
        string c = config == null ? "" : config.Trim();
        // ACC writes the literal "track config" for tracks without a layout variant;
        // treating it as a real config would give the same track two different keys.
        if (c.Equals("track config", StringComparison.OrdinalIgnoreCase) ||
            c.Equals("track configuration", StringComparison.OrdinalIgnoreCase) ||
            c == "-") c = "";
        string s = (c.Length > 0 ? t + "-" + c : t).ToLowerInvariant();
        StringBuilder sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            bool ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_';
            sb.Append(ok ? ch : '_');
        }
        return sb.ToString();
    }

    public static string PathFor(string key) { return Path.Combine(Dir, key + ".map"); }

    /// Unfinished outlines are parked here so closing the panel (or crashing) never
    /// loses the lap you are busy learning.
    public static string PartialFor(string key) { return Path.Combine(Dir, key + ".partial.map"); }

    public void AddPoint(float x, float z)
    {
        if (Points.Count > 0)
        {
            PointF p = Points[Points.Count - 1];
            float dx = x - p.X, dz = z - p.Y;
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d < 0.4f) return;
            LengthM += d;
        }
        Points.Add(new PointF(x, z));
    }

    /// Index of the sampled point closest to (x, z), or -1 when there are none.
    public int NearestIndex(float x, float z)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < Points.Count; i++)
        {
            float dx = Points[i].X - x, dz = Points[i].Y - z;
            float d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// Re-start the outline at the given point, so the recorded line begins at the
    /// start/finish line instead of wherever the panel happened to start sampling.
    public void RotateTo(int idx)
    {
        if (idx <= 0 || idx >= Points.Count) return;
        List<PointF> ordered = new List<PointF>(Points.Count);
        for (int i = 0; i < Points.Count; i++) ordered.Add(Points[(idx + i) % Points.Count]);
        Points = ordered;
        RecomputeLength();
    }

    public void RecomputeLength()
    {
        LengthM = 0f;
        for (int i = 1; i < Points.Count; i++)
        {
            float dx = Points[i].X - Points[i - 1].X, dz = Points[i].Y - Points[i - 1].Y;
            LengthM += (float)Math.Sqrt(dx * dx + dz * dz);
        }
        if (Closed && Points.Count > 1)
        {
            float dx = Points[0].X - Points[Points.Count - 1].X, dz = Points[0].Y - Points[Points.Count - 1].Y;
            LengthM += (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    /// A lap just completed: close the loop when the two ends meet.
    public void CloseLoop()
    {
        if (Points.Count < 16) return;
        PointF a = Points[0], b = Points[Points.Count - 1];
        float dx = a.X - b.X, dz = a.Y - b.Y;
        if (Math.Sqrt(dx * dx + dz * dz) < 80f) Closed = true;
        When = DateTime.Now;
    }

    public void Save(string file)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(file, ToText(), new UTF8Encoding(false));
        }
        catch { }
    }

    public static TrackOutline Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            return Parse(File.ReadAllText(file));
        }
        catch { return null; }
    }


    public string ToText()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# accdash track v1");
        sb.AppendLine("track=" + Track);
        sb.AppendLine("config=" + Config);
        sb.AppendLine("length=" + LengthM.ToString("0.0", Fmt.Inv));
        sb.AppendLine("closed=" + (Closed ? "1" : "0"));
        sb.AppendLine("when=" + When.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv));
        sb.AppendLine("x,z");
        for (int i = 0; i < Points.Count; i++)
            sb.AppendLine(string.Format(Fmt.Inv, "{0:0.00},{1:0.00}", Points[i].X, Points[i].Y));
        return sb.ToString();
    }

    public static TrackOutline Parse(string text)
    {
        TrackOutline o = new TrackOutline();
        bool data = false;
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string ln = lines[i].Trim();
            if (ln.Length == 0) continue;
            if (!data)
            {
                if (ln.StartsWith("#")) continue;
                int eq = ln.IndexOf('=');
                if (eq > 0)
                {
                    string k = ln.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = ln.Substring(eq + 1).Trim();
                    if (k == "track") o.Track = v;
                    else if (k == "config") o.Config = v;
                    else if (k == "length") { float f; if (float.TryParse(v, System.Globalization.NumberStyles.Float, Fmt.Inv, out f)) o.LengthM = f; }
                    else if (k == "closed") o.Closed = v == "1";
                    continue;
                }
                if (ln.IndexOf("x,", StringComparison.OrdinalIgnoreCase) == 0) { data = true; continue; }
                continue;
            }
            int comma = ln.IndexOf(',');
            if (comma <= 0) continue;
            float x, z;
            if (!float.TryParse(ln.Substring(0, comma), System.Globalization.NumberStyles.Float, Fmt.Inv, out x)) continue;
            if (!float.TryParse(ln.Substring(comma + 1), System.Globalization.NumberStyles.Float, Fmt.Inv, out z)) continue;
            o.Points.Add(new PointF(x, z));
        }
        if (o.Points.Count < 8) return null;
        return o;
    }
}
