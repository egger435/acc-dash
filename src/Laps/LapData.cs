// ===========================================================================
//  AccDash - lap sample, lap data and file format
//  src/Laps/LapData.cs
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
//  Lap recording, file format, scrubbable review
// ---------------------------------------------------------------------------

/// One sample of a recorded lap; times are seconds from the lap start.
sealed class LapSample
{
    public float T;
    public float Gas, Brake, Steer, Ff;
    public float Speed;
    public int Gear, Rpm;
    public float Lat, Long;
    public float Fuel, TyreF, TyreR;
    public float X, Z, Head;      // world position, for the track map
    public float S;               // 0..1 along the track, ACC's normalized position

    public LapSample Clone()
    {
        return (LapSample)MemberwiseClone();
    }
}

/// A whole lap: metadata plus the sample series.
sealed class LapData
{
    public string Track = "", Car = "";
    public int LapNo;
    public float LapMs;
    public int MaxRpm;
    public float AirTemp, RoadTemp;
    public int Grip = -1;
    public DateTime When = DateTime.Now;
    public bool FromFile;
    public string Path = "";
    public List<LapSample> Samples = new List<LapSample>();

    // --- embedded lap video (640x360, 12 fps, h264)
    public byte[] VideoBytes;
    public int VideoW = 320, VideoH = 180, VideoFps = 24;
    public float VideoMs;          // clip length, ms
    public float VideoT0;          // lap time (s) at which the clip starts

    // --- wall clock window, only meaningful while recording
    public double WallT0;          // ms, wall clock when the lap started
    public double WallT1;          // ms, wall clock when it ended

    public bool HasVideo { get { return VideoBytes != null && VideoBytes.Length > 1024; } }


    // Track names come back from ACC as "spa", "brands_hatch", "Brands Hatch"...

    public static string NormTrack(string t0)
    {
        if (t0 == null) return "";
        string t = t0.Trim().ToLowerInvariant().Replace('_', ' ');
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        return t;
    }

    public static bool SameTrack(string a, string b)
    {
        string x = NormTrack(a), y = NormTrack(b);
        return x.Length > 0 && x == y;
    }

    public float LastT
    {
        get { return Samples.Count == 0 ? 0f : Samples[Samples.Count - 1].T; }
    }

    /// Time axis used for drawing and scrubbing: the stamped lap time when it
    /// looks sane, otherwise the time actually covered by the samples.
    public float Span
    {
        get
        {
            float end = LastT;
            if (LapMs > 500f && Math.Abs(LapMs / 1000f - end) < 8f) return LapMs / 1000f;
            return Math.Max(0.5f, end);
        }
    }

    public string Name
    {
        get
        {
            string n = "L" + LapNo.ToString(Fmt.Inv).PadLeft(2, ' ') + "  " + Fmt.Mmss((int)LapMs);
            if (!string.IsNullOrEmpty(Track)) n += "  " + Track;
            if (HasVideo) n += "  [video]";
            if (FromFile) n += "  [file]";
            return n;
        }
    }

    public void Add(float t, float gas, float brake, float steer, float ff, float speed, int gear, int rpm, float lat, float lon, float fuel, float tyreF, float tyreR, float x, float z, float head, float prog)
    {
        LapSample s = new LapSample();
        s.T = t; s.Gas = gas; s.Brake = brake; s.Steer = steer; s.Ff = ff;
        s.Speed = speed; s.Gear = gear; s.Rpm = rpm; s.Lat = lat; s.Long = lon;
        s.Fuel = fuel; s.TyreF = tyreF; s.TyreR = tyreR;
        s.X = x; s.Z = z; s.Head = head; s.S = prog;
        Samples.Add(s);
    }

