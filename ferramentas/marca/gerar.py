# Gera o nome do jogo (nome.png) e o texto dos icones (icone-256/512.png) da marca RetroFoot:
# letras condensadas (Barlow Condensed ExtraBold, SIL OFL), "RETRO" verde e "FOOT" dourado,
# com contorno branco e uma sombra leve. Os icones guardam a bola de cima e trocam so o texto.
# uso: python3 ferramentas/marca/gerar.py [pasta Marca] [fonte .ttf]
import os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

AQUI = os.path.dirname(os.path.abspath(__file__))
MARCA = sys.argv[1] if len(sys.argv) > 1 else os.path.join(AQUI, '..', '..', 'RetroFoot.App', 'Assets', 'Marca')
FONTE = sys.argv[2] if len(sys.argv) > 2 else os.path.expanduser('~/.local/share/fonts/google-fonts/BarlowCondensed-ExtraBold.ttf')
VERDE, OURO = (23, 139, 64, 255), (221, 169, 23, 255)
PARTES = [('RETRO', VERDE), ('FOOT', OURO)]


def palavra(altura, contorno, sombra=True):
    """o nome numa imagem transparente, com as letras de 'altura' px (caixa alta)"""
    esc = 4  # desenha grande e reduz (bordas lisas)
    f = ImageFont.truetype(FONTE, int(altura * esc * 1.36))
    # largura de cada parte; o topo das maiusculas fica em y = c
    _, c, _, b = f.getbbox('R')
    larg = [f.getlength(t) for t, _ in PARTES]
    m = contorno * esc + 8 * esc
    W, H = int(sum(larg) + 2 * m), int((b - c) + 2 * m)
    camadas = Image.new('RGBA', (W, H))
    # mascara de todas as letras, para o contorno e a sombra
    masc = Image.new('L', (W, H))
    d = ImageDraw.Draw(masc)
    x = m
    for (t, _), l in zip(PARTES, larg):
        d.text((x, m - c), t, font=f, fill=255)
        x += l
    borda = masc.filter(ImageFilter.MaxFilter(2 * contorno * esc + 1))
    if sombra:
        s = borda.filter(ImageFilter.GaussianBlur(2 * esc)).point(lambda v: v * 0.35)
        camadas.paste((0, 0, 0, 255), (int(1.5 * esc), int(2 * esc)), s)
    camadas.paste((255, 255, 255, 255), (0, 0), borda)
    x = m
    for (t, cor), l in zip(PARTES, larg):
        cam = Image.new('L', (W, H))
        ImageDraw.Draw(cam).text((x, m - c), t, font=f, fill=255)
        escura = tuple(int(v * 0.8) for v in cor[:3]) + (255,)
        camadas.paste(escura, (0, 0), cam)                              # filete interno mais escuro
        camadas.paste(cor, (0, 0), cam.filter(ImageFilter.MinFilter(2 * esc + 1)))
        x += l
    camadas = camadas.crop(camadas.getbbox())
    return camadas.resize((max(1, camadas.width // esc), max(1, camadas.height // esc)), Image.LANCZOS)


def nome():
    # ocupa a mesma area do nome anterior (692x121): as letras ficam um pouco mais largas
    p = palavra(112, 4).resize((692, 121), Image.LANCZOS)
    out = Image.new('RGBA', (700, 135))
    out.alpha_composite(p, ((700 - p.width) // 2, (135 - p.height) // 2))
    out.save(os.path.join(MARCA, 'nome.png'))


def icone(tam, base):
    """troca o texto do icone de 'base' (a bola fica): a faixa do texto vira o fundo cinza"""
    im = Image.open(base).convert('RGBA').resize((512, 512), Image.LANCZOS)
    fundo = im.getpixel((20, 256))
    ImageDraw.Draw(im).rectangle((26, 377, 486, 466), fill=fundo)
    p = palavra(66, 3)
    if p.width > 420:
        p = p.resize((420, int(p.height * 420 / p.width)), Image.LANCZOS)
    im.alpha_composite(p, ((512 - p.width) // 2, 421 - p.height // 2))
    im.resize((tam, tam), Image.LANCZOS).save(os.path.join(MARCA, f'icone-{tam}.png'))


if __name__ == '__main__':
    base = os.path.join(MARCA, 'icone-512.png')
    orig = Image.open(base).copy()
    tmp = os.path.join(MARCA, '.icone-base.png'); orig.save(tmp)
    nome()
    icone(512, tmp); icone(256, tmp)
    os.remove(tmp)
