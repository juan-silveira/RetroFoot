# uso: python3 ferramentas/acentos-mssans.py RetroFoot.App/Assets/Fontes/MSSansSerifHD-*.ttf (precisa do fonttools; ja aplicado)
# Corrige os acentos da MS Sans Serif HD: as compostas (a + acute...) tinham o acento fora
# do lugar e o i acentuado era um desenho largo (avanco 2x o do i). Acento centrado na caixa
# da base, base sem deslocamento; i/I acentuados = dotlessi/I + acento, com o avanco do i/I.
import sys
from fontTools.ttLib import TTFont
from fontTools.ttLib.tables._g_l_y_f import Glyph, GlyphComponent
MARCAS = {'acute','grave','circumflex','tilde','dieresis','ring'}
def corrigir(arq, saida):
    t = TTFont(arq); g = t['glyf']; hm = t['hmtx']; cm = t.getBestCmap()
    def caixa(n):
        gl = g[n]; gl.recalcBounds(g); return gl.xMin, gl.yMin, gl.xMax, gl.yMax
    def comp(nome, x, y):
        c = GlyphComponent(); c.glyphName = nome; c.x = x; c.y = y; c.flags = 0x4; return c
    def poe(n, base, bdx, marca, my, avanco):
        bx0, _, bx1, _ = caixa(base)
        mx0, _, mx1, _ = caixa(marca)
        cx = (bx0 + bx1) / 2 + bdx
        gl = Glyph(); gl.numberOfContours = -1
        gl.components = [comp(base, bdx, 0), comp(marca, round(cx - (mx0 + mx1) / 2), my)]
        g[n] = gl
        gl.recalcBounds(g)
        hm[n] = (avanco, gl.xMin)
    feitos = []
    for u in range(0xC0, 0x100):
        n = cm.get(u)
        if not n: continue
        gl = g[n]
        if gl.isComposite() and len(gl.components) == 2:
            cs = gl.components
            m = [c for c in cs if c.glyphName in MARCAS]
            b = [c for c in cs if c.glyphName not in MARCAS]
            if len(m) != 1 or len(b) != 1: continue
            base, marca = b[0].glyphName, m[0].glyphName
            # vertical: o deslocamento original menos o da base
            my = m[0].y - b[0].y
            if base == 'I':
                poe(n, 'I', 0, marca, my, hm['I'][0])
            else:
                poe(n, base, 0, marca, my, hm[n][0])
            feitos.append(n)
    # o i minusculo acentuado: dotlessi alinhado ao i, acento do mesmo y do e acentuado
    ix0 = caixa('i')[0]; dx0 = caixa('dotlessi')[0]
    for u, marca in ((0xEC, 'grave'), (0xED, 'acute'), (0xEE, 'circumflex'), (0xEF, 'dieresis')):
        n = cm[u]
        ref = {'grave': 'egrave', 'acute': 'eacute', 'circumflex': 'ecircumflex', 'dieresis': 'edieresis'}[marca]
        my = [c for c in g[ref].components if c.glyphName == marca][0].y
        poe(n, 'dotlessi', ix0 - dx0, marca, my, hm['i'][0])
        feitos.append(n)
    # o c cedilha (o do negrito era o desenho do normal): c/C + cedilla centrada embaixo
    for n, base in (('ccedilla', 'c'), ('Ccedilla', 'C')):
        poe(n, base, 0, 'cedilla', 0, hm[base][0])
        feitos.append(n)
    t.save(saida)
    print(arq, len(feitos), 'glifos')
for a in sys.argv[1:]:
    corrigir(a, a)
