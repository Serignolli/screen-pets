using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ScreenPets
{
    // O programa rodando: os palcos nas bordas da tela e o ícone perto do relógio.
    public class Host : ApplicationContext
    {
        readonly List<Character> chars;
        readonly Dictionary<Zone, Stage> stages = new Dictionary<Zone, Stage>();
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem miToggle;
        readonly System.Windows.Forms.Timer watch;
        readonly Control invoker = new Control();
        RegisteredWaitHandle waitHandle;
        ConfigForm form;
        bool fullscreen, toldAboutTray;

        public Config Config { get; private set; }
        public List<Character> Characters { get { return chars; } }

        // openConfig: abre a configuração logo de cara. signal: outra cópia do programa pedindo a configuração.
        public Host(bool openConfig, EventWaitHandle signal)
        {
            var errors = new List<string>();
            chars = Character.LoadAll(errors);
            foreach (var e in errors) Paths.Log("personagem com erro: " + e);
            bool firstRun = !File.Exists(Paths.ConfigFile);
            Config = Config.Load();
            if (firstRun) Config.Save();

            var h = invoker.Handle;   // cria a janela invisível usada para receber pedidos de outras threads
            miToggle = new ToolStripMenuItem("", null, delegate { Config.Enabled = !Config.Enabled; Apply(); if (form != null) form.SyncFromConfig(); });
            var menu = new ContextMenuStrip();
            var miConfig = new ToolStripMenuItem("Configurar bichinhos...", null, delegate { ShowConfig(); }) { Font = new Font(SystemFonts.MenuFont, FontStyle.Bold) };
            menu.Items.Add(miConfig);
            menu.Items.Add(miToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Sair", null, delegate { ExitThread(); }));
            tray = new NotifyIcon { Text = "Screen Pets: seus bichinhos", ContextMenuStrip = menu, Visible = true };
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { tray.Icon = SystemIcons.Application; }
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowConfig(); };

            watch = new System.Windows.Forms.Timer { Interval = 1000 };
            watch.Tick += delegate { CheckFullscreen(); };
            watch.Start();

            if (signal != null)
                waitHandle = ThreadPool.RegisterWaitForSingleObject(signal, (s, timedOut) =>
                {
                    try { invoker.BeginInvoke((Action)ShowConfig); } catch { }
                }, null, -1, false);

            Rebuild();
            UpdateMenu();
            if (openConfig) ShowConfig();
            else if (firstRun)
                tray.ShowBalloonTip(6000, "Os bichinhos chegaram!", "Clique neste ícone para escolher quais aparecem, onde ficam e o tamanho.", ToolTipIcon.Info);
        }

        // Grava e refaz os palcos com a configuração atual.
        public void Apply()
        {
            try { Config.Save(); }
            catch (Exception ex) { Paths.Log("salvar config: " + ex.Message); }
            Rebuild();
            UpdateMenu();
        }

        void UpdateMenu()
        {
            miToggle.Text = Config.Enabled ? "Esconder bichinhos" : "Mostrar bichinhos";
        }

        static Zone ZoneOf(string place)
        {
            switch (place)
            {
                case Group.Top: return Zone.Top;
                case Group.Left: return Zone.Left;
                case Group.Right: return Zone.Right;
                default: return Zone.Ground;
            }
        }

        void Rebuild()
        {
            foreach (var s in stages.Values) { s.Close(); s.Dispose(); }
            stages.Clear();
            if (!Config.Enabled) return;
            foreach (var kv in CastsByZone(Config, chars))
            {
                try
                {
                    var s = new Stage(kv.Key, kv.Value, Config);
                    stages[kv.Key] = s;
                    s.Show();
                    if (fullscreen) s.SetHidden(true);
                }
                catch (Exception ex) { Paths.Log("palco " + kv.Key + ": " + ex); }
            }
        }

        // Quem vai em cada borda. "Nas duas laterais" divide a turma entre a direita e a esquerda.
        public static Dictionary<Zone, List<Cast>> CastsByZone(Config config, List<Character> chars)
        {
            var casts = new Dictionary<Zone, List<Cast>>();
            Action<Zone, Character, Group, int> add = (z, ch, g, n) =>
            {
                if (n <= 0) return;
                List<Cast> l;
                if (!casts.TryGetValue(z, out l)) casts[z] = l = new List<Cast>();
                l.Add(new Cast { Ch = ch, Skin = g.Skin, Count = n, Size = g.Size });
            };
            foreach (var g in config.Groups)
            {
                var ch = Character.Find(chars, g.Character);
                if (ch == null) continue;
                string place = Character.PlaceFor(ch.Type, g.Place);
                if (place == Group.Sides)
                {
                    add(Zone.Right, ch, g, (g.Count + 1) / 2);
                    add(Zone.Left, ch, g, g.Count / 2);
                }
                else add(ZoneOf(place), ch, g, g.Count);
            }
            return casts;
        }

        // Some quando um vídeo, jogo ou apresentação ocupa a tela inteira.
        void CheckFullscreen()
        {
            bool fs = Config.HideFullscreen && Native.FullscreenOn(Screen.PrimaryScreen.Bounds);
            if (fs == fullscreen) return;
            fullscreen = fs;
            foreach (var s in stages.Values) s.SetHidden(fs);
        }

        public void ShowConfig()
        {
            if (form == null || form.IsDisposed)
            {
                form = new ConfigForm(this);
                form.FormClosed += delegate
                {
                    form = null;
                    if (!toldAboutTray && Config.Enabled)
                    {
                        toldAboutTray = true;
                        tray.ShowBalloonTip(5000, "Os bichinhos continuam aqui", "Para mudar alguma coisa, é só clicar neste ícone perto do relógio.", ToolTipIcon.Info);
                    }
                };
                form.Show();
            }
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
            form.BringToFront();
        }

        protected override void ExitThreadCore()
        {
            watch.Stop();
            if (waitHandle != null) waitHandle.Unregister(null);
            if (form != null && !form.IsDisposed) form.Close();
            foreach (var s in stages.Values) { s.Close(); s.Dispose(); }
            stages.Clear();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }
}
