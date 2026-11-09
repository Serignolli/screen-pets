using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace ScreenPets
{
    // Instalação para usuário final: o ScreenPets-Setup.exe traz os personagens embutidos,
    // se copia para %LOCALAPPDATA%\Programs\ScreenPets, cria atalhos, liga junto com o Windows
    // e aparece em "Aplicativos instalados" (é por lá que se desinstala). Não precisa de administrador.
    public static class Installer
    {
        public const string Version = "1.1.0";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ScreenPets";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "ScreenPets";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ScreenPets"); }
        }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "bin", "ScreenPets.exe"); } }
        static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Screen Pets.lnk"); }
        }
        static string DesktopShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Screen Pets.lnk"); }
        }

        public static bool IsInstalled { get { return File.Exists(InstalledExe); } }

        // Versão gravada em "Aplicativos instalados" na última instalação (null se não houver).
        public static System.Version InstalledVersion
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    {
                        var v = k == null ? null : k.GetValue("DisplayVersion") as string;
                        System.Version parsed;
                        return v != null && System.Version.TryParse(v, out parsed) ? parsed : null;
                    }
                }
                catch { return null; }
            }
        }

        // O Setup.exe baixado só precisa instalar de novo se for mais novo do que o que já está instalado.
        public static bool SetupIsNewer
        {
            get
            {
                var installed = InstalledVersion;
                return !IsInstalled || installed == null || new System.Version(Version) > installed;
            }
        }

        // Rodando de dentro da instalação (ou da pasta do projeto, com personagens/ ao lado)?
        public static bool RunningInstalled { get { return Directory.Exists(Paths.CharactersDir); } }

        // "Abrir junto com o Windows": um valor em HKCU\...\Run apontando para este executável.
        public static bool AutoStart
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && k.GetValue(RunValue) != null;
                }
                catch { return false; }
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunValue, "\"" + Paths.Exe + "\" --fundo");
                    else k.DeleteValue(RunValue, false);
                }
            }
        }

        static void KillOthers()
        {
            foreach (var p in Process.GetProcessesByName("ScreenPets"))
                try { if (p.Id != Process.GetCurrentProcess().Id) { p.Kill(); p.WaitForExit(3000); } } catch { }
        }

        public static void Install(Action<string> progress)
        {
            progress("Abrindo a portinha...");
            KillOthers();
            Directory.CreateDirectory(Path.Combine(InstallDir, "bin"));

            progress("Copiando o programa...");
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, InstalledExe, true);

            progress("Chamando os bichinhos...");
            string chars = Path.Combine(InstallDir, "personagens");
            Directory.CreateDirectory(chars);
            foreach (var old in Directory.GetFiles(chars, "*.json")) File.Delete(old);
            foreach (var f in Character.EmbeddedFiles())
                File.WriteAllText(Path.Combine(chars, f.Key), f.Value, new UTF8Encoding(false));

            // Atualização: a configuração de quem já usava continua igual.
            string cfgPath = Path.Combine(InstallDir, "config.json");
            RemoveOldName(cfgPath);
            if (!File.Exists(cfgPath)) new Config().Save(cfgPath);

            progress("Criando os atalhos...");
            try { CreateShortcut(StartMenuShortcut, InstalledExe, "", "Screen Pets: bichinhos na tela"); } catch (Exception ex) { Paths.Log("atalho menu: " + ex.Message); }
            try { CreateShortcut(DesktopShortcut, InstalledExe, "", "Screen Pets: bichinhos na tela"); } catch (Exception ex) { Paths.Log("atalho área de trabalho: " + ex.Message); }
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    k.SetValue(RunValue, "\"" + InstalledExe + "\" --fundo");
            }
            catch (Exception ex) { Paths.Log("iniciar com o windows: " + ex.Message); }
            try { RegisterUninstall(); } catch (Exception ex) { Paths.Log("registro: " + ex.Message); }
            progress("Pronto!");
        }

        // O programa se chamava "Screen Crew": tira a instalação antiga (atalhos, iniciar com o Windows,
        // "Aplicativos instalados" e a pasta) e traz a turma configurada para a nova.
        static void RemoveOldName(string cfgPath)
        {
            const string old = "ScreenCrew";
            string oldDir = Path.Combine(Path.GetDirectoryName(InstallDir), old);
            foreach (var p in Process.GetProcessesByName(old))
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            try
            {
                string oldCfg = Path.Combine(oldDir, "config.json");
                if (File.Exists(oldCfg) && !File.Exists(cfgPath)) File.Copy(oldCfg, cfgPath);
            }
            catch (Exception ex) { Paths.Log("config antiga: " + ex.Message); }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null) k.DeleteValue(old, false);
            }
            catch { }
            foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
                try { File.Delete(Path.Combine(Environment.GetFolderPath(folder), "Screen Crew.lnk")); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + old, false); } catch { }
            try { if (Directory.Exists(oldDir)) Directory.Delete(oldDir, true); } catch (Exception ex) { Paths.Log("pasta antiga: " + ex.Message); }
        }

        static void RegisterUninstall()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Screen Pets (bichinhos na tela)");
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", "Serignolli");
                k.SetValue("DisplayIcon", InstalledExe);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" uninstall-app");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long size = new DirectoryInfo(InstallDir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
                k.SetValue("EstimatedSize", (int)size, RegistryValueKind.DWord);
            }
        }

        // Atalho via WScript.Shell (por reflexão, para não depender de referências COM na compilação).
        static void CreateShortcut(string lnk, string target, string args, string description)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        // Tira atalhos, o "iniciar com o Windows", o registro e, depois que este processo sair, a pasta.
        // O .exe em uso trava a pasta, então o cmd tenta de novo a cada segundo por até 30 segundos.
        public static void Uninstall()
        {
            KillOthers();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null) k.DeleteValue(RunValue, false);
            }
            catch { }
            foreach (var lnk in new[] { StartMenuShortcut, DesktopShortcut })
                try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            foreach (var dir in new[] { InstallDir, Paths.StateDir })
            {
                if (!Directory.Exists(dir)) continue;
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c for /l %i in (1,1,30) do (ping 127.0.0.1 -n 2 > nul & rmdir /s /q \"" + dir + "\" 2> nul & if not exist \"" + dir + "\" exit)")
                { UseShellExecute = false, CreateNoWindow = true };
                Process.Start(psi);
            }
        }
    }
}
