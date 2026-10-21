using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RetroFoot.Motor.Partida;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A jornada no visual Moderno (Visual.cs), no estilo dos aplicativos de resultados: fundo
// escuro, os jogos de cada divisao (e da Taca) num cartao arredondado com o logo e o nome da
// liga no cabecalho; cada jogo numa linha "Casa [logo] placar [logo] Fora", o placar no
// centro com o minuto (ou "Fim") em cima e o cartao vermelho ao lado do placar da equipa que
// teve um expulso. Como no original, o ultimo lance de cada jogo (golo, cartao, lesao,
// anulado) aparece ao lado, um de cada vez, enquanto os jogos correm.
//
// Usa o mesmo instantaneo (Linha) e o mesmo empacotamento (Empacotar: uma divisao nao se
// parte entre colunas quando cabe numa) da tela original: 4:3 numa coluna com paginas de 20
// linhas; 16:9 em colunas. O relogio vira a barra de progresso do topo e as setas das paginas
// vao para o topo, ao lado do minuto.
public sealed partial class TelaJornada
{
    const double MTopo = 46, MMargem = 10, MBase = 8;
    // a altura de cada linha: 27 pontos, mais com o texto acima de 100% (Acessibilidade), para
    // o minuto e o placar caberem
    static double MPasso => Math.Round(27 * Math.Max(1, Acessibilidade.Texto));
    const double MLarguraColuna = 620;

    // as cores sao as do TemaVerde (o tema do Moderno); o relogio ao vivo e amarelo
    static readonly IBrush MFundo = TemaVerde.FundoTela, MBarra = TemaVerde.Barra, MCartao = TemaVerde.Cartao;
    static readonly IBrush MLinha = TemaVerde.Linha, MTexto = TemaVerde.Texto, MFraco = TemaVerde.TextoFraco;
    static readonly IBrush MVivo = TemaVerde.Destaque, MProrrog = TemaVerde.Roxo, MBotao = TemaVerde.CartaoForte;
    static readonly IPen MBorda = new Pen(TemaVerde.Borda, 1);
    static readonly FontFamily Inter = new("fonts:Inter#Inter");

    double passoM = 27, passoBase = 27;
    Rect proximaM, anteriorM;

    static readonly IBrush FundoLogo = TemaVerde.DiscoLogo;

    static IBrush Hex(string s) => new SolidColorBrush(Color.Parse(s));

