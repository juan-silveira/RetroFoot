#!/usr/bin/env python3
"""Logos de clubes brasileiros que faltam no jogo, tirados do UOL Esporte.

uso: python3 tools/logos/uol.py [--app RetroFoot.App/Assets] [--alvos ARQ.tsv ...] [--local PASTA]
                                [--cache PASTA] [--extras] [--so-casar] [--max N]

1. Campeonatos: a lista "Nacionais" e "Estaduais" de
   https://www.uol.com.br/esporte/futebol/campeonatos/ (com --extras, tenta tambem outros
   estaduais pelo slug; 404 = nao existe). Cada pagina traz, no HTML, o JSON com as equipas
   de todas as fases ("football-team-<id>": nome, slug) e os jogos (mandante, estado do
   estadio): o estado da equipa e o UF do nome (America-RN) ou o estado mais comum dos jogos
   em casa. O fragmento #fase-de-grupos e so uma aba, os dados ja estao todos na pagina.
2. Alvos: as equipas BRA do jogo sem logo (Assets/Dados/teams/BRA cujo nome nao tem logo no
   escudos.tsv, e os nomes BRA com "-" no escudos.tsv), e as linhas BRA dos --alvos
   (pais, nome completo, nome curto em colunas 3-5 como o relatorio.tsv do launcher, ou
   pais, completo, curto). Alvos de --alvos sao "locais" (ex.: equipas de um pacote pago):
   os nomes deles so vao para --local, nunca para o git.
3. Casamento conservador: o nucleo do nome (sem acentos, sem FC/EC/CLUBE..., sem o UF) do
   completo ou do curto igual ao nucleo do nome ou do slug de UMA so equipa do UOL; com UF
   dos dois lados, o UF tem de ser igual; equipa do jogo com UF e equipa do UOL sem estado
   conhecido = ambiguo; nome generico (AMERICA, NACIONAL...) sem UF = ambiguo. Ambiguos e
   nao achados vao para <local>/uol-relatorio.tsv.
4. Baixa so os casados que faltam: e.imguol.com/futebol/brasoes/100x100/<slug>.png (o maior
   que existe; 200x200, 300x300, svg... dao 404). Um pedido por vez, pausa de 2,5 s,
   429/503 = Retry-After ou 30/60/120/240/480 s; tudo em cache (pode interromper e rodar de novo).
5. Converte: tira fundo uniforme (se houver), corta no conteudo, amplia para 256x256 com o
   lado maior encostado na borda (como os outros logos), centrado, transparente:
   realesrgan-ncnn-vulkan se estiver instalado, senao Pillow (Lanczos + unsharp mask leve).
   Grava Assets/Escudos/brazil_<slug>.png (nunca substitui um arquivo existente), o
   escudos.tsv (os nomes publicos: equipas do jogo e os nomes do UOL) e
   tools/logos/uol-fontes.tsv (de onde veio cada arquivo).
"""
import argparse, collections, html, json, os, re, shutil, subprocess, sys, tempfile, time
import unicodedata, urllib.error, urllib.request
from datetime import date

from PIL import Image, ImageFilter

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.normpath(os.path.join(AQUI, '..', '..'))
BASE = 'https://www.uol.com.br/esporte/futebol/campeonatos/'
BRASAO = 'https://e.imguol.com/futebol/brasoes/100x100/{}.png'
UA = 'Mozilla/5.0 (X11; Linux x86_64) RetroFoot-logos/1.0 (um pedido por vez)'
PAUSA = 2.5
TAM = 256
OK = ('ok', 'palpite')
UFS = set('AC AL AP AM BA CE DF ES GO MA MT MS MG PA PB PR PE PI RJ RN RS RO RR SC SP SE TO'.split())
# nomes que sozinhos sao de outro clube famoso, de selecao ou de mais de um clube
SEM_PALPITE = set('penarol river guarany gloria santana serra costa-rica nacao sao-francisco olimpia'.split())
ESTADUAIS = dict(paulista='SP', carioca='RJ', gaucho='RS', mineiro='MG', paranaense='PR', catarinense='SC',
    pernambucano='PE', baiano='BA', cearense='CE', goiano='GO', maranhense='MA', potiguar='RN', paraense='PA',
    alagoano='AL', sergipano='SE', paraibano='PB', piauiense='PI', capixaba='ES', amazonense='AM')
