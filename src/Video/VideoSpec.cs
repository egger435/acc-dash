// ===========================================================================
//  AccDash - one place for the capture size and frame rate
//  src/Video/VideoSpec.cs
//
//  The ini file decides the size of the clip recorded for every lap.  Lap
//  files keep their own width / height / fps, so changing these only affects
//  new recordings.
// ===========================================================================
using System;


static class Vid
{
    public const int DefW = 320, DefH = 180, DefFps = 24;

    public static int W = DefW;
    public static int H = DefH;
    public static int Fps = DefFps;

    public static int FrameBytes { get { return W * H * 4; } }

    /// Clamps to something the grabber and x264 both handle; the dimensions have
    /// to stay even because the encoder writes yuv420p.
    public static void Set(int w, int h, int fps)
    {
        w = Math.Max(160, Math.Min(1280, w));
        h = Math.Max(90, Math.Min(720, h));
        if ((w & 1) != 0) w++;
        if ((h & 1) != 0) h++;
        Fps = Math.Max(5, Math.Min(60, fps));
        W = w;
        H = h;
    }

    public static string Spec
    {
        get { return W.ToString(Fmt.Inv) + "x" + H.ToString(Fmt.Inv) + " " + Fps.ToString(Fmt.Inv) + "fps"; }
    }
}
