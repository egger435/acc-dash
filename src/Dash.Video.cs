// ===========================================================================
//  AccDash - video card wiring: lap recording, embedded playback
//  src/Dash.Video.cs
//
//  There is no live preview any more.  Frames are captured only while the car
//  is on track (they feed the recorder), and the video card shows the clip of
//  the lap being reviewed.
// ===========================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;


sealed partial class Dash
{
    VideoPane video;

    FrameGrabber grab;               // window capture, feeds the recorder
    Rectangle capArea;               // where the game window is, refreshed once a second
    IntPtr capHwnd;
    double capCheckAt = -1e9;
    int grabSeq = -1;                // last frame number seen from the grabber
    double grabSeqAt = -1e9;
    Recorder rec;                    // 1 s chunk recorder, running while driving

    LiveCapture clipCap;             // decoder for the embedded lap clip
    LapData clipFor;
    string clipPath = "";

    public bool NoVideo;             // --novideo, and the shot modes that must stay deterministic

    void StartVideo()
    {
        if (NoVideo || !cfg.Video) { video.Note = "video off"; return; }
        Ff.Locate(cfg.FfmpegPath);
        Ff.ForceArea = cfg.VideoArea;
        Ff.ForceOutput = cfg.VideoOutput;
    }

    /// Called from Tick: keeps the replay picture glued to the playhead.
    void TickVideo(double now)
    {
        if (video == null) return;
        if (NoVideo || !cfg.Video) return;

        if (review != null)
        {
            ShowReviewVideo(now);
            return;
        }

        StopClipPlayer();
        video.InReview = false;
        video.Frame = null;
        video.Live = null;
        video.Caption = "";
        video.Corner = "";
        video.Note = "";
    }

    // --- review: play the clip stored inside the lap file -------------------

    void ShowReviewVideo(double now)
    {
        video.InReview = true;
        video.Caption = "LAP " + review.LapNo.ToString(Fmt.Inv);
        video.Corner = review.VideoW.ToString(Fmt.Inv) + "x" + review.VideoH.ToString(Fmt.Inv) + " " + review.VideoFps.ToString(Fmt.Inv) + "fps";

        if (!review.HasVideo)
        {
            StopClipPlayer();
            video.Live = null;
            clipFor = null;                   // restart as soon as the clip is ready
            if (review.VideoState == 1)
                video.Note = "video is still being encoded - it plays here as soon as it is ready";
            else if (review.VideoState == 3)
                video.Note = "no video for this lap (encoding failed)";
            else
                video.Note = review.FromFile ? "no video in this file" : "no video for this lap";
            video.Invalidate();
            return;
        }
        video.Note = "";

        float clipEnd = review.VideoMs / 1000f;
        float want = (float)(reviewPos - review.VideoT0);
        bool beyond = want > clipEnd && clipEnd > 0.2f;
        if (want < 0f) { want = 0f; }
        if (clipEnd > 0.2f && want > clipEnd - 0.12f) want = Math.Max(0f, clipEnd - 0.12f);
        if (beyond) video.Note = "clip ends at " + Fmt.Mmss((int)((review.VideoT0 + clipEnd) * 1000f));

        int vfps = review.VideoFps > 0 ? review.VideoFps : Vid.Fps;
        int frame = (int)Math.Round(want * vfps);
        if (frame < 0) frame = 0;

        bool fresh = clipCap == null || clipFor != review;
        if (fresh)
        {
            if (clipCap != null) clipCap.Stop(false);
            clipCap = new LiveCapture();
            if (review.VideoW > 0 && review.VideoH > 0) { clipCap.W = review.VideoW; clipCap.H = review.VideoH; }
            clipFor = review;
            clipPath = WriteClipFile(review);
            if (clipPath.Length > 0)
            {
                clipCap.StartClip(clipPath, frame / (float)vfps, vfps);
                clipCap.Target = frame;
            }
        }
        else if (clipPath.Length > 0)
        {
            // playhead jumped backwards past what has been decoded: reopen there
            if (clipCap.FrameNo > frame + 1) clipCap.StartClip(clipPath, frame / (float)vfps, vfps);
            clipCap.Target = frame;      // forwards: the reader just races ahead
        }

        if (clipPath.Length > 0) video.Live = clipCap;
        video.Invalidate();
    }

    string WriteClipFile(LapData d)
    {
        try
        {
            string dir = Path.Combine(Recorder.Root, "lap");
            Directory.CreateDirectory(dir);
            string f = Path.Combine(dir, "lap" + d.LapNo.ToString(Fmt.Inv) + "_" + d.When.Ticks.ToString(Fmt.Inv) + ".mp4");
            File.WriteAllBytes(f, d.VideoBytes);
            return f;
        }
        catch (Exception ex) { video.Note = ex.Message; return ""; }
    }

