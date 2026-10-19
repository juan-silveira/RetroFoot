using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SEquipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.App;

// O estadio do visual Moderno (TelaEstadio): um desenho isometrico (2,5D, so geometria) com
// o relvado no centro e um anel oval de arquibancadas a volta (cantos incluidos), em 24 pecas
// de 5000 lugares: 3 aneis x 8 setores curvos (as 2 laterais, os 2 topos e os 4 cantos). A
// ordem de construcao: o anel de baixo (laterais, topos, cantos), depois o do meio e por fim
// o de cima. A peca k (1 a 24) aparece quando Estadio >= k; as que faltam ficam so com o
// contorno tracejado. Os assentos nas cores da equipa; a peca nova sobe do chao.
//
// Mundo em metros: o campo de 105 x 68 centrado na origem, x ao longo do campo, y na largura,
// z para cima. Na tela: X = (x - y) cos30, Y = (x + y) / 2 - z (quem ve esta em +x, +y, +z).
// O anel e uma superelipse (|x/a|^5 + |y/b|^5 = 1): oval, mas rente ao campo nos lados.
sealed class EstadioIsometrico : Control
{
    const double KX = 0.8660254, KY = 0.5, KZ = 1;
    const double MeioC = 52.5, MeioL = 34;
    const double A0 = 60, B0 = 38.5, Expoente = 5;
    const double DuracaoMs = 650;
    const int Segmentos = 8;

    // os aneis: recuo da frente e de tras (somado aos semieixos), altura na frente e atras,
    // e as filas desenhadas
    static readonly (double D0, double D1, double ZF, double ZT, int Filas)[] Aneis =
    {
        (4, 15, 1.5, 7.5, 6), (16, 25, 10, 16, 5), (26, 34, 18.5, 25, 5),
    };

    // os setores pelo angulo do centro (graus; 90 = lateral da frente, 270 = a de tras, 0 e
    // 180 = os topos), na ordem de construcao dentro de cada anel
    static readonly double[] Setores = { 270, 90, 180, 0, 225, 315, 135, 45 };

    readonly Func<int> _bancadas;
    readonly Bitmap? _escudo;
    readonly Color _fundo, _letra, _assento;
    int _visto = -1, _animDe, _animAte;
    readonly Stopwatch _relogio = new();
    DispatcherTimer? _timer;

    public EstadioIsometrico(SEquipa e)
    {
        _bancadas = () => e.Estadio;
        _escudo = Escudos.Escudo(e);
        _fundo = e.CorFundo.Length >= 3 ? Color.FromRgb(e.CorFundo[0], e.CorFundo[1], e.CorFundo[2]) : Colors.White;
        _letra = e.CorLetra.Length >= 3 ? Color.FromRgb(e.CorLetra[0], e.CorLetra[1], e.CorLetra[2]) : Colors.Black;
        // assentos muito escuros sumiriam no cartao verde: clareia um pouco
        _assento = Lum(_fundo) < 0.16 ? Mistura(_fundo, Color.FromRgb(120, 130, 122), 0.45) : _fundo;
    }

    readonly record struct V3(double X, double Y, double Z = 0);

    // o ponto do anel no angulo t (radianos), com o recuo d e a altura z
    static V3 Anel(double t, double d, double z)
    {
        double c = Math.Cos(t), s = Math.Sin(t);
        return new V3((A0 + d) * Math.Sign(c) * Math.Pow(Math.Abs(c), 2 / Expoente),
                      (B0 + d) * Math.Sign(s) * Math.Pow(Math.Abs(s), 2 / Expoente), z);
    }

    static (double T0, double T1) Faixa(int setor)
    {
        double c = Setores[setor] * Math.PI / 180, m = 22.5 * Math.PI / 180;
        return (c - m, c + m);
    }

    double _s, _ox, _oy;
    Point P(V3 v) => new(_ox + (v.X - v.Y) * KX * _s, _oy + ((v.X + v.Y) * KY - v.Z * KZ) * _s);
    Point P(double x, double y, double z = 0) => P(new V3(x, y, z));

