// Motor de animação do site: lê os mesmos JSON do programa (window.PERSONAGENS, gerado por
// ferramentas/gerar_site.py) e faz as turmas passearem pelas bordas de uma "tela" num <canvas>.
// Espelha o que o ScreenPets.exe faz (src/Stage.cs): cada borda é uma faixa "deitada", com o chão
// embaixo, que uma matriz leva para a barra de tarefas, o teto ou as laterais.
(function () {
  "use strict";

  const BLANK = new Set([".", " "]);

  // Objetos que eles carregam na cabeça (iguais aos de src/Data.cs).
  const PROPS = {
    chocolate: [["FFFPPPPP", "BLBLBPPP", "BBBBBPPP"], { F: "#d9d9d9", P: "#7b1fa2", B: "#5b3416", L: "#7d4a22" }],
    caixa: [["KKKKKKKK", "KCCTTCCK", "KCCTTCCK", "KCCCCCCK", "KKKKKKKK"], { K: "#6d4c2f", C: "#d2a86e", T: "#b98a4e" }],
    peixe: [["B.BBBB..", "BBBBBKBB", "B.BBBB.."], { B: "#64b5f6", K: "#0d47a1" }],
    acucar: [["WWWW", "WWWS", "WWWS", "WSSS"], { W: "#ffffff", S: "#cfd8dc" }],
    engrenagem: [["..G.G..", ".GGGGG.", "GGG.GGG", ".GGGGG.", "..G.G.."], { G: "#90a4ae" }],
    gema: [[".CCC.", "CCWCC", ".CCC.", "..C.."], { C: "#26c6da", W: "#e0f7fa" }],
    folha: [[".GGG.", "GGJGG", ".GGG."], { G: "#43a047", J: "#2e7d32" }],
    claude: [["...O...", ".O.O.O.", "..OOO..", "OOOCOOO", "..OOO..", ".O.O.O.", "...O..."], { O: "#d97757", C: "#b85f42" }],
    terminal: [["KKKKKKKKK", "KGKKKKKKK", "KKGKKKKKK", "KGKKGGGKK", "KKKKKKKKK"], { K: "#262624", G: "#f0eee6" }],
    cafe: [[".s..s..", "..s..s.", "RRRRR..", "RWRRRRR", "RWRRR.R", "RRRRRRR", ".RRR..."], { R: "#e53935", W: "#ffcdd2", s: "#cfd8dc" }],
    osso: [["WW....WW", "WWWWWWWW", "WW....WW"], { W: "#f3ead3" }],
    disquete: [["KKKKKK", "KSSSKK", "KSSSKK", "KKKKKK", "KWWWWK", "KWWWWK"], { K: "#1e3a8a", S: "#b0bec5", W: "#eceff1" }],
    bola: [[".YYY.", "WYYYY", "YWWYY", "YYYWW", ".YYY."], { Y: "#c6e03a", W: "#f5f5f5" }],
    pizza: [["OOOOOOO", ".YRYYY.", "..YYR..", "...Y..."], { O: "#c68642", Y: "#ffd54f", R: "#d32f2f" }],
    cenoura: [["GOOOO...", "GGOOOOOO", "GOOOO..."], { O: "#ff9800", G: "#43a047" }],
    flor: [[".P.P.", "PPYPP", ".PPP.", "..G..", ".GG.."], { P: "#ec407a", Y: "#ffeb3b", G: "#43a047" }],
  };

  // ---------- dados ----------

  // Normaliza um personagem como o programa faz: skins com desenho próprio ou herdado ("base"),
  // todos os quadros com a mesma largura/altura, alinhados por baixo, e a linha dos pés.
  function prepare(ch) {
    if (ch._ready) return ch;
    const byId = {};
    ch.skins.forEach((s) => (byId[s.id] = s));
    const first = ch.skins[0];
    ch.skins.forEach((s) => {
      let sprites = s.sprites || null, colors = Object.assign({}, s.cores || {});
      let efeito = s.efeito, carga = s.carga, b = s.base, seen = new Set([s.id]);
      while (b && byId[b] && !seen.has(b)) {
        const bs = byId[b]; seen.add(b);
        if (!sprites && bs.sprites) sprites = bs.sprites;
        for (const k in bs.cores || {}) if (!(k in colors)) colors[k] = bs.cores[k];
        if (efeito == null) efeito = bs.efeito;
        if (carga == null) carga = bs.carga;
        b = bs.base;
      }
      if (!s.base && s !== first) for (const k in first.cores || {}) if (!(k in colors)) colors[k] = first.cores[k];
      s._sprites = sprites || ch.sprites || ch.skins.find((x) => x.sprites).sprites;
      s._colors = colors;
      s._efeito = efeito != null ? efeito : ch.efeito;
      s._carga = carga != null ? carga : ch.carga;
    });
    let w = 0, h = 0;
    const sets = [...new Set(ch.skins.map((s) => s._sprites).concat(ch.sprites ? [ch.sprites] : []))];
    sets.forEach((set) => Object.values(set).forEach((fs) => fs.forEach((f) => {
      h = Math.max(h, f.length); f.forEach((r) => (w = Math.max(w, r.length)));
    })));
    let top = h, bottom = 0;
    sets.forEach((set) => {
      let t = h;
      for (const k in set) {
        if (k.startsWith("_")) continue;
        set[k] = set[k].map((f) => {
          const rows = [];
          for (let y = 0; y < h; y++) {
            const r = y < h - f.length ? "" : f[y - (h - f.length)];
            rows.push(r.padEnd(w, "."));
            if ([...rows[y]].some((c) => !BLANK.has(c))) { t = Math.min(t, y); bottom = Math.max(bottom, y); }
          }
          return rows;
        });
      }
      set._top = t; top = Math.min(top, t);
    });
    // pés: o fim da pose parada (ou do andar), que é o que encosta no chão ou na parede
    const base = ch.sprites || first._sprites;
    const pose = base.parado || base.andar;
    let feet = -1;
    pose.forEach((f) => { for (let y = f.length - 1; y > feet; y--) if ([...f[y]].some((c) => !BLANK.has(c))) { feet = y; break; } });
    ch._w = w; ch._h = h; ch._top = top; ch._bottom = bottom;
    ch._feet = feet >= top ? feet : bottom;
    ch._flies = (ch.voo || 0) > 0;
    ch._ready = true;
    return ch;
  }

  function workKinds(set) {
    const k = Object.keys(set).filter((x) => x === "trabalhar" || x.startsWith("trabalhar-"));
    return k.length ? k.sort() : ["andar"];
  }

  // Pixels de tela por ponto do desenho, como PixelScale no programa.
  function pixelScale(ch, unit) { return Math.max(1, Math.round(unit / (ch.detalhe || 1))); }

  function renderRows(rows, colors, scale, flip) {
    const w = rows[0].length, h = rows.length;
    const c = document.createElement("canvas");
    c.width = w * scale; c.height = h * scale;
    const g = c.getContext("2d");
    for (let y = 0; y < h; y++) {
      const r = rows[y];
      for (let x = 0; x < r.length; x++) {
        const ch = r[x];
        if (BLANK.has(ch)) continue;
        g.fillStyle = colors[ch] || "#888";
        g.fillRect((flip ? w - 1 - x : x) * scale, y * scale, scale, scale);
      }
    }
    return c;
  }

  const cache = new Map();
  function frames(ch, skin, kind, scale, flip) {
    prepare(ch);
    const key = ch.id + "|" + skin.id + "|" + kind + "|" + scale + "|" + flip;
    if (cache.has(key)) return cache.get(key);
    const set = skin._sprites;
    let list = set[kind];
    if (!list && kind.startsWith("trabalhar")) list = set.trabalhar;
    if (!list) list = kind === "parado" ? set.andar.slice(0, 1) : set.andar;
    const out = list.map((f) => renderRows(f, skin._colors, scale, flip));
    cache.set(key, out);
    return out;
  }

  function prop(name, scale, flip) {
    const p = PROPS[name];
    if (!p) return null;
    const key = "prop|" + name + "|" + scale + "|" + flip;
    if (!cache.has(key)) {
      const w = Math.max(...p[0].map((r) => r.length));
      cache.set(key, renderRows(p[0].map((r) => r.padEnd(w, ".")), p[1], scale, flip));
    }
    return cache.get(key);
  }

  const pick = (a) => a[Math.floor(Math.random() * a.length)];
  const R = (a, b) => a + Math.random() * (b - a);

  // ---------- a tela: turmas nas quatro bordas ----------

  const WALK = 0, WORK = 1, IDLE = 2, JUMP = 3;
  const ZONES = ["chao", "topo", "esquerda", "direita"];

  // Onde cada tipo pode ficar (como Character.Allows): terrestres só na barra de tarefas;
  // escaladores e voadores dividem o alto e as laterais.
  const LUGAR_PADRAO = { terrestre: "chao", escalador: "laterais", voador: "topo" };
  const pode = (tipo, lugar) => (tipo === "escalador" || tipo === "voador" ? lugar !== "chao" : lugar === "chao");
  const lugarPara = (tipo, lugar) => (pode(tipo, lugar) ? lugar : LUGAR_PADRAO[tipo] || "chao");

  // Quem vai em cada borda. "laterais" divide a turma entre a direita e a esquerda (como Host.CastsByZone).
  function castsByZone(turmas, chars) {
    const out = [];
    (turmas || []).forEach((g) => {
      const ch = chars.find((c) => c.id === g.personagem);
      if (!ch) return;
      const n = Math.max(0, g.quantidade | 0);
      const lugar = lugarPara(ch.tipo, g.lugar);
      if (lugar === "laterais") {
        out.push({ zone: "direita", ch, g, n: Math.ceil(n / 2) });
        out.push({ zone: "esquerda", ch, g, n: Math.floor(n / 2) });
      } else out.push({ zone: ZONES.includes(lugar) ? lugar : "chao", ch, g, n });
    });
    return out.filter((c) => c.n > 0);
  }

  class Screen {
    // opts: { chars, turmas, taskbar (altura da barra em px), baloes, velocidade, unitK (multiplica o tamanho) }
    constructor(canvas, opts) {
      this.canvas = canvas;
      this.ctx = canvas.getContext("2d");
      this.opts = Object.assign({ taskbar: 0, baloes: true, velocidade: 1, unitK: 1, bubbleEvery: [6, 12] }, opts);
      this.workers = []; this.parts = [];
      this.nextBubble = 3;
      this.resize();
      this.setTurmas(this.opts.turmas || []);
      this.last = performance.now();
      this._loop = this._loop.bind(this);
      requestAnimationFrame(this._loop);
      new ResizeObserver(() => this.resize()).observe(canvas);
    }

    resize() {
      const r = this.canvas.getBoundingClientRect();
      if (!r.width || !r.height) return;
      this.dpr = window.devicePixelRatio || 1;
      this.W = r.width; this.H = r.height;
      this.canvas.width = Math.round(r.width * this.dpr);
      this.canvas.height = Math.round(r.height * this.dpr);
      // área de trabalho: a tela sem a barra de tarefas. Em cada borda, o chão do espaço deitado (y = 0)
      // encosta na borda e "para cima" (y negativo) aponta para dentro da tela.
      const wa = this.H - this.opts.taskbar;
      this.wa = wa;
      this.zones = {
        chao: { L: this.W, M: [1, 0, 0, 1, 0, wa], horiz: true },
        topo: { L: this.W, M: [1, 0, 0, -1, 0, 0], horiz: true },
        esquerda: { L: wa, M: [0, -1, -1, 0, 0, wa], horiz: false },
        direita: { L: wa, M: [0, -1, 1, 0, this.W, wa], horiz: false },
      };
      for (const w of this.workers) {
        if (isNaN(w.x)) continue;
        const L = this.zones[w.zone].L;
        w.x = Math.max(-w.spriteW, Math.min(L + w.spriteW, w.x));
        if (w.target > L) w.target = this.randomTarget(w);
      }
    }

    setOptions(o) { Object.assign(this.opts, o); if ("taskbar" in o) this.resize(); }

    setTurmas(turmas) {
      this.opts.turmas = turmas;
      this.workers = []; this.parts = [];
      let index = 0;
      castsByZone(turmas, this.opts.chars).forEach(({ zone, ch, g, n }) => {
        prepare(ch);
        const scale = Math.max(1, Math.round(Math.max(1, Math.min(5, g.tamanho || 2)) * this.opts.unitK));
        const px = pixelScale(ch, scale);
        const upright = ch._flies && zone !== "chao";
        const visH = (ch._feet - ch._top + 1) * px;
        const order = [...ch.skins].sort(() => Math.random() - 0.5);
        for (let i = 0; i < n; i++, index++) {
          const skin = !g.cor || g.cor === "*" ? order[i % order.length] : (ch.skins.find((s) => s.id === g.cor) || ch.skins[0]);
          const w = {
            zone, ch, skin, scale, px, upright, spriteW: ch._w * px, feetOff: (ch._feet + 1) * px, topRow: skin._sprites._top,
            flyBase: !ch._flies ? 0 : upright ? 2 * scale : ch.voo * px, flyRange: ch._flies ? visH : 0, gravity: 900 * scale / 3,
            x: NaN, target: 0, timer: 0, ft: 0, frame: 0, dir: index % 2 ? -1 : 1, state: WALK,
            delay: index * 0.7 + Math.random() * 0.5, speedMul: R(0.8, 1.2), y: 0, vy: 0, squash: 0,
            meet: 2, bubble: null, bubbleT: 0, carrying: false, idleHold: 0.8, looked: false,
            works: workKinds(skin._sprites), work: null, effect: skin._efeito, phase: Math.random() * 6.28,
            canCarry: !upright && !!PROPS[skin._carga],
          };
          w.alt = w.altTarget = Math.random() * w.flyRange;
          this.pickWork(w);
          this.workers.push(w);
        }
      });
    }

    pickWork(w) {
      w.work = pick(w.works);
      const fx = w.ch.efeitosTrabalho && w.ch.efeitosTrabalho[w.work];
      w.effect = fx || w.skin._efeito;
    }

    randomTarget(w) {
      const m = w.spriteW * 0.75, L = this.zones[w.zone].L;
      return m + Math.random() * Math.max(1, L - 2 * m);
    }

    launch(w, h) {
      if (w.y > 0) return;
      w.vy = Math.sqrt(2 * w.gravity * h * w.scale);
      w.y = 0.01;
    }

    decide(w) {
      const r = Math.random(), flies = w.ch._flies;
      w.frame = 0; w.ft = 0; w.looked = false;
      if (r < (flies ? 0.3 : 0.42)) {
        w.state = WORK; this.pickWork(w); w.timer = R(2.5, 6.5);
        if (w.carrying) { w.carrying = false; this.spawn("poeira", w, 3); }
      } else if (r < 0.8) {
        w.state = WALK; w.target = this.randomTarget(w); w.altTarget = Math.random() * w.flyRange;
        w.carrying = w.canCarry && Math.random() < 0.45;
      } else if (r < 0.88 && w.y <= 0 && !flies) {
        w.state = JUMP; this.launch(w, R(5, 8));
      } else {
        w.state = IDLE; w.timer = R(0.9, 3.1); if (Math.random() < 0.5) w.dir = -w.dir;
      }
    }

    update(dt) {
      const vel = this.opts.velocidade;
      for (const w of this.workers) {
        if (w.delay > 0) { w.delay -= dt; continue; }
        if (isNaN(w.x)) {
          w.x = w.dir > 0 ? -w.spriteW : this.zones[w.zone].L + w.spriteW;
          w.target = this.randomTarget(w); w.state = WALK;
        }
        if (w.bubbleT > 0 && (w.bubbleT -= dt) <= 0) w.bubble = null;
        if (w.squash > 0) w.squash -= dt;
        if (w.meet > 0) w.meet -= dt;
        w.alt += (w.altTarget - w.alt) * Math.min(1, dt * 0.8);
        w.ft += dt;
        if (w.y > 0) {
          w.vy -= w.gravity * dt; w.y += w.vy * dt;
          if (w.y <= 0) {
            w.y = 0; w.vy = 0; w.squash = 0.12;
            if (!w.ch._flies) this.spawn("poeira", w, 2);
            if (w.state === JUMP) this.decide(w);
          }
        }
        const pace = vel * (w.ch.ritmo || 1);
        const walkN = w.skin._sprites.andar.length;
        const step = (walkN >= 8 ? 0.095 : walkN >= 4 ? 0.13 : 0.18) / (vel * Math.max(0.5, w.ch.ritmo || 1));
        switch (w.state) {
          case WALK: {
            const dx = w.target - w.x, sp = 34 * w.scale / 3 * pace * w.speedMul;
            if (Math.abs(dx) <= sp * dt) { w.x = w.target; this.decide(w); break; }
            w.dir = dx > 0 ? 1 : -1; w.x += w.dir * sp * dt;
            if (w.ft > step) { w.ft = 0; w.frame++; }
            break;
          }
          case WORK: {
            w.timer -= dt;
            const sp = w.skin._sprites, n = (sp[w.work] || sp.trabalhar || sp.andar).length;
            if (w.ft > (n >= 4 ? 0.11 : 0.3) / Math.max(0.5, vel)) {
              w.ft = 0; w.frame = (w.frame + 1) % n;
              if (w.frame === n - 1) this.spawn(w.effect, w, 0);
            }
            if (w.timer <= 0) this.decide(w);
            break;
          }
          case JUMP: break;
          default:
            w.timer -= dt;
            if (w.ch._flies) { if (w.ft > step * 1.3) { w.ft = 0; w.frame++; } }   // batendo as asas parado no ar
            else this.animateIdle(w);
            if (!w.looked && w.timer < 0.6 && Math.random() < 0.5) { w.dir = -w.dir; w.looked = true; }
            if (w.timer <= 0) this.decide(w);
        }
      }
      this.meetings();
      this.updateParts(dt);
      if (this.opts.baloes && (this.nextBubble -= dt) <= 0) {
        const cand = this.workers.filter((w) => this.zones[w.zone].horiz && this.visible(w) && !w.bubble && (w.ch.falas || []).length &&
          w.x > 30 && w.x < this.zones[w.zone].L - 30);
        if (cand.length) { const w = pick(cand); w.bubble = pick(w.ch.falas); w.bubbleT = 3.2; }
        this.nextBubble = R(this.opts.bubbleEvery[0], this.opts.bubbleEvery[1]);
      }
    }

    animateIdle(w) {
      const n = w.skin._sprites.parado ? w.skin._sprites.parado.length : 1;
      if (n < 2) { w.frame = 0; return; }
      if (w.ft < (w.frame === 0 ? w.idleHold : 0.09)) return;
      w.ft = 0; w.frame = (w.frame + 1) % n;
      if (w.frame === 0) w.idleHold = R(1.2, 3.4);
    }

    visible(w) { return w.delay <= 0 && !isNaN(w.x); }

    // Quando dois se cruzam andando na mesma borda, às vezes param, se olham e se cumprimentam.
    meetings() {
      const ws = this.workers;
      for (let i = 0; i < ws.length; i++) {
        const a = ws[i];
        if (a.state !== WALK || !this.visible(a) || a.meet > 0) continue;
        for (let j = i + 1; j < ws.length; j++) {
          const b = ws[j];
          if (b.state !== WALK || !this.visible(b) || b.meet > 0 || b.zone !== a.zone || a.dir === b.dir || a.ch._flies !== b.ch._flies) continue;
          const sw = (a.spriteW + b.spriteW) / 2, gap = b.x - a.x;
          if (Math.abs(gap) > sw * 0.9 || Math.abs(gap) < sw * 0.5 || (gap > 0) !== (a.dir > 0)) continue;
          a.meet = b.meet = 10;
          if (Math.random() > 0.45) continue;
          [a, b].forEach((w) => { w.state = IDLE; w.timer = R(1.3, 1.8); w.frame = 0; w.looked = true; });
          a.dir = gap > 0 ? 1 : -1; b.dir = -a.dir;
          const hopper = Math.random() < 0.5 ? a : b;
          if (!hopper.ch._flies) this.launch(hopper, 3);
          this.add(a, (b.x - a.x) / 2 - a.scale * 2, -this.visH(a) - a.scale * 2, R(-6, 6), -22, 0, 1.3, pick(["#ec407a", "#ffca28"]), Math.random() < 0.33 ? "!" : "♥", 0);
          break;
        }
      }
    }

    // ---------- geometria ----------

    map(z, x, y) { const M = z.M; return [M[0] * x + M[2] * y + M[4], M[1] * x + M[3] * y + M[5]]; }
    mapVec(z, x, y) { const M = z.M; return [M[0] * x + M[2] * y, M[1] * x + M[3] * y]; }
    visH(w) { return (w.ch._feet - w.topRow + 1) * w.px; }
    fly(w) { return w.ch._flies ? w.flyBase + w.alt + Math.sin(performance.now() / 1000 * 3.2 + w.phase) * w.scale * 1.3 : 0; }
    height(w) { return w.y + this.fly(w); }
    // para onde o desenho olha, para quem voa em pé nas laterais: para dentro da tela
    face(w) { return !w.upright ? w.dir : w.zone === "direita" ? -1 : w.zone === "esquerda" ? 1 : w.dir; }

    uprightTopLeft(w, bw) {
      const z = this.zones[w.zone], visH = this.visH(w);
      const across = z.horiz ? visH : w.spriteW;
      const c = this.map(z, w.x, -this.height(w) - across / 2);
      return [c[0] - bw / 2, c[1] - (w.topRow * w.px + visH / 2)];
    }

    spriteRect(w) {
      const z = this.zones[w.zone], visH = this.visH(w);
      if (w.upright) { const tl = this.uprightTopLeft(w, w.spriteW); return [tl[0], tl[1] + w.topRow * w.px, w.spriteW, visH]; }
      const top = -this.height(w) - visH;
      const a = this.map(z, w.x - w.spriteW / 2, top), b = this.map(z, w.x + w.spriteW / 2, top + visH);
      return [Math.min(a[0], b[0]), Math.min(a[1], b[1]), Math.abs(a[0] - b[0]), Math.abs(a[1] - b[1])];
    }

    // ---------- partículas ----------

    // Efeitos em coordenadas do desenho: origem nos pés, x para onde ele olha, y negativo para cima.
    spawn(effect, w, count) {
      if (!effect) return;
      if (effect.includes("+")) { effect.split("+").forEach((e) => this.spawn(e.trim(), w, count)); return; }
      const s = w.scale, f = this.face(w), sw = w.spriteW;
      const frontX = f * sw * 0.45, headX = f * sw * 0.2, headY = -this.visH(w), midY = -(w.ch._feet - w.topRow) * w.px * 0.3;
      const n = (d) => count > 0 ? count : d;
      const A = this.add.bind(this, w);
      switch (effect) {
        case "faiscas": for (let i = 0; i < n(7); i++) A(frontX, -s * 2, R(-110, 110), R(-190, -60), 420, R(0.35, 0.7), pick(["#ffd700", "#ffa500", "#ffffff", "#ff4500"]), null, s); break;
        case "poeira": for (let i = 0; i < n(4); i++) A(R(-sw * 0.3, sw * 0.3), -s, R(-25, 25), R(-35, -10), 0, R(0.4, 0.8), pick(["#beaa8c", "#96826e"]), null, s * (1 + (Math.random() * 2 | 0))); break;
        case "terra": for (let i = 0; i < n(6); i++) A(frontX, -s * 2, R(-40, 90) * -f, R(-170, -80), 420, R(0.4, 0.8), pick(["#79553a", "#5d402d", "#966e46"]), null, s * (1 + (Math.random() * 2 | 0))); break;
        case "neve": for (let i = 0; i < n(5); i++) A(frontX + R(-10, 10), midY, R(-30, 30), R(-60, -20), 60, R(0.6, 1.1), pick(["#ffffff", "#b4dcff"]), null, s); break;
        case "estrelas": for (let i = 0; i < n(3); i++) A(frontX + R(-6, 6), midY, R(-40, 40), R(-80, -30), 60, R(0.5, 0.9), pick(["#ffd700", "#fff096", "#ff69b4"]), null, s); break;
        case "magia": for (let i = 0; i < n(2); i++) A(headX + R(-10, 10), headY + R(0, 12), R(-24, 24), R(-40, -20), 0, R(1, 1.5), pick(["#b388ff", "#64ffda", "#ffd700"]), pick(["✦", "✧", "★", "✶"]), 0); break;
        case "zzz": A(f * sw * 0.3, headY + s * 4, f * R(8, 16), R(-26, -18), 0, 2.2, "#5a6ec8", Math.random() < 0.33 ? "Z" : "z", 0); break;
        case "notas": A(headX, headY, f * R(10, 22), R(-30, -20), 0, 1.8, pick(["#783ca0", "#28783c", "#c85028"]), pick(["♪", "♫"]), 0); break;
        case "claude": A(headX, headY, f * R(6, 20), R(-34, -20), 0, 1.6, pick(["#d97757", "#f5a37f", "#b85f42"]), pick(["✻", "✳", "✢", "✶", "✽"]), 0); break;
        case "codigo": for (let i = 0; i < n(2); i++) A(headX + R(-8, 8), headY + R(0, 10), f * R(4, 16), R(-34, -18), 0, R(1.2, 1.7), pick(["#00e676", "#69f0ae", "#2ea043"]), pick(["0", "1", "{ }", "</>", ";", "#", "()"]), 0); break;
        case "dados": for (let i = 0; i < n(2); i++) A(headX + R(-8, 8), headY + R(0, 10), f * R(4, 16), R(-34, -18), 0, R(1.2, 1.7), pick(["#42a5f5", "#26c6da", "#ffca28"]), pick(["%", "Σ", "π", "▲", "≈"]), 0); break;
        case "fumaca": for (let i = 0; i < n(3); i++) { const p = A(headX + R(-6, 6), headY + R(0, 8), R(-10, 10), R(-30, -16), 0, R(1, 1.6), pick(["#787878", "#969696", "#5f5f5f"]), null, s); if (p) p.grow = s * 1.8; } break;
        case "moedas": for (let i = 0; i < n(2); i++) A(frontX, midY, R(-50, 50), R(-190, -120), 420, R(0.6, 0.9), pick(["#ffd700", "#ffd54f"]), null, s * 2); if (Math.random() < 0.33) A(headX, headY, f * 10, -30, 0, 1.2, "#ffd700", "★", 0); break;
        case "latidos": A(headX + f * sw * 0.25, headY + s * 2, f * R(10, 20), R(-26, -16), 0, 1.3, pick(["#ffb74d", "#fff176"]), pick(["au!", "au au!", "auu!"]), 0); break;
        case "coracoes": A(headX, headY, f * R(6, 16), R(-30, -18), 0, 1.6, pick(["#ec407a", "#f48fb1", "#e53935"]), "♥", 0); break;
        case "fogo": for (let i = 0; i < n(12); i++) A(f * sw * 0.5, headY + R(2, 4) * s, f * R(140, 240), R(-25, 10), -40, R(0.25, 0.55), pick(["#ffeb3b", "#ff9800", "#f44336", "#fff3b0"]), null, s * (1 + (Math.random() * 2 | 0))); break;
        case "flechas": A(f * sw * 0.5, midY - s * 3, f * R(240, 300), R(-8, -2), 0, 0.8, "#8d6e63", f > 0 ? "➳" : "⟵", 0); break;
        case "flores": for (let i = 0; i < n(2); i++) A(frontX + R(-6, 6), midY, R(-30, 30), R(-60, -30), 20, R(1, 1.5), pick(["#ec407a", "#ffeb3b", "#ab47bc", "#66bb6a"]), pick(["✿", "❀", "✾"]), 0); break;
      }
    }

    // Cria a partícula convertendo das coordenadas do desenho para a tela.
    add(w, ox, oy, vx, vy, grav, life, color, text, size) {
      if (this.parts.length > 300) return null;
      const p = { life, max: life, color, text, size, grow: 0, font: 8 + w.scale * 3, floor: w.zone === "chao" };
      if (w.upright) {
        const tl = this.uprightTopLeft(w, w.spriteW);
        p.x = tl[0] + w.spriteW / 2 + ox; p.y = tl[1] + w.feetOff + oy; p.vx = vx; p.vy = vy; p.gx = 0; p.gy = grav;
      } else {
        const z = this.zones[w.zone];
        [p.x, p.y] = this.map(z, w.x + ox, -this.height(w) + oy);
        [p.vx, p.vy] = this.mapVec(z, vx, vy);
        [p.gx, p.gy] = this.mapVec(z, 0, grav);
      }
      this.parts.push(p);
      return p;
    }

    updateParts(dt) {
      for (let i = this.parts.length - 1; i >= 0; i--) {
        const p = this.parts[i];
        if ((p.life -= dt) <= 0 || p.x < -60 || p.y < -60 || p.x > this.W + 60 || p.y > this.H + 60) { this.parts.splice(i, 1); continue; }
        p.vx += p.gx * dt; p.vy += p.gy * dt; p.x += p.vx * dt; p.y += p.vy * dt;
        if (p.floor && p.gy > 0 && p.y > this.wa - p.size) { p.y = this.wa - p.size; p.vy *= -0.3; p.vx *= 0.6; }
      }
    }

    // ---------- desenho ----------

    draw() {
      const g = this.ctx, d = this.dpr;
      g.setTransform(d, 0, 0, d, 0, 0);
      g.clearRect(0, 0, this.W, this.H);
      g.imageSmoothingEnabled = false;
      for (const w of this.workers) {
        if (!this.visible(w)) continue;
        const z = this.zones[w.zone];
        const flapping = w.ch._flies && w.state === IDLE;
        const flip = this.face(w) < 0;
        const kind = w.state === WORK ? w.work : w.state === WALK || flapping ? "andar" : "parado";
        const set = frames(w.ch, w.skin, kind, w.px, flip);
        const f = w.state === JUMP ? set[0] : set[w.frame % set.length];
        if (w.upright) {
          const tl = this.uprightTopLeft(w, f.width);
          g.setTransform(d, 0, 0, d, 0, 0);
          g.drawImage(f, Math.round(tl[0]), Math.round(tl[1]));
          continue;
        }
        const M = z.M;
        g.setTransform(d * M[0], d * M[1], d * M[2], d * M[3], d * M[4], d * M[5]);
        const x = Math.round(w.x - f.width / 2), y = Math.round(-this.height(w)) - w.feetOff;
        if (w.squash > 0) {
          const dh = Math.max(w.scale, Math.round(f.height / 10));
          g.drawImage(f, x - dh / 2, y + dh, f.width + dh, f.height - dh);
        } else g.drawImage(f, x, y);
        if (w.carrying && w.state === WALK) {
          const c = prop(w.skin._carga, w.scale, flip);
          if (c) g.drawImage(c, Math.round(w.x - c.width / 2), Math.round(-this.height(w) - this.visH(w)) - c.height - w.scale + (w.frame % 2) * w.scale);
        }
      }
      g.setTransform(d, 0, 0, d, 0, 0);
      for (const p of this.parts) {
        g.globalAlpha = Math.min(1, p.life / 0.4);
        g.fillStyle = p.color;
        if (p.text) {
          g.font = `700 ${p.font}px "Segoe UI Symbol", system-ui, sans-serif`;
          g.fillText(p.text, p.x, p.y);
        } else {
          const s = p.size + (p.grow ? p.grow * (1 - p.life / p.max) : 0);
          g.fillRect(Math.round(p.x), Math.round(p.y), s, s);
        }
      }
      g.globalAlpha = 1;
      if (this.opts.baloes) for (const w of this.workers) if (w.bubble && this.visible(w)) this.drawBubble(g, w);
    }

    drawBubble(g, w) {
      g.font = '600 12px "Nunito", system-ui, sans-serif';
      const tw = g.measureText(w.bubble).width;
      const bw = Math.ceil(tw) + 16, bh = 24;
      const r = this.spriteRect(w);
      const below = w.zone === "topo";   // no alto, o balão fica embaixo do bichinho
      const carry = w.carrying && w.state === WALK ? 7 * w.scale : 0;
      const by = below ? Math.round(r[1] + r[3] + carry + 9) : Math.max(2, Math.round(r[1] - carry - bh - 9));
      const cx = r[0] + r[2] / 2;
      const bx = Math.max(2, Math.min(this.W - bw - 2, Math.round(cx - bw / 2)));
      g.fillStyle = "#1e2a3a"; g.fillRect(bx, by, bw, bh);
      g.fillStyle = "#ffffff"; g.fillRect(bx + 2, by + 2, bw - 4, bh - 4);
      const tx = Math.max(bx + 6, Math.min(bx + bw - 14, Math.round(cx) - 4));
      g.fillStyle = "#1e2a3a";
      if (below) { g.fillRect(tx, by - 3, 8, 3); g.fillRect(tx + 2, by - 6, 4, 3); g.fillStyle = "#ffffff"; g.fillRect(tx + 2, by - 1, 4, 3); }
      else { g.fillRect(tx, by + bh, 8, 3); g.fillRect(tx + 2, by + bh + 3, 4, 3); g.fillStyle = "#ffffff"; g.fillRect(tx + 2, by + bh - 2, 4, 3); }
      g.fillStyle = "#1e2a3a"; g.fillText(w.bubble, bx + 8, by + 16);
    }

    _loop(t) {
      // Com poucos quadros por segundo (aba em segundo plano), avança em passos pequenos sem a física pular.
      let dt = Math.min(1, (t - this.last) / 1000);
      this.last = t;
      if (!this.paused && this.W) {
        while (dt > 0) { const s = Math.min(dt, 0.05); this.update(s); dt -= s; }
        this.draw();
      }
      requestAnimationFrame(this._loop);
    }
  }

  // Um personagem só, parado no lugar, alternando entre andar, trabalhar e descansar (cards da galeria).
  class Showcase {
    constructor(canvas, ch, unit) {
      this.canvas = canvas; this.ctx = canvas.getContext("2d");
      this.ch = prepare(ch); this.unit = unit;
      this.skin = ch.skins[0]; this.t = 0; this.phase = 0; this.frame = 0; this.ft = 0;
      this.px = pixelScale(this.ch, unit);
      const dpr = window.devicePixelRatio || 1;
      const lift = ch._flies ? Math.min(ch.voo, 4) * this.px + 6 : 0;
      this.W = this.ch._w * this.px + 40; this.H = (this.ch._bottom - this.ch._top + 1) * this.px + 30 + lift;
      canvas.style.width = this.W + "px"; canvas.style.height = this.H + "px";
      canvas.width = Math.round(this.W * dpr); canvas.height = Math.round(this.H * dpr); this.dpr = dpr;
      this.setSkin(this.skin);
    }
    setSkin(s) {
      this.skin = s;
      const works = workKinds(s._sprites).filter((k) => k !== "andar");
      this.kinds = ["andar", ...works, ...(this.ch._flies ? [] : ["parado"])];
      this.phase = 0; this.kind = "andar"; this.hold = 2.4; this.t = 0; this.frame = 0;
    }
    step(dt) {
      this.t += dt; this.ft += dt;
      if (this.t > this.hold && this.kinds.length > 1) {
        this.t = 0; this.phase = (this.phase + 1) % this.kinds.length; this.kind = this.kinds[this.phase];
        this.frame = 0; this.hold = this.kind === "parado" ? 2.2 : this.kind === "andar" ? 2.4 : 2.8;
      }
      const set = frames(this.ch, this.skin, this.kind, this.px, false);
      const n = set.length, pace = Math.max(0.5, this.ch.ritmo || 1);
      const step = this.kind === "andar" ? (n >= 8 ? 0.095 : n >= 4 ? 0.13 : 0.18) / pace : this.kind === "parado" ? (this.frame === 0 ? 1.4 : 0.09) : (n >= 4 ? 0.11 : 0.3);
      if (this.ft > step) { this.ft = 0; this.frame = (this.frame + 1) % n; }
      const g = this.ctx;
      g.setTransform(this.dpr, 0, 0, this.dpr, 0, 0); g.clearRect(0, 0, this.W, this.H); g.imageSmoothingEnabled = false;
      const f = set[this.frame % n];
      const ground = this.H - 4;
      g.fillStyle = "rgba(30,42,58,.14)";
      g.beginPath(); g.ellipse(this.W / 2, ground - 1, this.ch._w * this.px * 0.32, 4, 0, 0, Math.PI * 2); g.fill();
      const fly = this.ch._flies ? Math.round(Math.min(this.ch.voo, 4) * this.px + Math.sin(performance.now() / 1000 * 3.2) * 3) : 0;
      g.drawImage(f, Math.round(this.W / 2 - f.width / 2), ground - (this.ch._feet + 1) * this.px - fly);
    }
  }

  window.Sprites = { prepare, frames, pixelScale, Screen, Showcase, pode, lugarPara };
})();
