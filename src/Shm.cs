// ===========================================================================
//  AccDash - ACC shared memory structs and the page reader
//  src/Shm.cs
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


// ===========================================================================
//  ACC shared memory structures (official ACC SDK layout, pack = 4, unicode)
// ===========================================================================
namespace Shm
{
    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
    public struct Physics
    {
        public int PacketId;
        public float Gas, Brake, Fuel;
        public int Gear, Rpms;
        public float SteerAngle, SpeedKmh;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public float[] Velocity;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public float[] AccG;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] WheelSlip;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] WheelLoad;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] WheelsPressure;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] WheelAngularSpeed;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreWear;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreDirtyLevel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreCoreTemperature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] CamberRAD;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] SuspensionTravel;
        public float DRS, TC, Heading, Pitch, Roll, CGHeight;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)] public float[] CarDamage;
        public int NumberOfTyresOut;
        public int PitLimiterOn;
        public float Abs;
        public float KersCharge, KersInput;
        public int AutoShifterOn;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] RideHeight;
        public float TurboBoost, Ballast, AirDensity, AirTemp, RoadTemp;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public float[] LocalAngularVelocity;
        public float FinalFF, PerformanceMeter;
        public int EngineBrake;
        public int ErsRecoveryLevel, ErsPowerLevel, ErsHeatCharging, ErsIsCharging;
        public float KersCurrentKJ;
        public int DrsAvailable, DrsEnabled;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] BrakeTemp;
        public float Clutch;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreTempI;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreTempM;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreTempO;
        public int IsAIControlled;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)] public float[] TyreContactPoint;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)] public float[] TyreContactNormal;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)] public float[] TyreContactHeading;
        public float BrakeBias;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public float[] LocalVelocity;
        public int P2PActivation, P2PStatus;
        public float CurrentMaxRPM;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] MZ;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] FX;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] FY;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] SlipRatio;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] SlipAngle;
        public int TCInAction, ABSInAction;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] SuspensionDamage;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreTemp;
        public float WaterTemp;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] brakePressure;
        public int frontBrakeCompound, rearBrakeCompound;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] padLife;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] discLife;
        public int ignitionOn, starterEngineOn, isEngineRunning;
        public float kerbVibration, slipVibration, gVibration, absVibration;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
    public struct Graphics
    {
        public int PacketId;
        public int Status;
        public int Session;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)] public string CurrentTime;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)] public string LastTime;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)] public string BestTime;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)] public string Split;
        public int CompletedLaps;
        public int Position;
        public int iCurrentTime, iLastTime, iBestTime;
        public float SessionTimeLeft;
        public float DistanceTraveled;
        public int IsInPit;
        public int CurrentSectorIndex;
        public int LastSectorTime;
        public int NumberOfLaps;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string TyreCompound;
        public float replayTimeMultiplier;
        public float NormalizedCarPosition;
        public int ActiveCars;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 180)] public float[] CarCoordinates;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)] public int[] CarID;
        public int PlayerCarId;
        public float PenaltyTime;
        public int Flag;
        public int Penalty;
        public int IdealLineOn;
        public int IsInPitLane;
        public float SurfaceGrip;
        public int MandatoryPitDone;
        public float WindSpeed, WindDirection;
        public int IsSetupMenuVisible;
        public int MainDisplayIndex, SecondaryDisplayIndex;
        public int TC, TCUT, EngineMap, ABS;
        public float FuelXLap;
        public int RainLights, FlashingLights, LightsStage;
        public float ExhaustTemperature;
        public int WiperLV;
        public int DriverStingTotalTimeLeft, DriverStingTimeLeft;
        public int RainTyres;
        public int SessionIndex;
        public float UsedFuel;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)] public string DeltaLapTime;
        public int IDeltaLapTime;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)] public string EstimatedLapTime;
        public int IEstimatedLapTime;
        public int IsDeltaPositive, ISplit, IsValidLap;
        public float FuelEstimatedLaps;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string TrackStatus;
        public int MissingMandatoryPits;
        public int directionLightsLeft, directionLightsRight;
        public int GlobalYellow, GlobalYellow1, GlobalYellow2, GlobalYellow3;
        public int GlobalWhite, GlobalGreen, GlobalChequered, GlobalRed;
        public int mfdTyreSet;
        public float mfdFuelToAdd;
        // ACC ships two extra dwords here that the older AC documentation does not
        // list: without them every field below lands 8 bytes early (trackGripStatus
        // used to read the tyre pressure next door, i.e. the float 24.5 -> 1103364096).
        public int padBeforeMfdPressure0, padBeforeMfdPressure1;
        public float mfdTyrePressureLF, mfdTyrePressureRF, mfdTyrePressureLR, mfdTyrePressureRR;
        public int trackGripStatus;
        public int rainIntensity, rainIntensityIn10min, rainIntensityIn30min;
        public int currentTyreSet, strategyTyreSet;
        public int gapAhead, gapBehind;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode)]
    public struct StaticInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)] public string SMVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)] public string ACVersion;
        public int NumberOfSessions;
        public int NumCars;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string CarModel;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string Track;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string PlayerName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string PlayerSurname;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string PlayerNick;
        public int SectorCount;
        public float MaxTorque, MaxPower;
        public int MaxRpm;
        public float MaxFuel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] SuspensionMaxTravel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public float[] TyreRadius;
        public float MaxTurboBoost, Deprecated1, Deprecated2;
        public int PenaltiesEnabled;
        public float AidFuelRate, AidTireRate, AidMechanicalDamage, AidAllowTyreBlankets, AidStability;
        public int AidAutoClutch, AidAutoBlip;
        public int HasDRS, HasERS, HasKERS;
        public float KersMaxJoules;
        public int EngineBrakeSettingsCount, ErsPowerControllerCount;
        public float TrackSPlineLength;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)] public string TrackConfiguration;
        public float ErsMaxJ;
        public int IsTimedRace, HasExtraLap;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string CarSkin;
        public int ReversedGridPositions;
        public int PitWindowStart, PitWindowEnd, IsOnline;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string dryTyresName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)] public string wetTyresName;
    }
}

