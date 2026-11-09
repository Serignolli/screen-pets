using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Drawing.Text;
using System.Windows.Forms;

namespace ScreenPets
{
    // Faixa com a equipe andando, trabalhando e pulando: o topo do instalador e da configuração.
    // É uma versão enxuta do OverlayForm, que roda dentro de uma janela comum.
    public class CrewBanner : Control
    {
        class Walker
        {
            public Character Ch; public Skin Skin; public int Px;
            public Bitmap[] WalkR, WalkL, WorkR, WorkL, IdleR, IdleL;
            public float X, Target, Timer, Ft, Y, VY; public int Dir = 1, State, Frame;
        }
        class Note { public float X, Y, VY, Life; public string Text; public Color Color; }

        const int WALK = 0, WORK = 1, IDLE = 2;
        readonly Timer timer = new Timer();
        readonly Random rnd = new Random();
        readonly List<Walker> walkers = new List<Walker>();
        readonly List<Note> notes = new List<Note>();
        readonly Dictionary<string, Bitmap[]> cache = new Dictionary<string, Bitmap[]>();
        DateTime last = DateTime.Now;
        double clock;
        float k = 1f;
        Font noteFont;
        public int Unit = 3;           // tamanho: como o "tamanho" da configuração (3 = padrão)
        public bool Floor = true;
        public string Title;           // escrito no canto, com a fonte pixelada
        public float TitleSize = 20f;
        Font titleFont;

        public CrewBanner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            timer.Interval = 30;
            timer.Tick += delegate { Step(); Invalidate(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (var g = CreateGraphics()) k = g.DpiX / 96f;
            noteFont = new Font("Segoe UI Symbol", 11f, FontStyle.Bold);
            timer.Start();
        }

        // Uma pessoa por (personagem, skin). Com skinId "*", sorteia uma skin para cada um.
        public void SetCrew(IList<Character> chars, string skinId, int count)
        {
            var picks = new List<KeyValuePair<Character, string>>();
            if (chars != null && chars.Count > 0)
                for (int i = 0; i < count; i++) picks.Add(new KeyValuePair<Character, string>(chars[i % chars.Count], skinId));
            SetMix(picks);
        }

        // Um bichinho por item: (personagem, cor), com "*" sorteando a cor.
        public void SetMix(IList<KeyValuePair<Character, string>> picks)
        {
            walkers.Clear(); notes.Clear();
            foreach (var pick in picks)
            {
                var ch = pick.Key;
                string skinId = pick.Value;
                var skin = skinId == "*" || skinId == null ? ch.Skins[rnd.Next(ch.Skins.Count)] : ch.GetSkin(skinId);
                var w = new Walker { Ch = ch, Skin = skin };
                w.Px = ch.PixelScale((int)Math.Round(Unit * k));
                w.WalkR = Frames(ch, skin, "andar", w.Px, false); w.WalkL = Frames(ch, skin, "andar", w.Px, true);
                w.IdleR = Frames(ch, skin, "parado", w.Px, false); w.IdleL = Frames(ch, skin, "parado", w.Px, true);
                var kind = ch.WorkKinds(skin).Count > 0 ? ch.WorkKinds(skin)[rnd.Next(ch.WorkKinds(skin).Count)] : "trabalhar";
                w.WorkR = Frames(ch, skin, kind, w.Px, false); w.WorkL = Frames(ch, skin, kind, w.Px, true);
                w.X = Width <= 0 ? 100 : (float)(Width * (0.1 + 0.8 * rnd.NextDouble()));
                w.Dir = rnd.Next(2) == 0 ? 1 : -1;
                Decide(w);
                walkers.Add(w);
            }
        }

        Bitmap[] Frames(Character ch, Skin s, string kind, int px, bool flip)
        {
            string key = ch.Id + "|" + s.Id + "|" + kind + "|" + px + "|" + flip;
            Bitmap[] b;
            if (!cache.TryGetValue(key, out b)) { b = ch.Render(s, kind, px, flip); cache[key] = b; }
            return b;
        }

        void Decide(Walker w)
        {
            double r = rnd.NextDouble();
            w.Frame = 0; w.Ft = 0;
            float spriteW = w.Ch.Width * w.Px;
            if (r < 0.35) { w.State = WORK; w.Timer = 2f + (float)rnd.NextDouble() * 3f; }
            else if (r < 0.8)
            {
                w.State = WALK;
                w.Target = spriteW / 2 + (float)rnd.NextDouble() * Math.Max(1, Width - spriteW);
            }
            else
            {
                w.State = IDLE; w.Timer = 1f + (float)rnd.NextDouble() * 1.5f;
                if (rnd.NextDouble() < 0.5) w.Dir = -w.Dir;
                if (rnd.NextDouble() < 0.4) w.VY = (float)Math.Sqrt(2 * 900 * k * 6 * k);   // pulinho
            }
        }

        void Step()
        {
            var now = DateTime.Now;
            float dt = (float)Math.Min(0.1, (now - last).TotalSeconds);
            last = now;
            clock += dt;
            float speed = 34f * k;
            foreach (var w in walkers)
            {
                w.Ft += dt;
                if (w.Y > 0 || w.VY > 0)
                {
                    w.VY -= 900 * k * dt; w.Y += w.VY * dt;
                    if (w.Y <= 0) { w.Y = 0; w.VY = 0; }
                }
                switch (w.State)
                {
                    case WALK:
                        float dx = w.Target - w.X;
                        if (Math.Abs(dx) <= speed * dt) { w.X = w.Target; Decide(w); break; }
                        w.Dir = dx > 0 ? 1 : -1; w.X += w.Dir * speed * dt;
                        if (w.Ft > (w.WalkR.Length >= 8 ? 0.095f : w.WalkR.Length >= 4 ? 0.13f : 0.18f)) { w.Ft = 0; w.Frame++; }
                        break;
                    case WORK:
                        w.Timer -= dt;
                        if (w.Ft > (w.WorkR.Length >= 4 ? 0.11f : 0.3f))
                        {
                            w.Ft = 0; w.Frame = (w.Frame + 1) % w.WorkR.Length;
                            if (w.Frame == w.WorkR.Length - 1) Sparkle(w);
                        }
                        if (w.Timer <= 0) Decide(w);
                        break;
                    default:
                        w.Timer -= dt;
                        if (w.IdleR.Length > 1 && w.Ft > (w.Frame == 0 ? 1.6f : 0.09f)) { w.Ft = 0; w.Frame = (w.Frame + 1) % w.IdleR.Length; }
                        if (w.Timer <= 0) Decide(w);
                        break;
                }
            }
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                var n = notes[i];
                n.Life -= dt; n.Y += n.VY * dt;
                if (n.Life <= 0) notes.RemoveAt(i);
            }
        }

