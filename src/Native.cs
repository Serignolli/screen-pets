using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenPets
{
    static class Native
    {
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        // Algum programa em tela cheia (vídeo, jogo, apresentação) cobrindo a tela inteira "screen"?
        // A área de trabalho e a barra de tarefas não contam.
        public static bool FullscreenOn(Rectangle screen)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == GetShellWindow()) return false;
            uint pid;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return false;
            var cls = new StringBuilder(64);
            GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
            RECT r;
            if (!GetWindowRect(fg, out r)) return false;
            return r.Left <= screen.Left && r.Top <= screen.Top && r.Right >= screen.Right && r.Bottom >= screen.Bottom;
        }
    }
}
