// ===========================================================================
//  AccDash - settings dialog
//  src/Ui/Settings.cs
//
//  Edits every key of AccDash.ini without touching the text file by hand, shows
//  how much disk the video cache uses and can clear it.
// ===========================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;


sealed class SettingsBox : Form
{
    const int LblX = 18, CtlX = 276, RightPad = 18, RowH = 42;
    const string AuthorUrl = "https://github.com/egger435";

    public Cfg C;
    Action preClear;                  // stops recording / playback before the cache goes

    // a touch larger than the dashboard's own ink: this window gets read, not glanced at
    readonly Font fLbl = new Font("Segoe UI", 10.5f);
    readonly Font fSec = new Font("Segoe UI", 11f, FontStyle.Bold);
    readonly Font fVal = new Font("Consolas", 10.5f, FontStyle.Bold);
    readonly Font fTxt = new Font("Consolas", 10f);
    readonly Font fBtn = new Font("Segoe UI", 10f, FontStyle.Bold);

    int y = 14;
    bool mph, fahrenheit, videoOn;

    Slider sTrace, sG, sW, sH, sFps, sCrf, sOut;
    Label lTrace, lG, lSize, lFps, lCrf, lOut, lCache;
    FlatBtn bUnit, bTemp, bVideo;
    TextBox tFf, tArea;

    public SettingsBox(Cfg c, Action beforeClear)
    {
        C = c;
        preClear = beforeClear;

        Text = "SETTINGS";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Pal.Bg;
        ForeColor = Pal.Text;
        Font = fLbl;
        ClientSize = new Size(752, 736);
        KeyPreview = true;

        mph = C.SpeedUnit == "mph";
        fahrenheit = C.TempUnit == "f";
        videoOn = C.Video;

        BuildRows();
        BuildFooter();
    }

    // --- small builders ----------------------------------------------------

    void Section(string text)
    {
        y += 8;
        Label l = new Label();
        l.Text = text;
        l.ForeColor = Pal.Text;
        l.Font = fSec;
        l.BackColor = Pal.Bg;
        l.Bounds = new Rectangle(LblX, y, 380, 24);
        Controls.Add(l);
        y += 30;
    }

    void RowLabel(string text)
    {
        Label l = new Label();
        l.Text = text;
        l.ForeColor = Pal.Dim;
        l.Font = fLbl;
        l.BackColor = Pal.Bg;
        l.TextAlign = ContentAlignment.MiddleLeft;
        l.Bounds = new Rectangle(LblX, y + 8, CtlX - LblX - 12, 26);
        Controls.Add(l);
    }

    Label Value(string text, int width)
    {
        Label l = new Label();
        l.Text = text;
        l.ForeColor = Pal.Text;
        l.Font = fVal;
        l.BackColor = Pal.Bg;
        l.TextAlign = ContentAlignment.MiddleRight;
        l.Bounds = new Rectangle(ClientSize.Width - RightPad - width - 10, y + 8, width, 26);
        Controls.Add(l);
        return l;
    }

    Slider MakeSlider(int min, int max, int val)
    {
        Slider s = new Slider();
        s.Min = min;
        s.Max = max;
        s.Value = val;
        s.BackColor = Pal.Bg;
        s.Bounds = new Rectangle(CtlX, y + 9, ClientSize.Width - CtlX - 160, 26);
        Controls.Add(s);
        return s;
    }

    FlatBtn MakeToggle(string text, EventHandler onHit)
    {
        FlatBtn b = new FlatBtn(text);
        b.FontOverride = fBtn;
        b.Bounds = new Rectangle(CtlX, y + 8, 112, 30);
        b.BackColor = Pal.Bg;
        Controls.Add(b);
        b.Hit += onHit;
        return b;
    }

