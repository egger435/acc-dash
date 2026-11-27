// ===========================================================================
//  AccDash - 640x360 12fps video source
//  live:    ffmpeg reads the game window at 12 fps and streams bgra frames
//  clip:    ffmpeg decodes an embedded lap clip; the frame shown is chosen by
//           the replay playhead, so the picture can never drift from the charts
//  src/Video/LiveCapture.cs
// ===========================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;


/// <summary>
/// Keeps one ffmpeg process turning video into BGRA frames the paint code can
/// blit without a copy.
///
/// In `synced` mode (lap replay) the reader decodes ahead as fast as ffmpeg can
/// and only publishes the frame the caller asked for through `Target`.  That is
/// what keeps the video glued to the telemetry: the picture is picked by the
/// playhead instead of by a decoder clock, so neither pausing nor fast
/// forwarding nor a slow frame can desynchronise it.
/// </summary>
sealed class LiveCapture : IVideoSource
{
    public const int DefFps = 24;                       // fallback only, Vid holds the real rate

    /// Size of the frames this decoder produces.  A lap file carries its own
    /// width / height, which need not match what this build records today, so
    /// the caller sets these before StartClip().
    public int W = Vid.W, H = Vid.H;

    int FrameBytes { get { return W * H * 4; } }

    Process p;
    Thread th;
    volatile bool run;
    int gen;

    readonly object gate = new object();
    byte[] mem;
    GCHandle pin;
    int slotWrite, slotReady = -1;
    int seq;
    double lastFrameAt = -1e9;

    /// <summary>Lap replay: publish only the frame whose number equals Target.</summary>
    public volatile bool Synced;

    /// <summary>Frame number the ui wants to see, -1 for "nothing yet".</summary>
    public volatile int Target = -1;

    /// <summary>Number of frames decoded so far (frame numbers are 0 based).</summary>
    int frameNo = -1;
    int frameBase;

    public string Error = "";
    public int Fps = Vid.Fps;
    public int Seq { get { return seq; } }
    public int FrameNo { get { return frameBase + frameNo; } }

    public bool Running
    {
        get { return p != null && run && SafeHasExited(p) == false; }
    }

    static bool SafeHasExited(Process q)
    {
        try { return q.HasExited; } catch { return true; }
    }

    /// <summary>
    /// Opens an embedded lap clip at `sec` seconds.  No pacing: the reader races
    /// ahead and the ui pulls the frame it needs, so pausing is free and a slow
    /// machine simply shows a lower frame rate instead of falling behind.
    /// </summary>
    public void StartClip(string file, float sec) { StartClip(file, sec, Vid.Fps); }

    /// `clipFps` is the frame rate stored in the lap file, which is not always
    /// the rate this build captures at: frame numbering has to follow the file.
    public void StartClip(string file, float sec, int clipFps)
    {
        if (!Ff.Ready)
        {
            Error = Ff.Error;
            return;
        }
        string at = Math.Max(0f, sec).ToString("0.000", Fmt.Inv);
        Synced = true;
        Target = -1;
        frameBase = (int)Math.Round(Math.Max(0f, sec) * (clipFps > 0 ? clipFps : Fps));
        // no scaling here: the clip keeps the size it was recorded at, and the
        // card letterboxes whatever comes out, so a lap recorded with another
        // VideoW/H/Fps still plays correctly
        StartPipe("-ss " + at + " -i \"" + file + "\"", "");
    }

    void StartPipe(string input, string chain)
    {
        Stop(false);        // never wait for the old decoder: this runs on seek
        string args = "-hide_banner -loglevel error -nostdin " + input
                    + (chain.Length > 0 ? " -vf \"" + chain + "\"" : "")
                    + " -f rawvideo -pix_fmt bgra pipe:1";
        p = Ff.Start(args, false, true);
        if (p == null) { Error = Ff.Error; return; }

        int need = FrameBytes * 2;
        if (mem == null || mem.Length != need)
        {
            if (pin.IsAllocated) pin.Free();
            mem = new byte[need];
            pin = GCHandle.Alloc(mem, GCHandleType.Pinned);
        }
        int myGen = ++gen;
        Error = "";
        run = true;
        frameNo = -1;
        th = new Thread(new ThreadStart(delegate { Reader(myGen); }));
        th.IsBackground = true;
        th.Name = "video";
        th.Start();
    }

    void Reader(int myGen)
    {
        byte[] frame = new byte[FrameBytes];
        try
        {
            Stream s = p.StandardOutput.BaseStream;
            while (run)
            {
                int want = Target;
                if (Synced)
                {
                    // nothing to show yet, or the playhead moved backwards and the
                    // caller still has to re-open the clip
                    if (want < 0 || want <= FrameNo) { Thread.Sleep(4); continue; }
                }
                int got = 0;
                while (got < FrameBytes)
                {
                    int n = s.Read(frame, got, FrameBytes - got);
                    if (n <= 0) return;
                    got += n;
                }
                if (myGen != gen) return;   // a newer decoder took over
                frameNo++;
                if (Synced && FrameNo != want) continue;   // decoding ahead, skip
                lock (gate)
                {
                    Buffer.BlockCopy(frame, 0, mem, slotWrite * FrameBytes, FrameBytes);
                    slotReady = slotWrite;
                    slotWrite = 1 - slotWrite;
                    seq++;
                    lastFrameAt = Clock.Now;
                }
            }
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    public void Stop() { Stop(true); }

    /// <summary>
    /// Tears the decoder down.  `wait` false kills it outright and does not join
    /// the reader: scrubbing restarts the player several times a second and
    /// waiting for a graceful shutdown there froze the ui for up to a second.
    /// </summary>
    public void Stop(bool wait)
    {
        run = false;
        Process q = p;
        p = null;
        if (q != null)
        {
            if (wait) Ff.Stop(q, 300);
            else { try { q.Kill(); } catch { } }
        }
        if (th != null && wait) { try { th.Join(250); } catch { } }
        if (wait) lock (gate) { slotReady = -1; }
        th = null;
    }

    /// <summary>Draws the newest published frame into `dst` (already fitted to 16:9).</summary>
    public bool Draw(Graphics g, Rectangle dst)
    {
        lock (gate)
        {
            if (slotReady < 0) return false;
            IntPtr scan = IntPtr.Add(pin.AddrOfPinnedObject(), slotReady * FrameBytes);
            using (Bitmap bmp = new Bitmap(W, H, W * 4, PixelFormat.Format32bppRgb, scan))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g.DrawImage(bmp, dst);
            }
            return true;
        }
    }

    public double FrameAgeMs
    {
        get { return lastFrameAt < -1e8 ? 1e9 : Clock.Now - lastFrameAt; }
    }
}