    static FormattedText TxtM(string t, double tam, IBrush cor, bool negrito = false) =>
        new(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Inter, FontStyle.Normal, negrito ? FontWeight.Bold : FontWeight.Normal), tam * Acessibilidade.Texto, cor);

    static double LarguraModerna(int c) => c == 1 ? L : c * MLarguraColuna;
    static double AlturaModerna(int r) => MTopo + 6 + MPasso * r + MBase;

    // como Arrumar: 4:3 numa coluna com paginas de 20 linhas; 16:9 com o numero de colunas
    // que deixa as linhas maiores, ou paginas quando nem assim fica legivel
    Arranjo ArrumarModerno(Size janela)
    {
        double bw = janela.Width > 0 ? janela.Width : 1600, bh = janela.Height > 0 ? janela.Height : 1000;
        int n = Math.Max(linhas.Count, 1);
        passoBase = MPasso;
        if (!Formato.Largo)
        {
            // 20 linhas por pagina; com divisoes de 10 jogos (11 linhas), 22 linhas um pouco
            // mais baixas para caberem duas divisoes por pagina
            int bloco = 0;
            for (int i = 0; i < linhas.Count; i++)
                if (linhas[i].Cabecalho != null)
                {
                    int b = 1;
                    while (i + b < linhas.Count && linhas[i + b].Cabecalho == null) b++;
                    bloco = Math.Max(bloco, b);
                }
            int r = LinhasPorPagina;
            if (2 * bloco > r && 2 * bloco <= 22) r = 2 * bloco;
            passoBase = Math.Min(MPasso, Math.Floor((A - (MTopo + 6 + MBase)) / r * 2) / 2);
            return new Arranjo { C = 1, R = r, Colunas = Empacotar(r), W = L, Hq = A };
        }
        Arranjo? melhor = null;
        double escala = 0;
        for (int c = 1; c <= 8; c++)
        {
            int r = Math.Max(2, (n + c - 1) / c);
            List<Coluna> cols;
            while ((cols = Empacotar(r)).Count > c) r++;
            double e = Math.Min(bw / LarguraModerna(c), bh / Math.Max(A, AlturaModerna(r)));
            if (e > escala + 1e-9)
            {
                escala = e;
                melhor = new Arranjo { C = c, R = r, Colunas = cols, W = LarguraModerna(c), Hq = Math.Max(A, AlturaModerna(r)) };
            }
            if (cols.Count < c) break;
        }
        if (melhor == null || escala < EscalaMinima)
        {
            double lw = bw / EscalaMinima, lh = bh / EscalaMinima;
            int c = lw >= 2 * MLarguraColuna ? (int)(lw / MLarguraColuna) : 1;
            int r = Math.Max(2, (int)((lh - (MTopo + 6 + MBase)) / MPasso));
            melhor = new Arranjo { C = c, R = r, Colunas = Empacotar(r), W = LarguraModerna(c), Hq = Math.Max(A, AlturaModerna(r)) };
        }
        return melhor;
    }

    // ---------------------------------------------------------------- desenho

    bool Terminado => acabou || minuto > fim;

    // o minuto do relogio: "Fim", "0'", "45'" ou, nos acrescimos, "45'+2'"
    string StatusRelogio => Terminado ? T("Fim") : minuto <= 0 ? "0'" : MinutoTexto.Texto(minuto, acrescimo);

    // o minuto de um jogo: nos acrescimos, o jogo que ja acabou os seus fica no intervalo ou
    // no fim (os que vao a prorrogacao da Taca, no ultimo acrescimo deles)
    string StatusDaLinha(Linha l, string status)
    {
        if (Terminado || acrescimo <= 0 || acrescimo <= l.Acrescimos) return status;
        if (minuto == 45) return T("Intervalo");
        if (minuto == 90 && taca && l.GC == l.GF) return MinutoTexto.Texto(minuto, l.Acrescimos);
        return T("Fim");
    }

    void DesenharModerno(DrawingContext dc)
    {
        var a = arranjo!;
        Ret(dc, MFundo, 0, 0, QL, QA);
        // quando a janela e mais alta que o arranjo, as linhas crescem ate 8 pontos
        passoM = passoBase + Math.Clamp(Math.Floor((QA - (MTopo + 6 + MBase + passoBase * a.R)) / a.R), 0, 8);
        double lc = QL / a.C;
        for (int k = pagina * a.C; k < Math.Min(a.Colunas.Count, (pagina + 1) * a.C); k++)
        {
            double x0 = Math.Round((k - pagina * a.C) * lc), w = Math.Round((k - pagina * a.C + 1) * lc) - x0;
            DesenharColunaModerna(dc, a.Colunas[k], x0, w);
        }
        BarraModerna(dc);
    }

    // os cartoes de uma coluna: cada cabecalho abre um; uma coluna que comeca por um jogo
    // (continuacao de uma divisao, ou a Taca sem cabecalho) abre o seu na linha em branco de
    // cima, com o nome do ultimo cabecalho antes dela
    void DesenharColunaModerna(DrawingContext dc, Coluna col, double x0, double w)
    {
        double y0 = MTopo + 6;
        var cartoes = new List<(int Ini, int Fim, string Nome, Bitmap? Logo)>();
        if (col.Itens.Count == 0) return;
        if (col.Topo)
        {
            string? nome = null; Bitmap? logo = null;
            for (int j = col.Itens[0].Item - 1; j >= 0; j--)
                if (linhas[j].Cabecalho != null) { nome = linhas[j].Cabecalho; logo = linhas[j].LogoCabecalho; break; }
            if (nome == null) { nome = taca ? T("Taça") : titulo; logo = taca ? Escudos.Taca(s.S) : null; }
            cartoes.Add((0, 1, TituloNoIdioma(nome).Trim(), logo));
        }
        foreach (var (i, li) in col.Itens)
        {
            if (linhas[i].Cabecalho != null) cartoes.Add((li, li + 1, TituloNoIdioma(linhas[i].Cabecalho!).Trim(), linhas[i].LogoCabecalho));
            else if (cartoes.Count > 0) cartoes[^1] = cartoes[^1] with { Fim = li + 1 };
        }
        double cx = x0 + MMargem, cw = w - 2 * MMargem;
        string status = StatusRelogio;
        var corStatus = Terminado ? MFraco : minuto > 90 ? MProrrog : MVivo;
        foreach (var (ini, fimC, nome, logo) in cartoes)
        {
            // o cartao vai ate o fim da ultima linha (antes parava 3 pontos antes e o placar da
            // ultima linha encostava na borda); o proximo comeca 3 pontos abaixo
            var r = new Rect(cx, y0 + passoM * ini + 3, cw, passoM * (fimC - ini) - 3);
            dc.DrawRectangle(MCartao, MBorda, r.Deflate(0.5), 10, 10);
            // cabecalho: o logo da liga e o nome
            double hy = y0 + passoM * ini;
            double xt = cx + 14;
            if (logo != null)
            {
                double t = Math.Min(18, passoM - 8);
                Imagem(dc, logo, new Rect(xt, hy + (passoM - t) / 2, t, t));
                xt += t + 8;
            }
            var tn = TxtM(nome, 12.5, MTexto, true);
            using (dc.PushClip(new Rect(cx, hy, cw - 10, passoM)))
                dc.DrawText(tn, new Point(xt, hy + (passoM - tn.Height) / 2));
            if (fimC - ini > 1) Ret(dc, MLinha, cx, hy + passoM - 1, cw, 1);
        }
        foreach (var (i, li) in col.Itens)
        {
            var l = linhas[i];
            if (l.Cabecalho != null || l.Vazia) continue;
            double ry = y0 + passoM * li;
            using (dc.PushClip(new Rect(cx, ry, cw, passoM)))
                LinhaModerna(dc, l, cx, cw, ry, StatusDaLinha(l, status), corStatus);
            // separador ate a proxima linha do mesmo cartao
            if (i + 1 < linhas.Count && linhas[i + 1].Cabecalho == null && Contem(col, i + 1))
                Ret(dc, MLinha, cx + 12, ry + passoM - 0.5, cw - 24, 0.5);
        }
    }

    static bool Contem(Coluna col, int item)
    {
        foreach (var (i, _) in col.Itens) if (i == item) return true;
        return false;
    }

    // a largura do lance a direita e a parte do jogo (o placar no centro dela)
    static (double Jogo, double Lance) Partes(double cw)
    {
        double hl = cw >= 520 ? Math.Min(230, Math.Round(cw * 0.32)) : 0;
        return (cw - hl, hl);
    }

    void LinhaModerna(DrawingContext dc, Linha l, double cx, double cw, double ry, string status, IBrush corStatus)
    {
        var (mp, hl) = Partes(cw);
        double mc = cx + mp / 2, meio = ry + passoM / 2;
        // o placar e o minuto em cima
        bool baixa = passoM < 26;
        var st = TxtM(status, baixa ? 8 : 9, corStatus, true);
        var pl = TxtM($"{l.GC} - {l.GF}", baixa ? 14 : 15, MTexto, true);
        double alto = st.Height + pl.Height - 2;
        // com o texto grande (Acessibilidade), o minuto e o placar encolhem ate caber na linha
        if (alto > passoM - 1)
        {
            double f = (passoM - 1) / alto;
            st = TxtM(status, (baixa ? 8 : 9) * f, corStatus, true);
            pl = TxtM($"{l.GC} - {l.GF}", (baixa ? 14 : 15) * f, MTexto, true);
            alto = st.Height + pl.Height - 2;
        }
        double yt = meio - alto / 2 - 1;
        dc.DrawText(st, new Point(mc - st.Width / 2, yt));
        dc.DrawText(pl, new Point(mc - pl.Width / 2, yt + st.Height - 2));
        // os logos (ou um circulo nas cores da equipa)
        double t = Math.Min(18, passoM - 8);
        double meia = Math.Max(pl.Width / 2 + 14, 28);
        double xc = mc - meia - t, xf = mc + meia;
        Logo(dc, l.LogoCasa, l.FundoCasa, l.LetraCasa, new Rect(xc, meio - t / 2, t, t));
        Logo(dc, l.LogoFora, l.FundoFora, l.LetraFora, new Rect(xf, meio - t / 2, t, t));
        // os vermelhos, entre o logo e o placar
        double yv = yt + st.Height - 2 + (pl.Height - 9) / 2;
        for (int k = 0; k < Math.Min(l.VermCasa, 3); k++) Cartao(dc, Vermelho, mc - meia + 3 + 3 * k, yv);
        for (int k = 0; k < Math.Min(l.VermFora, 3); k++) Cartao(dc, Vermelho, mc + meia - 9 - 3 * k, yv);
        // os nomes, a casa encostada ao logo dela
        bool humCasa = l.Jogo?.Treinador[Lado.Casa] is { Humano: not 0 };
        bool humFora = l.Jogo?.Treinador[Lado.Visit] is { Humano: not 0 };
        var nc = TxtM(l.Casa, 13, MTexto, humCasa);
        var nf = TxtM(l.Fora, 13, MTexto, humFora);
        using (dc.PushClip(new Rect(cx + 8, ry, Math.Max(0, xc - 8 - cx - 8), passoM)))
            dc.DrawText(nc, new Point(Math.Max(cx + 10, xc - 8 - nc.Width), meio - nc.Height / 2));
        using (dc.PushClip(new Rect(xf + t + 8, ry, Math.Max(0, cx + mp - 8 - xf - t - 8), passoM)))
            dc.DrawText(nf, new Point(xf + t + 8, meio - nf.Height / 2));
        // o ultimo lance, ao lado
        if (hl <= 0) return;
        Ret(dc, MLinha, cx + mp, ry + 6, 1, passoM - 12);
        if (l.LanceTipo == 0) return;
        double xi = cx + mp + 10, q = Math.Min(14, passoM - 10);
        Icones.Desenhar(dc, l.LanceTipo, l.SegundoAmarelo, new Rect(xi, meio - q / 2, q, q));
        string txt = MinutoTexto.Texto(l.LanceMin, l.LanceAcr) + " " + l.LanceNome.Trim();
        switch (l.LanceTipo)
        {
            case TipoLance.Penalti: txt += " (" + T("pen.") + ")"; break;
            case TipoLance.GolAnulado: txt += " - " + T("Anulado"); break;
        }
        var tl = TxtM(txt, 12, l.LanceTipo == TipoLance.GolAnulado ? MFraco : MTexto);
        dc.DrawText(tl, new Point(xi + q + 7, meio - tl.Height / 2));
    }

    static void Cartao(DrawingContext dc, IBrush cor, double x, double y) =>
        dc.DrawRectangle(cor, null, new Rect(x, y, 6, 9), 1, 1);

    // um logo; o que some no verde (escuro ou verde) com um circulo claro por tras
    internal static void Imagem(DrawingContext dc, Bitmap b, Rect r)
    {
        if (Escudos.PrecisaDisco(b))
        {
            double raio = Math.Max(r.Width, r.Height) / 2 + 2;
            dc.DrawEllipse(FundoLogo, null, r.Center, raio, raio);
        }
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            dc.DrawImage(b, r);
    }

    // o logo da equipa; sem logo, um circulo no fundo da equipa com o aro na cor da letra
    internal static void Logo(DrawingContext dc, Bitmap? b, Color fundo, Color letra, Rect r)
    {
        if (b != null) { Imagem(dc, b, r); return; }
        var c = r.Center;
        double raio = r.Width / 2 - 1;
        dc.DrawEllipse(new SolidColorBrush(fundo), new Pen(new SolidColorBrush(letra), 2), c, raio, raio);
    }

    // a barra do topo: titulo, pausa, paginas, o minuto e o progresso do relogio
    void BarraModerna(DrawingContext dc)
    {
        Ret(dc, MBarra, 0, 0, QL, MTopo);
        var tt = TxtM(titulo.Trim() + (pausado ? "   ·   " + T("Pausa") : ""), 15, MTexto, true);
        // o minuto num balao a direita
        // no fim, o balao vira o botao Continuar (a pilula ouro dos outros ecras; Enter ou clique)
        string st = acabou ? T("Continuar") : StatusRelogio;
        var ts = TxtM(st, 13, acabou || !Terminado ? TemaVerde.SobreDestaque : MTexto, true);
        double bw = Math.Max(ts.Width + (acabou ? 36 : 22), 52), bh = acabou ? 30 : 26, by = (MTopo - 3 - bh) / 2;
        var balao = new Rect(QL - 14 - bw, by, bw, bh);
        dc.DrawRectangle(acabou ? TemaVerde.BotaoPrimario : Terminado ? MBotao : minuto > 90 ? MProrrog : MVivo,
            acabou ? new Pen(TemaVerde.Ouro, 1) : null, balao, bh / 2, bh / 2);
        dc.DrawText(ts, new Point(balao.X + (bw - ts.Width) / 2, balao.Y + (bh - ts.Height) / 2));
        double xd = balao.X - 12;
        if (BotoesVisiveis)
        {
            proximaM = new Rect(xd - 28, by - 1, 28, 28);
            var tp = TxtM($"{pagina + 1}/{UltimaPagina + 1}", 12, MFraco);
            anteriorM = new Rect(proximaM.X - 8 - tp.Width - 8 - 28, by - 1, 28, 28);
            BotaoM(dc, anteriorM, false, pagina > 0);
            dc.DrawText(tp, new Point(anteriorM.Right + 8, MTopo / 2 - 1.5 - tp.Height / 2));
            BotaoM(dc, proximaM, true, pagina < UltimaPagina);
            xd = anteriorM.X - 12;
        }
        using (dc.PushClip(new Rect(0, 0, Math.Max(0, xd), MTopo)))
            dc.DrawText(tt, new Point(16, (MTopo - 3 - tt.Height) / 2));
        // o progresso: 0 a 90 (verde) e, com prorrogacao, 90 a 120 (roxo)
        Ret(dc, MLinha, 0, MTopo - 3, QL, 3);
        double frac = Math.Clamp((double)Math.Max(minuto, 0) / Math.Max(fim, 1), 0, 1);
        Ret(dc, Terminado ? MFraco : minuto > 90 ? MProrrog : MVivo, 0, MTopo - 3, QL * frac, 3);
    }

    static void BotaoM(DrawingContext dc, Rect r, bool direita, bool ativo)
    {
        dc.DrawRectangle(MBotao, null, r, 14, 14);
        var c = r.Center;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            double d = direita ? 1 : -1;
            ctx.BeginFigure(new Point(c.X - 3 * d, c.Y - 6), false);
            ctx.LineTo(new Point(c.X + 3 * d, c.Y));
            ctx.LineTo(new Point(c.X - 3 * d, c.Y + 6));
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(ativo ? MTexto : MLinha, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g);
    }

    // ---------------------------------------------------------------- clique

    void CliqueModerno(Point p)
    {
        if (BotoesVisiveis && proximaM.Contains(p)) { MudarPagina(1); return; }
        if (BotoesVisiveis && anteriorM.Contains(p)) { MudarPagina(-1); return; }
        if (!acabou && EquipaModerna(p) is var (jg, lado)) { LigacaoPartida.PedirDetalhes(s, jg, lado); return; }
        if (acabou) Seguir();
    }

    // o jogo e o lado (a metade da linha) no ponto p
    (JogoAtual, int)? EquipaModerna(Point p)
    {
        var a = arranjo;
        if (a == null) return null;
        double y0 = MTopo + 6, lc = QL / a.C;
        int li = (int)Math.Floor((p.Y - y0) / passoM);
        if (p.Y < y0 || li < 0) return null;
        for (int k = pagina * a.C; k < Math.Min(a.Colunas.Count, (pagina + 1) * a.C); k++)
        {
            double x0 = Math.Round((k - pagina * a.C) * lc), w = Math.Round((k - pagina * a.C + 1) * lc) - x0;
            double cx = x0 + MMargem, cw = w - 2 * MMargem;
            if (p.X < cx || p.X >= cx + cw) continue;
            var (mp, _) = Partes(cw);
            if (p.X >= cx + mp) return null;
            foreach (var (i, k2) in a.Colunas[k].Itens)
                if (k2 == li && linhas[i].Jogo is { } jg && linhas[i].Cabecalho == null && !linhas[i].Vazia)
                    return (jg, p.X < cx + mp / 2 ? Lado.Casa : Lado.Visit);
        }
        return null;
    }
}