    TextBox MakeText(string text)
    {
        TextBox t = new TextBox();
        t.Text = text;
        t.BorderStyle = BorderStyle.FixedSingle;
        t.BackColor = Pal.PanelHi;
        t.ForeColor = Pal.Text;
        t.Font = fTxt;
        t.Bounds = new Rectangle(CtlX, y + 7, ClientSize.Width - CtlX - RightPad, 30);
        Controls.Add(t);
        return t;
    }

    // --- rows --------------------------------------------------------------

    void BuildRows()
    {
        Section("DISPLAY");

        RowLabel("trace window");
        sTrace = MakeSlider(3, 60, Math.Max(3, Math.Min(60, C.TraceSeconds)));
        lTrace = Value(sTrace.Value.ToString(Fmt.Inv) + " s", 70);
        sTrace.ValueChanged += delegate { lTrace.Text = sTrace.Value.ToString(Fmt.Inv) + " s"; };
        y += RowH;

        RowLabel("speed unit");
        bUnit = MakeToggle(mph ? "MPH" : "KM/H", delegate
        {
            mph = !mph;
            bUnit.Caption = mph ? "MPH" : "KM/H";
            bUnit.Invalidate();
        });
        y += RowH;

        RowLabel("temp unit");
        bTemp = MakeToggle(fahrenheit ? "FAHRENHEIT" : "CELSIUS", delegate
        {
            fahrenheit = !fahrenheit;
            bTemp.Caption = fahrenheit ? "FAHRENHEIT" : "CELSIUS";
            bTemp.Invalidate();
        });
        y += RowH;

        RowLabel("G-G circle scale");
        sG = MakeSlider(5, 40, (int)Math.Round(Math.Max(0.5f, Math.Min(4f, C.GScale)) * 10f));
        lG = Value(GText(), 70);
        sG.ValueChanged += delegate { lG.Text = GText(); };
        y += RowH;

        Section("VIDEO");

        RowLabel("record clips");
        bVideo = MakeToggle(videoOn ? "ON" : "OFF", delegate
        {
            videoOn = !videoOn;
            bVideo.Caption = videoOn ? "ON" : "OFF";
            bVideo.Invalidate();
        });
        y += RowH;

        RowLabel("clip size");
        sW = MakeSlider(160, 1280, Math.Max(160, Math.Min(1280, C.VideoW)));
        lSize = Value(SizeText(), 120);
        sW.ValueChanged += delegate { lSize.Text = SizeText(); };
        y += RowH;

        RowLabel("clip height");
        sH = MakeSlider(90, 720, Math.Max(90, Math.Min(720, C.VideoH)));
        sH.ValueChanged += delegate { lSize.Text = SizeText(); };
        y += RowH;

        RowLabel("clip frame rate");
        sFps = MakeSlider(5, 60, Math.Max(5, Math.Min(60, C.VideoFps)));
        lFps = Value(sFps.Value.ToString(Fmt.Inv) + " fps", 70);
        sFps.ValueChanged += delegate { lFps.Text = sFps.Value.ToString(Fmt.Inv) + " fps"; };
        y += RowH;

        RowLabel("clip quality (crf)");
        sCrf = MakeSlider(16, 34, Math.Max(16, Math.Min(34, C.VideoCrf)));
        lCrf = Value(sCrf.Value.ToString(Fmt.Inv), 70);
        sCrf.ValueChanged += delegate { lCrf.Text = sCrf.Value.ToString(Fmt.Inv); };
        y += RowH;

        RowLabel("ffmpeg path");
        tFf = MakeText(C.FfmpegPath);
        y += RowH;

        RowLabel("capture area");
        tArea = MakeText(C.VideoArea.Width > 0 ? C.VideoArea.X.ToString(Fmt.Inv) + "," + C.VideoArea.Y.ToString(Fmt.Inv) + "," + C.VideoArea.Width.ToString(Fmt.Inv) + "," + C.VideoArea.Height.ToString(Fmt.Inv) : "");
        y += RowH;

        RowLabel("capture output");
        sOut = MakeSlider(-1, 4, Math.Max(-1, Math.Min(4, C.VideoOutput)));
        lOut = Value(OutText(), 70);
        sOut.ValueChanged += delegate { lOut.Text = OutText(); };
        y += RowH;

        Section("CACHE");

        RowLabel("video cache");
        lCache = new Label();
        lCache.Text = CacheText();
        lCache.ForeColor = Pal.Text;
        lCache.Font = fLbl;
        lCache.BackColor = Pal.Bg;
        lCache.TextAlign = ContentAlignment.MiddleLeft;
        lCache.Bounds = new Rectangle(CtlX, y + 8, 300, 26);
        Controls.Add(lCache);

        FlatBtn bClear = new FlatBtn("CLEAR");
        bClear.FontOverride = fBtn;
        bClear.Bounds = new Rectangle(ClientSize.Width - RightPad - 104, y + 8, 104, 30);
        Controls.Add(bClear);
        bClear.Hit += delegate { ClearCache(); };
        y += RowH;
    }