    Point[] Proj(V3[] v)
    {
        var r = new Point[v.Length];
        for (int i = 0; i < v.Length; i++) r[i] = P(v[i]);
        return r;
    }

    static V3 Menos(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    static V3 Cruz(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    static double Ponto(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    static V3 Norm(V3 a) { double k = Math.Sqrt(Ponto(a, a)); return k < 1e-9 ? a : new V3(a.X / k, a.Y / k, a.Z / k); }
    static V3 Media(V3[] p)
    {
        double x = 0, y = 0, z = 0;
        foreach (var q in p) { x += q.X; y += q.Y; z += q.Z; }
        return new V3(x / p.Length, y / p.Length, z / p.Length);
    }
    // a direcao de quem ve (a face e visivel se a normal aponta para ela) e a profundidade
    static readonly V3 Olho = new(1, 1, 1);
    static double Fundura(V3 c) => c.X + c.Y + c.Z;
    // a luz vem de cima, um pouco da esquerda da tela
    static readonly V3 Luz = Norm(new V3(-0.6, 0.25, 1));

    static double Lum(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
    static Color Mistura(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    static Color Sombra(Color c, double f) => Color.FromArgb(c.A,
        (byte)Math.Clamp(Math.Round(c.R * f), 0, 255), (byte)Math.Clamp(Math.Round(c.G * f), 0, 255), (byte)Math.Clamp(Math.Round(c.B * f), 0, 255));
    static double Brilho(V3 n) => 0.6 + 0.42 * Math.Max(0, Ponto(n, Luz));

    // as cores do tema verde (TemaVerde): o betao um cinza-esverdeado, a base quase o verde
    // mais fundo do quadro, e a relva mais viva que o fundo para o campo nao sumir no cartao
    static readonly Color Betao = Color.FromRgb(150, 160, 148), Base = Color.FromRgb(30, 46, 36);
    static readonly Color Relva1 = Color.FromRgb(58, 146, 56), Relva2 = Color.FromRgb(72, 162, 64), Margem = Color.FromRgb(44, 116, 48);

    static StreamGeometry Poligono(IReadOnlyList<Point> pts, bool fechar = true)
    {
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(pts[0], fechar);
        for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i]);
        ctx.EndFigure(fechar);
        return g;
    }

    // ------------------------------------------------------------ as pecas

    enum Tipo { Assento, Frente, Tras, Ponta }

    // uma face pequena da peca: os pontos, a normal para fora, o tipo e a fila (assentos)
    readonly record struct Face(V3[] Pts, V3 Normal, Tipo Tipo, int Fila);

    // as faces da peca (anel, setor); alt = 0..1 (a subida da animacao)
    static List<Face> Faces(int anel, int setor, double alt)
    {
        var (d0, d1, zf, zt, filas) = Aneis[anel];
        var (t0, t1) = Faixa(setor);
        zf *= alt; zt *= alt;
        var l = new List<Face>();
        // o meio da peca (para orientar as pontas)
        var meio = Anel((t0 + t1) / 2, (d0 + d1) / 2, (zf + zt) / 4);
        void Add(Tipo tipo, int fila, V3 dentro, params V3[] p)
        {
            var n = Norm(Cruz(Menos(p[1], p[0]), Menos(p[2], p[0])));
            var c = Media(p);
            bool fora = tipo switch
            {
                Tipo.Assento => n.Z > 0,
                Tipo.Frente => Ponto(n, new V3(-c.X, -c.Y)) > 0,
                Tipo.Tras => Ponto(n, new V3(c.X, c.Y)) > 0,
                _ => Ponto(n, Menos(c, dentro)) > 0,
            };
            if (!fora) n = new V3(-n.X, -n.Y, -n.Z);
            l.Add(new Face(p, n, tipo, fila));
        }
        for (int i = 0; i < Segmentos; i++)
        {
            double a = t0 + (t1 - t0) * i / Segmentos, b = t0 + (t1 - t0) * (i + 1) / Segmentos;
            for (int f = 0; f < filas; f++)
            {
                double u0 = (double)f / filas, u1 = (double)(f + 1) / filas;
                double da = d0 + (d1 - d0) * u0, db = d0 + (d1 - d0) * u1;
                double za = zf + (zt - zf) * u0, zb = zf + (zt - zf) * u1;
                Add(Tipo.Assento, f, meio, Anel(a, da, za), Anel(b, da, za), Anel(b, db, zb), Anel(a, db, zb));
            }
            Add(Tipo.Frente, 0, meio, Anel(a, d0, 0), Anel(b, d0, 0), Anel(b, d0, zf), Anel(a, d0, zf));
            Add(Tipo.Tras, 0, meio, Anel(a, d1, 0), Anel(b, d1, 0), Anel(b, d1, zt), Anel(a, d1, zt));
        }
        foreach (double t in new[] { t0, t1 })
            Add(Tipo.Ponta, 0, meio, Anel(t, d0, 0), Anel(t, d0, zf), Anel(t, d1, zt), Anel(t, d1, 0));
        return l;
    }

    // ------------------------------------------------------------ desenho

    public override void Render(DrawingContext dc)
    {
        int n = Math.Clamp(_bancadas(), 0, 24);
        if (_visto >= 0 && n > _visto)
        {
            _animDe = _visto; _animAte = n;
            _relogio.Restart();
            _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
            {
                InvalidateVisual();
                if (_relogio.ElapsedMilliseconds > DuracaoMs) _timer!.Stop();
            });
            _timer.Start();
        }
        _visto = n;
        double prog = _relogio.IsRunning ? Math.Min(1, _relogio.ElapsedMilliseconds / DuracaoMs) : 1;
        if (prog >= 1) _relogio.Reset();

        Ajustar();
        DesenharBase(dc);
        DesenharCampo(dc);
        // as que faltam: so o contorno, por baixo de tudo o que ja foi construido
        for (int k = n; k < 24; k++) Fantasma(dc, k / 8, k % 8);

        // a ordem de pintura: todas as faces (pequenas) e as balizas, de tras para a frente
        var itens = new List<(double Fundura, Action Pintar)>();
        var aresta = new Pen(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), 0.5);
        for (int k = 0; k < n; k++)
        {
            int anel = k / 8, setor = k % 8;
            bool anim = k >= _animDe && k < _animAte && prog < 1;
            double alt = anim ? Math.Max(0.02, 1 - Math.Pow(1 - prog, 3)) : 1;
            double op = anim ? Math.Min(1, prog * 1.6) : 1;
            foreach (var f in Faces(anel, setor, alt))
            {
                if (Ponto(f.Normal, Olho) <= 1e-6) continue;
                var face = f;
                itens.Add((Fundura(Media(f.Pts)), () => PintarFace(dc, face, op, aresta)));
            }
        }
        // as escadas entre os setores (e no meio de cada um), por cima dos assentos
        var escada = new Pen(new SolidColorBrush(Color.FromArgb(150, 205, 208, 214)), Math.Max(0.6, _s * 0.3));
        for (int k = 0; k < n; k++)
        {
            int anel = k / 8;
            if (k >= _animDe && k < _animAte && prog < 1) continue;
            var (d0, d1, zf, zt, _) = Aneis[anel];
            var (t0, t1) = Faixa(k % 8);
            foreach (double t in new[] { t0, (t0 + t1) / 2, t1 })
            {
                var a = Anel(t, d0, zf);
                var b = Anel(t, d1, zt);
                var m = Media(new[] { a, b });
                itens.Add((Fundura(m) + 0.05, () => dc.DrawLine(escada, P(a), P(b))));
            }
        }
        foreach (int lado in new[] { -1, 1 })
            itens.Add((lado * (MeioC + 1) + 1.2, () => Baliza(dc, lado)));
        itens.Sort((a, b) => a.Fundura.CompareTo(b.Fundura));
        foreach (var i in itens) i.Pintar();
    }

    void PintarFace(DrawingContext dc, Face f, double op, Pen aresta)
    {
        double b = Brilho(f.Normal);
        Color cor = f.Tipo switch
        {
            Tipo.Assento => Sombra(_assento, b * (f.Fila % 2 == 0 ? 1 : 0.82)),
            // a frente do anel (a grade) na outra cor da equipa
            Tipo.Frente => Sombra(Mistura(_letra, Betao, 0.35), b * 0.92),
            Tipo.Tras => Sombra(Betao, b * 0.85),
            _ => Sombra(Betao, b * 0.95),
        };
        if (op < 1) cor = Color.FromArgb((byte)Math.Round(255 * op), cor.R, cor.G, cor.B);
        var brush = new SolidColorBrush(cor);
        // a pena da cor da face tapa as frestas entre as faces pequenas
        dc.DrawGeometry(brush, f.Tipo == Tipo.Assento || f.Tipo == Tipo.Tras ? new Pen(brush, 0.6) : aresta, Poligono(Proj(f.Pts)));
    }

    // a escala e a origem: tudo (com todas as pecas) cabe no controle, centrado
    void Ajustar()
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Ver(V3 v)
        {
            double x = (v.X - v.Y) * KX, y = (v.X + v.Y) * KY - v.Z * KZ;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        var (_, d1, _, zt, _) = Aneis[2];
        for (int i = 0; i < 72; i++)
        {
            double t = i * Math.PI / 36;
            Ver(Anel(t, d1, zt));
            Ver(Anel(t, d1 + 1.5, -AlturaBase));
        }
        double w = Bounds.Width, h = Bounds.Height;
        _s = Math.Min((w - 8) / (maxX - minX), (h - 8) / (maxY - minY));
        _ox = w / 2 - (minX + maxX) / 2 * _s;
        _oy = h / 2 - (minY + maxY) / 2 * _s;
    }

    static V3[] Curva(double d, double z, int pontos = 96)
    {
        var r = new V3[pontos];
        for (int i = 0; i < pontos; i++) r[i] = Anel(i * 2 * Math.PI / pontos, d, z);
        return r;
    }

    const double AlturaBase = 2.2;

    // a base de betao oval por baixo de tudo, com a espessura nas bordas da frente
    void DesenharBase(DrawingContext dc)
    {
        var borda = Curva(Aneis[2].D1 + 1.5, 0);
        for (int i = 0; i < borda.Length; i++)
        {
            var a = borda[i];
            var b = borda[(i + 1) % borda.Length];
            var n = Norm(new V3(b.Y - a.Y, a.X - b.X));
            if (Ponto(n, new V3(a.X, a.Y)) < 0) n = new V3(-n.X, -n.Y);
            if (Ponto(n, Olho) <= 1e-6) continue;
            var cor = new SolidColorBrush(Sombra(Base, 0.45 + 0.35 * Math.Max(0, n.X)));
            dc.DrawGeometry(cor, new Pen(cor, 0.6), Poligono(new[] { P(a), P(b), P(b.X, b.Y, -AlturaBase), P(a.X, a.Y, -AlturaBase) }));
        }
        dc.DrawGeometry(new SolidColorBrush(Base), new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 0.6), Poligono(Proj(borda)));
    }

