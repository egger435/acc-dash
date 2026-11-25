// ===========================================================================
//  AccDash - P/Invoke helpers and the dark palette
//  src/Interop.cs
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



static class Native
{
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

static class Pal
{
    public static readonly Color Bg = Color.FromArgb(8, 8, 8);
    public static readonly Color Panel = Color.FromArgb(21, 21, 21);
    public static readonly Color PanelHi = Color.FromArgb(34, 34, 34);
    public static readonly Color Line = Color.FromArgb(60, 60, 60);
    public static readonly Color Text = Color.FromArgb(236, 236, 236);
    public static readonly Color Dim = Color.FromArgb(150, 150, 150);
    public static readonly Color Accent = Color.FromArgb(200, 200, 200);
    public static readonly Color Green = Color.FromArgb(96, 214, 126);
    public static readonly Color Red = Color.FromArgb(232, 88, 88);
    public static readonly Color Amber = Color.FromArgb(226, 176, 88);
    public static readonly Color Violet = Color.FromArgb(150, 150, 150);
    public static readonly Color Blue = Color.FromArgb(170, 170, 170);
}