    void BuildFooter()
    {
        int by = ClientSize.Height - 52;

        // bottom line: ACC mark, dash mark, then the author line (Egger is a link)
        const int ico = 30;
        Image acc = LoadIcon("acc-icon.png", ico);
        Image dash = LoadIcon("icon-256.png", ico);
        int x = LblX;
        if (acc != null)
        {
            PictureBox pb = new PictureBox();
            pb.Image = acc;
            pb.SizeMode = PictureBoxSizeMode.Zoom;
            pb.BackColor = Pal.Bg;
            pb.Bounds = new Rectangle(x, by, ico, ico);
            Controls.Add(pb);
            x += ico + 8;
        }
        if (dash != null)
        {
            PictureBox pb = new PictureBox();
            pb.Image = dash;
            pb.SizeMode = PictureBoxSizeMode.Zoom;
            pb.BackColor = Pal.Bg;
            pb.Bounds = new Rectangle(x, by, ico, ico);
            Controls.Add(pb);
            x += ico + 12;
        }

        Label who = new Label();
        who.Text = "Egger & dsh";
        who.UseMnemonic = false;                       // keep the ampersand
        who.ForeColor = Pal.Text;
        who.Font = new Font("Segoe UI", 11f);
        who.BackColor = Pal.Bg;
        who.TextAlign = ContentAlignment.MiddleLeft;
        who.Bounds = new Rectangle(x, by + 2, 300, 26);
        Controls.Add(who);

        FlatBtn bSave = new FlatBtn("SAVE");
        bSave.FontOverride = fBtn;
        bSave.Bounds = new Rectangle(ClientSize.Width - RightPad - 120, by, 120, 34);
        Controls.Add(bSave);
        bSave.Hit += delegate
        {
            Apply();
            DialogResult = DialogResult.OK;
            Close();
        };

        FlatBtn bCancel = new FlatBtn("CANCEL");
        bCancel.FontOverride = fBtn;
        bCancel.Bounds = new Rectangle(ClientSize.Width - RightPad - 120 - 8 - 112, by, 112, 34);
        Controls.Add(bCancel);
        bCancel.Hit += delegate { DialogResult = DialogResult.Cancel; Close(); };
    }

    string GText() { return (sG.Value / 10f).ToString("0.0", Fmt.Inv) + " g"; }
    string SizeText()
    {
        int w = sW != null ? sW.Value : C.VideoW;
        int h = sH != null ? sH.Value : C.VideoH;
        return w.ToString(Fmt.Inv) + "x" + h.ToString(Fmt.Inv);
    }
    string OutText() { return sOut.Value < 0 ? "auto" : sOut.Value.ToString(Fmt.Inv); }

    // --- apply / cache ------------------------------------------------------

