using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenPets
{
    // Identidade visual das telas (a mesma do site): creme, chocolate, laranja do Claude e fonte pixelada.
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(253, 243, 227);
        public static readonly Color Bg2 = Color.FromArgb(247, 230, 204);
        public static readonly Color Card = Color.FromArgb(255, 250, 240);
        public static readonly Color Ink = Color.FromArgb(43, 29, 20);
        public static readonly Color Ink2 = Color.FromArgb(107, 82, 66);
        public static readonly Color Accent = Color.FromArgb(217, 119, 87);
        public static readonly Color Accent2 = Color.FromArgb(184, 95, 66);
        public static readonly Color Mint = Color.FromArgb(127, 209, 174);
        public static readonly Color Sun = Color.FromArgb(255, 202, 58);

        static PrivateFontCollection fonts;
        static FontFamily pixel;

        [DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint count);

        // Pixelify Sans vem embutida no .exe (licença OFL, ver src/recursos). Sem ela, usa Segoe UI.
        public static FontFamily PixelFamily
        {
            get
            {
                if (pixel != null) return pixel;
                try
                {
                    using (var s = typeof(Theme).Assembly.GetManifestResourceStream("recursos/PixelifySans.ttf"))
                    {
                        if (s != null)
                        {
                            var data = new byte[s.Length];
                            s.Read(data, 0, data.Length);
                            fonts = new PrivateFontCollection();
                            IntPtr mem = Marshal.AllocCoTaskMem(data.Length);
                            Marshal.Copy(data, 0, mem, data.Length);
                            fonts.AddMemoryFont(mem, data.Length);   // a memória precisa ficar viva enquanto a fonte for usada
                            // os Labels desenham pelo GDI, que só enxerga a fonte se ela também for registrada nele
                            uint n = 0;
                            AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n);
                            pixel = fonts.Families[0];
                        }
                    }
                }
                catch { }
                return pixel ?? (pixel = new FontFamily("Segoe UI"));
            }
        }

        public static Font Pixel(float size) { return new Font(PixelFamily, size, FontStyle.Regular, GraphicsUnit.Point); }
        public static Font Body(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size, style); }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Cartão com borda grossa e sombra dura, como no site.
        public static void DrawCard(Graphics g, Rectangle r, Color fill, float k, int shadow = 5, int radius = 12)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int sh = (int)(shadow * k), bw = Math.Max(2, (int)(3 * k));
            var box = new RectangleF(r.X + bw / 2f, r.Y + bw / 2f, r.Width - sh - bw, r.Height - sh - bw);
            using (var p = Round(new RectangleF(box.X + sh, box.Y + sh, box.Width, box.Height), radius * k))
            using (var b = new SolidBrush(Ink)) g.FillPath(b, p);
            using (var p = Round(box, radius * k))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                using (var pen = new Pen(Ink, bw)) g.DrawPath(pen, p);
            }
        }

        // Pinta um formulário e seus controles comuns com as cores do tema.
        public static void Apply(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is PixelButton || c is CrewBanner) continue;
                if (c is TextBox || c is ComboBox || c is ListBox || c is NumericUpDown)
                {
                    c.BackColor = Card; c.ForeColor = Ink;
                    if (c is ListBox) ((ListBox)c).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is TabPage) { c.BackColor = Bg; c.ForeColor = Ink; }
                else if (c is Label || c is CheckBox || c is TrackBar)
                {
                    // TrackBar e CheckBox não aceitam fundo transparente: usam a cor do pai
                    c.BackColor = c is Label ? Color.Transparent : (c.Parent != null ? c.Parent.BackColor : Bg);
                    if (c.ForeColor == SystemColors.ControlText) c.ForeColor = Ink;
                }
                else if (c is Button)
                {
                    var b = (Button)c;
                    b.FlatStyle = FlatStyle.Flat; b.BackColor = Card; b.ForeColor = Ink;
                    b.FlatAppearance.BorderColor = Ink; b.FlatAppearance.BorderSize = 2;
                    b.FlatAppearance.MouseOverBackColor = Bg2; b.Font = Body(9f, FontStyle.Bold);
                }
                Apply(c);
            }
        }
    }

    // Botão no estilo do site: borda grossa, sombra dura que "afunda" ao clicar.
    public class PixelButton : Control
    {
        bool hover, down;
        public bool Primary;
        float k = 1f;

        public PixelButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.Body(10.5f, FontStyle.Bold);
            TabStop = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int sh = (int)(4 * k), bw = Math.Max(2, (int)(2.5f * k));
            int off = down ? sh - 1 : hover ? -1 : 0;
            var box = new RectangleF(bw / 2f + Math.Max(0, off), bw / 2f + Math.Max(0, off), Width - sh - bw - 1, Height - sh - bw - 1);
            Color fill = !Enabled ? Theme.Bg2 : Primary ? (hover ? Theme.Accent2 : Theme.Accent) : (hover ? Theme.Bg2 : Theme.Card);
            if (!down)
                using (var p = Theme.Round(new RectangleF(bw / 2f + sh, bw / 2f + sh, box.Width, box.Height), 10 * k))
                using (var b = new SolidBrush(Theme.Ink)) g.FillPath(b, p);
            using (var p = Theme.Round(box, 10 * k))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.Ink, bw)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(box),
                !Enabled ? Theme.Ink2 : Primary ? Color.White : Theme.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Theme.Accent2, 1) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, Rectangle.Inflate(Rectangle.Round(box), -(int)(5 * k), -(int)(5 * k)));
        }
    }
}
