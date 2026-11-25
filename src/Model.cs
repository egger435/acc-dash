// ===========================================================================
//  AccDash - formatting, the ring buffer, the snapshot and the clock
//  src/Model.cs
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


static class Fmt
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string I(double v) { return ((long)Math.Round(v)).ToString(Inv); }
    public static string I(double v, int pad) { return ((long)Math.Round(v)).ToString(Inv).PadLeft(pad); }
    public static string N(double v, int dec) { return v.ToString("F" + dec.ToString(Inv), Inv); }

    /// ms -> "1:47.901"  (empty for unset / invalid)
    public static string Mmss(int ms)
    {
        // ACC writes INT_MAX (and other huge values) for "no time set yet"
        if (ms <= 0 || ms > 3599999) return "-:--.---";
        int m = ms / 60000;
        int s = (ms / 1000) % 60;
        int r = ms % 1000;
        return m.ToString(Inv) + ":" + s.ToString("00", Inv) + "." + r.ToString("000", Inv);
    }

    /// true when a lap time from shared memory can be shown
    public static bool LapOk(int ms) { return ms > 0 && ms <= 3599999; }

    /// ms -> "+0.412" / "-1.203"
    public static string Delta(int ms)
    {
        if (ms == 0) return "+0.000";
        string sign = ms > 0 ? "+" : "-";
        int a = Math.Abs(ms);
        return sign + (a / 1000).ToString(Inv) + "." + (a % 1000).ToString("000", Inv);
    }

    public static string Clock(double secondsLeft)
    {
        if (secondsLeft < 0) secondsLeft = 0;
        int t = (int)Math.Round(secondsLeft);
        return (t / 3600 > 0 ? (t / 3600).ToString("0", Inv) + ":" : "")
             + ((t / 60) % 60).ToString("00", Inv) + ":" + (t % 60).ToString("00", Inv);
    }

    public static string Gear(int g)
    {
        if (g == 0) return "R";
        if (g == 1) return "N";
        return (g - 1).ToString(Inv);
    }
}

// ---------------------------------------------------------------------------
//  Ring buffer holding (timestamp, value) samples
// ---------------------------------------------------------------------------
sealed class Ring
{
    readonly float[] v;
    readonly double[] t;
    int head, n;

    public Ring(int cap) { v = new float[cap]; t = new double[cap]; }

    public int Count { get { return n; } }
    public int Capacity { get { return v.Length; } }
    public float Last { get { return n == 0 ? 0f : v[(head - 1 + v.Length) % v.Length]; } }

    public void Add(double time, float value)
    {
        v[head] = value; t[head] = time;
        head = (head + 1) % v.Length;
        if (n < v.Length) n++;
    }

    public float this[int i] { get { return v[Idx(i)]; } }
    public double Time(int i) { return t[Idx(i)]; }

    int Idx(int i)
    {
        int k = head - n + i;
        k %= v.Length;
        if (k < 0) k += v.Length;
        return k;
    }

    public void Clear() { head = 0; n = 0; }

    public float MinVal() { float m = 0f; for (int i = 0; i < n; i++) if (i == 0 || v[Idx(i)] < m) m = v[Idx(i)]; return m; }
    public float MaxVal() { float m = 0f; for (int i = 0; i < n; i++) if (i == 0 || v[Idx(i)] > m) m = v[Idx(i)]; return m; }
}

// ---------------------------------------------------------------------------
//  One frame of everything the UI draws
// ---------------------------------------------------------------------------
sealed class Snap
{
    // --- physics
    public float Speed;        // km/h
    public float Rpm;
    public int Gear;           // AC convention: 0=R 1=N 2..7 = 1st..6th
    public float SteerNorm;    // -1..1
    public float Gas, Brake;   // 0..1
    public float LatG, LongG;
    public float Yaw;          // deg/s
    public float Slip;         // 0..1 (max wheel slip)
    public float FinalFf;      // 0..1
    public float Fuel;         // litres
    public float BrakeBias;    // 0..1 (raw)
    public int TcLevel, AbsLevel;
    public float TcActive, AbsActive;   // 0..1
    public float AirTemp, RoadTemp, WaterTemp;
    public float TyreAvg;      // deg C
    public float TyreF, TyreR;
    public float BrakeTempAvg;
    public int MaxRpm;
    public int PitLimiter, EngineRunning;

    // --- graphics
    public int Status;         // 0 off 1 replay 2 live 3 pause
    public int Session;        // -1..8
    public int CompletedLaps, Position, NumCars, TotalLaps;
    public float SessionTimeLeft;
    public int CurMs, LastMs, BestMs, DeltaMs, EstMs;
    public bool DeltaPositive, ValidLap;
    public string CurStr, LastStr, BestStr, EstStr, DeltaStr;
    public int Flag;
    public float FuelXLap, UsedFuel;
    public int TrackGrip;      // 0..6
    public string TrackStatus;
    public int RainIntensity;

    // --- static
    public string Track, Car, TrackConfig, TyreName;
    public int SectorCount;
    public float MaxFuel;

    // --- world position (track map)
    public float WorldX, WorldZ;   // metres, AC world space: x/z are the ground plane
    public float Head;             // radians, car heading

    // --- meta
    public double Stamp;       // ms, from Clock.Now
    public bool Live;          // true = data came from ACC
}

// ---------------------------------------------------------------------------
//  Monotonic clock
// ---------------------------------------------------------------------------
static class Clock
{
    static readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
    public static double Now { get { return sw.Elapsed.TotalMilliseconds; } }
}
