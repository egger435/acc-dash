// ===========================================================================
//  AccDash - lap list control and the laps dialog
//  src/Laps/LapBrowser.cs
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


/// Modal list of recorded and imported laps.
// A plain ListBox paints its empty area with the system window colour and only
// lets owner-draw fill the item rects, so a short lap list showed a big white
// block.  This draws the whole box itself: rows, selection, and the empty space.
sealed class LapList : Pane
{
    public List<LapData> Rows;
    public int Sel = -1;
    public event EventHandler SelChanged;
    public event EventHandler Chosen;

    const int RowH = 24;
    int scroll;

    readonly Font rowFont = new Font("Consolas", 10f);

    public LapList()
    {
        BackColor = Pal.Panel;
        SetStyle(ControlStyles.Selectable, true);
    }

    int VisibleRows { get { return Math.Max(1, Height / RowH); } }

    void ClampScroll()
    {
        int maxFirst = Math.Max(0, (Rows == null ? 0 : Rows.Count) - VisibleRows);
        if (scroll > maxFirst) scroll = maxFirst;
        if (scroll < 0) scroll = 0;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        G.Fill(g, new Rectangle(0, 0, Width, Height), BackColor);
        if (Rows == null || Rows.Count == 0)
        {
            G.TextMidC(g, "no laps recorded yet", F.Small, Pal.Dim, new Rectangle(6, 0, Width - 12, Height));
            return;
        }
        ClampScroll();
        for (int i = scroll; i < Rows.Count && i < scroll + VisibleRows; i++)
        {
            int y = (i - scroll) * RowH;
            if (i == Sel) G.Fill(g, new Rectangle(0, y, Width, RowH), Pal.PanelHi);
            LapData d = Rows[i];
            Color c = d.FromFile ? Pal.Amber : Pal.Text;
            G.TextMidL(g, d.Name, rowFont, i == Sel ? Pal.Text : c, new Rectangle(6, y, Width - 12, RowH), 0f);
        }
        // a hairline under the last row so the list does not look cut off
        int used = Math.Min(VisibleRows, Rows.Count - scroll) * RowH;
        if (used < Height) G.Fill(g, new Rectangle(6, used, Width - 12, 1), Pal.Line);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Rows == null || Rows.Count == 0) return;
        int i = scroll + e.Y / RowH;
        if (i < 0 || i >= Rows.Count) return;
        if (i != Sel)
        {
            Sel = i;
            Invalidate();
            if (SelChanged != null) SelChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (Sel >= 0 && Chosen != null) Chosen(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        scroll -= Math.Sign(e.Delta) * 2;
        ClampScroll();
        Invalidate();
    }
}

sealed class LapBrowser : Form
{
    public List<LapData> Laps;
    public LapData Chosen;
    public bool Changed;

    const string HintDefault = "Laps you complete are saved here.  IMPORT... loads a lap from a file.";

    LapList list;
    FlatBtn bView, bExport, bImport, bDelete, bClose;
    Label hint;

    public LapBrowser(List<LapData> laps)
    {
        Laps = laps;
        Text = "Laps";
        BackColor = Pal.Bg;
        ForeColor = Pal.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(620, 440);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9f);
        DoubleBuffered = true;

        list = new LapList();
        list.Rows = Laps;
        list.BackColor = Pal.Panel;
        list.Chosen += delegate { Accept(); };
        Controls.Add(list);

        hint = new Label();
        hint.AutoSize = false;
        hint.AutoEllipsis = false;
        hint.Font = new Font("Segoe UI", 8f);
        hint.TextAlign = ContentAlignment.MiddleLeft;
        hint.ForeColor = Pal.Dim;
        hint.BackColor = Pal.Bg;
        hint.Text = HintDefault;
        Controls.Add(hint);

        bView = Mk("VIEW", 84);
        bExport = Mk("EXPORT...", 104);
        bImport = Mk("IMPORT...", 104);
        bDelete = Mk("DELETE", 84);
        bClose = Mk("CLOSE", 84);

        bView.Hit += delegate { if (NeedSel()) Accept(); };
        bExport.Hit += delegate { if (NeedSel()) Export(); };
        bImport.Hit += delegate { Import(); };
        bDelete.Hit += delegate { if (NeedSel()) Delete(); };
        bClose.Hit += delegate { DialogResult = DialogResult.Cancel; Close(); };

        list.SelChanged += delegate { SetHint(HintDefault, Pal.Dim); RefreshButtons(); };

        KeyPreview = true;
        KeyDown += delegate(object s, KeyEventArgs k)
        {
            if (k.KeyCode == Keys.Enter) { if (NeedSel()) Accept(); k.Handled = true; }
            else if (k.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };

        Resize += delegate { DoLayout(); };
        Refresh2();



    }

