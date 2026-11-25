// ===========================================================================
//  AccDash - track map learning and persistence
//  src/Dash.Map.cs
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


sealed partial class Dash
{
    /// Learns the track outline from the car's world position: a point every
    /// 100 ms while moving, closed and saved when a lap completes. Cached as
    /// tracks\<track>[-<config>].map so later sessions start with the shape.
    void UpdateTrackMap(double now)
    {
        if (map == null) return;

        string key = TrackOutline.Key(sn.Track, sn.TrackConfig);
        if (key != outlineKey)
        {
            outlineKey = key;
            outline = TrackOutline.Load(TrackOutline.PathFor(key));
            if (outline == null)
            {
                // no finished map yet - resume a lap that was parked by a previous run
                TrackOutline part = TrackOutline.Load(TrackOutline.PartialFor(key));
                if (part != null) { part.Closed = false; outline = part; }
            }
            mapHasLast = false;
            lapAnchor = -1;
            mapLastSave = -1e9;
            mapLastLap = sn.CompletedLaps;
        }
        if (outline == null)
        {
            outline = new TrackOutline();
            outline.Track = sn.Track;
            outline.Config = sn.TrackConfig == null ? "" : sn.TrackConfig;
        }

        bool live = review == null && (sn.Live || FakeLive);
        bool moving = live && sn.Speed > 2f && (Math.Abs(sn.WorldX) + Math.Abs(sn.WorldZ) > 1f);

        if (sn.CompletedLaps > mapLastLap && !outline.Closed)
        {
            // The lap counter ticks exactly when the car crosses start/finish, so the
            // car is standing on the line right now.  Keep everything sampled so far
            // (that is the out-lap) and remember where the real lap began - cutting the
            // ring here is what makes the saved map start at the start/finish line.
            if (lapAnchor < 0)
            {
                lapAnchor = outline.Points.Count;
                mapHasLast = false;
            }
            else if (outline.Points.Count - lapAnchor > 40)
            {
                List<PointF> ring = outline.Points.GetRange(lapAnchor, outline.Points.Count - lapAnchor);
                float gap = (float)Math.Sqrt(Math.Pow(ring[ring.Count - 1].X - ring[0].X, 2) +
                                             Math.Pow(ring[ring.Count - 1].Y - ring[0].Y, 2));

                TrackOutline lap = new TrackOutline();
                lap.Track = string.IsNullOrEmpty(outline.Track) ? sn.Track : outline.Track;
                lap.Config = outline.Config;
                lap.Points = ring;
                lap.RecomputeLength();

                if (gap < 80f && lap.LengthM >= 500f)
                {
                    lap.Closed = true;
                    lap.When = DateTime.Now;
                    // never cache a simulated lap
                    if (!FakeLive || SimSave) lap.Save(TrackOutline.PathFor(outlineKey));
                    outline = lap;                       // show the clean ring from now on
                    try { System.IO.File.Delete(TrackOutline.PartialFor(outlineKey)); } catch { }
                    mapHasLast = false;
                }
                else
                {
                    lapAnchor = outline.Points.Count;    // did not close - try the next lap
                }
            }
            else
            {
                lapAnchor = outline.Points.Count;
            }
        }
        mapLastLap = sn.CompletedLaps;

        // once the loop is closed there is nothing left to learn - appending a new
        // lap's points would turn the closing segment into a chord across the map
        if (outline.Closed) { mapHasLast = false; return; }

        if (!moving) { mapHasLast = false; return; }
        if (now - mapLastSample < 100.0) return;

        if (mapHasLast)
        {
            float dx = sn.WorldX - mapLastX, dz = sn.WorldZ - mapLastZ;
            if (dx * dx + dz * dz < 0.36f) return;      // 0.6 m
        }
        mapLastSample = now;
        mapLastX = sn.WorldX; mapLastZ = sn.WorldZ;
        mapHasLast = true;
        outline.AddPoint(sn.WorldX, sn.WorldZ);

        // park the half-finished outline now and then: if the panel is closed (or the
        // game crashes) the lap being learned is not lost
        if ((!FakeLive || SimSave) && outline.Points.Count >= 8 && now - mapLastSave > 15000)
        {
            mapLastSave = now;
            outline.Save(TrackOutline.PartialFor(outlineKey));
        }
    }

    /// Park the unfinished outline on exit; a finished (closed) map needs nothing.
    void SavePartialMap()
    {
        if (outline == null || outline.Closed || outline.Points.Count < 8) return;
        if (outlineKey.Length == 0) return;
        if (FakeLive && !SimSave) return;         // a simulated drive must never touch tracks\
        outline.Save(TrackOutline.PartialFor(outlineKey));
    }
}
