using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace ScreenPets
{
    // As bordas da tela onde os bichinhos ficam.
    public enum Zone { Ground, Top, Left, Right }

    // Quem entra num palco: personagem, cor, quantos e tamanho.
    public class Cast
    {
        public Character Ch;
        public string Skin;
        public int Count, Size;
    }

    // O que vale para todos de uma mesma turma dentro do palco.
    class Troupe
    {
        public Character Ch;
        public int Scale, Px, SpriteW, FeetOff;
        // "Em pé": voadores no alto e nas laterais voam sem girar junto com a borda.
        public bool Upright;
        public int FlyBase, FlyRange;
        public float Gravity;
        public Font SymbolFont;
    }

    class Worker
    {
        public Troupe T;
        public float X = float.NaN, Target, Timer, FrameTimer, Delay, SpeedMul, BubbleTimer;
        public float Y, VY, Squash, MeetCooldown;   // Y: altura do pulo (para longe da borda)
        public float Alt, AltTarget;                // altitude extra de quem voa
        public float IdleHold = 0.8f;
        public float Phase;                         // defasagem do sobe e desce de quem voa
        public int Dir = 1, State, Frame, TopRow;
        public bool Carrying, Looked;
        public Bitmap[] WalkR, WalkL, WorkR, WorkL, IdleR, IdleL;
        public Bitmap[][] WorksR, WorksL;           // um conjunto por tipo de trabalho (regar, cavar...)
        public string[] WorkFx;
        public Bitmap CarryR, CarryL;
        public string Bubble, Effect;
    }

    class Particle
    {
        public float X, Y, VX, VY, GX, GY, Life, MaxLife, Grow, Crawl;
        public Color Color;
        public string Text;
        public Font Font;
        public int Size;
        public bool Bug, Floor;
    }

    // Uma faixa transparente, que não recebe cliques, colada numa borda da tela.
    // Toda a lógica acontece num espaço "canônico": uma faixa deitada de comprimento L e espessura T,
    // com o chão embaixo (y = T). A matriz M leva esse espaço para a janela: no topo ele fica de cabeça
    // para baixo (andando no teto) e nas laterais ele gira 90° (escalando a parede).
    public class Stage : Form
    {
        const int WALK = 0, WORK = 1, IDLE = 2, JUMP = 3;

        readonly Zone zone;
        readonly Config cfg;
        readonly List<Troupe> troupes = new List<Troupe>();
        readonly List<Worker> workers = new List<Worker>();
        readonly List<Particle> parts = new List<Particle>();
        readonly List<Bitmap> bitmaps = new List<Bitmap>();
        readonly Random rnd = new Random();
        readonly Stopwatch clock = new Stopwatch();
        readonly Timer timer;
        readonly Font bubbleFont;
        readonly Dictionary<Color, SolidBrush> brushes = new Dictionary<Color, SolidBrush>();
        readonly float dpiK;
        readonly int thick, bubbleSpace;
        int length;
        Matrix M = new Matrix();
        double lastT, elapsed, nextBubble, nextZ, nextBounds;
        bool hidden;

        bool Horizontal { get { return zone == Zone.Ground || zone == Zone.Top; } }
        bool Bubbles { get { return cfg.Bubbles && Horizontal; } }

        public Stage(Zone zone, List<Cast> casts, Config cfg)
        {
            this.zone = zone;
            this.cfg = cfg;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpiK = g.DpiX / 96f;
            bubbleFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            bubbleSpace = (int)(34 * dpiK);
            nextBubble = 4 + rnd.NextDouble() * 6;

            int need = 0, index = 0;
            foreach (var cast in casts)
            {
                var t = MakeTroupe(cast);
                troupes.Add(t);
                var ch = t.Ch;
                int across = t.Upright && !Horizontal ? t.SpriteW : (ch.FeetRow - ch.TopRow + 1) * t.Px;
                int room = across + t.FlyBase + t.FlyRange + 3 * t.Scale + (t.Upright ? 0 : 16 * t.Scale);
                need = Math.Max(need, room);

                var cache = new Dictionary<string, Bitmap[][]>();
                var prop = Prop.Get(ch.CarryOf(ch.Skins[0]));
                // Misturando cores, cada um começa com uma diferente antes de repetir.
                var order = ch.Skins.OrderBy(x => rnd.Next()).ToList();
                for (int i = 0; i < cast.Count; i++, index++)
                {
                    Skin skin = cast.Skin == "*" ? order[i % order.Count] : ch.GetSkin(cast.Skin);
                    var workKinds = ch.WorkKinds(skin);
                    Bitmap[][] set;
                    if (!cache.TryGetValue(skin.Id, out set))
                    {
                        // andar R/L, parado R/L e, para cada tipo de trabalho, R/L
                        var l = new List<Bitmap[]> {
                            Keep(ch.Render(skin, "andar", t.Px, false)), Keep(ch.Render(skin, "andar", t.Px, true)),
                            Keep(ch.Render(skin, "parado", t.Px, false)), Keep(ch.Render(skin, "parado", t.Px, true)) };
                        foreach (var k in workKinds) { l.Add(Keep(ch.Render(skin, k, t.Px, false))); l.Add(Keep(ch.Render(skin, k, t.Px, true))); }
                        set = l.ToArray();
                        cache[skin.Id] = set;
                    }
                    var w = new Worker { T = t };
                    w.WalkR = set[0]; w.WalkL = set[1]; w.IdleR = set[2]; w.IdleL = set[3];
                    int nw = workKinds.Count;
                    w.WorksR = new Bitmap[nw][]; w.WorksL = new Bitmap[nw][]; w.WorkFx = new string[nw];
                    for (int k = 0; k < nw; k++)
                    {
                        w.WorksR[k] = set[4 + 2 * k]; w.WorksL[k] = set[5 + 2 * k];
                        w.WorkFx[k] = ch.EffectOf(skin, workKinds[k]);
                    }
                    PickWork(w);
                    var carry = Prop.Get(ch.CarryOf(skin)) ?? prop;
                    if (carry != null && !t.Upright)
                    {
                        w.CarryR = Keep(PixelArt.Render(carry.Rows, carry.Colors, t.Scale, false));
                        w.CarryL = Keep(PixelArt.Render(carry.Rows, carry.Colors, t.Scale, true));
                    }
                    w.TopRow = skin.TopRow;
                    w.SpeedMul = 0.8f + (float)rnd.NextDouble() * 0.4f;
                    w.Delay = index * 0.7f + (float)rnd.NextDouble() * 0.5f;
                    w.Dir = index % 2 == 0 ? 1 : -1;
                    w.State = WALK;
                    w.MeetCooldown = 2f;
                    w.Phase = (float)(rnd.NextDouble() * Math.PI * 2);
                    w.Alt = w.AltTarget = (float)rnd.NextDouble() * t.FlyRange;
                    workers.Add(w);
                }
            }
            thick = need + (Bubbles ? bubbleSpace : 0) + 2;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Text = "Screen Pets";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Bounds = new Rectangle(-32000, -32000, 10, 10);

            timer = new Timer();
            timer.Interval = 30;
            timer.Tick += OnTick;
        }

        Bitmap Keep(Bitmap b) { bitmaps.Add(b); return b; }
        Bitmap[] Keep(Bitmap[] b) { bitmaps.AddRange(b); return b; }

        Troupe MakeTroupe(Cast cast)
        {
            var ch = cast.Ch;
            var t = new Troupe { Ch = ch };
            t.Scale = Math.Max(1, (int)Math.Round(Math.Max(1, cast.Size) * dpiK));
            t.Px = ch.PixelScale(t.Scale);
            t.SpriteW = ch.Width * t.Px;
            t.FeetOff = (ch.FeetRow + 1) * t.Px;
            t.Upright = ch.Flies && zone != Zone.Ground;
            int visH = (ch.FeetRow - ch.TopRow + 1) * t.Px;
            t.FlyBase = !ch.Flies ? 0 : t.Upright ? 2 * t.Scale : ch.FlyHeight * t.Px;
            t.FlyRange = ch.Flies ? visH : 0;
            t.Gravity = 900f * t.Scale / 3f;
            t.SymbolFont = new Font("Segoe UI Symbol", 6f + Math.Max(1, cast.Size) * 1.6f, FontStyle.Bold);
            return t;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // layered + transparente a cliques + fora do Alt-Tab + nunca rouba o foco
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x08000000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Reposition();
            clock.Start();
            timer.Start();
        }

        // Esconde sem perder o lugar de ninguém (tela cheia, "esconder por agora").
        public void SetHidden(bool hide)
        {
            if (hide == hidden) return;
            hidden = hide;
            if (hide) { timer.Stop(); Hide(); }
            else { Show(); lastT = clock.Elapsed.TotalSeconds; timer.Start(); }
        }

        // ---------- posição ----------

        void Reposition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Rectangle b;
            int len;
            switch (zone)
            {
                case Zone.Top: b = new Rectangle(wa.Left, wa.Top, wa.Width, thick); len = wa.Width; break;
                case Zone.Left: b = new Rectangle(wa.Left, wa.Top, thick, wa.Height); len = wa.Height; break;
                case Zone.Right: b = new Rectangle(wa.Right - thick, wa.Top, thick, wa.Height); len = wa.Height; break;
                default: b = new Rectangle(wa.Left, wa.Bottom - thick, wa.Width, thick); len = wa.Width; break;
            }
            if (Bounds == b && len == length) return;
            Bounds = b;
            length = len;
            M.Dispose();
            switch (zone)
            {
                case Zone.Top: M = new Matrix(1, 0, 0, -1, 0, thick); break;
                case Zone.Left: M = new Matrix(0, -1, -1, 0, thick, length); break;
                case Zone.Right: M = new Matrix(0, -1, 1, 0, 0, length); break;
                default: M = new Matrix(); break;
            }
            foreach (var w in workers)
            {
                if (float.IsNaN(w.X)) continue;
                w.X = Math.Max(-w.T.SpriteW, Math.Min(length + w.T.SpriteW, w.X));
                if (w.Target > length) w.Target = RandomTarget(w);
            }
        }

        // ---------- ciclo ----------

        void OnTick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Max(0, Math.Min(0.1, now - lastT));
            lastT = now;

            if (now >= nextBounds) { nextBounds = now + 0.5; Reposition(); }
            if (now >= nextZ)
            {
                nextZ = now + 0.5;
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            Step(dt);
            Invalidate();
        }

        void Step(float dt)
        {
            elapsed += dt;
            foreach (var w in workers) UpdateWorker(w, dt);
            CheckMeetings();
            UpdateParticles(dt);
            UpdateBubbles(dt);
        }

        // Para testes sem mostrar a janela: simula alguns segundos e desenha a faixa numa imagem
        // (magenta = transparente). "where" é o retângulo da faixa na tela.
        public Bitmap Snapshot(double seconds, out Rectangle where)
        {
            Reposition();
            for (double t = 0; t < seconds; t += 0.03) Step(0.03f);
            where = Bounds;
            var bmp = new Bitmap(Width, Height);
            using (var g = Graphics.FromImage(bmp)) Draw(g);
            return bmp;
        }

        float RandomTarget(Worker w)
        {
            float margin = w.T.SpriteW * 0.75f;
            float span = Math.Max(1, length - 2 * margin);
            return margin + (float)rnd.NextDouble() * span;
        }

        void Decide(Worker w)
        {
            double r = rnd.NextDouble();
            bool flies = w.T.Ch.Flies;
            w.Frame = 0; w.FrameTimer = 0; w.Looked = false;
            if (r < (flies ? 0.3 : 0.42))
            {
                w.State = WORK;
                PickWork(w);
                w.Timer = 2.5f + (float)rnd.NextDouble() * 4f;
                if (w.Carrying) { w.Carrying = false; Spawn("poeira", w, 3); }
            }
            else if (r < 0.8)
            {
                w.State = WALK;
                w.Target = RandomTarget(w);
                w.AltTarget = (float)rnd.NextDouble() * w.T.FlyRange;
                w.Carrying = w.CarryR != null && rnd.NextDouble() < 0.45;
            }
            else if (r < 0.88 && w.Y <= 0 && !flies)
            {
                // pulinho de alegria no lugar
                w.State = JUMP;
                Launch(w, 5 + (float)rnd.NextDouble() * 3);
            }
            else
            {
                w.State = IDLE;
                w.Timer = 0.9f + (float)rnd.NextDouble() * 2.2f;
                if (rnd.NextDouble() < 0.5) w.Dir = -w.Dir;
            }
        }

        // Sorteia qual trabalho fazer (quando o personagem tem mais de um).
        void PickWork(Worker w)
        {
            int k = rnd.Next(w.WorksR.Length);
            w.WorkR = w.WorksR[k]; w.WorkL = w.WorksL[k]; w.Effect = w.WorkFx[k];
        }

        // Pulo de "height" pontos de altura.
        void Launch(Worker w, float height)
        {
            if (w.Y > 0) return;
            w.VY = (float)Math.Sqrt(2 * w.T.Gravity * height * w.T.Scale);
            w.Y = 0.01f;
        }

        void UpdateWorker(Worker w, float dt)
        {
            if (w.Delay > 0) { w.Delay -= dt; return; }
            var t = w.T;
            if (float.IsNaN(w.X))
            {
                w.X = w.Dir > 0 ? -t.SpriteW : length + t.SpriteW;
                w.Target = RandomTarget(w);
                w.State = WALK;
            }
            if (w.BubbleTimer > 0) { w.BubbleTimer -= dt; if (w.BubbleTimer <= 0) w.Bubble = null; }
            if (w.Squash > 0) w.Squash -= dt;
            if (w.MeetCooldown > 0) w.MeetCooldown -= dt;
            w.Alt += (w.AltTarget - w.Alt) * Math.Min(1f, dt * 0.8f);
            w.FrameTimer += dt;

            // física do pulo
            if (w.Y > 0)
            {
                w.VY -= t.Gravity * dt;
                w.Y += w.VY * dt;
                if (w.Y <= 0)
                {
                    w.Y = 0; w.VY = 0; w.Squash = 0.12f;
                    if (!t.Ch.Flies) Spawn("poeira", w, 2);
                    if (w.State == JUMP) Decide(w);
                }
            }

            double pace = cfg.Speed * t.Ch.Pace;
            float speed = (float)(34.0 * t.Scale / 3.0 * pace) * w.SpeedMul;
            // desenhos com mais quadros trocam mais rápido, para o passo continuar do mesmo tamanho
            float step = (w.WalkR.Length >= 8 ? 0.095f : w.WalkR.Length >= 4 ? 0.13f : 0.18f) / (float)(cfg.Speed * Math.Max(0.5, t.Ch.Pace));
            switch (w.State)
            {
                case WALK:
                    float dx = w.Target - w.X;
                    if (Math.Abs(dx) <= speed * dt) { w.X = w.Target; Decide(w); break; }
                    w.Dir = dx > 0 ? 1 : -1;
                    w.X += w.Dir * speed * dt;
                    if (w.FrameTimer > step) { w.FrameTimer = 0; w.Frame = (w.Frame + 1) % w.WalkR.Length; }
                    break;
                case WORK:
                    w.Timer -= dt;
                    // animações quadro a quadro andam mais rápido que as de 2 quadros
                    if (w.FrameTimer > (w.WorkR.Length >= 4 ? 0.11f : 0.3f) / Math.Max(0.5, cfg.Speed))
                    {
                        w.FrameTimer = 0;
                        w.Frame = (w.Frame + 1) % w.WorkR.Length;
                        if (w.Frame == w.WorkR.Length - 1) Spawn(w.Effect, w, 0);
                    }
                    if (w.Timer <= 0) Decide(w);
                    break;
                case JUMP:
                    break;
                default:
                    w.Timer -= dt;
                    if (t.Ch.Flies)
                    {
                        // quem voa continua batendo as asas parado no ar
                        if (w.FrameTimer > step * 1.3f) { w.FrameTimer = 0; w.Frame = (w.Frame + 1) % w.WalkR.Length; }
                    }
                    else AnimateIdle(w);
                    // olha para o outro lado no meio da pausa
                    if (!w.Looked && w.Timer < 0.6f && rnd.NextDouble() < 0.5) { w.Dir = -w.Dir; w.Looked = true; }
                    if (w.Timer <= 0) Decide(w);
                    break;
            }
        }

        // "parado" com vários quadros: o primeiro fica na tela um tempo (respirando) e os outros
        // passam rápido (uma piscada), depois volta ao primeiro.
        void AnimateIdle(Worker w)
        {
            if (w.IdleR.Length < 2) { w.Frame = 0; return; }
            float hold = w.Frame == 0 ? w.IdleHold : 0.09f;
            if (w.FrameTimer < hold) return;
            w.FrameTimer = 0;
            w.Frame = (w.Frame + 1) % w.IdleR.Length;
            if (w.Frame == 0) w.IdleHold = 1.2f + (float)rnd.NextDouble() * 2.2f;
        }

        bool Visible_(Worker w) { return w.Delay <= 0 && !float.IsNaN(w.X); }

        // Quando dois se cruzam andando, às vezes param, se olham e se cumprimentam.
        void CheckMeetings()
        {
            for (int i = 0; i < workers.Count; i++)
            {
                var a = workers[i];
                if (a.State != WALK || !Visible_(a) || a.MeetCooldown > 0) continue;
                for (int j = i + 1; j < workers.Count; j++)
                {
                    var b = workers[j];
                    if (b.State != WALK || !Visible_(b) || b.MeetCooldown > 0) continue;
                    if (a.Dir == b.Dir || a.T.Ch.Flies != b.T.Ch.Flies) continue;
                    float sw = (a.T.SpriteW + b.T.SpriteW) / 2f;
                    float gap = b.X - a.X;
                    if (Math.Abs(gap) > sw * 0.9f || Math.Abs(gap) < sw * 0.5f) continue;
                    if ((gap > 0) != (a.Dir > 0)) continue;   // só quando estão indo um na direção do outro
                    a.MeetCooldown = b.MeetCooldown = 10f;
                    if (rnd.NextDouble() > 0.45) continue;
                    foreach (var w in new[] { a, b })
                    {
                        w.State = IDLE; w.Timer = 1.3f + (float)rnd.NextDouble() * 0.5f; w.Frame = 0; w.Looked = true;
                    }
                    a.Dir = gap > 0 ? 1 : -1; b.Dir = -a.Dir;
                    var hopper = rnd.Next(2) == 0 ? a : b;
                    if (!hopper.T.Ch.Flies) Launch(hopper, 3);
                    Add(a, (b.X - a.X) / 2 - a.T.Scale * 2, -VisH(a) - a.T.Scale * 2, R(-6, 6), -22, 0, 1.3f,
                        new[] { Color.FromArgb(236, 64, 122), Color.FromArgb(255, 202, 40) }[rnd.Next(2)], rnd.Next(3) == 0 ? "!" : "♥", 0);
                    break;
                }
            }
        }

        void UpdateBubbles(float dt)
        {
            if (!Bubbles) return;
            nextBubble -= dt;
            if (nextBubble > 0) return;
            var candidates = workers.Where(w => Visible_(w) && w.Bubble == null && w.T.Ch.Speech.Count > 0
                                                && w.X > 0 && w.X < length).ToList();
            if (candidates.Count > 0)
            {
                var w = candidates[rnd.Next(candidates.Count)];
                w.Bubble = w.T.Ch.Speech[rnd.Next(w.T.Ch.Speech.Count)];
                w.BubbleTimer = 3.2f;
            }
            nextBubble = 10 + rnd.NextDouble() * 14;
        }

        // ---------- geometria ----------

        int VisH(Worker w) { return (w.T.Ch.FeetRow - w.TopRow + 1) * w.T.Px; }

        // Altura de quem voa: base + altitude própria + um sobe e desce suave (bater de asas).
        float Fly(Worker w)
        {
            var t = w.T;
            if (!t.Ch.Flies) return 0;
            return t.FlyBase + w.Alt + (float)(Math.Sin(elapsed * 3.2 + w.Phase) * t.Scale * 1.3);
        }

        float Height_(Worker w) { return w.Y + Fly(w); }

        // Para onde o desenho olha na tela, para quem voa em pé.
        int Facing(Worker w)
        {
            if (zone == Zone.Right) return -1;
            if (zone == Zone.Left) return 1;
            return w.Dir;
        }

        int Face(Worker w) { return w.T.Upright ? Facing(w) : w.Dir; }

        PointF Map(float x, float y)
        {
            var p = new[] { new PointF(x, y) };
            M.TransformPoints(p);
            return p[0];
        }

        PointF MapVector(float x, float y)
        {
            var p = new[] { new PointF(x, y) };
            M.TransformVectors(p);
            return p[0];
        }

        // Quem voa em pé: canto de cima do bitmap, na janela.
        PointF UprightTopLeft(Worker w, Bitmap bmp)
        {
            var t = w.T;
            int visH = VisH(w);
            float across = Horizontal ? visH : t.SpriteW;
            var c = Map(w.X, thick - Height_(w) - across / 2);
            return new PointF(c.X - bmp.Width / 2f, c.Y - (w.TopRow * t.Px + visH / 2f));
        }

        // Os pés, na janela (origem dos efeitos).
        PointF FeetOnScreen(Worker w)
        {
            if (!w.T.Upright) return Map(w.X, thick - Height_(w));
            var tl = UprightTopLeft(w, w.WalkR[0]);
            return new PointF(tl.X + w.WalkR[0].Width / 2f, tl.Y + w.T.FeetOff);
        }

        // Retângulo do desenho visível, na janela.
        RectangleF SpriteOnScreen(Worker w)
        {
            var t = w.T;
            int visH = VisH(w);
            if (t.Upright)
            {
                var tl = UprightTopLeft(w, w.WalkR[0]);
                return new RectangleF(tl.X, tl.Y + w.TopRow * t.Px, w.WalkR[0].Width, visH);
            }
            float top = thick - Height_(w) - visH;
            var a = Map(w.X - t.SpriteW / 2f, top);
            var b = Map(w.X + t.SpriteW / 2f, top + visH);
            return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        // ---------- partículas ----------

        float R(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }
        Color Pick(params Color[] c) { return c[rnd.Next(c.Length)]; }
        string Pick(params string[] s) { return s[rnd.Next(s.Length)]; }

        // Efeitos em coordenadas do desenho: origem nos pés, x para onde ele olha (já multiplicado por f), y para cima negativo.
        void Spawn(string effect, Worker w, int count)
        {
            if (string.IsNullOrEmpty(effect)) return;
            if (effect.IndexOf('+') >= 0)
            {
                foreach (var part in effect.Split('+')) Spawn(part.Trim(), w, count);
                return;
            }
            var t = w.T;
            int s = t.Scale, f = Face(w);
            float sw = t.SpriteW;
            float frontX = f * sw * 0.45f, headX = f * sw * 0.2f;
            float headY = -VisH(w), midY = -(t.Ch.FeetRow - w.TopRow) * t.Px * 0.3f;
            switch (effect.ToLowerInvariant())
            {
                case "faiscas":
                    for (int i = 0; i < (count > 0 ? count : 7); i++)
                        Add(w, frontX, -s * 2, R(-110, 110), R(-190, -60), 420, R(0.35f, 0.7f), Pick(Color.Gold, Color.Orange, Color.White, Color.OrangeRed), null, s);
                    break;
                case "poeira":
                    for (int i = 0; i < (count > 0 ? count : 4); i++)
                        Add(w, R(-sw * 0.3f, sw * 0.3f), -s, R(-25, 25), R(-35, -10), 0, R(0.4f, 0.8f),
                            Pick(Color.FromArgb(190, 170, 140), Color.FromArgb(150, 130, 110)), null, s * (1 + rnd.Next(2)));
                    break;
                case "terra":
                    for (int i = 0; i < (count > 0 ? count : 6); i++)
                        Add(w, frontX, -s * 2, R(-40, 90) * -f, R(-170, -80), 420, R(0.4f, 0.8f),
                            Pick(Color.FromArgb(121, 85, 58), Color.FromArgb(93, 64, 45), Color.FromArgb(150, 110, 70)), null, s * (1 + rnd.Next(2)));
                    break;
                case "neve":
                    for (int i = 0; i < (count > 0 ? count : 5); i++)
                        Add(w, frontX + R(-10, 10), midY, R(-30, 30), R(-60, -20), 60, R(0.6f, 1.1f), Pick(Color.White, Color.FromArgb(180, 220, 255)), null, s);
                    break;
                case "estrelas":
                    for (int i = 0; i < (count > 0 ? count : 3); i++)
                        Add(w, frontX + R(-6, 6), midY, R(-40, 40), R(-80, -30), 60, R(0.5f, 0.9f), Pick(Color.Gold, Color.FromArgb(255, 240, 150), Color.HotPink), null, s);
                    break;
                case "magia":
                    for (int i = 0; i < (count > 0 ? count : 2); i++)
                        Add(w, headX + R(-10, 10), headY + R(0, 12), R(-24, 24), R(-40, -20), 0, R(1.0f, 1.5f),
                            Pick(Color.FromArgb(179, 136, 255), Color.FromArgb(100, 255, 218), Color.Gold), Pick("✦", "✧", "★", "✶"), 0);
                    break;
                case "zzz":
                    Add(w, f * sw * 0.3f, headY + s * 4, f * R(8, 16), R(-26, -18), 0, 2.2f, Color.FromArgb(90, 110, 200), rnd.Next(3) == 0 ? "Z" : "z", 0);
                    break;
                case "notas":
                    Add(w, headX, headY, f * R(10, 22), R(-30, -20), 0, 1.8f,
                        Pick(Color.FromArgb(120, 60, 160), Color.FromArgb(40, 120, 60), Color.FromArgb(200, 80, 40)), rnd.Next(2) == 0 ? "♪" : "♫", 0);
                    break;
                case "claude":
                    Add(w, headX, headY, f * R(6, 20), R(-34, -20), 0, 1.6f,
                        Pick(Color.FromArgb(217, 119, 87), Color.FromArgb(245, 163, 127), Color.FromArgb(184, 95, 66)), Pick("✻", "✳", "✢", "✶", "✽"), 0);
                    break;
                case "codigo":
                    for (int i = 0; i < (count > 0 ? count : 2); i++)
                        Add(w, headX + R(-8, 8), headY + R(0, 10), f * R(4, 16), R(-34, -18), 0, R(1.2f, 1.7f),
                            Pick(Color.FromArgb(0, 230, 118), Color.FromArgb(105, 240, 174), Color.FromArgb(46, 160, 67)), Pick("0", "1", "{ }", "</>", ";", "#", "()"), 0);
                    break;
                case "dados":
                    for (int i = 0; i < (count > 0 ? count : 2); i++)
                        Add(w, headX + R(-8, 8), headY + R(0, 10), f * R(4, 16), R(-34, -18), 0, R(1.2f, 1.7f),
                            Pick(Color.FromArgb(66, 165, 245), Color.FromArgb(38, 198, 218), Color.FromArgb(255, 202, 40)), Pick("%", "Σ", "π", "▲", "≈"), 0);
                    break;
                case "bugs":
                    for (int i = 0; i < (count > 0 ? count : 1 + rnd.Next(2)); i++)
                    {
                        var p = Add(w, frontX, midY, R(-60, 60), R(-150, -90), 420, R(1.6f, 2.4f), Color.FromArgb(229, 57, 53), null, s * 2);
                        if (p != null) { p.Bug = true; p.Crawl = 38f * s / 3f; }
                    }
                    break;
                case "fumaca":
                    for (int i = 0; i < (count > 0 ? count : 3); i++)
                    {
                        var p = Add(w, headX + R(-6, 6), headY + R(0, 8), R(-10, 10), R(-30, -16), 0, R(1.0f, 1.6f),
                            Pick(Color.FromArgb(120, 120, 120), Color.FromArgb(150, 150, 150), Color.FromArgb(95, 95, 95)), null, s);
                        if (p != null) p.Grow = s * 1.8f;
                    }
                    break;
                case "moedas":
                    for (int i = 0; i < (count > 0 ? count : 2); i++)
                        Add(w, frontX, midY, R(-50, 50), R(-190, -120), 420, R(0.6f, 0.9f), Pick(Color.Gold, Color.FromArgb(255, 213, 79)), null, s * 2);
                    if (rnd.Next(3) == 0) Add(w, headX, headY, f * 10, -30, 0, 1.2f, Color.Gold, "★", 0);
                    break;
                case "latidos":
                    Add(w, headX + f * sw * 0.25f, headY + s * 2, f * R(10, 20), R(-26, -16), 0, 1.3f,
                        Pick(Color.FromArgb(255, 183, 77), Color.FromArgb(255, 241, 118)), Pick("au!", "au au!", "auu!"), 0);
                    break;
                case "coracoes":
                    Add(w, headX, headY, f * R(6, 16), R(-30, -18), 0, 1.6f,
                        Pick(Color.FromArgb(236, 64, 122), Color.FromArgb(244, 143, 177), Color.FromArgb(229, 57, 53)), "♥", 0);
                    break;
                case "fogo":
                    // labareda saindo da boca, para a frente
                    for (int i = 0; i < (count > 0 ? count : 12); i++)
                        Add(w, f * sw * 0.5f, headY + R(2, 4) * s, f * R(140, 240), R(-25, 10), -40, R(0.25f, 0.55f),
                            Pick(Color.FromArgb(255, 235, 59), Color.FromArgb(255, 152, 0), Color.FromArgb(244, 67, 54), Color.FromArgb(255, 243, 176)), null, s * (1 + rnd.Next(2)));
                    break;
                case "flechas":
                    Add(w, f * sw * 0.5f, midY - s * 3, f * R(240, 300), R(-8, -2), 0, 0.8f, Color.FromArgb(141, 110, 99), f > 0 ? "➳" : "⟵", 0);
                    break;
                case "flores":
                    for (int i = 0; i < (count > 0 ? count : 2); i++)
                        Add(w, frontX + R(-6, 6), midY, R(-30, 30), R(-60, -30), 20, R(1.0f, 1.5f),
                            Pick(Color.FromArgb(236, 64, 122), Color.FromArgb(255, 235, 59), Color.FromArgb(171, 71, 188), Color.FromArgb(102, 187, 106)), Pick("✿", "❀", "✾"), 0);
                    break;
            }
        }

        // Cria a partícula convertendo das coordenadas do desenho para a janela.
        Particle Add(Worker w, float ox, float oy, float vx, float vy, float grav, float life, Color c, string text, int size)
        {
            if (parts.Count > 300) return null;
            var p = new Particle();
            if (w.T.Upright)
            {
                var feet = FeetOnScreen(w);
                p.X = feet.X + ox; p.Y = feet.Y + oy;
                p.VX = vx; p.VY = vy; p.GY = grav;
            }
            else
            {
                var pos = Map(w.X + ox, thick - Height_(w) + oy);
                var v = MapVector(vx, vy);
                var g = MapVector(0, grav);
                p.X = pos.X; p.Y = pos.Y; p.VX = v.X; p.VY = v.Y; p.GX = g.X; p.GY = g.Y;
            }
            // no chão as coisas quicam; no alto e nas laterais elas caem para fora da faixa
            p.Floor = zone == Zone.Ground;
            p.Life = p.MaxLife = life;
            p.Color = PixelArt.Safe(c); p.Text = text; p.Size = size; p.Font = w.T.SymbolFont;
            parts.Add(p);
            return p;
        }

        void UpdateParticles(float dt)
        {
            int floor = ClientSize.Height;
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i];
                p.Life -= dt;
                if (p.Life <= 0 || p.X < -60 || p.Y < -60 || p.X > ClientSize.Width + 60 || p.Y > ClientSize.Height + 60) { parts.RemoveAt(i); continue; }
                p.VX += p.GX * dt;
                p.VY += p.GY * dt;
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                if (p.Floor && p.GY > 0 && p.Y > floor - p.Size)
                {
                    p.Y = floor - p.Size;
                    if (p.Bug) { p.VY = 0; p.GY = 0; p.VX = (p.VX >= 0 ? 1 : -1) * p.Crawl; }
                    else { p.VY *= -0.3f; p.VX *= 0.6f; }
                }
            }
        }

        // ---------- desenho ----------

        SolidBrush Brush(Color c)
        {
            SolidBrush b;
            if (!brushes.TryGetValue(c, out b)) { b = new SolidBrush(c); brushes[c] = b; }
            return b;
        }

        protected override void OnPaint(PaintEventArgs e) { Draw(e.Graphics); }

        void Draw(Graphics g)
        {
            g.Clear(Color.Magenta);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;

            foreach (var w in workers)
            {
                if (!Visible_(w)) continue;
                var t = w.T;
                bool flapping = t.Ch.Flies && w.State == IDLE;
                bool r = (t.Upright ? Facing(w) : w.Dir) > 0;
                Bitmap[] set = w.State == WORK ? (r ? w.WorkR : w.WorkL)
                             : w.State == WALK || flapping ? (r ? w.WalkR : w.WalkL)
                             : (r ? w.IdleR : w.IdleL);
                int frame = w.State == JUMP ? 0 : w.Frame % set.Length;
                var bmp = set[frame];
                if (t.Upright)
                {
                    var tl = UprightTopLeft(w, bmp);
                    g.DrawImage(bmp, new Rectangle((int)tl.X, (int)tl.Y, bmp.Width, bmp.Height));
                    continue;
                }
                g.Transform = M;
                int x = (int)w.X - bmp.Width / 2;
                int y = thick - (int)Height_(w) - t.FeetOff;
                if (w.Squash > 0)
                {
                    // amassadinho ao aterrissar
                    int dh = Math.Max(t.Scale, bmp.Height / 10);
                    g.DrawImage(bmp, new Rectangle(x - dh / 2, y + dh, bmp.Width + dh, bmp.Height - dh));
                }
                else g.DrawImage(bmp, new Rectangle(x, y, bmp.Width, bmp.Height));
                if (w.Carrying && w.State == WALK && w.CarryR != null)
                {
                    var c = r ? w.CarryR : w.CarryL;
                    int bob = (frame % 2) * t.Scale;
                    int head = thick - (int)Height_(w) - VisH(w);
                    g.DrawImage(c, new Rectangle((int)w.X - c.Width / 2, head - c.Height - t.Scale + bob, c.Width, c.Height));
                }
                g.ResetTransform();
            }

            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            foreach (var p in parts)
            {
                if (p.Text != null) { g.DrawString(p.Text, p.Font, Brush(p.Color), p.X, p.Y); continue; }
                int s = p.Size + (p.Grow > 0 ? (int)(p.Grow * (1 - p.Life / p.MaxLife)) : 0);
                g.FillRectangle(Brush(p.Color), (int)p.X, (int)p.Y, s, s);
                if (p.Bug)
                {
                    // cabeça e perninhas do besouro
                    int sc = Math.Max(1, s / 2);
                    int hx = p.VX >= 0 ? (int)p.X + s : (int)p.X - sc;
                    g.FillRectangle(Brush(Color.FromArgb(33, 33, 33)), hx, (int)p.Y, sc, sc);
                    g.FillRectangle(Brush(Color.FromArgb(33, 33, 33)), (int)p.X + s / 2 - sc / 2, (int)p.Y, Math.Max(1, sc / 2), s);
                }
            }

            if (Bubbles)
                foreach (var w in workers)
                    if (w.Bubble != null && Visible_(w)) DrawBubble(g, w);
        }

        void DrawBubble(Graphics g, Worker w)
        {
            var size = TextRenderer.MeasureText(g, w.Bubble, bubbleFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            int pw = 7, ph = 3;
            int bw = size.Width + pw * 2, bh = size.Height + ph * 2;
            var sprite = SpriteOnScreen(w);
            bool below = zone == Zone.Top;   // no alto, o balão fica embaixo do bichinho
            int carry = w.Carrying && w.CarryR != null ? w.CarryR.Height + w.T.Scale : 0;
            int by = below ? Math.Min(ClientSize.Height - bh - 1, (int)sprite.Bottom + carry + 7)
                           : Math.Max(0, (int)sprite.Top - carry - bh - 7);
            float cx = sprite.Left + sprite.Width / 2;
            int bx = Math.Max(1, Math.Min(ClientSize.Width - bw - 1, (int)cx - bw / 2));

            var border = Brush(Color.FromArgb(40, 40, 48));
            g.FillRectangle(border, bx, by, bw, bh);
            g.FillRectangle(Brushes.White, bx + 1, by + 1, bw - 2, bh - 2);
            // rabinho apontando para o bichinho
            int tx = Math.Max(bx + 4, Math.Min(bx + bw - 10, (int)cx - 3));
            for (int i = 0; i < 5; i++)
            {
                int ww = 7 - i * 2 + 2;
                if (ww <= 0) break;
                int ty = below ? by - i : by + bh - 1 + i;
                g.FillRectangle(border, tx + i, ty, Math.Max(1, ww), 1);
                if (ww > 2) g.FillRectangle(Brushes.White, tx + i + 1, ty, ww - 2, 1);
            }
            TextRenderer.DrawText(g, w.Bubble, bubbleFont, new Point(bx + pw, by + ph), Color.FromArgb(34, 34, 40), Color.White, TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                foreach (var b in brushes.Values) b.Dispose();
                foreach (var b in bitmaps) b.Dispose();
                foreach (var t in troupes) t.SymbolFont.Dispose();
                bubbleFont.Dispose();
                M.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
