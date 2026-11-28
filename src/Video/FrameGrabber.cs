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
    public int Errors;                    // frames that could not be captured
    // Halftone-StretchBlt is the sharpest way down to 320x180 but it costs a third
    // more than plain pixel dropping; when that makes 24 fps impossible the picture
    // would judder, so the grabber trades sharpness for smoothness instead.
    public bool FastBlit;
    int blackFrames;
    int misses;                           // consecutive frames without a window rect
    int screenFrames;                     // frames taken straight off the screen
    int slowFrames;                       // consecutive grabs that miss the frame budget

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
    public double Ms;          // average milliseconds one capture needs
    public int RateFps;        // frames actually captured in the last second
    long rateAt = Environment.TickCount;
    int rateFrames;
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
            Ms = Ms <= 0.0 ? spent : Ms * 0.9 + spent * 0.1;
            // Only give up the dithering after a full second of frames that do not
            // fit the budget - the first few grabs are always slower (cold DC, page
            // faults) and switching on those would cost sharpness for nothing.
            if (!FastBlit && Fps >= 20 && Ms > interval * 0.9)
            {
                if (++slowFrames > Fps) FastBlit = true;
            }
            else if (Ms <= interval * 0.9) slowFrames = 0;
            rateFrames++;
            if (t0 - rateAt >= 1000) { RateFps = (int)(rateFrames * 1000.0 / Math.Max(1, t0 - rateAt)); rateFrames = 0; rateAt = t0; }
            int wait = interval - spent;
            if (wait > 0) { try { Thread.Sleep(wait); } catch { } }
        }
    }

    void Step()
    {
        // The two capture paths need opposite DPI contexts:
        //
        //  * screen: our Area holds real screen pixels (the UI thread is DPI
        //    aware).  In the unaware context a DISPLAY DC is virtualised - at 125%
        //    it is only 2048x1152 - so a 2560x1440 source rectangle fell outside
        //    it and the clip came out cropped and soft.  Use the process context.
        //  * window: ACC is a system-DPI-unaware window, so PrintWindow and
        //    GetWindowRect only agree with the surface the game owns when this
        //    thread is unaware as well; otherwise the bitmap has the scaled size
        //    and comes back black.
        bool screen = UseScreen || !Native.IsIconic(Window);
        IntPtr prev = IntPtr.Zero;
        try { prev = Native.SetThreadDpiAwarenessContext(screen ? Native.DPI_SYSTEM : Native.DPI_UNAWARE); }
        catch { }
        try { Capture(); }
        finally
        {
            if (prev != IntPtr.Zero) { try { Native.SetThreadDpiAwarenessContext(prev); } catch { } }
        }
    }

    /// Is our game window the one that owns the middle of that rectangle?
    bool IsTopWindow(int x, int y, int w, int h)
    {
        try
        {
            if (Native.IsIconic(Window)) return false;
            Native.POINT p;
            p.X = x + w / 2;
            p.Y = y + h / 2;
            IntPtr top = Native.WindowFromPoint(p);
            if (top == IntPtr.Zero) return false;
            return Native.GetAncestor(top, Native.GA_ROOT) == Window;
        }
        catch { return false; }
    }

    void Capture()
    {
        Native.RECT r = new Native.RECT();
        Rectangle a = Area;
        bool haveArea = a.Width >= 64 && a.Height >= 64;
        int x = 0, y = 0, w, h;   // capture rectangle in screen pixels
        bool fromScreen = UseScreen;
        if (!UseScreen)
        {
            if (!Native.GetWindowRect(Window, out r))
            {
                Errors++;
                error = "window is gone";
                if (++misses > 40) run = false;      // give up only after ~1.6 s
                return;
            }
            misses = 0;
            // A DirectX game answers PrintWindow with whatever DWM composed last -
            // typically the menu screen - which froze entire recordings, so the screen
            // is the source unless the window is minimised (nothing to copy then).
            fromScreen = !Native.IsIconic(Window);
        }
        if (haveArea)
        {
            // Area comes from the window detector in real screen pixels; this thread
            // is DPI unaware, so GetWindowRect here would be virtualised and the clip
            // came out cropped.
            x = a.X; y = a.Y; w = a.Width; h = a.Height;
        }
        else
        {
            if (UseScreen) { error = "no capture area"; return; }
            x = r.Left; y = r.Top; w = r.Right - r.Left; h = r.Bottom - r.Top;
            if (w < 64 || h < 64) { Errors++; error = "window is minimised"; return; }
        }

        float sc = Math.Min((float)W / w, (float)H / h);
        int dw = Math.Max(1, (int)(w * sc)), dh = Math.Max(1, (int)(h * sc));

        lock (gate)
        {
            IntPtr target = new IntPtr(mem.Length > 0 ? pin.AddrOfPinnedObject().ToInt64() + slotWrite * FrameBytes : 0);
            using (Bitmap dst = new Bitmap(W, H, W * 4, PixelFormat.Format32bppRgb, target))
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Black);
                if (fromScreen)
                {
                    // StretchBlt straight from the screen into the small bitmap.  Copying
                    // a whole 2560x1440 frame first cost ~40 ms a frame and stuttered the
                    // desktop (and the game) while driving.
                    IntPtr src = Native.CreateDC("DISPLAY", null, null, IntPtr.Zero);
                    IntPtr dd = g.GetHdc();
                    try
                    {
                        Native.SetStretchBltMode(dd, FastBlit ? Native.COLORONCOLOR : Native.HALFTONE);
                        Native.SetBrushOrgEx(dd, 0, 0, IntPtr.Zero);
                        Native.StretchBlt(dd, (W - dw) / 2, (H - dh) / 2, dw, dh, src, x, y, w, h, Native.SRCCOPY);
                    }
                    finally
                    {
                        g.ReleaseHdc(dd);
                        if (src != IntPtr.Zero) { try { Native.DeleteDC(src); } catch { } }
                    }
                    screenFrames++;
                    error = "";
                }
                else
                {
                    if (shot == null || shot.Width != w || shot.Height != h)
                    {
                        if (shotG != null) { try { shotG.Dispose(); } catch { } }
                        if (shot != null) { try { shot.Dispose(); } catch { } }
                        // 32bppRgb, not Argb: PrintWindow leaves alpha at 0, and an
                        // Argb source would then composite every pixel away to black.
                        shot = new Bitmap(w, h, PixelFormat.Format32bppRgb);
                        shotG = Graphics.FromImage(shot);
                    }
                    IntPtr hdc = shotG.GetHdc();
                    try
                    {
                        // PrintWindow's return value is unreliable: many games answer the
                        // WM_PRINT message but still return FALSE.  Draw the result and let
                        // the caller judge the picture instead of dropping real frames.
                        Native.PrintWindow(Window, hdc, 2);   // 2 = PW_RENDERFULLCONTENT
                    }
                    finally { shotG.ReleaseHdc(hdc); }
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.DrawImage(shot, new Rectangle((W - dw) / 2, (H - dh) / 2, dw, dh));
                    error = "";
                }

                Mean = SampleMean(dst);
                ShotW = w; ShotH = h;

                // A window that refuses WM_PRINT (or a restricted token) yields black
                // frames; after a second of them, grab the screen instead.
                if (Mean < 1.0)
                {
                    if (++blackFrames > Fps && Area.Width > 64) { UseScreen = true; blackFrames = 0; }
                }
                else blackFrames = 0;
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