// ---------------------------------------------------------------------------
//  Shared memory reader.  Keeps the last good pages and exposes rates.
// ---------------------------------------------------------------------------
sealed class AccShm : IDisposable
{
    const string PHYS = "Local\\acpmf_physics";
    const string GRAPH = "Local\\acpmf_graphics";
    const string STATIC = "Local\\acpmf_static";

    MemoryMappedFile mmfPhys, mmfGraph, mmfStat;
    MemoryMappedViewStream stPhys, stGraph, stStat;
    byte[] bufPhys, bufGraph, bufStat;
    public byte[] RawPhys { get { return bufPhys; } }
    public byte[] RawGraph { get { return bufGraph; } }
    GCHandle pinPhys, pinGraph, pinStat;

    public bool PhysOk, GraphOk, StatOk;
    public int PhysLen, GraphLen, StatLen;
    public double PhysStamp, GraphStamp, StatStamp;
    public int PhysPacket, GraphPacket;
    public double PhysHz, GraphHz;
    public string LastError = "";

    public Shm.Physics P;
    public Shm.Graphics G;
    public Shm.StaticInfo S;

    int lastPhysPacket = -1, lastGraphPacket = -1;
    double hzT0 = -1;
    double lastStatRead = -1;
    int hzN;

    public void Open()
    {
        Close();
        try
        {
            int needPhys = Marshal.SizeOf(typeof(Shm.Physics));
            int needGraph = Marshal.SizeOf(typeof(Shm.Graphics));
            int needStat = Marshal.SizeOf(typeof(Shm.StaticInfo));

            // ACC (often started elevated through Steam) only grants read access, so the
            // view must be created explicitly for Read - the default asks for ReadWrite.
            mmfPhys = MemoryMappedFile.OpenExisting(PHYS, MemoryMappedFileRights.Read);
            stPhys = mmfPhys.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            PhysLen = (int)stPhys.Length;
            bufPhys = new byte[Math.Max(needPhys, PhysLen)];
            pinPhys = GCHandle.Alloc(bufPhys, GCHandleType.Pinned);

            mmfGraph = MemoryMappedFile.OpenExisting(GRAPH, MemoryMappedFileRights.Read);
            stGraph = mmfGraph.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            GraphLen = (int)stGraph.Length;
            bufGraph = new byte[Math.Max(needGraph, GraphLen)];
            pinGraph = GCHandle.Alloc(bufGraph, GCHandleType.Pinned);

            mmfStat = MemoryMappedFile.OpenExisting(STATIC, MemoryMappedFileRights.Read);
            stStat = mmfStat.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            StatLen = (int)stStat.Length;
            bufStat = new byte[Math.Max(needStat, StatLen)];
            pinStat = GCHandle.Alloc(bufStat, GCHandleType.Pinned);

            LastError = "";
            ReadStatic();
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
            Close();
        }
    }