    void StopClipPlayer()
    {
        if (clipCap != null) { clipCap.Stop(false); clipCap = null; }
        clipFor = null;
        clipPath = "";
    }

    // --- recording ----------------------------------------------------------

    /// Keeps the chunk recorder (and the window capture feeding it) running while
    /// the car is on track, and cuts the clip for a finished lap on a worker.
    void TickRecorder(double now)
    {
        if (NoVideo || !cfg.Video) return;
        if (rec == null) rec = new Recorder();
        rec.Crf = cfg.VideoCrf;

        // The capture pipeline stays warm the whole time the car is on track:
        // starting it at the timing line (or stopping it there) spawned ffmpeg and
        // the capture thread exactly when the player crossed the line, which showed
        // up as a stutter and a REC lamp that came on late.  The lap gate now only
        // decides whether a lap ends up in the list.
        bool want = review == null && sn.Live && !recordTest && Ff.Ready;
        // finding the game window walks the process list, so it runs at most
        // once a second and the answer is reused by every tick in between
        if (now - capCheckAt > 5000)        // walking the process table is slow: keep it rare
        {
            capCheckAt = now;
            bool found = false;
            if (want)
            {
                capArea = Ff.GameArea(out found);
                capHwnd = found ? Ff.LastWindow : IntPtr.Zero;
            }
            else
            {
                capArea = Rectangle.Empty;
                capHwnd = IntPtr.Zero;
            }
            if (!found) capArea = Rectangle.Empty;
        }
        // the offline sim has no game window: tape the whole screen so the
        // recorder and the lap gate can be exercised without ACC
        if (want && FakeLive && (capHwnd == IntPtr.Zero || capArea.Width < 64))
        {
            capArea = Screen.PrimaryScreen.Bounds;
            capHwnd = Native.GetShellWindow();
        }

        Rectangle area = capArea;
        if (want && (capHwnd == IntPtr.Zero || area.Width < 64)) want = false;   // never record the desktop

        if (!want)
        {
            StopGrabber();
            rec.Tick(false);
            return;
        }

        if (grab == null || grab.Window != capHwnd)
        {
            StopGrabber();
            grab = new FrameGrabber();
            grab.Fps = cfg.GrabFps > 0 ? (int)G.Clamp(cfg.GrabFps, 5, 30) : Vid.Fps;
            grab.Area = area;
            grab.Sink = delegate(byte[] buf, int off) { if (rec != null) rec.Push(buf, off); };
            grab.Start(capHwnd);
        }
        else if (!grab.Running)
        {
            StopGrabber();
        }
        else
        {
            // follow the window: a resolution change moves the capture region
            if (Math.Abs(area.Width - grab.Area.Width) > 4 || Math.Abs(area.Height - grab.Area.Height) > 4)
                grab.Area = area;

            // A capture that stops producing frames would leave the recorder
            // duplicating one picture (the menu screen) for the whole lap, so
            // notice the stall and start over with a fresh window lookup.
            if (grab.Seq != grabSeq) { grabSeq = grab.Seq; grabSeqAt = now; }
            else if (now - grabSeqAt > 1500)
            {
                Recorder.Log("grabber stalled " + (now - grabSeqAt).ToString("0", Fmt.Inv)
                             + " ms (errors=" + grab.Errors.ToString(Fmt.Inv) + ") - restarting");
                StopGrabber();
                capCheckAt = -1e9;
            }
        }

        rec.Tick(want);
        // Keep ten minutes of chunks.  Trimming relative to the current lap time
        // deleted the lap that had just ended whenever the clip was still being
        // cut, which produced one-second clips.
        rec.Trim(now - 600000);
    }

    void StopGrabber()
    {
        if (grab == null) return;
        grab.Stop();
        grab = null;
        grabSeq = -1;
        grabSeqAt = -1e9;
    }

    void QueueClip(LapData d)
    {
        if (NoVideo || !cfg.Video || rec == null || d == null) return;
        if (d.WallT1 - d.WallT0 < 1500) return;
        d.VideoState = 1;                     // the lap list shows "processing"
        LapData lap = d;
        Recorder r = rec;
        r.ClipPending(lap.WallT0);        // the trimmer must not eat these chunks
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                // let the new lap get going before encoding: the clip job competes
                // for CPU with the game right after the line
                System.Threading.Thread.Sleep(12000);
                float vt0, vms;
                byte[] b = r.Clip(lap.WallT0, lap.WallT1, out vt0, out vms);
                if (b == null) { lap.VideoState = 3; return; }
                lap.VideoBytes = b;
                lap.VideoT0 = vt0;
                lap.VideoMs = vms * 1000f;
                lap.VideoW = Vid.W;
                lap.VideoH = Vid.H;
                lap.VideoFps = Vid.Fps;
                lap.VideoState = 2;
            }
            catch { lap.VideoState = 3; }
            finally { r.ClipDone(); }
        });
    }
}