    /// Renders the dialog to a png without needing a visible desktop (layout check).
    public void ShotTo(string path)
    {
        // borderless, otherwise the bitmap includes the title bar and shifts the
        // whole client area down by its height
        FormBorderStyle = FormBorderStyle.None;
        Show();
        Application.DoEvents();
        DoLayout();
        Refresh();
        using (Bitmap b = new Bitmap(ClientSize.Width, ClientSize.Height))
        {
            DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
            b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        Close();
    }

    void DoLayout()
    {
        // Lay the dialog out from the bottom up: the hint and the button row get
        // their space first, the list takes whatever is left.  The old version
        // guessed the hint height (18px for a 2-line string), so the list grew
        // over the hint and over the top of the buttons.
        int w = ClientSize.Width, h = ClientSize.Height;
        int btnH = 30;
        int btnTop = h - 12 - btnH;
        int hintH = 26;
        int hintTop = btnTop - 6 - hintH;
        int listTop = 12;
        int listH = Math.Max(60, hintTop - 10 - listTop);
        list.SetBounds(12, listTop, Math.Max(120, w - 24), listH);
        hint.SetBounds(12, hintTop, Math.Max(120, w - 24), hintH);
        bView.SetBounds(12, btnTop, 84, btnH);
        bExport.SetBounds(104, btnTop, 104, btnH);
        bImport.SetBounds(216, btnTop, 104, btnH);
        bDelete.SetBounds(328, btnTop, 84, btnH);
        bClose.SetBounds(w - 12 - 84, btnTop, 84, btnH);
    }

    FlatBtn Mk(string text, int w)
    {
        FlatBtn b = new FlatBtn(text);
        b.Width = w;
        b.Height = 30;
        Controls.Add(b);
        return b;
    }

    void Refresh2()
    {
        int keep = list.Sel;
        list.Rows = Laps;
        if (Laps.Count == 0) list.Sel = -1;
        else if (keep < 0) list.Sel = -1;
        else list.Sel = Math.Max(0, Math.Min(Laps.Count - 1, keep));
        list.Invalidate();
        RefreshButtons();
        DoLayout();
    }

    // A FlatStyle.Flat button paints its disabled caption with the system's
    // disabled ink, which is almost invisible on the near-black dialog - that is
    // what made VIEW / EXPORT / DELETE look like empty frames.  Keep them enabled
    // and dim the caption instead; the click handler then asks for a selection.
    void RefreshButtons()
    {
        bool any = list.Sel >= 0;
        bView.Dimmed = bExport.Dimmed = bDelete.Dimmed = !any;
        bView.Invalidate(); bExport.Invalidate(); bDelete.Invalidate();
    }

    void SetHint(string text, Color color)
    {
        hint.ForeColor = color;
        hint.Text = text;
    }

    bool NeedSel()
    {
        if (list.Sel >= 0) return true;
        SetHint("Select a lap in the list first - or use IMPORT... to load one from a file.", Pal.Amber);
        return false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int one = 1;
            Native.DwmSetWindowAttribute(Handle, 20, ref one, 4);   // dark mode
            Native.DwmSetWindowAttribute(Handle, 19, ref one, 4);
            int caption = 0x000000;
            Native.DwmSetWindowAttribute(Handle, 35, ref caption, 4);
            int textCol = 0xE8E8E8;
            Native.DwmSetWindowAttribute(Handle, 36, ref textCol, 4);
            int border = 0x1A1A1A;
            Native.DwmSetWindowAttribute(Handle, 34, ref border, 4);
        }
        catch { }
    }


    LapData Selected
    {
        get
        {
            int i = list.Sel;
            return (i >= 0 && i < Laps.Count) ? Laps[i] : null;
        }
    }

    void Accept()
    {
        LapData d = Selected;
        if (d == null) return;
        Chosen = d;
        DialogResult = DialogResult.OK;
        Close();
    }

    void Export()
    {
        LapData d = Selected;
        if (d == null) return;
        SaveFileDialog fd = new SaveFileDialog();
        fd.Title = "Export lap";
        fd.Filter = "ACC Dash lap (*.accdash)|*.accdash|All files (*.*)|*.*";
        fd.FileName = SafeName(d);
        if (fd.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(fd.FileName, d.ToText());
            SetHint("Exported " + Path.GetFileName(fd.FileName) + "  (" + d.Samples.Count.ToString(Fmt.Inv) + " samples)", Pal.Dim);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Export failed: " + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void Import()
    {
        OpenFileDialog fd = new OpenFileDialog();
        fd.Title = "Import lap";
        fd.Filter = "ACC Dash lap (*.accdash;*.csv)|*.accdash;*.csv|All files (*.*)|*.*";
        if (fd.ShowDialog(this) != DialogResult.OK) return;
        string err;
        LapData d = LapData.Parse(File.ReadAllText(fd.FileName), fd.FileName, out err);
        if (d == null)
        {
            MessageBox.Show(this, "Could not read this lap:\n" + err, "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Laps.Add(d);
        Changed = true;
        Refresh2();
        list.Sel = Laps.Count - 1;
        list.Invalidate();
        RefreshButtons();
        SetHint("Imported " + Path.GetFileName(fd.FileName) + "  (" + d.Samples.Count.ToString(Fmt.Inv) + " samples)", Pal.Dim);
    }

    void Delete()
    {
        LapData d = Selected;
        if (d == null) return;
        Laps.Remove(d);
        Changed = true;
        Refresh2();
        SetHint("Removed " + d.Name.Trim(), Pal.Dim);
    }

    static string SafeName(LapData d)
    {
        string t = string.IsNullOrEmpty(d.Track) ? "lap" : d.Track;
        t = t.Replace(" ", "-").Replace("/", "-").Replace("\\", "-");
        return t + "-L" + d.LapNo.ToString(Fmt.Inv) + "-" + ((int)d.LapMs).ToString(Fmt.Inv) + ".accdash";
    }
}
