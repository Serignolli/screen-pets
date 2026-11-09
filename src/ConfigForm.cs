using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ScreenPets
{
    // A tela de configuração: as turmas à esquerda, a turma escolhida à direita e as opções gerais embaixo.
    // Tudo vale na hora: não tem botão de salvar.
    public class ConfigForm : Form
    {
        readonly Host host;
        readonly float k;
        readonly CrewBanner banner;
        readonly ListBox list;
        readonly Panel editor;
        readonly Label emptyHint;
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>();
        readonly Timer debounce = new Timer { Interval = 350 };
        CharTile tile;
        Label lblName, lblType, lblDesc, lblSize;
        ComboBox cbSkin;
        NumericUpDown numCount;
        RadioButton[] places;
        TrackBar tbSize, tbSpeed;
        CheckBox cbEnabled, cbBubbles, cbFull, cbAuto;
        bool loading;

        static readonly double[] Speeds = { 0.5, 0.75, 1.0, 1.4, 2.0 };

        Config Cfg { get { return host.Config; } }
        Group Current { get { return list.SelectedItem as Group; } }
        int S(int v) { return (int)Math.Round(v * k); }

        public ConfigForm(Host host)
        {
            this.host = host;
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Screen Pets: seus bichinhos";
            ClientSize = new Size(S(860), S(624));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            Font = Theme.Body(10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            banner = new CrewBanner { Location = Point.Empty, Size = new Size(ClientSize.Width, S(100)), BackColor = Theme.Bg2, Unit = 3, Title = "Seus bichinhos", TitleSize = 18f };
            Controls.Add(banner);

            // ---- esquerda: as turmas ----
            Label_(this, "Na sua tela", 16, 110, 280, 22, Theme.Body(10f, FontStyle.Bold), Theme.Ink);
            list = new ListBox
            {
                Location = new Point(S(16), S(134)), Size = new Size(S(280), S(330)), DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = S(58), BorderStyle = BorderStyle.None, BackColor = Theme.Bg, IntegralHeight = false
            };
            list.DrawItem += DrawGroup;
            list.SelectedIndexChanged += delegate { LoadEditor(); };
            Controls.Add(list);
            var add = new PixelButton { Text = "+  Adicionar bichinho", Primary = true, Location = new Point(S(16), S(472)), Size = new Size(S(280), S(46)) };
            add.Click += delegate { AddGroup(); };
            Controls.Add(add);

            // ---- direita: a turma escolhida ----
            editor = new Panel { Location = new Point(S(316), S(110)), Size = new Size(S(528), S(410)), BackColor = Theme.Bg };
            Controls.Add(editor);
            BuildEditor();
            emptyHint = Label_(this, "Nenhum bichinho na tela.\r\nClique em \"Adicionar bichinho\" para chamar um.", 316, 200, 520, 60, Theme.Body(11f), Theme.Ink2);
            emptyHint.TextAlign = ContentAlignment.MiddleCenter;

            // ---- embaixo: opções gerais ----
            var line = new Panel { Location = new Point(S(16), S(528)), Size = new Size(S(828), Math.Max(2, S(2))), BackColor = Theme.Bg2 };
            Controls.Add(line);
            cbEnabled = Check_("Mostrar os bichinhos", 16, 190, "Desmarque para esconder todos (eles também podem ser escondidos pelo ícone perto do relógio).");
            cbBubbles = Check_("Balõezinhos de fala", 212, 180, "De vez em quando um bichinho fala alguma coisa num balãozinho.");
            cbFull = Check_("Sumir em tela cheia", 398, 180, "Eles somem enquanto um vídeo, jogo ou apresentação ocupa a tela inteira.");
            cbAuto = Check_("Abrir com o Windows", 584, 190, "Os bichinhos aparecem sozinhos quando o computador liga.");
            Label_(this, "Velocidade", 16, 576, 80, 22, Theme.Body(9.5f, FontStyle.Bold), Theme.Ink);
            tbSpeed = new TrackBar { Location = new Point(S(96), S(570)), Size = new Size(S(150), S(30)), Minimum = 0, Maximum = Speeds.Length - 1, TickStyle = TickStyle.None, AutoSize = false };
            tbSpeed.ValueChanged += delegate { if (loading) return; Cfg.Speed = Speeds[tbSpeed.Value]; Changed(); };
            Controls.Add(tbSpeed);
            var uninstall = new LinkLabel { Text = "Desinstalar", Location = new Point(S(270), S(578)), AutoSize = true, Font = Theme.Body(9f), LinkColor = Theme.Ink2, ActiveLinkColor = Theme.Accent2, BackColor = Color.Transparent };
            uninstall.LinkClicked += delegate { Uninstall(); };
            Controls.Add(uninstall);
            var done = new PixelButton { Text = "Pronto", Primary = true, Location = new Point(S(724), S(570)), Size = new Size(S(120), S(44)) };
            done.Click += delegate { Close(); };
            Controls.Add(done);

            Theme.Apply(this);
            list.BackColor = Theme.Bg;
            debounce.Tick += delegate { debounce.Stop(); host.Apply(); };

            foreach (var g in Cfg.Groups) list.Items.Add(g);
            SyncFromConfig();
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            LoadEditor();
            RefreshBanner();
        }

        // ---------- montagem ----------

        Label Label_(Control parent, string text, int x, int y, int w, int h, Font font, Color color)
        {
            var l = new Label { Text = text, Location = new Point(S(x), S(y)), Size = new Size(S(w), S(h)), Font = font, ForeColor = color, BackColor = Color.Transparent };
            parent.Controls.Add(l);
            return l;
        }

        CheckBox Check_(string text, int x, int w, string tip)
        {
            var c = new CheckBox { Text = text, Location = new Point(S(x), S(538)), Size = new Size(S(w), S(26)), Font = Theme.Body(9.5f) };
            new ToolTip().SetToolTip(c, tip);
            c.CheckedChanged += delegate { OnGlobal(c); };
            Controls.Add(c);
            return c;
        }

        void BuildEditor()
        {
            lblName = Label_(editor, "", 120, 0, 400, 30, Theme.Pixel(16f), Theme.Ink);
            lblType = Label_(editor, "", 120, 32, 400, 20, Theme.Body(9.5f, FontStyle.Bold), Theme.Accent2);
            lblDesc = Label_(editor, "", 120, 52, 400, 40, Theme.Body(9.5f), Theme.Ink2);
            var change = new PixelButton { Text = "Trocar bichinho", Location = new Point(S(120), S(92)), Size = new Size(S(160), S(40)) };
            change.Font = Theme.Body(9.5f, FontStyle.Bold);
            change.Click += delegate { ChangeCharacter(); };
            editor.Controls.Add(change);
            var remove = new PixelButton { Text = "Tirar da tela", Location = new Point(S(290), S(92)), Size = new Size(S(140), S(40)) };
            remove.Font = Theme.Body(9.5f, FontStyle.Bold);
            remove.Click += delegate { RemoveGroup(); };
            editor.Controls.Add(remove);

            Label_(editor, "Cor", 0, 144, 240, 20, Theme.Body(9.5f, FontStyle.Bold), Theme.Ink);
            cbSkin = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(0, S(166)), Size = new Size(S(250), S(26)), Font = Theme.Body(9.5f) };
            cbSkin.SelectedIndexChanged += delegate
            {
                if (loading || Current == null) return;
                var ch = CharOf(Current);
                Current.Skin = cbSkin.SelectedIndex <= 0 || ch == null ? "*" : ch.Skins[cbSkin.SelectedIndex - 1].Id;
                ReplaceTile();
                Changed();
            };
            editor.Controls.Add(cbSkin);
            Label_(editor, "Quantos", 270, 144, 120, 20, Theme.Body(9.5f, FontStyle.Bold), Theme.Ink);
            numCount = new NumericUpDown { Minimum = 1, Maximum = Config.MaxCount, Location = new Point(S(270), S(166)), Size = new Size(S(70), S(26)), Font = Theme.Body(9.5f) };
            numCount.ValueChanged += delegate { if (loading || Current == null) return; Current.Count = (int)numCount.Value; Changed(); };
            editor.Controls.Add(numCount);

            Label_(editor, "Onde ficam", 0, 206, 240, 20, Theme.Body(9.5f, FontStyle.Bold), Theme.Ink);
            places = new RadioButton[Group.Places.Length];
            for (int i = 0; i < Group.Places.Length; i++)
            {
                string place = Group.Places[i];
                var rb = new RadioButton
                {
                    Text = Group.PlaceName(place), Location = new Point(S(i % 2 == 0 ? 0 : 260), S(228 + (i / 2) * 28)),
                    Size = new Size(S(250), S(26)), Font = Theme.Body(9.5f)
                };
                rb.CheckedChanged += delegate { if (loading || Current == null || !rb.Checked) return; Current.Place = place; Changed(); };
                places[i] = rb;
                editor.Controls.Add(rb);
            }

            Label_(editor, "Tamanho", 0, 322, 240, 20, Theme.Body(9.5f, FontStyle.Bold), Theme.Ink);
            tbSize = new TrackBar { Minimum = 1, Maximum = Config.MaxSize, Location = new Point(0, S(344)), Size = new Size(S(250), S(32)), TickStyle = TickStyle.None, AutoSize = false };
            tbSize.ValueChanged += delegate
            {
                lblSize.Text = Group.SizeName(tbSize.Value);
                if (loading || Current == null) return;
                Current.Size = tbSize.Value;
                Changed();
            };
            editor.Controls.Add(tbSize);
            lblSize = Label_(editor, "", 260, 348, 160, 22, Theme.Body(9.5f), Theme.Ink2);
        }

        // ---------- turmas ----------

        Character CharOf(Group g) { return Character.Find(host.Characters, g.Character); }

        Skin SkinOf(Group g, Character ch) { return g.Skin == "*" ? ch.Skins[0] : ch.GetSkin(g.Skin); }

        void LoadEditor()
        {
            var g = Current;
            var ch = g == null ? null : CharOf(g);
            editor.Visible = ch != null;
            emptyHint.Visible = list.Items.Count == 0;
            if (ch == null) return;
            loading = true;
            ReplaceTile();
            lblName.Text = ch.Name;
            lblType.Text = Character.TypeName(ch.Type) + TypeHint(ch.Type);
            lblDesc.Text = ch.Description;
            cbSkin.Items.Clear();
            cbSkin.Items.Add("Misturar as cores");
            foreach (var s in ch.Skins) cbSkin.Items.Add(s.Name);
            int si = ch.Skins.FindIndex(s => s.Id == g.Skin);
            cbSkin.SelectedIndex = g.Skin == "*" || si < 0 ? 0 : si + 1;
            numCount.Value = Math.Max(1, Math.Min(Config.MaxCount, g.Count));
            // só aparecem os lugares que o tipo permite, um embaixo do outro em duas colunas
            g.Place = Character.PlaceFor(ch.Type, g.Place);
            for (int i = 0, n = 0; i < places.Length; i++)
            {
                bool ok = Character.Allows(ch.Type, Group.Places[i]);
                places[i].Visible = ok;
                places[i].Checked = Group.Places[i] == g.Place;
                if (ok) { places[i].Location = new Point(S(n % 2 == 0 ? 0 : 260), S(228 + (n / 2) * 28)); n++; }
            }
            tbSize.Value = Math.Max(1, Math.Min(Config.MaxSize, g.Size));
            lblSize.Text = Group.SizeName(tbSize.Value);
            loading = false;
        }

        static string TypeHint(string type)
        {
            if (type == Character.Climber) return " · combina com as laterais";
            if (type == Character.Flyer) return " · combina com o alto da tela";
            return " · combina com a barra de tarefas";
        }

        void ReplaceTile()
        {
            var g = Current;
            var ch = g == null ? null : CharOf(g);
            if (tile != null) { editor.Controls.Remove(tile); tile.Dispose(); tile = null; }
            if (ch == null) return;
            tile = new CharTile(ch, k, SkinOf(g, ch)) { Location = Point.Empty, Size = new Size(S(104), S(132)), Cursor = Cursors.Default };
            editor.Controls.Add(tile);
        }

        void AddGroup()
        {
            var ch = PickForm.Pick(this, host.Characters, k, null);
            if (ch == null) return;
            var g = new Group { Character = ch.Id, Place = Character.DefaultPlace(ch.Type), Count = ch.Type == Character.Climber ? 1 : 2 };
            Cfg.Groups.Add(g);
            Cfg.Enabled = true;
            list.Items.Add(g);
            list.SelectedItem = g;
            SyncFromConfig();
            Changed();
        }

        void ChangeCharacter()
        {
            var g = Current;
            if (g == null) return;
            var old = CharOf(g);
            var ch = PickForm.Pick(this, host.Characters, k, g.Character);
            if (ch == null || ch.Id == g.Character) return;
            // quem estava no lugar "natural" do bicho antigo vai para o lugar natural do novo
            if (old == null || g.Place == Character.DefaultPlace(old.Type)) g.Place = Character.DefaultPlace(ch.Type);
            g.Place = Character.PlaceFor(ch.Type, g.Place);
            g.Character = ch.Id;
            g.Skin = "*";
            LoadEditor();
            Changed();
        }

        void RemoveGroup()
        {
            var g = Current;
            if (g == null) return;
            int i = list.SelectedIndex;
            Cfg.Groups.Remove(g);
            list.Items.Remove(g);
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(i, list.Items.Count - 1);
            LoadEditor();
            Changed();
        }

        void Changed()
        {
            if (loading) return;
            list.Invalidate();
            RefreshBanner();
            debounce.Stop();
            debounce.Start();
        }

        void OnGlobal(CheckBox c)
        {
            if (loading) return;
            if (c == cbAuto)
            {
                try { Installer.AutoStart = c.Checked; }
                catch (Exception ex) { Paths.Log("iniciar com o windows: " + ex.Message); }
                return;
            }
            Cfg.Enabled = cbEnabled.Checked;
            Cfg.Bubbles = cbBubbles.Checked;
            Cfg.HideFullscreen = cbFull.Checked;
            Changed();
        }

        // Atualiza as opções gerais (o ícone perto do relógio também liga e desliga os bichinhos).
        public void SyncFromConfig()
        {
            loading = true;
            cbEnabled.Checked = Cfg.Enabled;
            cbBubbles.Checked = Cfg.Bubbles;
            cbFull.Checked = Cfg.HideFullscreen;
            cbAuto.Checked = Installer.AutoStart;
            int best = 0;
            for (int i = 0; i < Speeds.Length; i++) if (Math.Abs(Speeds[i] - Cfg.Speed) < Math.Abs(Speeds[best] - Cfg.Speed)) best = i;
            tbSpeed.Value = best;
            loading = false;
        }

        void RefreshBanner()
        {
            var mix = new List<KeyValuePair<Character, string>>();
            foreach (var g in Cfg.Groups)
            {
                var ch = CharOf(g);
                if (ch == null) continue;
                for (int i = 0; i < Math.Min(3, g.Count); i++) mix.Add(new KeyValuePair<Character, string>(ch, g.Skin));
            }
            banner.SetMix(mix.Take(12).ToList());
        }

        void Uninstall()
        {
            var r = MessageBox.Show(this, "Tirar o Screen Pets e todos os bichinhos deste computador?", "Screen Pets",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            Installer.Uninstall();
            MessageBox.Show(this, "Pronto, o Screen Pets foi desinstalado. Tchau, bichinhos!", "Screen Pets");
            host.ExitThread();
        }

        // ---------- desenho da lista ----------

        Bitmap IconOf(Character ch, Skin skin)
        {
            string key = ch.Id + "|" + skin.Id;
            Bitmap b;
            if (icons.TryGetValue(key, out b)) return b;
            int rows = ch.BottomRow - ch.TopRow + 1;
            int px = Math.Max(1, (int)Math.Floor(40 * k / Math.Max(rows, ch.Width)));
            using (var full = ch.Render(skin, "parado", px, false)[0])
            {
                int top = ch.TopRow * px, h = rows * px;
                b = full.Clone(new Rectangle(0, top, full.Width, Math.Min(h, full.Height - top)), full.PixelFormat);
            }
            icons[key] = b;
            return b;
        }

        void DrawGroup(object sender, DrawItemEventArgs e)
        {
            var gr = e.Graphics;
            using (var bg = new SolidBrush(Theme.Bg)) gr.FillRectangle(bg, e.Bounds);
            if (e.Index < 0 || e.Index >= list.Items.Count) return;
            var g = (Group)list.Items[e.Index];
            var ch = CharOf(g);
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var r = new Rectangle(e.Bounds.X, e.Bounds.Y + S(2), e.Bounds.Width - S(4), e.Bounds.Height - S(4));
            Theme.DrawCard(gr, r, sel ? Theme.Sun : Theme.Card, k, 3, 8);
            gr.SmoothingMode = SmoothingMode.None;
            gr.InterpolationMode = InterpolationMode.NearestNeighbor;
            gr.PixelOffsetMode = PixelOffsetMode.Half;
            int textX = r.X + S(12);
            if (ch != null)
            {
                var icon = IconOf(ch, SkinOf(g, ch));
                int box = S(44);
                gr.DrawImage(icon, r.X + S(10) + (box - icon.Width) / 2, r.Y + (r.Height - S(4) - icon.Height) / 2);
                textX = r.X + S(10) + box + S(8);
            }
            using (var f1 = Theme.Body(10f, FontStyle.Bold))
            using (var f2 = Theme.Body(8.5f))
            {
                TextRenderer.DrawText(gr, (ch != null ? ch.Name : g.Character) + "  ×" + g.Count, f1, new Point(textX, r.Y + S(7)), Theme.Ink);
                TextRenderer.DrawText(gr, Group.PlaceName(g.Place), f2, new Point(textX, r.Y + S(28)), Theme.Ink2);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // alguma mudança ainda esperando: vale agora
                if (debounce.Enabled) { debounce.Stop(); host.Apply(); }
                debounce.Dispose();
                foreach (var b in icons.Values) b.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Escolha de bichinho, separados pelos três tipos.
    class PickForm : Form
    {
        Character picked;

        public static Character Pick(IWin32Window owner, List<Character> chars, float k, string current)
        {
            using (var f = new PickForm(chars, k, current))
                return f.ShowDialog(owner) == DialogResult.OK ? f.picked : null;
        }

        internal static Form Preview(List<Character> chars)
        {
            float k;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            return new PickForm(chars, k, "gatos");
        }

        PickForm(List<Character> chars, float k, string current)
        {
            Func<int, int> S = v => (int)Math.Round(v * k);
            AutoScaleMode = AutoScaleMode.None;
            Text = "Escolha um bichinho";
            ClientSize = new Size(S(720), S(560));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            Font = Theme.Body(10f);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
            Controls.Add(scroll);
            int tileW = S(100), tileH = S(112), gap = S(8), left = S(20);
            int perRow = Math.Max(1, (ClientSize.Width - left * 2 - SystemInformation.VerticalScrollBarWidth + gap) / (tileW + gap));
            int y = S(14);
            var sections = new[] {
                new[] { Character.Walker, "Terrestres", "Andam em cima da barra de tarefas." },
                new[] { Character.Climber, "Escaladores", "Sobem e descem pelas laterais da tela." },
                new[] { Character.Flyer, "Voadores", "Voam pelo alto da tela." } };
            foreach (var sec in sections)
            {
                var group = chars.Where(c => c.Type == sec[0]).ToList();
                if (group.Count == 0) continue;
                scroll.Controls.Add(new Label { Text = sec[1], Font = Theme.Pixel(15f), ForeColor = Theme.Ink, Location = new Point(left, y), AutoSize = true, BackColor = Color.Transparent });
                scroll.Controls.Add(new Label { Text = sec[2], Font = Theme.Body(9.5f), ForeColor = Theme.Ink2, Location = new Point(left + S(160), y + S(8)), AutoSize = true, BackColor = Color.Transparent });
                y += S(40);
                for (int i = 0; i < group.Count; i++)
                {
                    var ch = group[i];
                    var t = new CharTile(ch, k)
                    {
                        Location = new Point(left + (i % perRow) * (tileW + gap), y + (i / perRow) * (tileH + gap)),
                        Size = new Size(tileW, tileH), Selected = ch.Id == current
                    };
                    new ToolTip().SetToolTip(t, ch.Description);
                    t.Click += delegate { picked = ch; DialogResult = DialogResult.OK; Close(); };
                    scroll.Controls.Add(t);
                }
                y += ((group.Count + perRow - 1) / perRow) * (tileH + gap) + S(14);
            }
            scroll.Controls.Add(new Label { Text = "Dica: depois de escolher, dá para mudar onde ele fica.", Font = Theme.Body(9f), ForeColor = Theme.Ink2, Location = new Point(left, y), AutoSize = true, BackColor = Color.Transparent });
        }
    }
}