EXCLUIR = {'amistosos-da-selecao-brasileira'}
EXTRAS = ('paranaense catarinense pernambucano baiano cearense goiano paraense potiguar alagoano '
          'sergipano paraibano maranhense piauiense capixaba mato-grossense sul-mato-grossense '
          'amazonense brasiliense candango tocantinense rondoniense acreano amapaense roraimense '
          'copa-verde copa-paulista paulista-a2 serie-a2 brasileirao-feminino copa-do-brasil-sub-20 '
          'brasileirao-sub-17 copa-do-brasil-sub-17 paulista-serie-a2 carioca-serie-a2 gaucho-a2 copa-fgf').split()
STOP = set('''FC F C SC EC AC AA CA CR SE SER S A AD CN GE UA CF CD FBC FBPA FPA ECF FE CP SS SAD SA LTDA
LIMITADA FUTEBOL FOOTBALL FOOT BALL CLUBE CLUB ESPORTE ESPORTES ESPORTIVO ASSOCIACAO ATLETICA SOCIEDADE
ESPORTIVA DE DO DA DOS DAS E THE OF SPORT SPORTS SPORTING SUB 17 20 23 FEMININO'''.split())
ABREV = {'POR': 'PORTUGUESA', 'ATL': 'ATLETICO', 'STA': 'SANTA', 'STO': 'SANTO', 'S': 'SAO'}
GENERICOS = set('''AMERICA AMERICANO NACIONAL ATLETICO INDEPENDENTE INTERNACIONAL PORTUGUESA JUVENTUS
OPERARIO GUARANI BOTAFOGO UNIAO CRUZEIRO FERROVIARIO PALMEIRAS GREMIO VASCO ESTRELA REAL CAPITAL TIRADENTES
PRINCESA GLOBO OESTE DESPORTIVA'''.split()) | {'SAO JOSE', 'RIO BRANCO', 'SANTA CRUZ', 'SAO BENTO', 'VILA NOVA',
                                               'SAO PAULO', 'SAO RAIMUNDO'}


def norm(s):
    """O nome normalizado do indice (Escudos.Normalizar no app)."""
    s = ''.join(c for c in unicodedata.normalize('NFD', s) if unicodedata.category(c) != 'Mn').upper()
    return ' '.join(re.sub(r'[^A-Z0-9]', ' ', s).split())


def sem_uf(nome):
    """(nome sem o UF no fim, UF ou None): 'CRATO EC (CE)', 'AMERICA-RN', 'ALTOS PI'."""
    n = nome.strip()
    m = re.search(r'^(.*?)[\s(/-]+([A-Za-z]{2})\)?\s*$', n)
    if m and m.group(2).upper() in UFS and norm(m.group(1)):
        return m.group(1), m.group(2).upper()
    return n, None


def nucleo(nome):
    toks = [ABREV.get(t, t) for t in norm(nome).split()]
    return ' '.join(t for t in toks if t not in STOP)


def gravar(caminho, dados):
    os.makedirs(os.path.dirname(caminho), exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=os.path.dirname(caminho), suffix='.tmp')
    with os.fdopen(fd, 'wb') as f:
        f.write(dados)
    os.replace(tmp, caminho)


class Rede:
    def __init__(self, cache):
        self.cache, self.ultimo, self.pedidos = cache, 0.0, 0

    def pegar(self, url, arq):
        """conteudo (cache em arq) ou None se 404."""
        p = os.path.join(self.cache, arq)
        if os.path.exists(p):
            return open(p, 'rb').read()
        if os.path.exists(p + '.404'):
            return None
        esperas = [30, 60, 120, 240, 480]
        while True:
            dt = time.time() - self.ultimo
            if dt < PAUSA:
                time.sleep(PAUSA - dt)
            self.ultimo = time.time()
            self.pedidos += 1
            try:
                with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent': UA}), timeout=60) as r:
                    dados = r.read()
                    tipo = r.headers.get('Content-Type', '')
                if arq.endswith('.png') and not tipo.startswith('image/'):
                    gravar(p + '.404', b'')
                    return None
                gravar(p, dados)
                return dados
            except urllib.error.HTTPError as e:
                if e.code == 404:
                    gravar(p + '.404', b'')
                    return None
                if e.code in (429, 503) and esperas:
                    ra = e.headers.get('Retry-After')
                    w = int(ra) if ra and ra.isdigit() else esperas[0]
                    esperas.pop(0)
                    print(f'  {e.code}: esperando {w} s', flush=True)
                    time.sleep(w)
                    continue
                raise
            except urllib.error.URLError:
                if esperas:
                    time.sleep(esperas.pop(0))
                    continue
                raise


