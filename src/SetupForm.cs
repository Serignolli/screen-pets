using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ScreenPets
{
    // Instalador: boas-vindas → instalando → pronto. A escolha dos bichinhos fica para a configuração,
    // que já abre com uma turma de exemplo na tela.
    public class SetupForm : Form
    {
        readonly List<Character> chars;
        readonly float k;
        readonly CrewBanner banner;
        readonly Panel body;

        int S(int v) { return (int)Math.Round(v * k); }

        // page: só para revisar o visual ("pronto", "erro"), sem instalar nada.
        public SetupForm(string page = null)
        {
            chars = Character.LoadEmbedded(null);
            if (chars.Count == 0) chars = Character.LoadAll(null);
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Screen Pets: instalação";
            ClientSize = new Size(S(720), S(500));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Font = Theme.Body(10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            banner = new CrewBanner { Location = new Point(0, 0), Size = new Size(ClientSize.Width, S(150)), BackColor = Theme.Bg2, Unit = 3 };
            Controls.Add(banner);
            body = new Panel { Location = new Point(0, S(150)), Size = new Size(ClientSize.Width, ClientSize.Height - S(150)), BackColor = Theme.Bg };
            Controls.Add(body);
            Shown += delegate
            {
                // um pouco de cada tipo: quem anda, quem escala e quem voa
                var mix = new[] { "gatos", "passarinho", "cachorros", "lagartixa", "borboleta", "pinguins", "joaninha" }
                    .Select(id => Character.Find(chars, id)).Where(c => c != null)
                    .Select(c => new KeyValuePair<Character, string>(c, "*")).ToList();
                banner.SetMix(mix);
            };
            if (page == "pronto") ShowDone();
            else if (page == "erro") ShowError("Exemplo de erro: não consegui gravar na pasta de instalação.");
            else ShowWelcome();
        }

        // ---------- utilidades de layout ----------

        Label Text_(string text, int x, int y, int w, int h, Font font, Color color)
        {
            var l = new Label { Text = text, Location = new Point(S(x), S(y)), Size = new Size(S(w), S(h)), Font = font, ForeColor = color, BackColor = Color.Transparent };
            body.Controls.Add(l);
            return l;
        }

        PixelButton Button_(string text, int x, int y, int w, bool primary, Action onClick)
        {
            var b = new PixelButton { Text = text, Primary = primary, Location = new Point(S(x), S(y)), Size = new Size(S(w), S(46)) };
            b.Click += delegate { onClick(); };
            body.Controls.Add(b);
            return b;
        }

        void Clear()
        {
            foreach (Control c in body.Controls.Cast<Control>().ToList()) c.Dispose();
            body.Controls.Clear();
        }

        // ---------- 1. boas-vindas ----------

        void ShowWelcome()
        {
            Clear();
            Text_("Bichinhos na sua tela!", 36, 20, 650, 50, Theme.Pixel(26f), Theme.Ink);
            Text_("Uma turma de bichinhos em pixel art para fazer companhia enquanto você usa o computador: " +
                  "uns andam em cima da barra de tarefas, outros sobem pelas laterais e outros voam no alto da tela.",
                  38, 76, 640, 70, Theme.Body(11f), Theme.Ink2);
            Text_("Eles não atrapalham: o mouse passa direto por eles, e eles somem sozinhos quando você assiste a um vídeo em tela cheia. " +
                  "Depois é só escolher quais aparecem, onde ficam e o tamanho.",
                  38, 150, 640, 48, Theme.Body(10f), Theme.Ink2);
            if (Installer.IsInstalled && !Installer.RunningInstalled)
                Text_("Já existe uma instalação: ela será atualizada e os seus bichinhos continuam como estão.", 38, 206, 640, 22, Theme.Body(9.5f), Theme.Accent2);

            Text_("Não precisa de administrador · nada sai do seu computador", 38, 278, 380, 22, Theme.Body(8.5f), Theme.Ink2);
            Button_("Cancelar", 430, 270, 110, false, Close);
            Button_(Installer.IsInstalled ? "Atualizar  ▸" : "Instalar  ▸", 552, 270, 130, true, ShowInstalling);
        }

        // ---------- 2. instalando ----------

        void ShowInstalling()
        {
            Clear();
            Text_("Chamando a turma...", 36, 40, 650, 44, Theme.Pixel(22f), Theme.Ink);
            var step = Text_("", 38, 96, 640, 24, Theme.Body(11f), Theme.Ink2);
            var bar = new BusyBar { Location = new Point(S(38), S(134)), Size = new Size(S(640), S(34)) };
            body.Controls.Add(bar);
            var t = new Thread(() =>
            {
                try
                {
                    Installer.Install(s => BeginInvoke((Action)(() => step.Text = s)));
                    Thread.Sleep(500);
                    BeginInvoke((Action)ShowDone);
                }
                catch (Exception ex)
                {
                    Paths.Log("setup: " + ex);
                    BeginInvoke((Action)(() => ShowError(ex.Message)));
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ---------- 3. pronto ----------

        void ShowDone()
        {
            Clear();
            Text_("Prontinho!", 36, 20, 650, 50, Theme.Pixel(26f), Theme.Ink);
            Text_("Os bichinhos já estão chegando na sua tela. Eles voltam sozinhos sempre que o computador ligar.",
                  38, 76, 640, 48, Theme.Body(11f), Theme.Ink2);
            Text_("Para escolher quais aparecem, onde ficam e o tamanho, clique no ícone deles perto do relógio " +
                  "(se não aparecer, ele fica escondido na setinha ˄) ou abra \"Screen Pets\" na área de trabalho ou no menu Iniciar.",
                  38, 130, 640, 64, Theme.Body(10f), Theme.Ink2);
            Button_("Concluir", 340, 270, 130, false, () => { Run("--fundo"); Close(); });
            Button_("Escolher bichinhos  ▸", 482, 270, 200, true, () => { Run(""); Close(); });
        }

        void ShowError(string msg)
        {
            Clear();
            Text_("Ops, algo deu errado.", 36, 20, 650, 50, Theme.Pixel(24f), Theme.Ink);
            Text_(msg, 38, 80, 640, 90, Theme.Body(10.5f), Color.Firebrick);
            Text_("Os detalhes ficam em " + Paths.LogFile, 38, 176, 640, 22, Theme.Body(9f), Theme.Ink2);
            Button_("Fechar", 430, 270, 110, false, Close);
            Button_("Tentar de novo", 552, 270, 130, true, ShowWelcome);
        }

        static void Run(string args)
        {
            try { Process.Start(new ProcessStartInfo(Installer.InstalledExe, args) { UseShellExecute = true }); }
            catch (Exception ex) { Paths.Log("setup run: " + ex.Message); }
        }
    }

    // Cartão de personagem (na escolha de bichinhos e na configuração).
    class CharTile : Control
    {
        public readonly Character Character;
        public bool Selected;
        readonly Bitmap img;
        readonly float k;
        bool hover;

        public CharTile(Character ch, float k, Skin skin = null)
        {
            Character = ch; this.k = k;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            int rows = ch.BottomRow - ch.TopRow + 1;
            int px = Math.Max(1, (int)Math.Floor(64 * k / Math.Max(rows, ch.Width * 0.8)));
            img = ch.Render(skin ?? ch.Skins[0], "parado", px, false)[0];
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.DrawCard(g, ClientRectangle, Selected ? Theme.Sun : hover && Cursor == Cursors.Hand ? Theme.Bg2 : Theme.Card, k, Selected ? 4 : 3, 10);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int top = Character.TopRow * (img.Height / Character.Height);
            int visH = img.Height - top;
            int area = Height - (int)(38 * k);
            g.DrawImage(img, new Rectangle((Width - (int)(4 * k) - img.Width) / 2, (int)(8 * k) + Math.Max(0, area - visH - (int)(8 * k)), img.Width, visH),
                new Rectangle(0, top, img.Width, visH), GraphicsUnit.Pixel);
            using (var f = Theme.Body(8.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, Character.Name, f,
                    new Rectangle((int)(4 * k), Height - (int)(36 * k), Width - (int)(10 * k), (int)(30 * k)), Theme.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        protected override void Dispose(bool disposing) { if (disposing) img.Dispose(); base.Dispose(disposing); }
    }

    // Barra de "trabalhando" com listras andando.
    class BusyBar : Control
    {
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 40 };
        int offset;
        public BusyBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            timer.Tick += delegate { offset = (offset + 2) % 40; Invalidate(); };
            timer.Start();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float k; using (var dg = CreateGraphics()) k = dg.DpiX / 96f;
            g.Clear(Theme.Bg);
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Round(r, 8 * k))
            {
                g.SetClip(p);
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r);
                using (var b = new SolidBrush(Theme.Accent2))
                    for (int x = -40 + offset; x < Width + 40; x += 40)
                        g.FillPolygon(b, new[] { new Point(x, Height), new Point(x + 20, Height), new Point(x + 20 + Height, 0), new Point(x + Height, 0) });
                g.ResetClip();
                using (var pen = new Pen(Theme.Ink, 3 * k)) g.DrawPath(pen, p);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