    void DesenharCampo(DrawingContext dc)
    {
        // o chao do anel (relva escura) ate a primeira fila
        dc.DrawGeometry(new SolidColorBrush(Margem), null, Poligono(Proj(Curva(Aneis[0].D0, 0))));
        // as faixas do corte da relva
        int faixas = 14;
        var r1 = new SolidColorBrush(Relva1);
        var r2 = new SolidColorBrush(Relva2);
        for (int i = 0; i < faixas; i++)
        {
            double x0 = -MeioC + 2 * MeioC * i / faixas, x1 = -MeioC + 2 * MeioC * (i + 1) / faixas;
            var br = i % 2 == 0 ? r1 : r2;
            dc.DrawGeometry(br, new Pen(br, 0.5), Poligono(new[] { P(x0, -MeioL), P(x1, -MeioL), P(x1, MeioL), P(x0, MeioL) }));
        }
        var giz = new Pen(new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)), Math.Max(0.8, _s * 0.55));
        void Ret(double x0, double y0, double x1, double y1) =>
            dc.DrawGeometry(null, giz, Poligono(new[] { P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1) }));
        Ret(-MeioC, -MeioL, MeioC, MeioL);
        dc.DrawLine(giz, P(0, -MeioL), P(0, MeioL));
        foreach (int s in new[] { -1, 1 })
        {
            double g = s * MeioC;
            Ret(Math.Min(g, g - s * 16.5), -20.16, Math.Max(g, g - s * 16.5), 20.16);
            Ret(Math.Min(g, g - s * 5.5), -9.16, Math.Max(g, g - s * 5.5), 9.16);
            dc.DrawEllipse(giz.Brush, null, P(g - s * 11, 0), Math.Max(0.8, _s * 0.5), Math.Max(0.6, _s * 0.3));
        }
        // o circulo central (no isometrico, uma elipse alinhada a tela)
        double rc = 9.15;
        var c = P(0, 0);
        double ax = rc * Math.Sqrt(2) * KX * _s, ay = rc * Math.Sqrt(2) * KY * _s;
        dc.DrawEllipse(null, giz, c, ax, ay);
        // o escudo deitado no circulo (achatado como o chao)
        double lado = ax * 1.45;
        using (dc.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(1, KY / KX) * Matrix.CreateTranslation(c.X, c.Y)))
        using (dc.PushOpacity(0.92))
            TelaJornada.Logo(dc, _escudo, _fundo, _letra, new Rect(c.X - lado / 2, c.Y - lado / 2, lado, lado));
    }

    // a baliza: postes, travessao e a rede para tras (lado -1 = a de tras)
    void Baliza(DrawingContext dc, int lado)
    {
        double g = lado * MeioC, f = g + lado * 2, h = 2.44, w = 3.66;
        var rede = new Pen(new SolidColorBrush(Color.FromArgb(150, 230, 230, 230)), Math.Max(0.5, _s * 0.25));
        var poste = new Pen(Brushes.White, Math.Max(1, _s * 0.45), lineCap: PenLineCap.Round);
        var fundoRede = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        dc.DrawGeometry(fundoRede, rede, Poligono(new[] { P(f, -w, 0), P(f, w, 0), P(f, w, h * 0.8), P(f, -w, h * 0.8) }));
        dc.DrawGeometry(fundoRede, rede, Poligono(new[] { P(g, -w, h), P(g, w, h), P(f, w, h * 0.8), P(f, -w, h * 0.8) }));
        foreach (double y in new[] { -w, w })
            dc.DrawGeometry(null, rede, Poligono(new[] { P(g, y, 0), P(f, y, 0), P(f, y, h * 0.8), P(g, y, h) }));
        dc.DrawLine(poste, P(g, -w, 0), P(g, -w, h));
        dc.DrawLine(poste, P(g, w, 0), P(g, w, h));
        dc.DrawLine(poste, P(g, -w, h), P(g, w, h));
    }

    // a peca que falta: o contorno tracejado do plano dos assentos e as arestas das pontas
    void Fantasma(DrawingContext dc, int anel, int setor)
    {
        var (d0, d1, zf, zt, _) = Aneis[anel];
        var (t0, t1) = Faixa(setor);
        var tracejado = new Pen(new SolidColorBrush(Color.FromArgb(70, 200, 205, 212)), 0.8, new DashStyle(new double[] { 3, 3 }, 0));
        var veu = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
        var pts = new List<Point>();
        for (int i = 0; i <= Segmentos; i++) pts.Add(P(Anel(t0 + (t1 - t0) * i / Segmentos, d0, zf)));
        for (int i = Segmentos; i >= 0; i--) pts.Add(P(Anel(t0 + (t1 - t0) * i / Segmentos, d1, zt)));
        dc.DrawGeometry(veu, tracejado, Poligono(pts));
        foreach (double t in new[] { t0, t1 })
        {
            dc.DrawLine(tracejado, P(Anel(t, d0, 0)), P(Anel(t, d0, zf)));
            dc.DrawLine(tracejado, P(Anel(t, d1, 0)), P(Anel(t, d1, zt)));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }
}