// Os icones dos lances no visual Moderno (a jornada e os detalhes do jogo), desenhados (a
// fonte pode nao ter os simbolos): bola (golo, penalti), bola riscada (anulado), cartao
// amarelo, vermelho (com o amarelo atras quando veio do 2o amarelo) e a cruz da lesao.
static class Icones
{
    static readonly IBrush Amarelo = new SolidColorBrush(Color.FromRgb(0xfb, 0xc0, 0x2d));
    static readonly IBrush Vermelho = new SolidColorBrush(Color.FromRgb(0xe5, 0x39, 0x35));
    static readonly IBrush Escuro = new SolidColorBrush(Color.FromRgb(0x20, 0x21, 0x24));
    static readonly IBrush Cinza = new SolidColorBrush(Color.FromRgb(0x9a, 0xa0, 0xa6));

    public static void Desenhar(DrawingContext dc, int tipo, bool segundoAmarelo, Rect r)
    {
        switch (tipo)
        {
            case TipoLance.Gol:
            case TipoLance.Penalti:
                Bola(dc, r, Brushes.White);
                break;
            case TipoLance.GolAnulado:
                Bola(dc, r, Cinza);
                dc.DrawLine(new Pen(Vermelho, 2), new Point(r.X + 1, r.Bottom - 1), new Point(r.Right - 1, r.Y + 1));
                break;
            case TipoLance.Amarelo:
                Cartao(dc, Amarelo, r, 0);
                break;
            case TipoLance.Expulsao:
                if (segundoAmarelo) Cartao(dc, Amarelo, r, -2.5);
                Cartao(dc, Vermelho, r, segundoAmarelo ? 1.5 : 0);
                break;
            case TipoLance.Lesao:
                double q = Math.Min(r.Width, r.Height), b = Math.Max(2, Math.Round(q * 0.28));
                var c = r.Center;
                dc.DrawRectangle(Vermelho, null, new Rect(c.X - q / 2 + 1, c.Y - b / 2, q - 2, b), 1, 1);
                dc.DrawRectangle(Vermelho, null, new Rect(c.X - b / 2, c.Y - q / 2 + 1, b, q - 2), 1, 1);
                break;
        }
    }

