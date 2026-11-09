using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ScreenPets
{
    public static class Paths
    {
        public static string Exe { get { return Application.ExecutablePath; } }
        public static string Root { get { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Exe), "..")); } }
        public static string ConfigFile { get { return Path.Combine(Root, "config.json"); } }
        public static string CharactersDir { get { return Path.Combine(Root, "personagens"); } }
        public static string StateDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenPets"); } }
        public static string LogFile { get { return Path.Combine(StateDir, "log.txt"); } }

        public static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(StateDir);
                var fi = new FileInfo(LogFile);
                if (fi.Exists && fi.Length > 200000) fi.Delete();
                File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    public class Skin
    {
        public string Id;
        public string Name;
        public Dictionary<char, Color> Colors = new Dictionary<char, Color>();
        // Opcionais: desenho, efeito e carga próprios (outra raça, outro modelo...). Sem eles, valem os do personagem.
        public Dictionary<string, List<string[]>> Frames;
        public string Effect, Carry, Base;
        public int TopRow;
    }

    public class Character
    {
        // Os três tipos de bichinho e onde cada um fica por padrão.
        public const string Walker = "terrestre", Climber = "escalador", Flyer = "voador";
        public static readonly string[] Types = { Walker, Climber, Flyer };

        public string Id, Name, Type, Description, Effect, Carry, SourceFile;
        public Dictionary<string, List<string[]>> Frames = new Dictionary<string, List<string[]>>();
        public List<Skin> Skins = new List<Skin>();
        public List<string> Speech = new List<string>();
        public int Width, Height, TopRow, BottomRow;
        // Linha dos pés: o fim da pose parada (ou do andar). É o que encosta no chão (ou na parede).
        public int FeetRow;
        // Quantos pontos do desenho cabem em um "ponto" do tamanho padrão (1 = normal, 2 = 2×2, 3 = 3×3).
        public int Detail = 1;
        // Quem voa ("voo": altura em pontos do desenho acima do chão). 0 = anda.
        public int FlyHeight;
        // Ritmo próprio ("ritmo"): o caracol é devagar, o passarinho é rápido.
        public double Pace = 1;

        public bool Flies { get { return FlyHeight > 0; } }

        static bool Blank(char c) { return c == '.' || c == ' '; }

        static Dictionary<string, List<string[]>> ReadSprites(Dictionary<string, object> sprites)
        {
            var result = new Dictionary<string, List<string[]>>();
            foreach (var kv in sprites)
            {
                var frames = kv.Value as IEnumerable;
                if (frames == null) continue;
                var list = new List<string[]>();
                foreach (object f in frames)
                {
                    var rows = new List<string>();
                    var fr = f as IEnumerable;
                    if (fr == null || f is string) continue;
                    foreach (object r in fr) rows.Add(Convert.ToString(r));
                    if (rows.Count > 0) list.Add(rows.ToArray());
                }
                if (list.Count > 0) result[kv.Key] = list;
            }
            if (result.Count == 0) return result;
            if (!result.ContainsKey("andar")) throw new InvalidDataException("faltou \"sprites.andar\"");
            if (!result.ContainsKey("trabalhar")) result["trabalhar"] = result["andar"];
            return result;
        }

        public static Character Load(string path)
        {
            return Load(Json.LoadObject(path), path);
        }

        static Character Load(Dictionary<string, object> d, string path)
        {
            var c = new Character();
            c.SourceFile = path;
            c.Id = Json.Str(d, "id", Path.GetFileNameWithoutExtension(path));
            c.Name = Json.Str(d, "nome", c.Id);
            c.Description = Json.Str(d, "descricao", "");
            c.Effect = Json.Str(d, "efeito", "");
            c.Carry = Json.Str(d, "carga", "");
            c.Detail = Math.Max(1, Math.Min(8, (int)Json.Num(d, "detalhe", 1)));
            c.FlyHeight = Math.Max(0, Math.Min(64, (int)Json.Num(d, "voo", 0)));
            c.Pace = Math.Max(0.1, Math.Min(4, Json.Num(d, "ritmo", 1)));
            c.Type = Json.Str(d, "tipo", c.FlyHeight > 0 ? Flyer : Walker);
            if (!Types.Contains(c.Type)) c.Type = Walker;
            c.Speech = Json.Strs(d, "falas") ?? new List<string>();
            var fx = Json.Obj(d, "efeitosTrabalho");
            if (fx != null) foreach (var kv in fx) c.WorkEffects[kv.Key] = Convert.ToString(kv.Value);

            var sprites = Json.Obj(d, "sprites");
            if (sprites != null) c.Frames = ReadSprites(sprites);

            var skins = Json.Arr(d, "skins");
            if (skins != null)
            {
                foreach (object so in skins)
                {
                    var sd = so as Dictionary<string, object>;
                    if (sd == null) continue;
                    var s = new Skin();
                    s.Id = Json.Str(sd, "id", "skin" + c.Skins.Count);
                    s.Name = Json.Str(sd, "nome", s.Id);
                    s.Effect = Json.Str(sd, "efeito", null);
                    s.Carry = Json.Str(sd, "carga", null);
                    s.Base = Json.Str(sd, "base", null);
                    var cores = Json.Obj(sd, "cores");
                    if (cores != null)
                        foreach (var kv in cores)
                            if (kv.Key.Length > 0) s.Colors[kv.Key[0]] = PixelArt.ParseColor(Convert.ToString(kv.Value));
                    var own = Json.Obj(sd, "sprites");
                    if (own != null) { s.Frames = ReadSprites(own); if (s.Frames.Count == 0) s.Frames = null; }
                    c.Skins.Add(s);
                }
            }
            if (c.Skins.Count == 0) throw new InvalidDataException("precisa de pelo menos uma skin");

            // "base": a skin reaproveita desenho, cores, efeito e carga de outra, e só troca o que definir.
            foreach (var s in c.Skins)
            {
                var seen = new HashSet<Skin> { s };
                string b = s.Base;
                while (!string.IsNullOrEmpty(b))
                {
                    var bs = c.Skins.FirstOrDefault(x => x.Id == b);
                    if (bs == null || !seen.Add(bs)) break;
                    if (s.Frames == null) s.Frames = bs.Frames;
                    foreach (var kv in bs.Colors) if (!s.Colors.ContainsKey(kv.Key)) s.Colors[kv.Key] = kv.Value;
                    if (s.Effect == null) s.Effect = bs.Effect;
                    if (s.Carry == null) s.Carry = bs.Carry;
                    b = bs.Base;
                }
            }
            if (c.Frames.Count == 0)
            {
                var first = c.Skins.FirstOrDefault(s => s.Frames != null);
                if (first == null) throw new InvalidDataException("faltou \"sprites\"");
                c.Frames = first.Frames;
            }
            foreach (var s in c.Skins) if (s.Frames == null) s.Frames = c.Frames;

            // Normaliza: todos os quadros (de todas as skins) com a mesma largura/altura, alinhados por baixo.
            var sets = c.Skins.Select(s => s.Frames).Concat(new[] { c.Frames }).Distinct().ToList();
            int w = 0, h = 0;
            foreach (var set in sets)
                foreach (var l in set.Values)
                    foreach (var fr in l) { h = Math.Max(h, fr.Length); foreach (var r in fr) w = Math.Max(w, r.Length); }
            c.Width = w; c.Height = h;
            c.TopRow = h; c.BottomRow = 0;
            var tops = new Dictionary<Dictionary<string, List<string[]>>, int>();
            foreach (var set in sets)
            {
                int top = h;
                foreach (var key in set.Keys.ToList())
                {
                    var norm = new List<string[]>();
                    foreach (var fr in set[key])
                    {
                        var rows = new string[h];
                        int pad = h - fr.Length;
                        for (int y = 0; y < h; y++)
                        {
                            string r = y < pad ? "" : fr[y - pad];
                            rows[y] = r.PadRight(w, '.');
                            if (rows[y].Any(ch => !Blank(ch)))
                            {
                                top = Math.Min(top, y);
                                c.BottomRow = Math.Max(c.BottomRow, y);
                            }
                        }
                        norm.Add(rows);
                    }
                    set[key] = norm;
                }
                tops[set] = top;
                c.TopRow = Math.Min(c.TopRow, top);
            }
            if (c.TopRow > c.BottomRow) throw new InvalidDataException("os sprites estão vazios");
            foreach (var s in c.Skins) s.TopRow = Math.Min(tops[s.Frames], c.BottomRow);
            c.FeetRow = c.BottomRow;
            List<string[]> pose;
            if (c.Frames.TryGetValue("parado", out pose) || c.Frames.TryGetValue("andar", out pose))
            {
                int feet = -1;
                foreach (var fr in pose)
                    for (int y = fr.Length - 1; y > feet; y--)
                        if (fr[y].Any(ch => !Blank(ch))) { feet = y; break; }
                if (feet >= c.TopRow) c.FeetRow = feet;
            }

            // Skins sem "base" herdam da primeira as cores que não definirem.
            for (int i = 1; i < c.Skins.Count; i++)
                if (string.IsNullOrEmpty(c.Skins[i].Base))
                    foreach (var kv in c.Skins[0].Colors)
                        if (!c.Skins[i].Colors.ContainsKey(kv.Key)) c.Skins[i].Colors[kv.Key] = kv.Value;
            return c;
        }

        // Personagens embutidos no .exe (usados pelo instalador antes de existir a pasta personagens/).
        public const string ResourcePrefix = "personagens/";

        public static IEnumerable<KeyValuePair<string, string>> EmbeddedFiles()
        {
            var asm = typeof(Character).Assembly;
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix)).OrderBy(n => n))
                using (var r = new StreamReader(asm.GetManifestResourceStream(name), Encoding.UTF8))
                    yield return new KeyValuePair<string, string>(name.Substring(ResourcePrefix.Length), r.ReadToEnd());
        }

        public static List<Character> LoadEmbedded(List<string> errors)
        {
            var list = new List<Character>();
            foreach (var f in EmbeddedFiles())
            {
                try { list.Add(Load(Json.Parse(f.Value) as Dictionary<string, object>, f.Key)); }
                catch (Exception ex) { if (errors != null) errors.Add(f.Key + ": " + ex.Message); }
            }
            return Sort(list);
        }

        public static List<Character> LoadAll(List<string> errors)
        {
            if (!Directory.Exists(Paths.CharactersDir)) return LoadEmbedded(errors);
            var list = new List<Character>();
            foreach (var f in Directory.GetFiles(Paths.CharactersDir, "*.json").OrderBy(x => x))
            {
                try { list.Add(Load(f)); }
                catch (Exception ex) { if (errors != null) errors.Add(Path.GetFileName(f) + ": " + ex.Message); }
            }
            return Sort(list);
        }

        // Terrestres, escaladores e voadores, nesta ordem.
        static readonly string[] Order = {
            "gatos", "cachorros", "pinguins", "formigas", "coelhos",
            "lagartixa", "joaninha", "caracol", "aranha", "besouro",
            "passarinho", "borboleta", "abelha", "morcego", "dragao" };

        static List<Character> Sort(List<Character> list)
        {
            return list.OrderBy(c => Array.IndexOf(Order, c.Id) < 0 ? 99 : Array.IndexOf(Order, c.Id)).ThenBy(c => c.Name).ToList();
        }

        public static Character Find(List<Character> all, string id)
        {
            foreach (var c in all) if (c.Id == id) return c;
            return null;
        }

        public override string ToString() { return Name; }

        // Pixels de tela por ponto do desenho, a partir da escala padrão (tamanho × DPI).
        public int PixelScale(int unit) { return Math.Max(1, (int)Math.Round(unit / (double)Detail)); }

        public Skin GetSkin(string id)
        {
            foreach (var s in Skins) if (s.Id == id) return s;
            return Skins[0];
        }

        public string EffectOf(Skin s) { return s != null && s.Effect != null ? s.Effect : Effect; }

        // Efeito de um tipo de trabalho ("efeitosTrabalho": { "trabalhar-cavar": "terra" }); sem ele, o da skin/personagem.
        public Dictionary<string, string> WorkEffects = new Dictionary<string, string>();
        public string EffectOf(Skin s, string kind)
        {
            string e;
            return WorkEffects.TryGetValue(kind, out e) ? e : EffectOf(s);
        }

        // Tipos de trabalho: "trabalhar" e quaisquer "trabalhar-algo" (um é sorteado cada vez que ele vai trabalhar).
        public List<string> WorkKinds(Skin s)
        {
            var set = s.Frames ?? Frames;
            return set.Keys.Where(k => k == "trabalhar" || k.StartsWith("trabalhar-")).OrderBy(k => k == "trabalhar" ? "" : k).ToList();
        }
        public string CarryOf(Skin s) { return s != null && s.Carry != null ? s.Carry : Carry; }

        // Tipos de quadro: "andar", "trabalhar" e o opcional "parado" (sem ele, usa o primeiro de "andar").
        // Um "trabalhar-algo" que o personagem não tem vira o "trabalhar" dele.
        public Bitmap[] Render(Skin skin, string kind, int scale, bool flip)
        {
            List<string[]> frames;
            var set = skin.Frames ?? Frames;
            if (!set.TryGetValue(kind, out frames) && !(kind.StartsWith("trabalhar-") && set.TryGetValue("trabalhar", out frames)))
                frames = kind == "parado" ? set["andar"].Take(1).ToList() : set["andar"];
            return frames.Select(f => PixelArt.Render(f, skin.Colors, scale, flip)).ToArray();
        }

        public static string TypeName(string type)
        {
            return type == Climber ? "Escalador" : type == Flyer ? "Voador" : "Terrestre";
        }

        public static string DefaultPlace(string type)
        {
            return type == Climber ? Group.Sides : type == Flyer ? Group.Top : Group.Ground;
        }

        // Onde cada tipo pode ficar: terrestres só na barra de tarefas; escaladores e voadores
        // dividem o alto e as laterais (um escalador pode ir para o teto, um voador para as laterais).
        public static bool Allows(string type, string place)
        {
            return type == Climber || type == Flyer ? place != Group.Ground : place == Group.Ground;
        }

        // O lugar pedido, se o tipo puder ficar lá; senão, o lugar natural dele.
        public static string PlaceFor(string type, string place)
        {
            return Allows(type, place) ? place : DefaultPlace(type);
        }
    }

    // Uma "turma": um tipo de bichinho, quantos, onde e de que tamanho.
    public class Group
    {
        public const string Ground = "chao", Top = "topo", Sides = "laterais", Left = "esquerda", Right = "direita";
        public static readonly string[] Places = { Ground, Top, Sides, Left, Right };

        public string Character = "gatos";
        public string Skin = "*";      // "*" = misturar as cores
        public string Place = Ground;
        public int Count = 2;
        public int Size = 2;           // pixels de tela por ponto do desenho (antes do ajuste de DPI)

        public static string PlaceName(string place)
        {
            switch (place)
            {
                case Top: return "No alto da tela";
                case Sides: return "Nas duas laterais";
                case Left: return "Na lateral esquerda";
                case Right: return "Na lateral direita";
                default: return "Em cima da barra de tarefas";
            }
        }

        public static string SizeName(int size)
        {
            switch (size)
            {
                case 1: return "Bem pequeno";
                case 2: return "Pequeno";
                case 3: return "Médio";
                case 4: return "Grande";
                default: return "Enorme";
            }
        }

        public Group Clone() { return (Group)MemberwiseClone(); }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            d["personagem"] = Character;
            d["cor"] = Skin;
            d["lugar"] = Place;
            d["quantidade"] = Count;
            d["tamanho"] = Size;
            return d;
        }

        public static Group FromJson(Dictionary<string, object> d)
        {
            var g = new Group();
            g.Character = Json.Str(d, "personagem", g.Character);
            g.Skin = Json.Str(d, "cor", g.Skin);
            g.Place = Json.Str(d, "lugar", g.Place);
            if (!Places.Contains(g.Place)) g.Place = Ground;
            g.Count = Math.Max(1, Math.Min(Config.MaxCount, (int)Json.Num(d, "quantidade", g.Count)));
            g.Size = Math.Max(1, Math.Min(Config.MaxSize, (int)Json.Num(d, "tamanho", g.Size)));
            return g;
        }
    }

    public class Config
    {
        public const int MaxCount = 10, MaxSize = 5;

        public bool Enabled = true;            // os bichinhos estão na tela
        public bool Bubbles = true;            // falas em balõezinhos
        public bool HideFullscreen = true;     // somem em vídeos, jogos e apresentações em tela cheia
        public double Speed = 1.0;
        public List<Group> Groups = Starter();

        // A turma de quem acabou de instalar: um de cada tipo, para mostrar o que dá para fazer.
        public static List<Group> Starter()
        {
            return new List<Group> {
                new Group { Character = "gatos", Place = Group.Ground, Count = 2 },
                new Group { Character = "lagartixa", Place = Group.Right, Count = 1 },
                new Group { Character = "borboleta", Place = Group.Top, Count = 1 },
            };
        }

        public static Config Load() { return Load(Paths.ConfigFile); }

        public static Config Load(string path)
        {
            var c = new Config();
            Dictionary<string, object> d;
            try { d = Json.LoadObject(path); }
            catch (Exception ex) { Paths.Log("config inválida: " + ex.Message); return c; }
            c.Enabled = Json.Bool(d, "ativo", c.Enabled);
            c.Bubbles = Json.Bool(d, "baloes", c.Bubbles);
            c.HideFullscreen = Json.Bool(d, "esconderTelaCheia", c.HideFullscreen);
            c.Speed = Math.Max(0.3, Math.Min(3, Json.Num(d, "velocidade", c.Speed)));
            var groups = Json.Arr(d, "turmas");
            if (groups != null)
                c.Groups = groups.OfType<Dictionary<string, object>>().Select(Group.FromJson).ToList();
            return c;
        }

        public void Save() { Save(Paths.ConfigFile); }

        public void Save(string path)
        {
            var d = new Dictionary<string, object>();
            d["ativo"] = Enabled;
            d["baloes"] = Bubbles;
            d["esconderTelaCheia"] = HideFullscreen;
            d["velocidade"] = Speed;
            d["turmas"] = Groups.Select(g => g.ToJson()).ToList();
            Json.Save(path, d);
        }
    }

    public static class PixelArt
    {
        public static Color ParseColor(string html)
        {
            Color c;
            try { c = ColorTranslator.FromHtml(html); }
            catch { c = Color.Gray; }
            return Safe(c);
        }

        // O magenta puro é a cor "transparente" da janela; nenhum pixel pode usá-lo.
        public static Color Safe(Color c)
        {
            if (c.R == 255 && c.G == 0 && c.B == 255) return Color.FromArgb(254, 0, 254);
            return Color.FromArgb(255, c.R, c.G, c.B);
        }

        public static Bitmap Render(string[] rows, Dictionary<char, Color> colors, int scale, bool flip)
        {
            int w = rows.Max(r => r.Length), h = rows.Length;
            var bmp = new Bitmap(Math.Max(1, w * scale), Math.Max(1, h * scale), PixelFormat.Format32bppArgb);
            var brushes = new Dictionary<char, SolidBrush>();
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < rows[y].Length; x++)
                    {
                        char ch = rows[y][x];
                        if (ch == '.' || ch == ' ') continue;
                        SolidBrush b;
                        if (!brushes.TryGetValue(ch, out b))
                        {
                            Color col;
                            if (!colors.TryGetValue(ch, out col)) col = Color.Gray;
                            b = new SolidBrush(Safe(col));
                            brushes[ch] = b;
                        }
                        int dx = flip ? (w - 1 - x) : x;
                        g.FillRectangle(b, dx * scale, y * scale, scale, scale);
                    }
                }
            }
            foreach (var b in brushes.Values) b.Dispose();
            return bmp;
        }
    }

    // Objetos que os bichinhos carregam na cabeça.
    public class Prop
    {
        public string[] Rows;
        public Dictionary<char, Color> Colors = new Dictionary<char, Color>();

        static Prop Make(string[] rows, params string[] colors)
        {
            var p = new Prop();
            p.Rows = rows;
            foreach (var c in colors) p.Colors[c[0]] = PixelArt.ParseColor(c.Substring(2));
            return p;
        }

        public static Prop Get(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "chocolate":
                    return Make(new[] { "FFFPPPPP", "BLBLBPPP", "BBBBBPPP" }, "F=#d9d9d9", "P=#7b1fa2", "B=#5b3416", "L=#7d4a22");
                case "caixa":
                    return Make(new[] { "KKKKKKKK", "KCCTTCCK", "KCCTTCCK", "KCCCCCCK", "KKKKKKKK" }, "K=#6d4c2f", "C=#d2a86e", "T=#b98a4e");
                case "peixe":
                    return Make(new[] { "B.BBBB..", "BBBBBKBB", "B.BBBB.." }, "B=#64b5f6", "K=#0d47a1");
                case "acucar":
                    return Make(new[] { "WWWW", "WWWS", "WWWS", "WSSS" }, "W=#ffffff", "S=#cfd8dc");
                case "engrenagem":
                    return Make(new[] { "..G.G..", ".GGGGG.", "GGG.GGG", ".GGGGG.", "..G.G.." }, "G=#90a4ae");
                case "gema":
                    return Make(new[] { ".CCC.", "CCWCC", ".CCC.", "..C.." }, "C=#26c6da", "W=#e0f7fa");
                case "folha":
                    return Make(new[] { ".GGG.", "GGJGG", ".GGG." }, "G=#43a047", "J=#2e7d32");
                case "claude":
                    return Make(new[] { "...O...", ".O.O.O.", "..OOO..", "OOOCOOO", "..OOO..", ".O.O.O.", "...O..." }, "O=#d97757", "C=#b85f42");
                case "terminal":
                    return Make(new[] { "KKKKKKKKK", "KGKKKKKKK", "KKGKKKKKK", "KGKKGGGKK", "KKKKKKKKK" }, "K=#262624", "G=#f0eee6");
                case "cafe":
                    return Make(new[] { ".s..s..", "..s..s.", "RRRRR..", "RWRRRRR", "RWRRR.R", "RRRRRRR", ".RRR..." }, "R=#e53935", "W=#ffcdd2", "s=#cfd8dc");
                case "osso":
                    return Make(new[] { "WW....WW", "WWWWWWWW", "WW....WW" }, "W=#f3ead3");
                case "disquete":
                    return Make(new[] { "KKKKKK", "KSSSKK", "KSSSKK", "KKKKKK", "KWWWWK", "KWWWWK" }, "K=#1e3a8a", "S=#b0bec5", "W=#eceff1");
                case "bola":
                    return Make(new[] { ".YYY.", "WYYYY", "YWWYY", "YYYWW", ".YYY." }, "Y=#c6e03a", "W=#f5f5f5");
                case "pizza":
                    return Make(new[] { "OOOOOOO", ".YRYYY.", "..YYR..", "...Y..." }, "O=#c68642", "Y=#ffd54f", "R=#d32f2f");
                case "cenoura":
                    return Make(new[] { "GOOOO...", "GGOOOOOO", "GOOOO..." }, "O=#ff9800", "G=#43a047");
                case "flor":
                    return Make(new[] { ".P.P.", "PPYPP", ".PPP.", "..G..", ".GG.." }, "P=#ec407a", "Y=#ffeb3b", "G=#43a047");
                default:
                    return null;
            }
        }
    }
}
