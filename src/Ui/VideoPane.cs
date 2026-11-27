// ===========================================================================
//  AccDash - the video card (live preview and lap replay share it)
//  src/Ui/VideoPane.cs
// ===========================================================================
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;


/// <summary>
/// Anything that can paint a picture into the video card: the live window
/// grabber while driving, the clip player during review.
/// </summary>
interface IVideoSource
{
    bool Draw(Graphics g, Rectangle dst);
    int Seq { get; }
}


/// <summary>
/// One 640x360 surface used by both modes: the live preview while driving and
/// the lap clip during review.  Same size, same frame rate, same control.
/// </summary>
sealed class VideoPane : Pane
{
    public IVideoSource Live;         // live preview source (may be null)
    public Bitmap Frame;              // review frame pushed by the player
    public bool InReview;
    public string Caption = "VIDEO";
    public string Corner = "";
    public string Note = "";

    /// Marks the area the picture may use, keeping 16:9 inside the card.
    public Rectangle Fit()
    {
        Rectangle r = ClientRectangle;
        int w = r.Width - 4, h = r.Height - 4;
        if (w < 8 || h < 8) return r;
        int fw = w, fh = (int)(w * 9.0 / 16.0);
        if (fh > h) { fh = h; fw = (int)(h * 16.0 / 9.0); }
        return new Rectangle(r.X + (r.Width - fw) / 2, r.Y + (r.Height - fh) / 2, fw, fh);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        Rectangle r = ClientRectangle;
        G.Fill(g, r, Pal.Panel);

        Rectangle fit = Fit();
        G.Fill(g, fit, Color.Black);

        bool drew = false;
        if (InReview && Frame != null)
        {
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(Frame, fit);
            drew = true;
        }
        else if (Live != null)
        {
            drew = Live.Draw(g, fit);
        }
        if (!drew)
        {
            string msg = Note.Length > 0 ? Note : "no video";
            G.TextMidC(g, msg, F.Small, Pal.Dim, fit);
        }

        // caption band over the top of the picture, like the track map
        Rectangle band = new Rectangle(fit.X, fit.Y, Math.Max(10, fit.Width), 17);
        using (SolidBrush veil = new SolidBrush(Color.FromArgb(165, 0, 0, 0))) g.FillRectangle(veil, band);
        G.TextMidL(g, Caption, F.CapS, Pal.Text, new Rectangle(band.X + 6, band.Y, band.Width - 12, band.Height), 0f);
        if (Corner.Length > 0) G.TextMidR(g, Corner, F.CapS, Pal.Dim, new Rectangle(band.X + 6, band.Y, band.Width - 12, band.Height), 0f);

        // a warning stays visible even when frames are flowing
        if (drew && Note.Length > 0)
        {
            Rectangle foot = new Rectangle(fit.X, fit.Bottom - 17, Math.Max(10, fit.Width), 17);
            using (SolidBrush veil = new SolidBrush(Color.FromArgb(180, 0, 0, 0))) g.FillRectangle(veil, foot);
            G.TextMidL(g, Note, F.CapS, Pal.Amber, new Rectangle(foot.X + 6, foot.Y, foot.Width - 12, foot.Height), 0f);
        }

        using (Pen p = new Pen(Pal.Line)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
    }
}
