#!/usr/bin/env python3
# uso: python3 ferramentas/logos/fundo.py [--aplicar] [--relatorio ARQ] [pasta ou arquivos...]
#      (padrao: RetroFoot.App/Assets/Escudos; sem --aplicar so mostra o que faria)
#
# Logos com um fundo opaco (quadrado ou circulo branco atras do escudo) em vez de transparente.
# Para cada logo:
#   - borda ja transparente: nada a fazer (o caso normal);
#   - borda opaca, de UMA cor quase branca, e o fundo nao chega ao centro: o fundo (a regiao
#     ligada a borda com essa cor) fica transparente e a orla passa por "cor para alfa" (alfa
#     parcial e a cor sem a mistura com o fundo: sem franja branca);
#   - borda opaca de outra cor, ou variada, ou fundo que entra no desenho: so lista para rever
#     (ligas com o desenho ate a borda, escudos com disco branco que faz parte do desenho).
# Idempotente: um logo ja corrigido tem a borda transparente e fica como esta.
# A funcao tirar_fundo(imagem) e a que a importacao de logos do jogador deve usar (docs/ferramentas.md).
import sys, os
from collections import deque
from PIL import Image

TOL = 28          # distancia (maior diferenca de canal) ate a cor do fundo que ainda e fundo
CLARO = 225       # o fundo automatico tem de ser quase branco (todos os canais >= CLARO)
UNIFORME = 0.6    # fracao da borda opaca na cor do fundo (o escudo pode tocar a borda)
CENTRO = 0.30     # o fundo nao pode cobrir pontos do quadrado central (30% do lado)


def _dist(a, b):
    return max(abs(a[0] - b[0]), abs(a[1] - b[1]), abs(a[2] - b[2]))


def _borda(w, h):
    for x in range(w):
        yield x, 0
        yield x, h - 1
    for y in range(1, h - 1):
        yield 0, y
        yield w - 1, y