    static readonly IBrush Verde = TemaVerde.Verde;

    // a seta da substituicao: verde para cima (entra) ou vermelha para baixo (sai)
    public static void Seta(DrawingContext dc, bool entra, Rect r)
    {
        double w = r.Width, h = r.Height, x = r.X, y = r.Y;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            double hc = h * 0.5, hs = w * 0.32;
            Point P(double px, double py) => entra ? new Point(x + px, y + py) : new Point(x + px, y + h - py);
            ctx.BeginFigure(P(w / 2, 0), true);
            ctx.LineTo(P(w, hc));
            ctx.LineTo(P(w / 2 + hs / 2, hc));
            ctx.LineTo(P(w / 2 + hs / 2, h));
            ctx.LineTo(P(w / 2 - hs / 2, h));
            ctx.LineTo(P(w / 2 - hs / 2, hc));
            ctx.LineTo(P(0, hc));
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(entra ? Verde : Vermelho, null, g);
    }

    // o penalti sem golo: a bola com um X vermelho no canto
    public static void PenaltiFalhado(DrawingContext dc, Rect r)
    {
        Bola(dc, r, Brushes.White);
        double q = r.Width * 0.55;
        var c = new Point(r.Right - q / 2 + 1, r.Bottom - q / 2 + 1);
        dc.DrawEllipse(Vermelho, new Pen(Escuro, 1), c, q / 2, q / 2);
        double d = q * 0.2;
        var pen = new Pen(Brushes.White, 1.4, lineCap: PenLineCap.Round);
        dc.DrawLine(pen, new Point(c.X - d, c.Y - d), new Point(c.X + d, c.Y + d));
        dc.DrawLine(pen, new Point(c.X - d, c.Y + d), new Point(c.X + d, c.Y - d));
    }

