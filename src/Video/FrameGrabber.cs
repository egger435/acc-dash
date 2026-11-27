// ===========================================================================
//  AccDash - window capture at 640x360 / 12 fps
//  src/Video/FrameGrabber.cs
//
//  Captures the game *window* (not a screen region) with PrintWindow and
//  PW_RENDERFULLCONTENT.  That keeps working when the dashboard overlaps the
//  game, when the game sits on a second monitor, and when its position or
//  resolution changes - the three cases where a screen grab shows the desktop
//  or the dashboard itself.
// ===========================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;


sealed class FrameGrabber : IVideoSource
{
    static int W { get { return Vid.W; } }
    static int H { get { return Vid.H; } }
    static int FrameBytes { get { return Vid.FrameBytes; } }

    public IntPtr Window;
    public int Fps = Vid.Fps;

    // Screen fallback, used when the window cannot be printed (a process with a
    // restricted token, or a game that refuses WM_PRINT).  Without it the card
    // would just stay black on such machines.
    public Rectangle Area;
    public bool UseScreen;
    int blackFrames;

    /// Called on the grab thread with the finished BGRA frame (letterboxed).
    public Action<byte[], int> Sink;

    Thread th;
    volatile bool run;
    int seq;

    readonly object gate = new object();
    readonly byte[] mem = new byte[FrameBytes * 2];
    readonly GCHandle pin;
    int slotWrite, slotReady = -1;

    Bitmap shot;
    Graphics shotG;
    string error = "";

    public FrameGrabber()
    {
        pin = GCHandle.Alloc(mem, GCHandleType.Pinned);
    }

    public string Error { get { return error; } }
    public int Seq { get { return seq; } }
    public bool Running { get { return run && th != null && th.IsAlive; } }

    // diagnostics, filled on every grab
    public double Mean;
    public int ShotW, ShotH;

    static double SampleMean(Bitmap b)
    {
        long sum = 0;
        int n = 0;
        int sx = Math.Max(1, b.Width / 24), sy = Math.Max(1, b.Height / 14);
        for (int y = 0; y < b.Height; y += sy)
            for (int x = 0; x < b.Width; x += sx)
            {
                Color c = b.GetPixel(x, y);
                sum += c.R + c.G + c.B;
                n++;
            }
        return n == 0 ? 0 : sum / (double)n;
    }

    public void Start(IntPtr hwnd)
    {
        Stop();
        Window = hwnd;
        if (hwnd == IntPtr.Zero) { error = "no game window"; return; }
        run = true;
        th = new Thread(Loop);
        th.IsBackground = true;
        th.Priority = ThreadPriority.AboveNormal;
        th.Start();
    }

    public void Stop()
    {
        run = false;
        Thread t = th;
        th = null;
        if (t != null) { try { t.Join(700); } catch { } }
        lock (gate) { slotReady = -1; }
        if (shotG != null) { try { shotG.Dispose(); } catch { } shotG = null; }
        if (shot != null) { try { shot.Dispose(); } catch { } shot = null; }
    }

    void Loop()
    {
        int interval = Math.Max(20, 1000 / Math.Max(1, Fps));
        while (run)
        {
            long t0 = Environment.TickCount;
            try { Step(); } catch (Exception ex) { error = ex.Message; }
            int spent = (int)(Environment.TickCount - t0);
            int wait = interval - spent;
            if (wait > 0) { try { Thread.Sleep(wait); } catch { } }
        }
    }

    void Step()
    {
        // ACC is a system-DPI-unaware window; this process is DPI aware.  Doing
        // the capture in the unaware context makes GetWindowRect and
        // PrintWindow agree with the surface the game actually owns - otherwise
        // the bitmap has the scaled size and comes back black.
        IntPtr prev = IntPtr.Zero;
        try { prev = Native.SetThreadDpiAwarenessContext(Native.DPI_UNAWARE); }
        catch { }
        try { Capture(); }
        finally
        {
            if (prev != IntPtr.Zero) { try { Native.SetThreadDpiAwarenessContext(prev); } catch { } }
        }
    }

    void Capture()
    {
        Native.RECT r;
        int x = 0, y = 0, w, h;   // capture rectangle in screen pixels
        if (UseScreen)
        {
            Rectangle a = Area;
            if (a.Width < 64 || a.Height < 64) { error = "no capture area"; return; }
            x = a.X; y = a.Y; w = a.Width; h = a.Height;
        }
        else
        {
            if (!Native.GetWindowRect(Window, out r)) { error = "window is gone"; run = false; return; }
            x = r.Left; y = r.Top; w = r.Right - r.Left; h = r.Bottom - r.Top;
            if (w < 64 || h < 64) { error = "window is minimised"; return; }
        }

        if (shot == null || shot.Width != w || shot.Height != h)
        {
            if (shotG != null) { try { shotG.Dispose(); } catch { } }
            if (shot != null) { try { shot.Dispose(); } catch { } }
            // 32bppRgb, not Argb: PrintWindow leaves alpha at 0, and an Argb
            // source would then composite every pixel away to black.
            shot = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            shotG = Graphics.FromImage(shot);
        }

        if (UseScreen)
        {
            try { shotG.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy); }
            catch (Exception ex) { error = ex.Message; return; }
        }
        else
        {
            IntPtr hdc = shotG.GetHdc();
            try
            {
                // PrintWindow's return value is unreliable: many games answer the
                // WM_PRINT message but still return FALSE.  Draw the result and let
                // the caller judge the picture instead of dropping real frames.
                Native.PrintWindow(Window, hdc, 2);   // 2 = PW_RENDERFULLCONTENT
            }
            finally { shotG.ReleaseHdc(hdc); }
            error = "";
            Mean = SampleMean(shot);
            ShotW = w; ShotH = h;

            // A window that refuses WM_PRINT (or a restricted token) yields black
            // frames; after a second of them, grab the screen instead.
            if (Mean < 1.0)
            {
                if (++blackFrames > Fps && Area.Width > 64) { UseScreen = true; blackFrames = 0; }
            }
            else blackFrames = 0;
        }
        if (UseScreen) { Mean = SampleMean(shot); ShotW = w; ShotH = h; }

        lock (gate)
        {
            IntPtr target = new IntPtr(mem.Length > 0 ? pin.AddrOfPinnedObject().ToInt64() + slotWrite * FrameBytes : 0);
            using (Bitmap dst = new Bitmap(W, H, W * 4, PixelFormat.Format32bppRgb, target))
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.Bilinear;
                float sc = Math.Min((float)W / w, (float)H / h);
                int dw = (int)(w * sc), dh = (int)(h * sc);
                g.DrawImage(shot, new Rectangle((W - dw) / 2, (H - dh) / 2, dw, dh));
            }
            slotWrite ^= 1;
            slotReady = slotWrite ^ 1;
            seq++;

            Action<byte[], int> sink = Sink;
            if (sink != null) { try { sink(mem, slotReady * FrameBytes); } catch { } }
        }
    }

    public bool Draw(Graphics g, Rectangle dst)
    {
        lock (gate)
        {
            if (slotReady < 0) return false;
            IntPtr p = new IntPtr(pin.AddrOfPinnedObject().ToInt64() + slotReady * FrameBytes);
            using (Bitmap b = new Bitmap(W, H, W * 4, PixelFormat.Format32bppRgb, p))
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.DrawImage(b, dst);
            }
        }
        return true;
    }
}