    /// Linear interpolation; returns the sample nearest to t when out of range.
    public LapSample At(float t)
    {
        if (Samples.Count == 0) return null;
        if (t <= Samples[0].T) return Samples[0];
        if (t >= LastT) return Samples[Samples.Count - 1];
        int lo = 0, hi = Samples.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (Samples[mid].T <= t) lo = mid; else hi = mid;
        }
        LapSample a = Samples[lo], b = Samples[hi];
        float span = b.T - a.T;
        float u = span > 0.0001f ? (t - a.T) / span : 0f;
        LapSample r = new LapSample();
        r.T = t;
        r.Gas = a.Gas + (b.Gas - a.Gas) * u;
        r.Brake = a.Brake + (b.Brake - a.Brake) * u;
        r.Steer = a.Steer + (b.Steer - a.Steer) * u;
        r.Ff = a.Ff + (b.Ff - a.Ff) * u;
        r.Speed = a.Speed + (b.Speed - a.Speed) * u;
        r.Lat = a.Lat + (b.Lat - a.Lat) * u;
        r.Long = a.Long + (b.Long - a.Long) * u;
        r.Fuel = a.Fuel + (b.Fuel - a.Fuel) * u;
        r.TyreF = a.TyreF + (b.TyreF - a.TyreF) * u;
        r.TyreR = a.TyreR + (b.TyreR - a.TyreR) * u;
        r.X = a.X + (b.X - a.X) * u;
        r.Z = a.Z + (b.Z - a.Z) * u;
        r.S = a.S + (b.S - a.S) * u;
        float dh = b.Head - a.Head;
        while (dh > (float)Math.PI) dh -= (float)(Math.PI * 2.0);
        while (dh < -(float)Math.PI) dh += (float)(Math.PI * 2.0);
        r.Head = a.Head + dh * u;
        r.Gear = u < 0.5f ? a.Gear : b.Gear;
        r.Rpm = (int)(a.Rpm + (b.Rpm - a.Rpm) * u);
        return r;
    }

    string HeadText()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# accdash lap v3");
        sb.AppendLine("track=" + Track);
        sb.AppendLine("car=" + Car);
        sb.AppendLine("lap=" + LapNo.ToString(Fmt.Inv));
        sb.AppendLine("time_ms=" + ((int)LapMs).ToString(Fmt.Inv));
        sb.AppendLine("when=" + When.ToString("yyyy-MM-dd HH:mm:ss", Fmt.Inv));
        sb.AppendLine("maxrpm=" + MaxRpm.ToString(Fmt.Inv));
        sb.AppendLine("air=" + AirTemp.ToString("0.0", Fmt.Inv));
        sb.AppendLine("road=" + RoadTemp.ToString("0.0", Fmt.Inv));
        sb.AppendLine("grip=" + Grip.ToString(Fmt.Inv));
        sb.AppendLine("vfps=" + VideoFps.ToString(Fmt.Inv));
        sb.AppendLine("vt0=" + VideoT0.ToString("0.000", Fmt.Inv));
        sb.AppendLine("vms=" + VideoMs.ToString("0", Fmt.Inv));
        return sb.ToString();
    }

    string CsvText()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("t,gas,brake,steer,ff,speed,gear,rpm,lat,long,fuel,tyref,tyrer,x,z,head,s");
        for (int i = 0; i < Samples.Count; i++)
        {
            LapSample s = Samples[i];
            sb.AppendLine(string.Format(Fmt.Inv, "{0:0.000},{1:0.000},{2:0.000},{3:0.000},{4:0.000},{5:0.0},{6},{7},{8:0.000},{9:0.000},{10:0.00},{11:0.0},{12:0.0},{13:0.00},{14:0.00},{15:0.000},{16:0.0000}",
                s.T, s.Gas, s.Brake, s.Steer, s.Ff, s.Speed, s.Gear, s.Rpm, s.Lat, s.Long, s.Fuel, s.TyreF, s.TyreR, s.X, s.Z, s.Head, s.S));
        }
        return sb.ToString();
    }

    /// Plain text, no video: still used by the debug tools and by hand.
    public string ToText()
    {
        return HeadText() + CsvText();
    }

    static readonly byte[] Magic = Encoding.ASCII.GetBytes("ACCDASH3");

    /// Binary container: magic, header text, the mp4 itself, then the samples.
    public byte[] ToBytes()
    {
        byte[] head = Encoding.UTF8.GetBytes(HeadText());
        byte[] csv = Encoding.UTF8.GetBytes(CsvText());
        byte[] vid = VideoBytes == null ? new byte[0] : VideoBytes;
        using (MemoryStream ms = new MemoryStream(csv.Length + vid.Length + head.Length + 64))
        using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8))
        {
            w.Write(Magic);
            w.Write(head.Length); w.Write(head);
            w.Write(vid.Length); w.Write(vid);
            w.Write(csv.Length); w.Write(csv);
            w.Flush();
            return ms.ToArray();
        }
    }

    public static LapData Parse(byte[] data, string path, out string err)
    {
        err = "";
        if (data == null || data.Length < 16) { err = "file is empty"; return null; }
        bool bin = true;
        for (int i = 0; i < Magic.Length && bin; i++) if (i >= data.Length || data[i] != Magic[i]) bin = false;
        if (!bin) return Parse(Encoding.UTF8.GetString(data), path, out err);
        try
        {
            using (MemoryStream ms = new MemoryStream(data))
            using (BinaryReader r = new BinaryReader(ms, Encoding.UTF8))
            {
                r.ReadBytes(Magic.Length);
                int hl = r.ReadInt32(); string head = Encoding.UTF8.GetString(r.ReadBytes(hl));
                int vl = r.ReadInt32(); byte[] vid = r.ReadBytes(vl);
                int cl = r.ReadInt32(); string csv = Encoding.UTF8.GetString(r.ReadBytes(cl));
                LapData d = Parse(head + "\n" + csv, path, out err);
                if (d == null) return null;
                if (vl > 1024)
                {
                    d.VideoBytes = vid;
                    d.FromFile = true;
                    d.Path = path ?? "";
                }
                return d;
            }
        }
        catch (Exception ex) { err = ex.Message; return null; }
    }

    public static LapData Parse(string text, string path, out string err)
    {
        err = "";
        LapData d = new LapData();
        d.FromFile = true;
        d.Path = path ?? "";
        bool inData = false;
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string ln = lines[i].Trim();
            if (ln.Length == 0) continue;
            if (!inData)
            {
                if (ln.StartsWith("#")) continue;
                int eq = ln.IndexOf('=');
                if (eq > 0)
                {
                    string k = ln.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = ln.Substring(eq + 1).Trim();
                    if (k == "track") d.Track = v;
                    else if (k == "car") d.Car = v;
                    else if (k == "lap") { int n; if (int.TryParse(v, out n)) d.LapNo = n; }
                    else if (k == "time_ms") { int n; if (int.TryParse(v, out n)) d.LapMs = n; }
                    else if (k == "when") { DateTime w; if (DateTime.TryParse(v, out w)) d.When = w; }
                    else if (k == "maxrpm") { int n; if (int.TryParse(v, out n)) d.MaxRpm = n; }
                    else if (k == "air") { float n; if (float.TryParse(v, System.Globalization.NumberStyles.Float, Fmt.Inv, out n)) d.AirTemp = n; }
                    else if (k == "road") { float n; if (float.TryParse(v, System.Globalization.NumberStyles.Float, Fmt.Inv, out n)) d.RoadTemp = n; }
                    else if (k == "grip") { int n; if (int.TryParse(v, out n)) d.Grip = n; }
                    else if (k == "vfps") { int n; if (int.TryParse(v, out n) && n > 0) d.VideoFps = n; }
                    else if (k == "vt0") { float n; if (float.TryParse(v, NumberStyles.Float, Fmt.Inv, out n)) d.VideoT0 = n; }
                    else if (k == "vms") { float n; if (float.TryParse(v, NumberStyles.Float, Fmt.Inv, out n)) d.VideoMs = n; }
                    continue;
                }
                if (ln.IndexOf("t,gas", StringComparison.OrdinalIgnoreCase) == 0) { inData = true; continue; }
                continue;   // unknown header line
            }

            string[] f = ln.Split(',');
            if (f.Length < 10) continue;
            float t, gas, brake, steer, ff, speed, lat, lon;
            int gear, rpm;
            if (!float.TryParse(f[0], System.Globalization.NumberStyles.Float, Fmt.Inv, out t)) continue;
            if (!float.TryParse(f[1], System.Globalization.NumberStyles.Float, Fmt.Inv, out gas)) continue;
            if (!float.TryParse(f[2], System.Globalization.NumberStyles.Float, Fmt.Inv, out brake)) continue;
            if (!float.TryParse(f[3], System.Globalization.NumberStyles.Float, Fmt.Inv, out steer)) continue;
            if (!float.TryParse(f[4], System.Globalization.NumberStyles.Float, Fmt.Inv, out ff)) continue;
            if (!float.TryParse(f[5], System.Globalization.NumberStyles.Float, Fmt.Inv, out speed)) continue;
            if (!int.TryParse(f[6], System.Globalization.NumberStyles.Integer, Fmt.Inv, out gear)) continue;
            if (!int.TryParse(f[7], System.Globalization.NumberStyles.Integer, Fmt.Inv, out rpm)) continue;
            if (!float.TryParse(f[8], System.Globalization.NumberStyles.Float, Fmt.Inv, out lat)) continue;
            if (!float.TryParse(f[9], System.Globalization.NumberStyles.Float, Fmt.Inv, out lon)) continue;
            float fuel = 0f, tyref = 0f, tyrer = 0f;
            if (f.Length > 12)
            {
                float.TryParse(f[10], System.Globalization.NumberStyles.Float, Fmt.Inv, out fuel);
                float.TryParse(f[11], System.Globalization.NumberStyles.Float, Fmt.Inv, out tyref);
                float.TryParse(f[12], System.Globalization.NumberStyles.Float, Fmt.Inv, out tyrer);
            }
            float px = 0f, pz = 0f, phd = 0f, prog = 0f;
            if (f.Length > 15)
            {
                float.TryParse(f[13], System.Globalization.NumberStyles.Float, Fmt.Inv, out px);
                float.TryParse(f[14], System.Globalization.NumberStyles.Float, Fmt.Inv, out pz);
                float.TryParse(f[15], System.Globalization.NumberStyles.Float, Fmt.Inv, out phd);
            }
            if (f.Length > 16)
                float.TryParse(f[16], System.Globalization.NumberStyles.Float, Fmt.Inv, out prog);
            d.Add(t, gas, brake, steer, ff, speed, gear, rpm, lat, lon, fuel, tyref, tyrer, px, pz, phd, prog);
        }

        if (d.Samples.Count < 2)
        {
            err = "no lap samples found";
            return null;
        }
        if (d.LapMs <= 0f) d.LapMs = d.LastT * 1000f;
        if (string.IsNullOrEmpty(d.Track)) d.Track = "unknown";
        return d;
    }
}

