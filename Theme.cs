using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UhfTagAnalyzer
{
    // Jemne, elegantni vizualni tema: svetle plochy, jeden kultivovany akcent,
    // mekka zaoblena tlacitka, karty misto etched ramecku.
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(0xF6, 0xF7, 0xF9);
        public static readonly Color Surface = Color.White;
        public static readonly Color Ink = Color.FromArgb(0x2A, 0x2E, 0x37);
        public static readonly Color Muted = Color.FromArgb(0x8A, 0x91, 0x9E);
        public static readonly Color Faint = Color.FromArgb(0xB6, 0xBC, 0xC6);
        public static readonly Color Hairline = Color.FromArgb(0xE6, 0xE8, 0xEC);
        public static readonly Color Accent = Color.FromArgb(0x5B, 0x8D, 0xEF);     // jemna modra
        public static readonly Color AccentHover = Color.FromArgb(0x4F, 0x7D, 0xD6);
        public static readonly Color AccentSoft = Color.FromArgb(0xED, 0xF3, 0xFE); // svetle pozadi akcentu
        public static readonly Color Good = Color.FromArgb(0x2E, 0xA6, 0x72);
        public static readonly Color Green = Color.FromArgb(0x24, 0xA7, 0x6A);
        public static readonly Color GreenHover = Color.FromArgb(0x1E, 0x93, 0x5C);
        public static readonly Color Warn = Color.FromArgb(0xD8, 0x9A, 0x30);
        public static readonly Color Bad = Color.FromArgb(0xE0, 0x5A, 0x52);
        public static readonly Color BadSoft = Color.FromArgb(0xFB, 0xEC, 0xEB);
        public static readonly Color LogBg = Color.FromArgb(0xFB, 0xFB, 0xFD);
        public static readonly Color LogText = Color.FromArgb(0x5B, 0x61, 0x6E);

        public static Font UiFont => new Font("Segoe UI", 9f);
        public static Font MonoFont => new Font("Consolas", 9.5f);

        static Image _appIcon;
        public static Image AppIcon
        {
            get
            {
                if (_appIcon == null)
                {
                    try
                    {
                        using var st = typeof(Theme).Assembly.GetManifestResourceStream("UhfTagAnalyzer.app.ico");
                        if (st != null) using (var ic = new Icon(st, 128, 128)) _appIcon = ic.ToBitmap();
                    }
                    catch { }
                }
                return _appIcon;
            }
        }

        static Image _headerImg;
        static bool _headerTried;
        public static Image HeaderImage
        {
            get
            {
                if (!_headerTried)
                {
                    _headerTried = true;
                    try
                    {
                        using var st = typeof(Theme).Assembly.GetManifestResourceStream("UhfTagAnalyzer.header.png");
                        if (st != null) _headerImg = Image.FromStream(st);
                    }
                    catch { }
                }
                return _headerImg;
            }
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void StyleTabs(TabControl t)
        {
            t.DrawMode = TabDrawMode.OwnerDrawFixed;
            t.SizeMode = TabSizeMode.Fixed;
            t.ItemSize = new Size(116, 33);
            t.Appearance = TabAppearance.Normal;
            t.DrawItem += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = t.GetTabRect(e.Index);
                bool sel = e.Index == t.SelectedIndex;
                using (var bg = new SolidBrush(sel ? Surface : Bg)) g.FillRectangle(bg, rect);
                if (sel)
                    using (var bar = new SolidBrush(Accent))
                        g.FillRectangle(bar, new Rectangle(rect.X + 14, rect.Bottom - 3, rect.Width - 28, 3));
                string txt = t.TabPages[e.Index].Text;
                var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using var tb = new SolidBrush(sel ? Ink : Muted);
                using var f = new Font("Segoe UI", 9.5f, sel ? FontStyle.Bold : FontStyle.Regular);
                g.DrawString(txt, f, tb, rect, fmt);
            };
        }
    }

    // Mekke zaoblene tlacitko s jemnym hoverem.
    public class SoftButton : Button
    {
        public enum Kind { Primary, Secondary, Danger, Success }
        Kind _kind;
        bool _hover;

        public SoftButton(Kind kind)
        {
            _kind = kind;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AutoSize = false;
            Height = 32;
            Font = new Font("Segoe UI Semibold", 9f);
            Margin = new Padding(4, 2, 4, 2);
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        public void FitWidth()
        {
            int w = TextRenderer.MeasureText(Text, Font).Width + 30;
            Width = Math.Max(w, 76);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = Theme.Round(rect, 7);

            Color fill, text, border = Color.Empty;
            bool en = Enabled;
            switch (_kind)
            {
                case Kind.Primary:
                    fill = !en ? Color.FromArgb(0xCF, 0xDD, 0xF6) : _hover ? Theme.AccentHover : Theme.Accent;
                    text = Color.White; break;
                case Kind.Success:
                    fill = !en ? Color.FromArgb(0xC9, 0xE6, 0xD6) : _hover ? Theme.GreenHover : Theme.Green;
                    text = Color.White; break;
                case Kind.Danger:
                    fill = !en ? Color.FromArgb(0xF0, 0xCF, 0xCD) : _hover ? Color.FromArgb(0xD1, 0x48, 0x40) : Theme.Bad;
                    text = Color.White; break;
                default: // Secondary – ghost
                    fill = _hover ? Theme.AccentSoft : Theme.Surface;
                    text = en ? Theme.Ink : Theme.Faint;
                    border = _hover ? Theme.Accent : Theme.Hairline; break;
            }
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (border != Color.Empty) using (var p = new Pen(border)) g.DrawPath(p, path);
            TextRenderer.DrawText(g, Text, Font, rect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // Stavova kontrolka (LED) – seda kdyz vypnuto, zelena se zarim kdyz zapnuto.
    public class StatusLed : Control
    {
        bool _on;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool On { get => _on; set { _on = value; Invalidate(); } }
        public StatusLed()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(20, 32);
            BackColor = Color.Transparent;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = 11, cx = Width / 2, cy = Height / 2;
            var core = new Rectangle(cx - d / 2, cy - d / 2, d, d);
            Color c = _on ? Theme.Green : Color.FromArgb(0xC7, 0xCC, 0xD4);
            if (_on)
            {
                using var glow = new SolidBrush(Color.FromArgb(70, Theme.Green));
                g.FillEllipse(glow, cx - 10, cy - 10, 20, 20);
            }
            using (var b = new SolidBrush(c)) g.FillEllipse(b, core);
            using (var hl = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillEllipse(hl, cx - 3, cy - 4, 4, 4);
            using (var p = new Pen(Color.FromArgb(40, 0, 0, 0))) g.DrawEllipse(p, core);
        }
    }

    // Karta: bila zaoblena plocha s jemnym ramem a volitelnym titulkem.
    public class Card : Panel
    {
        public string Title;
        public Card(string title = null)
        {
            Title = title;
            BackColor = Theme.Bg;
            Padding = new Padding(14, title != null ? 34 : 14, 14, 14);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            var rect = new Rectangle(2, 2, Width - 5, Height - 5);
            using var path = Theme.Round(rect, 10);
            using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
            using (var p = new Pen(Theme.Hairline)) g.DrawPath(p, path);
            if (!string.IsNullOrEmpty(Title))
            {
                using var f = new Font("Segoe UI Semibold", 8.5f);
                using var br = new SolidBrush(Theme.Muted);
                g.DrawString(Title.ToUpperInvariant(), f, br, 16, 12);
                using var hp = new Pen(Theme.Hairline);
                g.DrawLine(hp, 14, 30, Width - 16, 30);
            }
        }
    }

    // Hlavicka = brandovany banner (header.png) pres celou sirku. Nazev, ikona, vlna
    // i podpis jsou primo v obrazku. Vyska se dopocitava z pomeru stran, aby se cely
    // banner zobrazil bez oriznuti. Klikaci podpis zustava ve spodnim status baru.
    public class HeaderBar : Panel
    {
        static readonly Color HdrBg = Color.FromArgb(0x08, 0x0C, 0x11);
        static readonly Color SigText = Color.FromArgb(0xC7, 0xCE, 0xD8);
        static readonly Color SigLink = Color.FromArgb(0x5E, 0xE8, 0xC8);
        const string SigA = "Hesse Valentino · ";
        const string SigB = "hesse.works";
        const string Url = "https://hesse.works";
        double _aspect = 1024.0 / 36.0;
        Rectangle _linkRect;
        readonly Font _sigFont = new Font("Segoe UI", 9f);

        public HeaderBar()
        {
            Dock = DockStyle.Top;
            BackColor = HdrBg;
            // UserPaint + DoubleBuffer + ResizeRedraw: cely panel se prekresli pri kazde
            // zmene velikosti (jinak zustane stara grafika pod novou -> zdvojeni pri maximalizaci).
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            var img = Theme.HeaderImage;
            if (img != null && img.Height > 0) _aspect = img.Width / (double)img.Height;
            Height = 44;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0) return;
            int h = Math.Max(28, Math.Min(80, (int)Math.Round(Width / _aspect)));
            if (Height != h) { Height = h; return; } // zmena vysky vyvola dalsi prekresleni
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(HdrBg)) g.FillRectangle(bg, ClientRectangle);

            var img = Theme.HeaderImage;
            if (img != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                // banner vyplni celou plochu panelu; vyska ~ Width/pomer -> bez deformace
                g.DrawImage(img, new Rectangle(0, 0, Width, Height), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel);
            }

            // === prekryti rozmazaneho zapecenaho podpisu vpravo + cisty textovy kredit ===
            var fl = TextFormatFlags.NoPadding;
            Size sa = TextRenderer.MeasureText(g, SigA, _sigFont, Size.Empty, fl);
            Size sb = TextRenderer.MeasureText(g, SigB, _sigFont, Size.Empty, fl);
            int total = sa.Width + sb.Width;
            int tx = Width - total - 18;
            int ty = (Height - sa.Height) / 2;

            // cover: plynule z banneru do tmave, pak plna tmava pod textem
            int coverLeft = tx - 56;
            var fade = new Rectangle(coverLeft, 0, 56, Height);
            using (var lg = new LinearGradientBrush(fade, Color.FromArgb(0, HdrBg), HdrBg, LinearGradientMode.Horizontal))
                g.FillRectangle(lg, fade);
            using (var b = new SolidBrush(HdrBg)) g.FillRectangle(b, coverLeft + 56, 0, Width - (coverLeft + 56), Height);

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(g, SigA, _sigFont, new Point(tx, ty), SigText, fl);
            TextRenderer.DrawText(g, SigB, _sigFont, new Point(tx + sa.Width, ty), SigLink, fl);
            _linkRect = new Rectangle(tx + sa.Width, ty, sb.Width, sb.Height);

            using (var hp = new Pen(Color.FromArgb(60, 0x5E, 0xE8, 0xC8))) g.DrawLine(hp, 0, Height - 1, Width, Height - 1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = _linkRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_linkRect.Contains(e.Location))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Url) { UseShellExecute = true }); } catch { }
        }
    }
}