    static void Cartao(DrawingContext dc, IBrush cor, Rect r, double dx)
    {
        double h = r.Height * 0.95, w = h * 0.7;
        var c = r.Center;
        dc.DrawRectangle(cor, new Pen(Escuro, 0.8), new Rect(c.X - w / 2 + dx, c.Y - h / 2 + dx * 0.6, w, h), 1.5, 1.5);
    }

    // a bola da marca reduzida uma vez a 64 pontos (o bastante para as telas de alta densidade)
    static Avalonia.Media.Imaging.Bitmap? _bolaPequena;
    static bool _bolaFeita;
    static Avalonia.Media.Imaging.Bitmap? BolaPequena
    {
        get
        {
            if (_bolaFeita) return _bolaPequena;
            _bolaFeita = true;
            try { _bolaPequena = Logotipo.Bola?.CreateScaledBitmap(new PixelSize(64, 64), Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality); }
            catch { _bolaPequena = Logotipo.Bola; }
            return _bolaPequena;
        }
    }

    // a bola do golo: a da marca do RetroFoot (Assets/Marca/bola-256.png, a do launcher); no
    // golo anulado (cor cinza), apagada. Sem a imagem, o circulo com o pentagono
    static void Bola(DrawingContext dc, Rect r, IBrush cor)
    {
        if (BolaPequena is { } img)
        {
            double t = Math.Min(r.Width, r.Height) + 2;
            var q = new Rect(r.Center.X - t / 2, r.Center.Y - t / 2, t, t);
            // (a imagem de 256 encolhida na hora ficava serrilhada: a de 64, reduzida com
            // qualidade, e desenhada com suavizacao)
            using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality }))
            using (dc.PushOpacity(cor == Cinza ? 0.45 : 1))
                dc.DrawImage(img, q);
            return;
        }
        var c = r.Center;
        double raio = Math.Min(r.Width, r.Height) / 2 - 0.5;
        dc.DrawEllipse(cor, new Pen(Escuro, 1), c, raio, raio);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            double p = raio * 0.42;
            for (int k = 0; k < 5; k++)
            {
                double a = -Math.PI / 2 + k * 2 * Math.PI / 5;
                var pt = new Point(c.X + p * Math.Cos(a), c.Y + p * Math.Sin(a));
                if (k == 0) ctx.BeginFigure(pt, true); else ctx.LineTo(pt);
            }
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(Escuro, null, g);
    }
}
