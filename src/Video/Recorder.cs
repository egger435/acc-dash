// ===========================================================================
//  AccDash - 1 s mkv chunk recorder and per-lap clip builder
//  src/Video/Recorder.cs
//
//  Takes BGRA frames at the size set in the ini from FrameGrabber (window capture) and pipes them
//  into one ffmpeg that cuts them into 1 s mkv chunks.  Chunks are already
//  finalised, so a lap can be cut out of them the moment the car crosses the
//  line - no process needs to start at the line itself.
// ===========================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;


sealed class Recorder
{
    static int Fps { get { return Vid.Fps; } }
    static int W { get { return Vid.W; } }
    static int H { get { return Vid.H; } }
    static int FrameBytes { get { return Vid.FrameBytes; } }

    public sealed class Run
    {
        public string Dir;
        public double Start;      // wall clock, ms
    }

    readonly object gate = new object();
    readonly object qGate = new object();
    readonly List<Run> runs = new List<Run>();
    readonly Queue<byte[]> q = new Queue<byte[]>();
    byte[] latest;                 // newest frame, repeated when the capture is slower
    bool hasLatest;
    public int FramesIn, FramesOut, Dupes;
    Process p;
    Thread writer;
    volatile bool run;
    volatile bool firstFrame;
    Run cur;
    int runIndex;
    double lastBegin = -1e9;
    public int Crf = 26;
    public string Error = "";
    public int Dropped;                 // frames the writer could not keep up with
    public double LastFrameMean;        // brightness of the newest frame, for --captest
    public bool On { get { return p != null; } }

    int pendingClips;
    double pendingFrom = double.MaxValue;

    /// Marks a clip that is being built: the trimmer must keep those chunks
    /// until ClipDone(), even though the next lap has already started.
    public void ClipPending(double t0)
    {
        lock (gate)
        {
            pendingClips++;
            if (t0 < pendingFrom) pendingFrom = t0;
        }
    }

    public void ClipDone()
    {
        lock (gate)
        {
            pendingClips--;
            if (pendingClips <= 0) { pendingClips = 0; pendingFrom = double.MaxValue; }
        }
    }