# ---------------------------------------------------------------- 1. campeonatos
def campeonatos(rede, extras):
    h = rede.pegar(BASE, 'campeonatos.html').decode('utf-8')
    lst = []
    for m in re.finditer(r'\{"title":"([^"]*)","group":"nacionais","items":\[(.*?)\]\}', h):
        for it in re.finditer(r'"description":"([^"]*)","slug":"([^"]*)"', m.group(2)):
            if it.group(2) not in EXCLUIR and it.group(2) not in [s for s, _ in lst]:
                lst.append((it.group(2), it.group(1)))
        if len(lst) >= 15:
            break
    if extras:
        lst += [(s, s) for s in EXTRAS if s not in [x for x, _ in lst]]
    return lst


def equipas_do_campeonato(rede, slug):
    """[(nome, slug do brasao, UF ou None, apelidos)] de todas as fases, ou None (404).
    Dois formatos de pagina: o novo ("football-team-<id>" e os jogos, com o estado do estadio)
    e o antigo, das edicoes 2025 dos estaduais ("equipes": nome, nome-comum, brasao)."""
    b = rede.pegar(BASE + slug + '/', f'camp-{slug}.html')
    if b is None:
        return None
    h = b.decode('utf-8')
    eq = {}
    for m in re.finditer(r'"football-team-(\d+)":(\{[^{}]*\})', h):
        try:
            d = json.loads(m.group(2))
        except ValueError:
            continue
        if not d.get('national') and d.get('slug'):
            eq[m.group(1)] = (html.unescape(d['name']).strip(), d['slug'], set())
    casa = collections.defaultdict(collections.Counter)
    for m in re.finditer(r'"teams":\{"home":(\d+),"away":\d+\}.{0,200}?"location":\{"city":"[^"]*","state":"([A-Z]{2})"', h):
        casa[m.group(1)][m.group(2)] += 1
    # formato antigo (JSON dentro de atributos HTML)
    hu = html.unescape(h)
    for m in re.finditer(r'\{"id":(\d+),"nome":"([^"]*)","nome-comum":"([^"]*)","nome-slug":"[^"]*"[^{}]*?'
                         r'"tipo":"clube"[^{}]*?"brasao":"[^"]*/brasoes/\d+x\d+/([a-z0-9-]+)\.png"', hu):
        eq['a' + m.group(1)] = (m.group(3).strip(), m.group(4), {m.group(2).strip()})
    # jogos sem a equipa no JSON: o slug do link do placar (nome = slug)
    slugs = {e[1] for e in eq.values()}
    for m in re.finditer(r'placar\.uol\.com\.br/esporte/futebol/[^/"]+/\d{4}/\d\d/\d\d/([a-z0-9-]+?)-x-([a-z0-9-]+)\.htm', h):
        for sl in m.groups():
            if sl not in slugs:
                slugs.add(sl)
                eq['s:' + sl] = (sl.replace('-', ' '), sl, set())
    uf_camp = ESTADUAIS.get(slug)
    return [(nome, sl, (casa[i].most_common(1)[0][0] if casa[i] else None) or uf_camp, ap)
            for i, (nome, sl, ap) in eq.items()]


# ---------------------------------------------------------------- 2. alvos
def ler_indice(caminho):
    ind, linhas = {}, []
    for l in open(caminho, encoding='utf-8'):
        linhas.append(l)
        if l.startswith('#') or not l.strip():
            continue
        p = l.rstrip('\n').split('\t')
        ind[(p[0], p[1])] = p[2]
    return ind, linhas


def tem_logo(ind, nomes):
    for n in nomes:
        a = ind.get(('BRA', norm(n)))
        if a is not None:
            return a != '-'
    return False


