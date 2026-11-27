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

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

    // --- window enumeration, used to find the game's screen ------------------

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int n);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder s, int n);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    // flag 2 = PW_RENDERFULLCONTENT: the only one that returns content from a
    // DirectX window instead of a black rectangle
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, int flags);

    // A DPI-unaware caller cannot print a DPI-unaware window: the sizes do not
    // match and the result is an empty bitmap.  Switching the grab thread to the
    // unaware context makes PrintWindow see the same pixels the game owns.
    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);

    [DllImport("user32.dll")]
    public static extern IntPtr GetThreadDpiAwarenessContext();

    public static readonly IntPtr DPI_UNAWARE = new IntPtr(-1);
    public static readonly IntPtr DPI_SYSTEM = new IntPtr(-2);

    // Opening a web page: ShellExecute is what the shell itself uses, so it goes
    // through the default browser even when Process.Start is refused.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr ShellExecute(IntPtr hwnd, string op, string file, string args, string dir, int show);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
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
