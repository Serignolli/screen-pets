(function () {
  "use strict";

  const { Screen, Showcase, frames, prepare } = window.Sprites;
  const CHARS = window.PERSONAGENS;
  CHARS.forEach(prepare);
  const byId = (id) => CHARS.find((c) => c.id === id);
  const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);

  const TIPO_NOME = { terrestre: "Terrestre", escalador: "Escalador", voador: "Voador" };
  const TIPO_PLURAL = { terrestre: "Terrestres", escalador: "Escaladores", voador: "Voadores" };
  const TIPO_DICA = { terrestre: "combina com a barra de tarefas", escalador: "combina com as laterais", voador: "combina com o alto da tela" };
  const LUGAR_PADRAO = { terrestre: "chao", escalador: "laterais", voador: "topo" };
  const { pode, lugarPara } = window.Sprites;
  const REGRA = {
    terrestre: "Quem anda fica em cima da barra de tarefas.",
    escalador: "Escaladores ficam nas laterais ou no alto da tela.",
    voador: "Voadores ficam no alto da tela ou nas laterais.",
  };
  const LUGAR_NOME = { chao: "Em cima da barra de tarefas", topo: "No alto da tela", laterais: "Nas duas laterais", esquerda: "Na lateral esquerda", direita: "Na lateral direita" };
  const TAMANHOS = ["Bem pequeno", "Pequeno", "Médio", "Grande", "Enorme"];
  const VELOCIDADES = [0.5, 0.75, 1, 1.4, 2];
  const VEL_NOME = ["Devagar", "Calma", "Normal", "Rápida", "Correria"];

  // Pausa as animações que não estão na tela.
  const watch = (el, s) => new IntersectionObserver((es) => es.forEach((e) => (s.paused = !e.isIntersecting))).observe(el);

  // ícone: primeiro quadro de "parado", reduzido para caber no quadrado
  function icon(ch, size, skin) {
    const c = document.createElement("canvas");
    drawIcon(c, ch, size, skin);
    return c;
  }
  function drawIcon(c, ch, size, skin) {
    const dpr = window.devicePixelRatio || 1;
    c.width = c.height = size * dpr; c.style.width = c.style.height = size + "px";
    const f = frames(ch, skin || ch.skins[0], "parado", 1, false)[0];
    const h = ch._bottom - ch._top + 1;
    const s = Math.max(1, Math.floor(Math.min(size * dpr / h, size * dpr / ch._w) * 0.9));
    const g = c.getContext("2d"); g.imageSmoothingEnabled = false;
    g.clearRect(0, 0, c.width, c.height);
    g.drawImage(f, 0, ch._top, f.width, h, Math.round((size * dpr - f.width * s) / 2), Math.round((size * dpr - h * s) / 2), f.width * s, h * s);
  }

  // Tamanho dos bichinhos conforme a largura da telinha (no celular ficam menores).
  const unitFor = (el) => Math.max(0.8, Math.min(1.4, el.getBoundingClientRect().width / 600));

  // ---------- topo: uma telinha com uma turma em cada borda ----------

  const heroCanvas = document.getElementById("heroStage");
  const hero = new Screen(heroCanvas, {
    chars: CHARS, taskbar: 22, baloes: true, unitK: 1.5, bubbleEvery: [4, 8],
    turmas: [
      { personagem: "gatos", cor: "*", lugar: "chao", quantidade: 2, tamanho: 2 },
      { personagem: "cachorros", cor: "*", lugar: "chao", quantidade: 1, tamanho: 2 },
      { personagem: "lagartixa", cor: "*", lugar: "direita", quantidade: 1, tamanho: 2 },
      { personagem: "joaninha", cor: "*", lugar: "esquerda", quantidade: 1, tamanho: 2 },
      { personagem: "passarinho", cor: "*", lugar: "topo", quantidade: 2, tamanho: 2 },
    ].filter((g) => byId(g.personagem)),
  });
  watch(heroCanvas, hero);

  document.querySelectorAll("[data-icon]").forEach((el) => { const ch = byId(el.dataset.icon); if (ch) el.appendChild(icon(ch, 44)); });

  // ---------- simulador ----------

  const STORE = "screenpets:simulador:v2";
  const PADRAO = {
    ativo: true, baloes: true, esconderTelaCheia: true, velocidade: 1,
    turmas: [
      { personagem: "gatos", cor: "*", lugar: "chao", quantidade: 2, tamanho: 2 },
      { personagem: "lagartixa", cor: "*", lugar: "direita", quantidade: 1, tamanho: 2 },
      { personagem: "borboleta", cor: "*", lugar: "topo", quantidade: 1, tamanho: 2 },
    ],
  };
  const clone = (o) => JSON.parse(JSON.stringify(o));
  let cfg = clone(PADRAO);
  try { const s = JSON.parse(localStorage.getItem(STORE)); if (s && Array.isArray(s.turmas)) cfg = Object.assign(clone(PADRAO), s); } catch { /* sem armazenamento */ }
  cfg.turmas = cfg.turmas.filter((g) => byId(g.personagem));
  cfg.turmas.forEach((g) => (g.lugar = lugarPara(byId(g.personagem).tipo, g.lugar)));
  let sel = cfg.turmas.length ? 0 : -1;

  const simCanvas = document.getElementById("simStage");
  const sim = new Screen(simCanvas, { chars: CHARS, taskbar: 30, unitK: unitFor(simCanvas), turmas: [] });
  watch(simCanvas, sim);
  window.addEventListener("resize", () => {
    const k = unitFor(simCanvas);
    if (Math.abs(k - sim.opts.unitK) > 0.05) { sim.opts.unitK = k; sim.setTurmas(cfg.ativo ? cfg.turmas : []); }
  });

  const $ = (id) => document.getElementById(id);
  const groupsEl = $("groups"), detail = $("detail");

  function save() { try { localStorage.setItem(STORE, JSON.stringify(cfg)); } catch { /* sem armazenamento */ } }

  // Mudou a turma (personagem, cor, quantos, onde, tamanho): recria os bichinhos.
  function apply() {
    sim.setOptions({ baloes: cfg.baloes, velocidade: cfg.velocidade });
    sim.setTurmas(cfg.ativo ? cfg.turmas : []);
    render();
    save();
  }
  // Só opções gerais (balões, velocidade): não precisa recomeçar.
  function applySoft() { sim.setOptions({ baloes: cfg.baloes, velocidade: cfg.velocidade }); render(); save(); }

  function render() {
    groupsEl.innerHTML = "";
    cfg.turmas.forEach((g, i) => {
      const ch = byId(g.personagem);
      const li = document.createElement("li"), b = document.createElement("button");
      b.type = "button"; b.setAttribute("aria-pressed", i === sel ? "true" : "false");
      b.appendChild(icon(ch, 34, g.cor !== "*" ? ch.skins.find((s) => s.id === g.cor) : null));
      b.insertAdjacentHTML("beforeend", `<span>${esc(ch.nome)} ×${g.quantidade}<small>${LUGAR_NOME[g.lugar] || ""}</small></span>`);
      b.addEventListener("click", () => { sel = i; render(); });
      li.appendChild(b); groupsEl.appendChild(li);
    });
    $("emptyMsg").hidden = cfg.turmas.length > 0;
    detail.hidden = sel < 0;
    if (sel >= 0) {
      const g = cfg.turmas[sel], ch = byId(g.personagem);
      $("detailName").textContent = ch.nome;
      $("detailKind").textContent = `${TIPO_NOME[ch.tipo]} · ${TIPO_DICA[ch.tipo]}`;
      drawIcon($("detailIco"), ch, 52, g.cor !== "*" ? ch.skins.find((s) => s.id === g.cor) : null);
      $("fCor").innerHTML = `<option value="*">Misturar as cores</option>` + ch.skins.map((s) => `<option value="${esc(s.id)}">${esc(s.nome)}</option>`).join("");
      $("fCor").value = g.cor;
      $("fQtd").value = g.quantidade;
      // só os lugares que o tipo permite
      document.querySelectorAll('input[name="lugar"]').forEach((r) => {
        r.closest("label").hidden = !pode(ch.tipo, r.value);
        r.checked = r.value === g.lugar;
      });
      $("fRule").textContent = REGRA[ch.tipo];
      $("fTam").value = g.tamanho; $("fTamName").textContent = TAMANHOS[g.tamanho - 1];
    }
    $("gAtivo").checked = cfg.ativo; $("gBaloes").checked = cfg.baloes;
    const vi = VELOCIDADES.reduce((b, v, i) => (Math.abs(v - cfg.velocidade) < Math.abs(VELOCIDADES[b] - cfg.velocidade) ? i : b), 0);
    $("gVel").value = vi; $("gVelName").textContent = VEL_NOME[vi];
  }

  const cur = () => cfg.turmas[sel];
  $("fCor").addEventListener("change", (e) => { cur().cor = e.target.value; apply(); });
  $("fQtd").addEventListener("change", (e) => { cur().quantidade = Math.max(1, Math.min(10, parseInt(e.target.value, 10) || 1)); apply(); });
  document.querySelectorAll('input[name="lugar"]').forEach((r) => r.addEventListener("change", () => { cur().lugar = r.value; apply(); }));
  $("fTam").addEventListener("input", (e) => { cur().tamanho = +e.target.value; apply(); });
  $("removeGroup").addEventListener("click", () => { cfg.turmas.splice(sel, 1); sel = Math.min(sel, cfg.turmas.length - 1); apply(); });
  $("gAtivo").addEventListener("change", (e) => { cfg.ativo = e.target.checked; apply(); });
  $("gBaloes").addEventListener("change", (e) => { cfg.baloes = e.target.checked; applySoft(); });
  $("gVel").addEventListener("input", (e) => { cfg.velocidade = VELOCIDADES[+e.target.value]; applySoft(); });
  $("resetCfg").addEventListener("click", () => { cfg = clone(PADRAO); sel = 0; apply(); });

  // escolha de bichinho: adicionar uma turma nova ou trocar o da escolhida
  const dlg = $("pickerDlg");
  let picking = "add";
  const list = $("pickerList");
  ["terrestre", "escalador", "voador"].forEach((tipo) => {
    const chars = CHARS.filter((c) => c.tipo === tipo);
    if (!chars.length) return;
    list.insertAdjacentHTML("beforeend", `<h4>${TIPO_PLURAL[tipo]}</h4>`);
    const grid = document.createElement("div"); grid.className = "pick-grid";
    chars.forEach((ch) => {
      const b = document.createElement("button"); b.type = "button";
      b.appendChild(icon(ch, 40));
      b.insertAdjacentHTML("beforeend", esc(ch.nome));
      b.addEventListener("click", () => { choose(ch); dlg.close(); });
      grid.appendChild(b);
    });
    list.appendChild(grid);
  });
  function choose(ch) {
    if (picking === "add") {
      cfg.turmas.push({ personagem: ch.id, cor: "*", lugar: LUGAR_PADRAO[ch.tipo], quantidade: ch.tipo === "escalador" ? 1 : 2, tamanho: 2 });
      sel = cfg.turmas.length - 1;
    } else {
      // trocou de bichinho: se o lugar era o padrão do antigo, vai para o padrão do novo (como o programa)
      const g = cur(), old = byId(g.personagem);
      if (!old || g.lugar === LUGAR_PADRAO[old.tipo]) g.lugar = LUGAR_PADRAO[ch.tipo];
      g.personagem = ch.id; g.cor = "*"; g.lugar = lugarPara(ch.tipo, g.lugar);
    }
    apply();
  }
  $("addGroup").addEventListener("click", () => { picking = "add"; dlg.showModal(); });
  $("swapChar").addEventListener("click", () => { picking = "swap"; dlg.showModal(); });
  $("pickerClose").addEventListener("click", () => dlg.close());
  dlg.addEventListener("click", (e) => { if (e.target === dlg) dlg.close(); });

  apply();

  // relógio da barra de tarefas de mentira
  const clock = () => { const d = new Date(); document.querySelectorAll(".tb-clock").forEach((el) => (el.textContent = d.toTimeString().slice(0, 5))); };
  clock(); setInterval(clock, 15000);

  // ---------- galeria ----------

  const EFEITO = {
    faiscas: "faíscas", poeira: "poeira", terra: "terra", neve: "neve", estrelas: "estrelinhas", magia: "magia", zzz: "cochilos (zzz)",
    notas: "cantoria ♪", claude: "estrelinhas ✻", codigo: "código", dados: "dados", fumaca: "fumaça", moedas: "moedas",
    latidos: "latidos", coracoes: "coraçõezinhos", flores: "flores ✿", fogo: "fogo",
  };

  // Cor que representa a skin nas bolinhas: entre as cores que mudam de uma skin para outra, a mais usada e mais viva.
  function skinColor(ch, s) {
    const changing = new Set();
    ch.skins.slice(1).forEach((x) => Object.keys(x.cores || {}).forEach((k) => changing.add(k)));
    const counts = {};
    s._sprites.andar[0].forEach((r) => [...r].forEach((c) => { if (c !== "." && c !== " ") counts[c] = (counts[c] || 0) + 1; }));
    const rgb = (hex) => { const n = parseInt(hex.slice(1), 16); return [n >> 16, (n >> 8) & 255, n & 255]; };
    const score = (k) => {
      const [r, g, b] = rgb(s._colors[k]), mx = Math.max(r, g, b), mn = Math.min(r, g, b);
      const lum = (0.3 * r + 0.59 * g + 0.11 * b) / 255;
      return lum < 0.12 ? 0 : (counts[k] || 0) * (0.25 + (mx - mn) / 255);
    };
    const keys = Object.keys(counts).filter((k) => s._colors[k] && (!changing.size || changing.has(k)));
    const best = (keys.length ? keys : Object.keys(counts)).sort((a, b) => score(b) - score(a))[0];
    return s._colors[best] || "#888";
  }

  const gallery = $("gallery");
  const shows = [];
  CHARS.forEach((ch) => {
    const card = document.createElement("article");
    card.className = "card"; card.dataset.kind = ch.tipo;
    const effects = new Set([ch.efeito, ...Object.values(ch.efeitosTrabalho || {}), ...ch.skins.map((s) => s.efeito)].join("+").split("+").filter(Boolean));
    const ritmo = ch.ritmo && ch.ritmo < 0.6 ? "bem devagar" : ch.ritmo && ch.ritmo > 1.2 ? "ligeiro" : null;
    card.innerHTML = "<div class='stagebox'></div>" +
      `<h3>${esc(ch.nome)}</h3><p>${esc(ch.descricao || "")}</p>` +
      `<div class='tags'>${[...effects].map((e) => `<span>${EFEITO[e] || esc(e)}</span>`).join("")}` +
      (ritmo ? `<span>${ritmo}</span>` : "") + `<span>${ch.skins.length} cores</span></div>` +
      "<div class='skins'></div>" +
      `<a class='btn btn-small try' href='#simulador'>Pôr na minha tela</a>`;
    const canvas = document.createElement("canvas");
    canvas.className = "stage";
    card.querySelector(".stagebox").appendChild(canvas);
    const show = new Showcase(canvas, ch, ch._w > 24 ? 5 : 7);
    show.card = card;
    shows.push(show);
    const skins = card.querySelector(".skins");
    ch.skins.forEach((s, i) => {
      const b = document.createElement("button");
      b.type = "button"; b.title = s.nome; b.style.background = skinColor(ch, s);
      b.setAttribute("aria-label", s.nome); b.setAttribute("aria-pressed", i === 0 ? "true" : "false");
      b.addEventListener("click", () => {
        show.setSkin(s);
        skins.querySelectorAll("button").forEach((x) => x.setAttribute("aria-pressed", x === b ? "true" : "false"));
      });
      skins.appendChild(b);
    });
    card.querySelector(".try").addEventListener("click", () => { picking = "add"; choose(ch); });
    gallery.appendChild(card);
  });

  const tabs = document.querySelectorAll("#kindTabs button");
  function showKind(kind) {
    tabs.forEach((t) => t.setAttribute("aria-selected", t.dataset.kind === kind ? "true" : "false"));
    gallery.querySelectorAll(".card").forEach((c) => (c.hidden = c.dataset.kind !== kind));
  }
  tabs.forEach((t) => t.addEventListener("click", () => showKind(t.dataset.kind)));
  showKind("terrestre");

  let lastShow = performance.now(), galleryVisible = true;
  new IntersectionObserver((es) => es.forEach((e) => (galleryVisible = e.isIntersecting))).observe(gallery);
  (function tick(t) {
    const dt = Math.min(0.1, (t - lastShow) / 1000); lastShow = t;
    if (galleryVisible) shows.forEach((s) => { if (!s.card.hidden) s.step(dt); });
    requestAnimationFrame(tick);
  })(lastShow);

  // ---------- desfile no rodapé: os terrestres passeando no gramado ----------

  const paradeCanvas = $("parade");
  const parade = new Screen(paradeCanvas, {
    chars: CHARS, baloes: false, unitK: 1,
    turmas: CHARS.filter((c) => c.tipo === "terrestre").map((c) => ({ personagem: c.id, cor: "*", lugar: "chao", quantidade: 2, tamanho: 3 })),
  });
  watch(paradeCanvas, parade);

  // ---------- Supporters Pack (em breve): o gato de hoje, andando, ao lado do HD ----------

  const packCanvas = $("packNow");
  if (packCanvas && byId("gatos")) {
    const packShow = new Showcase(packCanvas, byId("gatos"), 6);
    let packVisible = false, lastPack = performance.now();
    new IntersectionObserver((es) => es.forEach((e) => (packVisible = e.isIntersecting))).observe(packCanvas);
    (function tick(t) {
      const dt = Math.min(0.1, (t - lastPack) / 1000); lastPack = t;
      if (packVisible) packShow.step(dt);
      requestAnimationFrame(tick);
    })(lastPack);
  }

  // ---------- aviso de apoio: só no primeiro download ----------

  const SUPPORT_KEY = "screenpets:support-shown";
  const support = $("support");
  let shown = false;
  const wasShown = () => { try { return localStorage.getItem(SUPPORT_KEY) === "1"; } catch { return shown; } };
  const markShown = () => { shown = true; try { localStorage.setItem(SUPPORT_KEY, "1"); } catch { /* sem armazenamento */ } };
  document.addEventListener("click", (e) => {
    const a = e.target.closest("a.dl-link[download]");
    if (!a || shown || wasShown()) return;
    markShown();
    // não bloqueia o download: o navegador segue o link e o aviso abre logo depois
    setTimeout(() => support.showModal(), 400);
  });
  $("supportClose").addEventListener("click", () => support.close());
  $("supportPack").addEventListener("click", () => support.close());
  support.addEventListener("click", (e) => { if (e.target === support) support.close(); });

  // ---------- download: macOS e Linux ----------

  // O install.sh baixa os pacotes do mesmo site que o serve; publicado em outro endereço, o comando avisa qual.
  const SITE = "https://screenpets.serignolli.com";
  const local = location.protocol === "file:" || ["localhost", "127.0.0.1", "[::1]"].includes(location.hostname);
  const origin = local ? SITE : location.origin;
  $("unixCmd").textContent = `curl -fsSL ${origin}/install.sh | ` + (origin === SITE ? "sh" : `SCREENPETS_URL=${origin} sh`);
  $("copyCmd").addEventListener("click", () => {
    const btn = $("copyCmd");
    const done = () => { btn.textContent = "Copiado!"; setTimeout(() => (btn.textContent = "Copiar"), 1600); };
    if (navigator.clipboard) navigator.clipboard.writeText($("unixCmd").textContent).then(done, () => {});
  });

  // Quem chega pelo Mac ou pelo Linux vê o botão do topo levando ao comando de instalação.
  const ua = navigator.userAgent;
  const mobile = /Android|iPhone|iPad|iPod/i.test(ua);
  const unix = mobile ? null : /Mac OS X|Macintosh/i.test(ua) ? "macOS" : /Linux|X11|CrOS/i.test(ua) ? "Linux" : null;
  if (unix) {
    const hero = $("heroDl");
    hero.href = "#dlUnix"; hero.removeAttribute("download");
    $("heroDlText").textContent = "Instalar no " + unix;
  }
})();