def analisar(im):
    """(situacao, cor do fundo, detalhe). situacao: 'transparente', 'fundo', 'rever'."""
    im = im.convert('RGBA')
    w, h = im.size
    px = im.load()
    borda = [px[x, y] for x, y in _borda(w, h)]
    opacos = [p for p in borda if p[3] >= 250]
    if len(opacos) < 0.5 * len(borda):
        return 'transparente', None, ''
    # a cor mais comum da borda (por baldes) e a media dela
    baldes = {}
    for p in opacos:
        baldes.setdefault((p[0] // 16, p[1] // 16, p[2] // 16), []).append(p)
    grupo = max(baldes.values(), key=len)
    cor = tuple(sum(p[i] for p in grupo) // len(grupo) for i in range(3))
    na_cor = sum(1 for p in opacos if _dist(p, cor) <= TOL) / len(opacos)
    if na_cor < UNIFORME:
        return 'rever', cor, f'borda opaca variada ({na_cor:.0%} na cor {cor})'
    if min(cor) < CLARO:
        return 'rever', cor, f'borda opaca de uma cor que nao e branca {cor}'
    reg = _regiao(px, w, h, cor)
    x0, x1 = int(w * (0.5 - CENTRO / 2)), int(w * (0.5 + CENTRO / 2))
    y0, y1 = int(h * (0.5 - CENTRO / 2)), int(h * (0.5 + CENTRO / 2))
    if any(reg[y * w + x] for y in range(y0, y1) for x in range(x0, x1)):
        return 'rever', cor, 'o fundo branco chega ao centro (disco do proprio escudo?)'
    area = sum(reg) / (w * h)
    return 'fundo', cor, f'fundo {cor} em {area:.0%} da imagem'


def _regiao(px, w, h, cor):
    # os pontos opacos da cor do fundo ligados a borda (4 vizinhos)
    reg = bytearray(w * h)
    fila = deque()
    for x, y in _borda(w, h):
        p = px[x, y]
        if p[3] >= 128 and _dist(p, cor) <= TOL and not reg[y * w + x]:
            reg[y * w + x] = 1
            fila.append((x, y))
    while fila:
        x, y = fila.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not reg[ny * w + nx]:
                p = px[nx, ny]
                if p[3] >= 128 and _dist(p, cor) <= TOL:
                    reg[ny * w + nx] = 1
                    fila.append((nx, ny))
    return reg


def tirar_fundo(im, cor=None):
    """O logo com o fundo (cor uniforme ligada a borda) transparente; a orla (ate 2 pontos do
    fundo) passa pelo "cor para alfa": o alfa minimo que explica o ponto como mistura do
    desenho com o fundo, e a cor sem essa mistura (nada de franja clara).
    Sem cor: a da analise (so se ela disser 'fundo'; senao devolve a imagem igual)."""
    im = im.convert('RGBA')
    if cor is None:
        sit, cor, _ = analisar(im)
        if sit != 'fundo':
            return im
    w, h = im.size
    px = im.load()
    reg = _regiao(px, w, h, cor)
    # a orla: ate 2 pontos do fundo
    perto = bytearray(reg)
    for _ in range(2):
        prox = bytearray(perto)
        for y in range(h):
            for x in range(w):
                if not perto[y * w + x] and any(0 <= nx < w and 0 <= ny < h and perto[ny * w + nx]
                                                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1))):
                    prox[y * w + x] = 1
        perto = prox
    out = im.copy()
    po = out.load()
    for y in range(h):
        for x in range(w):
            i = y * w + x
            r, g, b, a = px[x, y]
            if reg[i]:
                po[x, y] = (r, g, b, 0)
            elif perto[i]:
                k = _alfa((r, g, b), cor)
                if k <= 0.02:
                    po[x, y] = (r, g, b, 0)
                elif k < 1:
                    c = tuple(max(0, min(255, round(f + (v - f) / k))) for v, f in zip((r, g, b), cor))
                    po[x, y] = (*c, round(a * k))
    return out


def _alfa(c, fundo):
    # o menor alfa com que c = fundo*(1-alfa) + desenho*alfa, desenho em 0..255
    k = 0.0
    for v, f in zip(c, fundo):
        if v < f:
            k = max(k, (f - v) / f)
        elif v > f and f < 255:
            k = max(k, (v - f) / (255 - f))
    return min(1.0, k)


def escuro(im):
    """O mesmo teste de Escudos.Escuro (RetroFoot.App/Escudos.cs): o logo ganha um circulo claro
    por tras no visual Moderno (media escura e menos de 1/4 dos pontos claros ou de cor viva)."""
    soma = n = vivos = 0
    for r, g, b, a in im.convert('RGBA').getdata():
        if a < 128:
            continue
        soma += r + g + b
        n += 1
        mx, mn = max(r, g, b), min(r, g, b)
        if 0.2126 * r + 0.7152 * g + 0.0722 * b > 0.3 * 255 or (mx - mn > 90 and mx > 120):
            vivos += 1
    return n > 0 and soma / (n * 3 * 255) < 0.22 and vivos < n / 4


def main(args):
    aplicar = '--aplicar' in args
    relatorio = None
    if '--relatorio' in args:
        relatorio = args[args.index('--relatorio') + 1]
    alvos = [a for i, a in enumerate(args) if not a.startswith('--') and (i == 0 or args[i - 1] != '--relatorio')]
    if not alvos:
        alvos = [os.path.join(os.path.dirname(__file__), '..', '..', 'RetroFoot.App', 'Assets', 'Escudos')]
    arquivos = []
    for a in alvos:
        if os.path.isdir(a):
            arquivos += [os.path.join(a, f) for f in sorted(os.listdir(a)) if f.lower().endswith(('.png', '.jpg', '.bmp'))]
        else:
            arquivos.append(a)
    linhas = {'fundo': [], 'rever': [], 'transparente': []}
    circulo = []
    for f in arquivos:
        im = Image.open(f)
        if escuro(im):
            circulo.append(os.path.basename(f))
        sit, cor, det = analisar(im)
        linhas[sit].append(f'{os.path.basename(f)}\t{det}')
        if sit == 'fundo' and aplicar:
            # (um .jpg/.bmp passa a .png ao lado: so o png tem transparencia)
            dest = os.path.splitext(f)[0] + '.png'
            tirar_fundo(im, cor).save(dest, optimize=True)
    txt = [f'# ferramentas/logos/fundo.py{" --aplicar" if aplicar else " (so analise)"}: {len(arquivos)} logos',
           f'# {len(linhas["transparente"])} ja com a borda transparente (nada a fazer)',
           '', f'## fundo opaco quase branco {"tirado" if aplicar else "a tirar"} ({len(linhas["fundo"])})']
    txt += linhas['fundo'] or ['(nenhum)']
    txt += ['', f'## para rever a mao, nao mexidos ({len(linhas["rever"])})'] + (linhas['rever'] or ['(nenhum)'])
    txt += ['', f'## com o circulo claro por tras no visual Moderno (Escudos.Escuro; escuros demais para o fundo escuro) ({len(circulo)})']
    txt += circulo or ['(nenhum)']
    saida = '\n'.join(txt) + '\n'
    print(saida, end='')
    if relatorio:
        with open(relatorio, 'w', encoding='utf-8') as r:
            r.write(saida)


if __name__ == '__main__':
    main(sys.argv[1:])