def alvos_publicos(app, ind):
    out = []
    pasta = os.path.join(app, 'Dados', 'teams', 'BRA')
    for f in sorted(os.listdir(pasta)) if os.path.isdir(pasta) else []:
        d = json.load(open(os.path.join(pasta, f), encoding='utf-8'))
        nomes = (d.get('name', ''), d.get('shortName', ''))
        if not tem_logo(ind, nomes):
            out.append(nomes)
    for (pais, n), a in ind.items():
        if pais == 'BRA' and a == '-':
            out.append((n, n))
    return out


def alvos_locais(arqs, ind):
    out = []
    for arq in arqs:
        for l in open(arq, encoding='utf-8'):
            if l.startswith('#') or not l.strip():
                continue
            p = l.rstrip('\n').split('\t')
            if len(p) >= 5 and p[2] == 'BRA' and p[0] == 'equipa':
                nomes = (p[3], p[4])
            elif len(p) >= 3 and p[0] == 'BRA':
                nomes = (p[1], p[2])
            else:
                continue
            if not tem_logo(ind, nomes):
                out.append(nomes)
    return out


# ---------------------------------------------------------------- 3. casamento
def chaves_uol(uol):
    """nucleo -> [(slug, nome, uf)] com o nucleo do nome e o do slug."""
    ch = collections.defaultdict(dict)
    for slug, (nome, uf_casa, _camps, apelidos) in uol.items():
        base, uf = sem_uf(nome)
        sbase, suf = sem_uf(slug.replace('-', ' '))
        uf = uf or suf or uf_casa
        for k in {nucleo(base), nucleo(sbase)} | {nucleo(sem_uf(x)[0]) for x in apelidos}:
            if k:
                ch[k][slug] = (nome, uf)
    return ch


def uf_de(nomes):
    for n in nomes:
        u = sem_uf(n)[1]
        if u:
            return u
    return None


def casar_chave(k, uf, ch):
    """o resultado de um nucleo: ('ok', slug) | ('ambiguo', nota) | None (sem candidato)."""
    if not k or k not in ch:
        return None
    cands = ch[k]
    if uf:
        ok = {s: v for s, v in cands.items() if v[1] == uf}
        sem = {s: v for s, v in cands.items() if v[1] is None}
        if not ok and sem:
            return 'ambiguo', f'{k} ({uf}): UOL sem estado: ' + ', '.join(sorted(sem))
        cands = ok
    elif k in GENERICOS or len(k) < 4:
        return 'ambiguo', f'{k} sem UF: ' + ', '.join(f'{s}({v[1]})' for s, v in sorted(cands.items()))
    if not cands:
        return None
    # o mesmo clube em varios slugs (sub-20, feminino): fica o slug mais curto sem "sub"
    principais = {s: v for s, v in cands.items() if not re.search(r'(^|-)(sub|fem)', s)} or cands
    if len({(nucleo(sem_uf(v[0])[0]), v[1]) for v in principais.values()}) == 1:
        return 'ok', min(principais, key=len)
    return 'ambiguo', f'{k}: ' + ', '.join(f'{s}({v[1]})' for s, v in sorted(principais.items()))


def casar(nomes, ch):
    """('ok', slug) | ('ambiguo', nota) | ('nao', ''). Cada nome (completo, curto) da um
    resultado; resultados diferentes = ambiguo, a nao ser que o divergente seja de um nucleo
    generico (AA PORTUGUESA (SP) / POR. SANTISTA: fica portuguesa-santista)."""
    uf = uf_de(nomes)
    res = {}
    for n in nomes:
        k = nucleo(sem_uf(n)[0])
        r = casar_chave(k, uf, ch)
        if r:
            res[k] = r
    if not res:
        return 'nao', ''
    especificos = {k: r for k, r in res.items() if k not in GENERICOS} or res
    oks = {r[1] for r in especificos.values() if r[0] == 'ok'}
    amb = [r[1] for r in especificos.values() if r[0] == 'ambiguo']
    if len(oks) == 1 and not amb:
        return 'ok', oks.pop()
    if len(oks) > 1:
        return 'ambiguo', 'nomes discordam: ' + ', '.join(sorted(oks))
    return 'ambiguo', '; '.join(amb + sorted(oks))


