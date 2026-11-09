using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ScreenPets
{
    static class Program
    {
        const string HostMutex = "Local\\ScreenPetsHost";
        const string ShowConfigEvent = "Local\\ScreenPetsMostrarConfig";

        [STAThread]
        static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            try
            {
                switch (cmd)
                {
                    // ao ligar o computador (Iniciar com o Windows): só os bichinhos, sem abrir a configuração
                    case "--fundo": return RunHost(false);
                    case "config": return RunHost(true);
                    case "setup": return RunForm(new SetupForm(args.Length > 1 ? args[1] : null));
                    case "uninstall-app": return UninstallApp();
                    // ferramentas de desenvolvimento
                    case "folha": return Sheet(args.Length > 1 ? args[1] : Path.Combine(Paths.StateDir, "folha.png"));
                    case "foto": return Photo(args);
                    case "telas": return Screens(args.Length > 1 ? args[1] : Paths.StateDir);
                    default:
                        // Baixado sozinho (sem a pasta personagens/ ao lado): é o instalador.
                        if (Installer.RunningInstalled) return RunHost(true);
                        // Já instalado (mesma versão ou mais nova): não há o que instalar, abre o programa instalado.
                        if (!Installer.SetupIsNewer)
                        {
                            Process.Start(new ProcessStartInfo(Installer.InstalledExe) { UseShellExecute = true });
                            return 0;
                        }
                        return RunForm(new SetupForm());
                }
            }
            catch (Exception ex)
            {
                Paths.Log(cmd + ": " + ex);
                MessageBox.Show("Algo deu errado: " + ex.Message + "\r\n\r\nOs detalhes ficam em " + Paths.LogFile, "Screen Pets",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }

        static int RunForm(Form f)
        {
            Application.Run(f);
            return 0;
        }

        // Uma cópia só: se já estiver rodando, pede para ela abrir a configuração e sai.
        static int RunHost(bool openConfig)
        {
            bool created;
            using (var mutex = new Mutex(true, HostMutex, out created))
            using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowConfigEvent))
            {
                if (!created)
                {
                    if (openConfig) signal.Set();
                    return 0;
                }
                Application.Run(new Host(openConfig, signal));
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        static int UninstallApp()
        {
            var r = MessageBox.Show("Tirar o Screen Pets e todos os bichinhos deste computador?", "Screen Pets",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return 0;
            Installer.Uninstall();
            MessageBox.Show("Pronto, o Screen Pets foi desinstalado. Tchau, bichinhos!", "Screen Pets");
            return 0;
        }

        // Folha com todos os personagens (primeira cor): andar, trabalhar e parado. Para conferir os desenhos.
        static int Sheet(string output)
        {
            var chars = Character.LoadAll(null);
            const int px = 4, pad = 8, label = 18;
            int cell = 16 * px + pad;
            int cols = 1 + chars.Max(c => c.Frames.Where(k => k.Key == "andar" || k.Key.StartsWith("trabalhar") || k.Key == "parado").Sum(k => k.Value.Count));
            using (var bmp = new Bitmap(cols * cell + 140, chars.Count * (cell + label) + pad))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                g.Clear(Color.FromArgb(253, 243, 227));
                int y = pad;
                foreach (var ch in chars)
                {
                    g.DrawString(ch.Name + " (" + Character.TypeName(ch.Type) + ")", font, Brushes.Black, pad, y);
                    int x = pad;
                    var skin = ch.Skins[0];
                    foreach (var kind in new[] { "andar" }.Concat(ch.WorkKinds(skin)).Concat(new[] { "parado" }))
                        foreach (var f in ch.Render(skin, kind, px, false))
                        {
                            g.DrawRectangle(Pens.Tan, x - 1, y + label - 1, f.Width + 1, f.Height + 1);
                            g.DrawImage(f, x, y + label);
                            x += cell;
                            f.Dispose();
                        }
                    y += cell + label;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                bmp.Save(output, ImageFormat.Png);
            }
            return 0;
        }

        // telas <pasta>: desenha as janelas (configuração, escolha, instalador) em PNG, para revisar o visual.
        static int Screens(string dir)
        {
            Directory.CreateDirectory(dir);
            Action<Form, string> save = (f, name) =>
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-32000, -32000);
                f.Show();
                Application.DoEvents();
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                    bmp.Save(Path.Combine(dir, name), ImageFormat.Png);
                }
                f.Close();
            };
            var host = new Host(false, null);
            save(new ConfigForm(host), "config.png");
            save(PickForm.Preview(host.Characters), "escolher.png");
            save(new SetupForm(), "setup.png");
            save(new SetupForm("pronto"), "pronto.png");
            host.ExitThread();
            return 0;
        }

        // foto <segundos> <arquivo.png> [config.json]: simula os bichinhos por alguns segundos e desenha
        // as faixas numa tela de mentira (cinza, com a barra de tarefas escura), sem abrir janela nenhuma.
        static int Photo(string[] args)
        {
            double secs = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 6;
            string output = args.Length > 2 ? args[2] : Path.Combine(Paths.StateDir, "foto.png");
            var cfg = args.Length > 3 ? Config.Load(args[3]) : Config.Load();
            var chars = Character.LoadAll(null);
            var screen = Screen.PrimaryScreen;
            using (var bmp = new Bitmap(screen.Bounds.Width, screen.Bounds.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(120, 144, 156));
                using (var bar = new SolidBrush(Color.FromArgb(32, 32, 36)))
                    g.FillRectangle(bar, Rectangle.FromLTRB(0, screen.WorkingArea.Bottom, screen.Bounds.Width, screen.Bounds.Height));
                foreach (var kv in Host.CastsByZone(cfg, chars))
                    using (var stage = new Stage(kv.Key, kv.Value, cfg))
                    {
                        Rectangle where;
                        using (var shot = stage.Snapshot(secs, out where))
                        {
                            shot.MakeTransparent(Color.Magenta);
                            g.DrawImage(shot, where.Location);
                            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                                g.DrawRectangle(pen, where.X, where.Y, where.Width - 1, where.Height - 1);
                        }
                    }
                bmp.Save(output, ImageFormat.Png);
            }
            return 0;
        }
    }
}