    public void Close()
    {
        if (pinPhys.IsAllocated) pinPhys.Free();
        if (pinGraph.IsAllocated) pinGraph.Free();
        if (pinStat.IsAllocated) pinStat.Free();
        if (stPhys != null) stPhys.Dispose();
        if (stGraph != null) stGraph.Dispose();
        if (stStat != null) stStat.Dispose();
        if (mmfPhys != null) mmfPhys.Dispose();
        if (mmfGraph != null) mmfGraph.Dispose();
        if (mmfStat != null) mmfStat.Dispose();
        stPhys = null; stGraph = null; stStat = null;
        mmfPhys = null; mmfGraph = null; mmfStat = null;
        PhysOk = GraphOk = StatOk = false;
    }

    public void Dispose() { Close(); }

    static bool FromStream(MemoryMappedViewStream s, byte[] buf, GCHandle h, Type t)
    {
        s.Position = 0;
        int want = (int)Math.Min(buf.Length, s.Length);
        int got = 0;
        while (got < want)
        {
            int n = s.Read(buf, got, want - got);
            if (n <= 0) break;
            got += n;
        }
        return got > 0;
    }

    public void ReadStatic()
    {
        if (stStat == null || !pinStat.IsAllocated) { StatOk = false; return; }
        try
        {
            if (!FromStream(stStat, bufStat, pinStat, typeof(Shm.StaticInfo))) { StatOk = false; return; }
            S = (Shm.StaticInfo)Marshal.PtrToStructure(pinStat.AddrOfPinnedObject(), typeof(Shm.StaticInfo));
            StatOk = true;
            StatStamp = Clock.Now;
        }
        catch { StatOk = false; }
    }

    public void Poll()
    {
        if (stPhys != null && pinPhys.IsAllocated)
        {
            try
            {
                if (FromStream(stPhys, bufPhys, pinPhys, typeof(Shm.Physics)))
                {
                    P = (Shm.Physics)Marshal.PtrToStructure(pinPhys.AddrOfPinnedObject(), typeof(Shm.Physics));
                    PhysOk = true; PhysStamp = Clock.Now;
                    if (P.PacketId != lastPhysPacket)
                    {
                        if (lastPhysPacket >= 0) { hzN++; }
                        lastPhysPacket = P.PacketId;
                    }
                }
            }
            catch { PhysOk = false; }
        }

        if (stGraph != null && pinGraph.IsAllocated)
        {
            try
            {
                if (FromStream(stGraph, bufGraph, pinGraph, typeof(Shm.Graphics)))
                {
                    G = (Shm.Graphics)Marshal.PtrToStructure(pinGraph.AddrOfPinnedObject(), typeof(Shm.Graphics));
                    GraphOk = true; GraphStamp = Clock.Now;
                    if (G.PacketId != lastGraphPacket)
                    {
                        lastGraphPacket = G.PacketId;
                    }
                }
            }
            catch { GraphOk = false; }
        }

        double now = Clock.Now;

        // ACC fills the static page (track, car, max rpm) only once the track has
        // finished loading, and it rewrites that page in place when you switch track.
        // Reading it once at open leaves the panel with an empty/old track name, so
        // refresh it now and then - it is 4 KB.
        if (now - lastStatRead > 2000)
        {
            lastStatRead = now;
            ReadStatic();
        }

        if (hzT0 < 0) hzT0 = now;
        if (now - hzT0 >= 1000)
        {
            PhysHz = hzN * 1000.0 / (now - hzT0);
            hzN = 0; hzT0 = now;
        }
        PhysPacket = lastPhysPacket;
        GraphPacket = lastGraphPacket;
    }

    /// Which way ACC's accG[2] is presented: -1 flips it so braking reads negative.
    public int LongGSign = -1;