CLUBE = set('''FC F C SC EC AC AA CA CR SE SER AD CN GE UA CF CD FBC ECF FE CP SAD SA LTDA LIMITADA FUTEBOL
FOOTBALL CLUBE CLUB ESPORTE ESPORTES ASSOCIACAO SOCIEDADE ESPORTIVA ESPORTIVO SPORT'''.split())


def palpites(nomes, uol, existentes):
    """slugs a tentar no UOL para uma equipa que nao esta em nenhum campeonato de agora:
    <nome>-<uf> (seguro: o UF esta no slug) e, para nome nao generico de 5+ letras que nao
    e de nenhuma equipa conhecida (do UOL ou ja com logo no jogo), <nome> sozinho."""
    uf = uf_de(nomes)
    bases = []
    for n in nomes:
        b = sem_uf(n)[0]
        for v in (nucleo(b), ' '.join(t for t in norm(b).split() if t not in CLUBE)):
            if v and v not in bases:
                bases.append(v)
    out = []
    for b in bases:
        sl = b.lower().replace(' ', '-')
        if uf:
            out.append((sl + '-' + uf.lower(), True))
    for b in bases:
        sl = b.lower().replace(' ', '-')
        if (b not in GENERICOS and len(b.replace(' ', '')) >= 5 and sl not in SEM_PALPITE
                and not any(e == sl or e.startswith(sl + '-') for e in set(existentes) | set(uol))):
            out.append((sl, False))
    vistos, res = set(), []
    for sl, seg in out:
        if sl not in vistos:
            vistos.add(sl)
            res.append((sl, seg))
    return res


# ---------------------------------------------------------------- 4/5. imagem
def tirar_fundo(im):
    """fundo uniforme (os 4 cantos opacos e da mesma cor) vira transparente, por preenchimento."""
    px = im.load()
    w, h = im.size
    cantos = [px[0, 0], px[w - 1, 0], px[0, h - 1], px[w - 1, h - 1]]
    if any(c[3] < 250 for c in cantos):
        return im
    ref = cantos[0]
    perto = lambda c: c[3] > 200 and sum(abs(c[i] - ref[i]) for i in range(3)) < 40
    if not all(perto(c) for c in cantos):
        return im
    vis, pilha = set(), [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]
    while pilha:
        x, y = pilha.pop()
        if (x, y) in vis or not (0 <= x < w and 0 <= y < h) or not perto(px[x, y]):
            continue
        vis.add((x, y))
        pilha += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]
    for x, y in vis:
        px[x, y] = (0, 0, 0, 0)
    return im


def ampliar(im, lado):
    esr = shutil.which('realesrgan-ncnn-vulkan')
    if esr and max(im.size) * 2 <= lado * 2:
        with tempfile.TemporaryDirectory() as d:
            im.save(os.path.join(d, 'a.png'))
            r = subprocess.run([esr, '-i', os.path.join(d, 'a.png'), '-o', os.path.join(d, 'b.png'), '-s', '4'],
                               capture_output=True)
            if r.returncode == 0 and os.path.exists(os.path.join(d, 'b.png')):
                im = Image.open(os.path.join(d, 'b.png')).convert('RGBA')
    f = lado / max(im.size)
    tam = (max(1, round(im.width * f)), max(1, round(im.height * f)))
    # pre-multiplica o alfa para a borda nao ganhar halo escuro
    pm = im.convert('RGBa').resize(tam, Image.LANCZOS)
    out = pm.convert('RGBA')
    rgb = out.convert('RGB').filter(ImageFilter.UnsharpMask(radius=1.2, percent=60, threshold=2))
    out = Image.merge('RGBA', (*rgb.split(), out.split()[3]))
    return out


