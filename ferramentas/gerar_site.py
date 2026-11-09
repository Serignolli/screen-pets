"""Gera site/dados.js com os personagens (mesmos JSON do programa), para o site animá-los no navegador.

    python ferramentas/gerar_site.py

O site abre direto do arquivo (file://), por isso os dados vão num .js e não são buscados por fetch.
"""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
SRC = os.path.join(ROOT, "personagens")
OUT = os.path.join(ROOT, "site", "dados.js")

# ordem de exibição dentro de cada tipo (terrestres, escaladores, voadores)
TIPOS = ["terrestre", "escalador", "voador"]
ORDEM = ["gatos", "cachorros", "pinguins", "formigas", "coelhos", "lagartixa", "joaninha", "caracol", "aranha", "besouro", "passarinho", "borboleta", "abelha", "morcego", "dragao"]
CAMPOS = ["id", "nome", "tipo", "descricao", "detalhe", "voo", "ritmo", "efeito", "carga", "efeitosTrabalho",
          "sprites", "skins", "falas"]


def main():
    chars = []
    for f in os.listdir(SRC):
        if not f.endswith(".json"): continue
        d = json.load(open(os.path.join(SRC, f), encoding="utf-8"))
        c = {k: d[k] for k in CAMPOS if k in d}
        c.setdefault("tipo", "voador" if d.get("voo") else "terrestre")
        chars.append(c)
    chars.sort(key=lambda c: (TIPOS.index(c["tipo"]) if c["tipo"] in TIPOS else 9,
                              ORDEM.index(c["id"]) if c["id"] in ORDEM else 99, c["nome"]))
    text = "// Gerado por ferramentas/gerar_site.py a partir de personagens/*.json. Não edite à mão.\n"
    text += "window.PERSONAGENS = " + json.dumps(chars, ensure_ascii=False, separators=(",", ":")) + ";\n"
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    open(OUT, "w", encoding="utf-8").write(text)
    print("site/dados.js:", len(chars), "personagens,", len(text) // 1024, "KB")


if __name__ == "__main__":
    main()