    public static string Root
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"cache\video"); }
    }

    /// Starts the chunk writer.  Frames arrive through Push().
    public void Begin()
    {
        lock (gate)
        {
            if (!Ff.Ready) { Error = Ff.Error; return; }
            if (p != null) return;
            try
            {
                if (runIndex == 0) ResetCache();
                runIndex++;
                string dir = Path.Combine(Root, "r" + runIndex.ToString(Fmt.Inv));
                Directory.CreateDirectory(dir);
                string pattern = Path.Combine(dir, "seg_%05d.mkv");

                string args = "-hide_banner -loglevel error"
                            + " -f rawvideo -pixel_format bgra -video_size " + W + "x" + H
                            + " -framerate " + Fps.ToString(Fmt.Inv) + " -i -"
                            + " -c:v libx264 -preset ultrafast -crf " + Crf.ToString(Fmt.Inv)
                            + " -g " + Fps.ToString(Fmt.Inv) + " -pix_fmt yuv420p -an"
                            + " -f segment -segment_time 1 -reset_timestamps 1 -segment_format mkv"
                            + " \"" + pattern + "\"";
                lastBegin = Clock.Now;
                p = Ff.Start(args, true, false);
                if (p == null) { Error = Ff.Error; return; }

                lock (qGate) q.Clear();
                cur = new Run();
                cur.Dir = dir;
                cur.Start = Clock.Now;
                runs.Add(cur);
                Error = "";
                firstFrame = true;
                run = true;
                writer = new Thread(WriteLoop);
                writer.IsBackground = true;
                writer.Name = "rec-writer";
                writer.Start();
            }
            catch (Exception ex) { Error = ex.Message; p = null; cur = null; }
        }
    }

    public void End()
    {
        lock (gate) StopLocked();
    }

    void StopLocked()
    {
        Process q0 = p;
        p = null;
        cur = null;
        run = false;
        Thread w = writer;
        writer = null;
        if (w != null) { try { w.Join(500); } catch { } }
        lock (qGate) q.Clear();
        if (q0 != null) Ff.Stop(q0, 1500);
    }

    /// A finished frame from the grabber.  Copies it, because the grabber reuses
    /// its buffers on the next tick.  The writer decides pacing, so a slow
    /// capture repeats the newest frame instead of shortening the timeline.
    public void Push(byte[] src, int offset)
    {
        if (!run || p == null) return;
        byte[] copy = new byte[FrameBytes];
        Buffer.BlockCopy(src, offset, copy, 0, FrameBytes);
        lock (qGate) { latest = copy; hasLatest = true; }
        FramesIn++;
        // cheap sanity signal: a frame that is entirely black means the capture,
        // not the encoder, is at fault
        long sum = 0;
        for (int i = 0; i < FrameBytes; i += 4096) sum += copy[i] + copy[i + 1] + copy[i + 2];
        LastFrameMean = sum / (double)((FrameBytes / 4096) * 3);
    }

    /// Writes exactly Fps frames per second of wall clock.  Capture speed and
    /// encode rate are decoupled: slow capture duplicates the last frame, fast
    /// capture drops the extras, so video time always equals lap time.
    void WriteLoop()
    {
        try
        {
            Stream s = p.StandardInput.BaseStream;
            double start = -1;
            long idx = 0;
            while (run)
            {
                if (start < 0)
                {
                    bool any;
                    lock (qGate) any = hasLatest;
                    if (!any) { Thread.Sleep(2); continue; }
                    lock (qGate) hasLatest = false;
                    start = Clock.Now;
                    lock (gate) { if (cur != null) cur.Start = start; }
                }

                double now = Clock.Now;
                long due = (long)Math.Floor((now - start) * Fps / 1000.0);
                byte[] f;
                lock (qGate) { f = latest; hasLatest = false; }
                if (f == null)
                {
                    lock (qGate) f = latest;
                    if (f != null) Dupes++;
                }
                if (f == null) { Thread.Sleep(2); continue; }

                for (; idx <= due; idx++) { s.Write(f, 0, f.Length); FramesOut++; }
                // a long stall must not be back-filled frame by frame
                if ((now - start) * Fps / 1000.0 - idx > Fps) idx = (long)((now - start) * Fps / 1000.0);
            }
            try { s.Flush(); } catch { }
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    /// Keeps the chunks coming while the car is on track.
    public void Tick(bool want)
    {
        if (!want) { if (p != null) End(); return; }
        if (p == null)
        {
            // a broken ffmpeg must not be respawned on every frame
            if (Clock.Now - lastBegin < 3000) return;
            Begin();
            return;
        }
        bool dead;
        try { dead = p.HasExited; } catch { dead = true; }
        if (dead) { End(); Begin(); }
    }

    void ResetCache()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
        }
        catch { }
    }

    /// Drops chunks no lap can still need.  `keepFromMs` is the wall clock of the
    /// lap being recorded right now.
    public void Trim(double keepFromMs)
    {
        lock (gate)
        {
            double keep = keepFromMs;
            if (pendingClips > 0 && pendingFrom < keep) keep = pendingFrom;
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                Run r = runs[i];
                string[] files;
                try { files = Directory.Exists(r.Dir) ? Directory.GetFiles(r.Dir, "seg_*.mkv") : new string[0]; }
                catch { continue; }
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string f in files)
                {
                    double segStart = r.Start + SegIndex(f) * 1000.0;
                    if (segStart + 3000 < keep)
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
        }
    }

    public static void Log(string s)
    {
        try
        {
            Directory.CreateDirectory(Root);
            File.AppendAllText(Path.Combine(Root, "clip.log"), DateTime.Now.ToString("HH:mm:ss", Fmt.Inv) + "  " + s + Environment.NewLine);
        }
        catch { }
    }

    static int SegIndex(string path)
    {
        string n = Path.GetFileNameWithoutExtension(path);
        int u = n.LastIndexOf('_');
        int v;
        if (u >= 0 && int.TryParse(n.Substring(u + 1), NumberStyles.Integer, Fmt.Inv, out v)) return v;
        return 0;
    }

    /// Copies out the chunks overlapping [t0,t1] (wall clock ms), oldest first.
    List<KeyValuePair<string, double>> Snapshot(double t0, double t1)
    {
        List<KeyValuePair<string, double>> got = new List<KeyValuePair<string, double>>();
        lock (gate)
        {
            foreach (Run r in runs)
            {
                if (!Directory.Exists(r.Dir)) continue;
                string[] files;
                try { files = Directory.GetFiles(r.Dir, "seg_*.mkv"); }
                catch { continue; }
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string f in files)
                {
                    long len;
                    try { len = new FileInfo(f).Length; } catch { continue; }
                    if (len < 2000) continue;                     // half-written chunk
                    double segStart = r.Start + SegIndex(f) * 1000.0;
                    if (segStart + 1000 <= t0) continue;          // entirely before the lap
                    if (segStart >= t1) continue;                 // entirely after it
                    got.Add(new KeyValuePair<string, double>(f, segStart));
                }
            }
        }
        got.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return a.Value.CompareTo(b.Value); });
        return got;
    }

    /// Cuts [t0,t1] (wall clock ms) out of the chunks and returns a small mp4.
    /// `clipT0` is the lap time at which that clip starts, `clipDur` its length.
    public byte[] Clip(double t0, double t1, out float clipT0, out float clipDur)
    {
        clipT0 = 0f;
        clipDur = 0f;
        if (t1 - t0 < 1000) return null;

        List<KeyValuePair<string, double>> segs = Snapshot(t0, t1);
        if (segs.Count == 0) { Log("no chunks left for this lap"); return null; }

        double firstStart = segs[0].Value;
        double videoStart = Math.Max(t0, firstStart);
        double dur = (t1 - videoStart) / 1000.0;
        if (dur < 0.5) return null;

        Log("clip t0=" + t0.ToString("0", Fmt.Inv) + " t1=" + t1.ToString("0", Fmt.Inv)
            + " segs=" + segs.Count.ToString(Fmt.Inv)
            + " first=" + (segs.Count > 0 ? segs[0].Value.ToString("0", Fmt.Inv) : "-")
            + " last=" + (segs.Count > 0 ? segs[segs.Count - 1].Value.ToString("0", Fmt.Inv) : "-")
            + " dur=" + dur.ToString("0.000", Fmt.Inv));
        string tmp = Path.Combine(Root, "clip_" + DateTime.Now.Ticks.ToString(Fmt.Inv));
        try
        {
            Directory.CreateDirectory(tmp);
            string lst = Path.Combine(tmp, "list.txt");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, double> kv in segs) sb.AppendLine("file '" + kv.Key.Replace("'", "'\\''") + "'");
            File.WriteAllText(lst, sb.ToString(), new UTF8Encoding(false));

            string outFile = Path.Combine(tmp, "clip.mp4");
            double off = (videoStart - firstStart) / 1000.0;   // into the concat timeline
            string args = "-hide_banner -loglevel error -nostdin -f concat -safe 0 -i \"" + lst + "\""
                 + " -ss " + off.ToString("0.000", Fmt.Inv)
                 + " -t " + dur.ToString("0.000", Fmt.Inv)
                 + " -vf \"fps=" + Fps.ToString(Fmt.Inv) + ",setpts=PTS-STARTPTS\""
                 + " -c:v libx264 -preset veryfast -crf " + Crf.ToString(Fmt.Inv)
                 + " -g " + Fps.ToString(Fmt.Inv) + " -pix_fmt yuv420p -an -y \"" + outFile + "\"";
            string log = Ff.Run(args, 120000);

            byte[] bytes = null;
            if (File.Exists(outFile))
            {
                FileInfo fi = new FileInfo(outFile);
                if (fi.Length > 1024) bytes = File.ReadAllBytes(outFile);
            }
            if (bytes == null && log.Length > 0) { Error = log; Log("ffmpeg: " + log); }
            else Log("ok bytes=" + (bytes == null ? 0 : bytes.Length) + " crf=" + Crf);
            if (bytes == null) return null;

            clipT0 = (float)((videoStart - t0) / 1000.0);
            clipDur = (float)dur;
            // trust the produced file, not the requested window: a missing chunk
            // at the end used to give the lap a duration it did not have, which
            // made the replay run ahead of the charts
            double realDur; int realFrames;
            if (Ff.Measure(outFile, out realDur, out realFrames) && realDur > 0.1)
            {
                clipDur = (float)realDur;
                Log("measured dur=" + realDur.ToString("0.000", Fmt.Inv) + " frames=" + realFrames.ToString(Fmt.Inv));
            }
            return bytes;
        }
        catch (Exception ex) { Error = ex.Message; return null; }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }
}