def converter(dados):
    import io
    im = tirar_fundo(Image.open(io.BytesIO(dados)).convert('RGBA'))
    a = im.split()[3].point(lambda v: 255 if v > 8 else 0)
    bb = a.getbbox()
    if not bb:
        return None
    im = ampliar(im.crop(bb), TAM)
    tela = Image.new('RGBA', (TAM, TAM), (0, 0, 0, 0))
    tela.alpha_composite(im, ((TAM - im.width) // 2, (TAM - im.height) // 2))
    return tela


def metodo():
    return 'realesrgan-ncnn-vulkan + Lanczos' if shutil.which('realesrgan-ncnn-vulkan') else \
        'Pillow Lanczos (alfa pre-multiplicado) + UnsharpMask(1.2, 60, 2)'


# ---------------------------------------------------------------- principal
def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--app', default=os.path.join(RAIZ, 'RetroFoot.App', 'Assets'))
    lanc = os.path.expanduser(os.environ.get('RETROFOOT_ESCUDOS_LOCAL', '~/.cache/retrofoot-uol/local'))
    ap.add_argument('--alvos', nargs='*', default=[p for p in [os.path.join(lanc, 'relatorio.tsv')] if os.path.exists(p)])
    ap.add_argument('--local', default=lanc if os.path.isdir(lanc) else os.path.expanduser('~/.cache/retrofoot-uol'))
    ap.add_argument('--cache', default=os.path.expanduser('~/.cache/retrofoot-uol'))
    ap.add_argument('--extras', action='store_true', help='tenta tambem outros estaduais pelo slug')
    ap.add_argument('--so-casar', action='store_true', help='nao baixa logos nem grava nada no app')
    ap.add_argument('--sem-palpites', action='store_true', help='nao tenta slugs deduzidos do nome')
    ap.add_argument('--max', type=int, default=0, help='no maximo N logos baixados nesta rodada (0 = todos)')
    a = ap.parse_args()
    rede = Rede(a.cache)
    escudos = os.path.join(a.app, 'Escudos')
    ind, linhas = ler_indice(os.path.join(escudos, 'escudos.tsv'))

    # 1
    uol, por_camp = {}, []
    for slug, desc in campeonatos(rede, a.extras):
        eqs = equipas_do_campeonato(rede, slug)
        if not eqs:
            continue
        por_camp.append((desc, len(eqs)))
        print(f'{desc}: {len(eqs)} equipas', flush=True)
        for nome, s, uf, ap in eqs:
            n0, u0, c0, a0 = uol.get(s, (nome, uf, [], set()))
            uol[s] = (n0 if n0 != s.replace('-', ' ') else nome, u0 or uf, c0 + [slug], a0 | ap)
    print(f'UOL: {len(uol)} equipas distintas em {len(por_camp)} campeonatos')

    # 2, 3
    ch = chaves_uol(uol)
    pub, loc = alvos_publicos(a.app, ind), alvos_locais(a.alvos, ind)
    res = []
    for origem, lst in (('publico', pub), ('local', loc)):
        vistos = set()
        for nomes in lst:
            if nomes in vistos:
                continue
            vistos.add(nomes)
            res.append((origem, nomes, *casar(nomes, ch)))
    # os sem candidato: o slug deduzido do nome, se o UOL tem o brasao (um pedido por slug)
    if not a.so_casar and not a.sem_palpites:
        existentes = {f[7:-4] for f in os.listdir(escudos) if f.startswith('brazil_') and f.endswith('.png')}
        for i, (origem, nomes, r, x) in enumerate(res):
            if r != 'nao':
                continue
            for sl, seguro in palpites(nomes, uol, existentes):
                if rede.pegar(BRASAO.format(sl), os.path.join('png', sl + '.png')):
                    res[i] = (origem, nomes, 'palpite', sl)
                    break
    cont = collections.Counter((o, r) for o, _, r, _ in res)
    print('casamento:', dict(cont))

    # 4, 5
    hoje = date.today().isoformat()
    fontes_p = os.path.join(AQUI, 'uol-fontes.tsv')
    fontes = {}
    if os.path.exists(fontes_p):
        for l in open(fontes_p, encoding='utf-8'):
            if not l.startswith('#') and l.strip():
                fontes[l.split('\t')[0]] = l
    slugs = sorted({x for _, _, r, x in res if r in OK})
    arquivo, novos, existentes, falhas = {}, [], [], []
    for s in slugs:
        arq = f'brazil_{s.strip("-")}.png'
        destino = os.path.join(escudos, arq)
        if os.path.exists(destino):
            if arq in fontes:
                arquivo[s] = arq
            else:
                existentes.append(arq)
                arquivo[s] = arq
            continue
        if a.so_casar or (a.max and len(novos) >= a.max):
            continue
        dados = rede.pegar(BRASAO.format(s), os.path.join('png', s + '.png'))
        im = converter(dados) if dados else None
        if im is None:
            falhas.append(s)
            continue
        buf = __import__('io').BytesIO()
        im.save(buf, 'PNG', optimize=True)
        gravar(destino, buf.getvalue())
        arquivo[s] = arq
        novos.append(arq)
        nome, uf, camps, _ = uol.get(s, ('', None, [], set()))
        camps = ','.join(sorted(set(camps))) or '(slug deduzido do nome; fora dos campeonatos atuais)'
        fontes[arq] = f'{arq}\t{nome}\t{s}\t{BRASAO.format(s)}\t{camps}\t{hoje}\n'
    if a.so_casar:
        arquivo = {s: f'brazil_{s.strip("-")}.png' for s in slugs}

    # indices e relatorios
    if not a.so_casar:
        novo_ind = dict(ind)
        for origem, nomes, r, s in res:
            if origem == 'publico' and r in OK and s in arquivo:
                for n in nomes:
                    if norm(n) and novo_ind.get(('BRA', norm(n)), '-') == '-':
                        novo_ind[('BRA', norm(n))] = arquivo[s]
        # os nomes do UOL (publicos) dos logos novos: as equipas de pacotes acham pelo nome curto
        for s, arq in arquivo.items():
            if arq not in fontes or s not in uol:
                continue
            k = norm(uol[s][0])
            base, uf = sem_uf(uol[s][0])
            if k and ('BRA', k) not in novo_ind and (uf or nucleo(base) not in GENERICOS):
                novo_ind[('BRA', k)] = arq
        cab = [l for l in linhas if l.startswith('#')]
        corpo = sorted('\t'.join((*k, v)) + '\n' for k, v in novo_ind.items())
        gravar(os.path.join(escudos, 'escudos.tsv'), ''.join(cab + corpo).encode('utf-8'))
        gravar(fontes_p, ('# arquivo em RetroFoot.App/Assets/Escudos, nome no UOL, slug, url, campeonatos do UOL, data '
                          '(tools/logos/uol.py; logos do UOL Esporte)\n' + ''.join(fontes[k] for k in sorted(fontes))).encode('utf-8'))
        # indice completo local (com os nomes dos alvos locais): fora do git
        comp = os.path.join(a.local, 'escudos-completo.tsv')
        if os.path.exists(comp):
            ci, cl = ler_indice(comp)
            for k, v in novo_ind.items():
                if ci.get(k, '-') == '-':
                    ci[k] = v
            for origem, nomes, r, s in res:
                if r in OK and s in arquivo:
                    for n in nomes:
                        if norm(n) and ci.get(('BRA', norm(n)), '-') == '-':
                            ci[('BRA', norm(n))] = arquivo[s]
            gravar(comp, ''.join([l for l in cl if l.startswith('#')] +
                                 sorted('\t'.join((*k, v)) + '\n' for k, v in ci.items())).encode('utf-8'))
    os.makedirs(a.local, exist_ok=True)
    with open(os.path.join(a.local, 'uol-relatorio.tsv'), 'w', encoding='utf-8') as f:
        f.write('# origem, resultado (ok/ambiguo/nao), nome completo, nome curto, slug do UOL ou nota\n')
        for origem, nomes, r, x in sorted(res, key=lambda t: (t[2], t[1])):
            f.write(f'{origem}\t{r}\t{nomes[0]}\t{nomes[1]}\t{x}\n')
    with open(os.path.join(a.local, 'uol-equipas.tsv'), 'w', encoding='utf-8') as f:
        f.write('# slug, nome, UF, campeonatos (UOL Esporte)\n')
        for s in sorted(uol):
            f.write(f'{s}\t{uol[s][0]}\t{uol[s][1] or ""}\t{",".join(sorted(set(uol[s][2])))}\t{"; ".join(sorted(uol[s][3]))}\n')
    print(f'logos: {len(slugs)} casados, {len(novos)} novos, {len(existentes)} ja existiam, '
          f'{len(falhas)} falharam {falhas}; {rede.pedidos} pedidos; metodo: {metodo()}')
    print(f'relatorio: {os.path.join(a.local, "uol-relatorio.tsv")}')


if __name__ == '__main__':
    main()