        static readonly string[] Glyphs = { "✦", "✿", "♪", "✻", "★" };
        static readonly Color[] GlyphColors = { Color.FromArgb(217, 119, 87), Color.FromArgb(236, 64, 122), Color.FromArgb(255, 179, 0), Color.FromArgb(102, 187, 106) };

        void Sparkle(Walker w)
        {
            if (notes.Count > 40) return;
            notes.Add(new Note
            {
                X = w.X + w.Dir * w.Ch.Width * w.Px * 0.25f, Y = Ground - (w.Ch.FeetRow - w.Skin.TopRow) * w.Px,
                VY = -28 * k, Life = 1.4f, Text = Glyphs[rnd.Next(Glyphs.Length)], Color = GlyphColors[rnd.Next(GlyphColors.Length)]
            });
        }

        int Ground { get { return Height - (Floor ? (int)(12 * k) : 0); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (Floor)
            {
                int fh = (int)(12 * k);
                using (var b = new SolidBrush(Color.FromArgb(139, 90, 60))) g.FillRectangle(b, 0, Height - fh, Width, fh);
                using (var b = new SolidBrush(Color.FromArgb(122, 76, 48)))
                    for (int x = 0; x < Width; x += (int)(52 * k)) g.FillRectangle(b, x + (int)(26 * k), Height - fh, (int)(26 * k), fh);
                using (var p = new Pen(Theme.Ink, Math.Max(2, 3 * k))) g.DrawLine(p, 0, Height - fh, Width, Height - fh);
            }
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            foreach (var w in walkers.OrderBy(x => x.Y))
            {
                bool r = w.Dir > 0;
                var set = w.State == WORK ? (r ? w.WorkR : w.WorkL) : w.State == WALK ? (r ? w.WalkR : w.WalkL) : (r ? w.IdleR : w.IdleL);
                var bmp = set[w.Frame % set.Length];
                // quem voa fica no alto, subindo e descendo devagar
                int fly = w.Ch.FlyHeight == 0 ? 0 : (int)(w.Ch.FlyHeight * w.Px * 0.6 + Math.Sin(clock * 3.2 + w.Target) * 3 * k);
                int top = Ground - (w.Ch.FeetRow + 1) * w.Px - (int)w.Y - fly;
                g.DrawImage(bmp, new Rectangle((int)w.X - bmp.Width / 2, top, bmp.Width, bmp.Height));
            }
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (!string.IsNullOrEmpty(Title))
            {
                if (titleFont == null) titleFont = Theme.Pixel(TitleSize);
                using (var b = new SolidBrush(Theme.Ink)) g.DrawString(Title, titleFont, b, 18 * k, 10 * k);
            }
            foreach (var n in notes)
                using (var b = new SolidBrush(Color.FromArgb((int)(255 * Math.Min(1, n.Life / 0.5f)), n.Color)))
                    g.DrawString(n.Text, noteFont, b, n.X, n.Y);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                foreach (var arr in cache.Values) foreach (var b in arr) b.Dispose();
                if (noteFont != null) noteFont.Dispose();
                if (titleFont != null) titleFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