/// Horizontal scrubber used to move through a recorded lap.
sealed class Scrub : Pane
{
    public float Max = 100f;
    public float Pos;
    public string Title = "";
    public string RightText = "";
    public event EventHandler Moved;

    bool drag;

    public Scrub()
    {
        TabStop = false;
        Height = 50;
    }

    Rectangle BarRect
    {
        get { return new Rectangle(12, Height - 14, Math.Max(20, Width - 24), 6); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        G.Box(g, new Rectangle(0, 0, Width, Height), Pal.Panel, Pal.Line);

        // Text lives in its own band above the bar; the knob used to be painted
        // on top of the "LAP 7 ..." caption.
        Rectangle text = new Rectangle(10, 0, Math.Max(20, Width - 20), 26);
        G.TextMidL(g, Title, F.Cap, Pal.Dim, text, 2f);
        string tstr = Fmt.Mmss((int)(Pos * 1000f)) + " / " + Fmt.Mmss((int)(Max * 1000f));
        G.TextMidR(g, tstr, F.CapS, Pal.Dim, text, 2f);

        Rectangle bar = BarRect;
        using (SolidBrush b = new SolidBrush(Color.FromArgb(34, 34, 34)))
            g.FillRectangle(b, bar);

        float frac = Max > 0.001f ? Math.Max(0f, Math.Min(1f, Pos / Max)) : 0f;
        int fillW = (int)(bar.Width * frac);
        if (fillW > 0)
            using (SolidBrush b = new SolidBrush(Color.FromArgb(104, 104, 104)))
                g.FillRectangle(b, bar.X, bar.Y, fillW, bar.Height);

        using (Pen p = new Pen(Pal.Line))
            g.DrawRectangle(p, bar.X, bar.Y, bar.Width - 1, bar.Height - 1);

        int marks = (int)(Max + 0.5f);
        int step = marks > 120 ? 30 : (marks > 60 ? 20 : (marks > 20 ? 5 : 2));
        for (int t = 0; t <= marks; t += step)
        {
            int x = bar.X + (int)(bar.Width * (Max > 0.001f ? t / Max : 0f));
            G.Line(g, Color.FromArgb(56, 56, 56), 1f, x, bar.Bottom + 2, x, bar.Bottom + 4);
        }

        int cx = bar.X + (int)(bar.Width * frac);
        G.Line(g, Pal.Text, 2f, cx, bar.Y - 6, cx, bar.Bottom + 2);
        using (SolidBrush b = new SolidBrush(Pal.Text))
            g.FillEllipse(b, cx - 4, bar.Y - 9, 8, 8);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        drag = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        drag = false;
    }

    void SetFromX(int x)
    {
        Rectangle bar = BarRect;
        float u = bar.Width <= 0 ? 0f : (float)(x - bar.X) / bar.Width;
        u = Math.Max(0f, Math.Min(1f, u));
        Pos = u * Max;
        Invalidate();
        if (Moved != null) Moved(this, EventArgs.Empty);
    }
}