    public void Apply()
    {
        C.TraceSeconds = sTrace.Value;
        C.SpeedUnit = mph ? "mph" : "kmh";
        C.TempUnit = fahrenheit ? "f" : "c";
        C.GScale = sG.Value / 10f;
        C.Video = videoOn;
        C.VideoW = sW.Value;
        C.VideoH = sH.Value;
        C.VideoFps = sFps.Value;
        C.VideoCrf = sCrf.Value;
        C.FfmpegPath = tFf.Text.Trim();
        C.VideoArea = ParseRect(tArea.Text.Trim());
        C.VideoOutput = sOut.Value;
        C.Save();
    }

    static Rectangle ParseRect(string s)
    {
        string[] f = s.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 4) return Rectangle.Empty;
        int x, yy, w, h;
        if (!int.TryParse(f[0], out x)) return Rectangle.Empty;
        if (!int.TryParse(f[1], out yy)) return Rectangle.Empty;
        if (!int.TryParse(f[2], out w)) return Rectangle.Empty;
        if (!int.TryParse(f[3], out h)) return Rectangle.Empty;
        if (w < 64 || h < 64) return Rectangle.Empty;
        return new Rectangle(x, yy, w, h);
    }

    static string CacheRoot
    {
        get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache"); }
    }

    static string CacheText()
    {
        long bytes = 0;
        int files = 0;
        try
        {
            if (Directory.Exists(CacheRoot))
            {
                foreach (string f in Directory.GetFiles(CacheRoot, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; files++; } catch { }
                }
            }
        }
        catch { }
        return Size(bytes) + "   " + files.ToString(Fmt.Inv) + " files";
    }

    static string Size(long b)
    {
        if (b >= 1073741824L) return (b / 1073741824.0).ToString("0.00", Fmt.Inv) + " GB";
        if (b >= 1048576L) return (b / 1048576.0).ToString("0.0", Fmt.Inv) + " MB";
        if (b >= 1024L) return (b / 1024.0).ToString("0", Fmt.Inv) + " KB";
        return b.ToString(Fmt.Inv) + " B";
    }

    void ClearCache()
    {
        if (MessageBox.Show(this,
                "Delete the recorded chunks and the clip cache?\n\n"
              + "Lap videos already stored inside .accdash files are not touched.",
                "Clear cache", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        if (preClear != null) preClear();
        try
        {
            if (Directory.Exists(CacheRoot)) Directory.Delete(CacheRoot, true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not clear the cache: " + ex.Message, "Clear cache");
        }
        lCache.Text = CacheText();
    }

    /// Opens the author page; shell execute with a cmd fallback so it works even
    /// when the shell association is missing.
    /// Loads one of the png files in assets\ and scales it into a square box.
    static Image LoadIcon(string file, int px)
    {
        try
        {
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", file);
            if (!File.Exists(p)) return null;
            using (Image src = Image.FromFile(p))
            {
                Bitmap b = new Bitmap(px, px);
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    float sc = Math.Min(px / (float)src.Width, px / (float)src.Height);
                    int w = Math.Max(1, (int)Math.Round(src.Width * sc));
                    int h = Math.Max(1, (int)Math.Round(src.Height * sc));
                    g.DrawImage(src, (px - w) / 2, (px - h) / 2, w, h);
                }
                return b;
            }
        }
        catch { return null; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    // --- window chrome ------------------------------------------------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int one = 1;
            int zero = 0;
            Native.DwmSetWindowAttribute(Handle, 20, ref one, 4);
            Native.DwmSetWindowAttribute(Handle, 19, ref one, 4);
            Native.DwmSetWindowAttribute(Handle, 35, ref zero, 4);
            int text = 0x00E8E8E8;
            Native.DwmSetWindowAttribute(Handle, 36, ref text, 4);
            int border = 0x001A1A1A;
            Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);
        }
        catch { }
    }

    /// Off screen render for the screenshot modes: no title bar, so DrawToBitmap
    /// pictures exactly what the user sees.
    public void ShotTo(string path)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(0, 0);
        Show();
        Application.DoEvents();
        Refresh();
        using (Bitmap b = new Bitmap(ClientSize.Width, ClientSize.Height))
        {
            DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
            b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        Close();
    }
}