    /// Fill a snapshot from the last pages read. Returns false when stale.
    public bool Fill(Snap sn, out string why)
    {
        why = "";
        double now = Clock.Now;
        if (!PhysOk || now - PhysStamp > 800) { why = "physics stale"; return false; }
        if (!GraphOk || now - GraphStamp > 800) { why = "graphics stale"; return false; }

        sn.Speed = P.SpeedKmh;
        sn.Rpm = P.Rpms;
        sn.Gear = P.Gear;
        sn.SteerNorm = SteerNorm(P.SteerAngle);
        sn.Gas = P.Gas;
        sn.Brake = P.Brake;
        sn.LatG = P.AccG != null && P.AccG.Length > 0 ? P.AccG[0] : 0f;
        sn.LongG = (P.AccG != null && P.AccG.Length > 2 ? P.AccG[2] : 0f) * LongGSign;
        sn.Yaw = P.LocalAngularVelocity != null && P.LocalAngularVelocity.Length > 1 ? P.LocalAngularVelocity[1] * 57.29578f : 0f;
        sn.FinalFf = P.FinalFF;
        sn.Fuel = P.Fuel;
        sn.BrakeBias = P.BrakeBias;
        sn.TcActive = Math.Max(P.TC, P.TCInAction > 0 ? 1f : 0f);
        sn.AbsActive = Math.Max(P.Abs, P.ABSInAction > 0 ? 1f : 0f);
        sn.AirTemp = P.AirTemp; sn.RoadTemp = P.RoadTemp; sn.WaterTemp = P.WaterTemp;
        sn.TyreF = Avg2(P.TyreCoreTemperature, 0); sn.TyreR = Avg2(P.TyreCoreTemperature, 2);
        sn.TyreAvg = (sn.TyreF + sn.TyreR) * 0.5f;
        sn.BrakeTempAvg = Avg4(P.BrakeTemp);
        sn.MaxRpm = P.CurrentMaxRPM > 500 ? (int)P.CurrentMaxRPM : (S.MaxRpm > 500 ? S.MaxRpm : 8000);
        sn.PitLimiter = P.PitLimiterOn;
        sn.EngineRunning = P.isEngineRunning;

        // world position: average of the four tyre contact points (x/y/z each).
        // ACC reports 0,0,0 while the car sits in the pits or a menu.
        if (P.TyreContactPoint != null && P.TyreContactPoint.Length >= 12)
        {
            float sx = 0f, sz = 0f;
            for (int i = 0; i < 4; i++)
            {
                sx += P.TyreContactPoint[i * 3];
                sz += P.TyreContactPoint[i * 3 + 2];
            }
            sn.WorldX = sx * 0.25f;
            sn.WorldZ = sz * 0.25f;
        }
        sn.Head = P.Heading;

        float slip = 0f;
        if (P.WheelSlip != null) for (int i = 0; i < P.WheelSlip.Length; i++) slip = Math.Max(slip, Math.Abs(P.WheelSlip[i]));
        sn.Slip = slip;
        sn.TcLevel = G.TC;
        sn.AbsLevel = G.ABS;

        sn.Status = G.Status;
        sn.Session = G.Session;
        sn.CompletedLaps = G.CompletedLaps;
        sn.Position = G.Position;
        sn.TotalLaps = G.NumberOfLaps;
        sn.SessionTimeLeft = G.SessionTimeLeft;
        sn.CurMs = G.iCurrentTime; sn.LastMs = G.iLastTime; sn.BestMs = G.iBestTime;
        sn.DeltaMs = G.IDeltaLapTime;
        sn.DeltaPositive = G.IsDeltaPositive != 0;
        sn.ValidLap = G.IsValidLap != 0;
        sn.CurStr = Clean(G.CurrentTime); sn.LastStr = Clean(G.LastTime);
        sn.BestStr = Clean(G.BestTime); sn.EstStr = Clean(G.EstimatedLapTime);
        sn.DeltaStr = Clean(G.DeltaLapTime);
        sn.Flag = G.Flag;
        sn.FuelXLap = G.FuelXLap;
        sn.UsedFuel = G.UsedFuel;
        sn.TrackGrip = G.trackGripStatus;
        sn.TrackStatus = Clean(G.TrackStatus);
        sn.RainIntensity = G.rainIntensity;

        sn.NumCars = S.NumCars;
        sn.Track = Clean(S.Track).Replace('_', ' ');
        sn.Car = Clean(S.CarModel).Replace('_', ' ');
        sn.TrackConfig = Clean(S.TrackConfiguration);
        sn.SectorCount = S.SectorCount;
        sn.MaxFuel = S.MaxFuel;
        sn.TyreName = Clean(S.dryTyresName);

        sn.Stamp = now;
        sn.Live = true;
        return true;
    }

    /// ACC gives steerAngle as a normalised input on most builds; older ones
    /// report radians.  Detect and normalise, then clamp.
    public static float SteerNorm(float raw)
    {
        float v = raw;
        if (Math.Abs(v) > 1.6f) v = (float)(v / (Math.PI / 2.0));   // radians
        if (Math.Abs(v) > 1.6f) v = v / 450f;                        // degrees of lock
        if (v > 1f) v = 1f;
        if (v < -1f) v = -1f;
        return v;
    }

    static float Avg2(float[] a, int i)
    {
        if (a == null || a.Length < i + 2) return 0f;
        return (a[i] + a[i + 1]) * 0.5f;
    }

    static float Avg4(float[] a)
    {
        if (a == null || a.Length < 4) return 0f;
        return (a[0] + a[1] + a[2] + a[3]) * 0.25f;
    }

    public static string Clean(string s)
    {
        if (s == null) return "";
        int z = s.IndexOf('\0');
        if (z >= 0) s = s.Substring(0, z);
        return s.Trim();
    }
}
