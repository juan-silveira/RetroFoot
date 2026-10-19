using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using RetroFoot.Motor.Patrocinio;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SJogador = RetroFoot.Motor.Save.Jogador;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O campinho do visual Moderno (aba Escalacao): o relvado em 2,5D, so geometria, visto da
// bancada atras da nossa baliza (o goleiro perto, o ataque la no fundo), com as faixas do
// corte da relva, as linhas, as balizas e as placas de publicidade. Cada titular e uma
// pecinha com a camisola nas cores da equipa, o numero e o nome, pintadas de tras para a
// frente. Mudou a escalacao: as pecas deslizam para o lugar novo; quem entra sobe do banco
// (a linha de baixo) e quem sai desce para ele. Os botoes desta area e o Jogar do cabecalho
// tem brilho, realce ao passar o rato e um "afundar" no clique.
// Clique numa peca: escolhe o jogador (a linha da lista); clicar de novo muda a escalacao,
// como na lista.
public sealed partial class TelaEquipa
{
    sealed partial class Painel
    {
        // ------------------------------------------------------------ animacao
        readonly Stopwatch _relogioAnim = Stopwatch.StartNew();
        double _ultimoQuadro, _dt;
        bool _animando;
        DispatcherTimer? _quadros;
        readonly Dictionary<string, double> _anims = new();
        Point _ponteiro = new(-1e6, -1e6);
        string? _premido;
        double _premidoT;
        string? _sobreChave;
        // as zonas que reagem ao rato (botoes e pecas), refeitas a cada desenho
        readonly List<(Rect R, string Chave)> _brilhos = new();

        double Agora => _relogioAnim.Elapsed.TotalSeconds;

        void ComecarQuadro()
        {
            double t = Agora;
            _dt = Math.Clamp(t - _ultimoQuadro, 0, 0.05);
            _ultimoQuadro = t;
            _animando = false;
            _brilhos.Clear();
        }

        // ainda ha o que mexer: um quadro a cada 16 ms ate tudo parar
        void FimQuadro()
        {
            if (!_animando) return;
            _quadros ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
            {
                if (!_animando || VisualRoot == null) { _quadros!.Stop(); return; }
                InvalidateVisual();
            });
            if (!_quadros.IsEnabled) _quadros.Start();
        }

        // o valor que corre atras do alvo (amortecido); o primeiro ja nasce no alvo
        double Anim(string chave, double alvo, double vel = 14)
        {
            if (!_anims.TryGetValue(chave, out var v)) { _anims[chave] = alvo; return alvo; }
            double n = v + (alvo - v) * (1 - Math.Exp(-vel * _dt));
            if (Math.Abs(alvo - n) < 0.003) n = alvo;
            else _animando = true;
            _anims[chave] = n;
            return n;
        }

        // o "afundar" do clique: 0 -> 1 -> 0 em 0,26 s
        double Premido(string chave)
        {
            if (_premido != chave) return 0;
            double t = (Agora - _premidoT) / 0.26;
            if (t >= 1) { _premido = null; return 0; }
            _animando = true;
            return Math.Sin(t * Math.PI);
        }

        void MarcarPremido(Point pc)
        {
            for (int i = _brilhos.Count - 1; i >= 0; i--)
                if (_brilhos[i].R.Contains(pc)) { _premido = _brilhos[i].Chave; _premidoT = Agora; return; }
        }

        // o rato por cima de outra zona: redesenha (o realce corre sozinho)
        void PonteiroModerno(Point pc)
        {
            _ponteiro = pc;
            string? k = null;
            for (int i = _brilhos.Count - 1; i >= 0 && k == null; i--)
                if (_brilhos[i].R.Contains(pc)) k = _brilhos[i].Chave;
            if (k != _sobreChave) { _sobreChave = k; InvalidateVisual(); }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (!_moderno) return;
            _ponteiro = new(-1e6, -1e6);
            _sobreChave = null;
            InvalidateVisual();
        }

        static Color Cor(string s) => Color.Parse(s);
        static Color Mist(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb((byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        }
        static Color Alfa(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * c.A), c.R, c.G, c.B);
        static double Lum(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

        static LinearGradientBrush Vertical(params (Color C, double O)[] p)
        {
            var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            foreach (var (c, o) in p) b.GradientStops.Add(new GradientStop(c, o));
            return b;
        }

        static LinearGradientBrush Horizontal(params (Color C, double O)[] p)
        {
            var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            foreach (var (c, o) in p) b.GradientStops.Add(new GradientStop(c, o));
            return b;
        }

        // ------------------------------------------------------------ botoes com brilho
        // principal = o verde cheio (Jogar); os outros, vidro escuro com o realce azul
        Rect BotaoBrilho(DrawingContext dc, double x, double yc, string chave, string texto, bool principal, bool hab, Action? acao,
            double tam = 12.5, double minimo = 0)
        {
            var t0 = TM(texto, tam, MTexto, true);
            double w = Math.Max(minimo, t0.WidthIncludingTrailingWhitespace + 30), h = tam + 17;
            var r = new Rect(x, yc - h / 2, w, h);
            bool sobre = hab && r.Contains(_ponteiro);
            double s = Anim(chave + ":s", sobre ? 1 : 0, 16);
            double e = Anim(chave + ":e", hab ? 1 : 0, 9);
            double p = Premido(chave);
            double esc = 1 + 0.04 * s - 0.07 * p;
            double raio = h / 2;
            var c = r.Center;
            using (dc.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateScale(esc, esc) * Matrix.CreateTranslation(c.X, c.Y + p * 1.2)))
            {
                if (principal)
                {
                    // a pilula ouro do Jogar do hub, com o halo amarelo (mais forte com o rato em cima)
                    double brilho = e * (0.55 + 0.75 * s) * (1 - 0.5 * p);
                    for (int i = 4; i >= 1; i--)
                        dc.DrawRectangle(new SolidColorBrush(Alfa(TemaVerde.Cores.Destaque, 0.07 * brilho)), null, r.Inflate(i * 2.2), raio + i * 2.2, raio + i * 2.2);
                    var topo = Mist(Mist(TemaVerde.Cores.CartaoForte, Cor("#FCE46A"), e), Colors.White, 0.18 * s);
                    var meio = Mist(TemaVerde.Cores.Cartao, TemaVerde.Cores.Destaque, e);
                    var baixo = Mist(Mist(TemaVerde.Cores.Barra, TemaVerde.Cores.Ouro, e), Colors.Black, 0.25 * p);
                    dc.DrawRectangle(new SolidColorBrush(Alfa(Colors.Black, 0.45)), null, r.Translate(new Vector(0, 2)), raio, raio);
                    dc.DrawRectangle(Vertical((topo, 0), (meio, 0.45), (baixo, 1)),
                        new Pen(new SolidColorBrush(Mist(TemaVerde.Cores.Linha, Alfa(Colors.White, 0.75), e)), 1.2), r, raio, raio);
                }
                else
                {
                    var topo = Mist(TemaVerde.Cores.CartaoForte, TemaVerde.Cores.Hover, s);
                    var baixo = Mist(Mist(TemaVerde.Cores.Cartao, TemaVerde.Cores.CartaoForte, s), Colors.Black, 0.3 * p);
                    dc.DrawRectangle(new SolidColorBrush(Alfa(Colors.Black, 0.4)), null, r.Translate(new Vector(0, 2)), raio, raio);
                    var borda = hab ? Mist(TemaVerde.Cores.Borda, TemaVerde.Cores.Destaque, 0.25 + 0.75 * s) : TemaVerde.Cores.Linha;
                    dc.DrawRectangle(Vertical((topo, 0), (baixo, 1)), new Pen(new SolidColorBrush(borda), 1), r, raio, raio);
                    if (s > 0.01)
                        for (int i = 3; i >= 1; i--)
                            dc.DrawRectangle(null, new Pen(new SolidColorBrush(Alfa(TemaVerde.Cores.Destaque, 0.08 * s)), 2), r.Inflate(i * 1.6), raio + i * 1.6, raio + i * 1.6);
                }
                // o brilho de vidro na metade de cima e o fio claro da borda
                var vidro = new Rect(r.X + 3, r.Y + 1.5, r.Width - 6, r.Height * 0.48);
                dc.DrawRectangle(Vertical((Alfa(Colors.White, principal ? 0.42 * e + 0.08 : 0.16), 0), (Alfa(Colors.White, 0.02), 1)), null, vidro, raio - 2, raio - 2);
                var cor = principal ? (hab ? (IBrush)new SolidColorBrush(TemaVerde.Cores.SobreDestaque) : MFraco) : hab ? new SolidColorBrush(Mist(TemaVerde.Cores.Destaque, TemaVerde.Cores.DestaqueClaro, s)) : MFraco;
                var t = TM(texto, tam, cor, true);
                double tx = r.X + (w - t.WidthIncludingTrailingWhitespace) / 2, ty = yc - t.Height / 2;
                if (principal && hab) dc.DrawText(TM(texto, tam, new SolidColorBrush(Alfa(Colors.White, 0.35)), true), new Point(tx, ty + 1));
                dc.DrawText(t, new Point(tx, ty));
            }
            if (hab && acao != null) _alvos.Add((r, acao));
            if (hab) _brilhos.Add((r, chave));
            return r;
        }

        // ------------------------------------------------------------ o campinho
        // mundo em metros: u na largura do campo (-34 a 34, esquerda para a direita) e d a
        // fundura a partir da nossa linha de fundo (0 perto, 105 la no fundo); z para cima
        const double CampoL = 34, CampoC = 105, Recuo = 4.5;
        // as linhas das posicoes (G, D, M, A), em metros da nossa linha de fundo
        static readonly double[] FunduraPosicao = { 7, 29, 58, 87 };

        // a peca de cada jogador: u, d e a opacidade, que correm atras do lugar dele
        readonly Dictionary<SJogador, (double U, double D, double A)> _pecas = new(ReferenceEqualityComparer.Instance);
        bool _pecasPostas;

        void CampinhoIsometrico(DrawingContext dc, Rect r)
        {
            // a perspectiva: a escala no fundo e sFundo da de perto
            const double sFundo = 0.68;
            double d0 = CampoC * sFundo / (1 - sFundo);
            double mx = 14, topo = 42, baixo = 18;
            double S = (r.Width - 2 * mx) / (2 * (CampoL + Recuo));
            double hp = r.Height - topo - baixo;
            double yPerto = r.Bottom - baixo, cx = r.Center.X;
            double alto = hp / (1 - sFundo);
            double Esc(double d) => d0 / (d0 + d);
            Point P(double u, double d, double z = 0)
            {
                double s = Esc(d);
                return new Point(cx + u * S * s, yPerto - alto * (1 - s) - z * S * s);
            }
            StreamGeometry Poli(params Point[] pts)
            {
                var g = new StreamGeometry();
                using var ctx = g.Open();
                ctx.BeginFigure(pts[0], true);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i]);
                ctx.EndFigure(true);
                return g;
            }
            StreamGeometry Linha(IReadOnlyList<Point> pts, bool fechar)
            {
                var g = new StreamGeometry();
                using var ctx = g.Open();
                ctx.BeginFigure(pts[0], false);
                for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i]);
                ctx.EndFigure(fechar);
                return g;
            }

            // o fundo: a noite do estadio com a luz dos refletores
            dc.DrawRectangle(Vertical((Cor("#0a2c1a"), 0), (Cor("#0b3a20"), 0.55), (Cor("#062414"), 1)), null, r);
            var luz = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.18, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.18, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
            };
            luz.GradientStops.Add(new GradientStop(Alfa(Cor("#cfe8ff"), 0.16), 0));
            luz.GradientStops.Add(new GradientStop(Alfa(Cor("#cfe8ff"), 0), 1));
            dc.DrawRectangle(luz, null, r);

            var corFundo = CorC(_e.CorFundo, Colors.White);
            var corLetra = CorC(_e.CorLetra, Colors.Black);

            // a base (relva da margem) e a espessura dela na frente
            double ul = CampoL + Recuo, df = CampoC + Recuo;
            var b0 = P(-ul, -Recuo); var b1 = P(ul, -Recuo);
            double esp = 2.6 * S;
            dc.DrawGeometry(Vertical((Cor("#1b5e20"), 0), (Cor("#0b2a10"), 1)), null,
                Poli(b0, b1, new Point(b1.X, b1.Y + esp), new Point(b0.X, b0.Y + esp)));
            dc.DrawGeometry(new SolidColorBrush(Cor("#24662a")), null, Poli(b0, b1, P(ul, df), P(-ul, df)));

            // as placas de publicidade la no fundo, nas cores da equipa: com patrocinador, 3
            // paineis brancos (so o master: M M M; so o pontual: P P P; os dois: M P M); sem
            // patrocinador, o nome da equipa e "RETROFOOT"
            var (master, pontual) = LogosPatrocinio();
            var logos = new List<Bitmap>();
            if (master != null) logos.Add(master);
            if (pontual != null) logos.Add(pontual);
            var nome = Texto(_e.NomeCurto).ToUpperInvariant();
            {
                var a0 = P(-ul, df); var a1 = P(ul, df);
                double hz = (logos.Count > 0 ? 4.8 : 1.6) * S * Esc(df);
                var placa = new Rect(a0.X, a0.Y - hz, a1.X - a0.X, hz);
                dc.DrawRectangle(Vertical((Mist(corFundo, Colors.White, 0.15), 0), (Mist(corFundo, Colors.Black, 0.35), 1)), null, placa);
                dc.FillRectangle(new SolidColorBrush(Alfa(Colors.White, 0.35)), new Rect(placa.X, placa.Y, placa.Width, 1));
                using (dc.PushClip(placa))
                {
                    if (logos.Count > 0)
                    {
                        var pontas = master ?? pontual!;
                        var meio = pontual ?? master!;
                        var itens = new[] { pontas, meio, pontas };
                        double w = placa.Width / 3;
                        for (int q = 0; q < 3; q++)
                            PainelPlaca(dc, itens[q], new Rect(placa.X + q * w, placa.Y, w, hz).Deflate(1), nome, corLetra);
                    }
                    else
                    {
                        // o texto so quando cabe na placa: no 4:3 a placa tem poucos pontos de altura
                        double tl = hz * 0.7;
                        if (tl >= 6)
                            for (int q = 0; q < 3; q++)
                                CentroM(dc, q == 1 ? "RETROFOOT" : nome, placa.X + placa.Width * (q * 2 + 1) / 6, placa.Center.Y, tl, new SolidColorBrush(corLetra), true, placa.Width / 3 - 6);
                    }
                }
            }

            // as faixas do corte da relva (14 de 7,5 m, mais finas ao fundo)
            var rel1 = Cor("#2e7d32"); var rel2 = Cor("#388e3c");
            for (int i = 0; i < 14; i++)
            {
                double da = CampoC * i / 14, db = CampoC * (i + 1) / 14;
                var br = new SolidColorBrush(i % 2 == 0 ? rel2 : rel1);
                dc.DrawGeometry(br, new Pen(br, 0.6), Poli(P(-CampoL, da), P(CampoL, da), P(CampoL, db), P(-CampoL, db)));
            }
            // a sombra do fundo e a luz de perto
            var campoPoli = Poli(P(-CampoL, 0), P(CampoL, 0), P(CampoL, CampoC), P(-CampoL, CampoC));
            var pf = P(0, CampoC); var pp = P(0, 0);
            var sombra = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            };
            sombra.GradientStops.Add(new GradientStop(Alfa(Colors.Black, 0.28), 0));
            sombra.GradientStops.Add(new GradientStop(Alfa(Colors.Black, 0), 0.55));
            sombra.GradientStops.Add(new GradientStop(Alfa(Colors.White, 0.06), 1));
            dc.DrawGeometry(sombra, null, campoPoli);

            // as linhas (perspectiva: retas continuam retas; os circulos, pontos a pontos)
            var giz = new Pen(new SolidColorBrush(Alfa(Colors.White, 0.85)), Math.Max(1, S * 0.32), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            void Ret(double u0, double da, double u1, double db) => dc.DrawGeometry(null, giz, Linha(new[] { P(u0, da), P(u1, da), P(u1, db), P(u0, db) }, true));
            void Arco(double uc, double dc0, double raio, double a0, double a1, int n = 40)
            {
                var pts = new Point[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    double a = a0 + (a1 - a0) * i / n;
                    pts[i] = P(uc + raio * Math.Cos(a), dc0 + raio * Math.Sin(a));
                }
                dc.DrawGeometry(null, giz, Linha(pts, false));
            }
            Ret(-CampoL, 0, CampoL, CampoC);
            dc.DrawLine(giz, P(-CampoL, CampoC / 2), P(CampoL, CampoC / 2));
            Arco(0, CampoC / 2, 9.15, 0, 2 * Math.PI, 64);
            foreach (var (fundo, sinal) in new[] { (0.0, 1.0), (CampoC, -1.0) })
            {
                Ret(-20.16, fundo, 20.16, fundo + sinal * 16.5);
                Ret(-9.16, fundo, 9.16, fundo + sinal * 5.5);
                // a meia-lua: a parte do circulo de 9,15 m da marca fora da area
                double marca = fundo + sinal * 11, ang = Math.Acos(5.5 / 9.15);
                Arco(0, marca, 9.15, sinal * Math.PI / 2 - ang, sinal * Math.PI / 2 + ang, 20);
                var pm = P(0, marca);
                dc.DrawEllipse(giz.Brush, null, pm, Math.Max(1, S * 0.35 * Esc(marca)), Math.Max(0.8, S * 0.22 * Esc(marca)));
            }
            dc.DrawEllipse(giz.Brush, null, P(0, CampoC / 2), Math.Max(1, S * 0.3), Math.Max(0.8, S * 0.18));

            // as pecas: o lugar de cada titular (as colunas da formacao), a ordem da lista
            var ordem = Ordem();
            var tit = ordem.Where(p => Mem.Escalacao(p) == Mem.EscTitular).ToList();
            var numero = new Dictionary<SJogador, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < tit.Count; i++) numero[tit[i]] = i + 1;
            var porPos = new int[4];
            foreach (var p in tit) porPos[Math.Min((int)p.Posicao, 3)]++;
            var alvo = new Dictionary<SJogador, (double U, double D, double V)>(ReferenceEqualityComparer.Instance);
            var visto = new int[4];
            foreach (var p in tit)
            {
                int s = Math.Min((int)p.Posicao, 3);
                int n = porPos[s];
                double largura = Math.Min(62, 22 * n);
                double u = n == 1 ? 0 : -largura / 2 + largura * (visto[s] + 0.5) / n;
                visto[s]++;
                // os do meio um pouco mais a frente (as linhas curvas da formacao)
                double d = FunduraPosicao[s] + (s > 0 && n >= 3 ? 2.5 * Math.Cos(Math.PI * (visto[s] - 0.5) / n - Math.PI / 2) : 0);
                alvo[p] = (u, d, n == 1 ? 40 : largura / n);
            }
            // o banco: quem entra vem da linha de baixo, quem sai volta para ela
            const double Banco = -9;
            foreach (var (p, (u, d, _)) in alvo)
                if (!_pecas.ContainsKey(p)) _pecas[p] = _pecasPostas ? (u, Banco, 0) : (u, d, 1);
            _pecasPostas = true;
            double k = 1 - Math.Exp(-9 * _dt);
            var desenhar = new List<(SJogador P, double U, double D, double A, double V)>();
            foreach (var p in _pecas.Keys.ToList())
            {
                var (u, d, a) = _pecas[p];
                bool fica = alvo.TryGetValue(p, out var t);
                double tu = fica ? t.U : u, td = fica ? t.D : Banco, ta = fica ? 1 : 0;
                u += (tu - u) * k; d += (td - d) * k; a += (ta - a) * Math.Min(1, k * 1.4);
                bool parado = Math.Abs(tu - u) < 0.05 && Math.Abs(td - d) < 0.05 && Math.Abs(ta - a) < 0.01;
                if (parado) { u = tu; d = td; a = ta; } else _animando = true;
                if (!fica && a < 0.02) { _pecas.Remove(p); continue; }
                _pecas[p] = (u, d, a);
                desenhar.Add((p, u, d, a, fica ? t.V : 12));
            }

            // as balizas entram na mesma ordem de pintura (a do fundo atras de todos)
            // a baliza de perto vai por baixo das pecas (nao tapa o goleiro)
            Baliza(dc, P, CampoC, S, Esc, 1);
            // os tapetes 3D atras da nossa baliza (so com patrocinador): deitados na relva mas
            // pintados para parecer placas em pe, um de cada lado da baliza
            // (os dois: o master a esquerda e o pontual a direita; um so: ele nos dois). Ficam na
            // faixa de relva entre a linha de fundo e a borda, sem entrar no campo
            if (logos.Count > 0)
            {
                for (int lado = 0; lado < 2; lado++)
                {
                    double u0 = lado == 0 ? -19 : 6, u1 = lado == 0 ? -6 : 19;
                    var pa = P(u0, -0.8); var pb = P(u1, -Recuo);
                    double topoT = pa.Y, baixoT = Math.Max(pb.Y + esp * 0.5, topoT + 9);
                    var rt = new Rect(pa.X, topoT, P(u1, -0.8).X - pa.X, baixoT - topoT);
                    // a sombra na relva, um pouco para tras
                    dc.DrawRectangle(new SolidColorBrush(Alfa(Colors.Black, 0.25)), null, rt.Translate(new Vector(0, 2)), 2, 2);
                    PainelPlaca(dc, lado == 0 ? (master ?? pontual) : (pontual ?? master), rt, nome, corLetra);
                }
            }
            Baliza(dc, P, 0, S, Esc, 0.8);
            desenhar.Sort((a, b) => b.D.CompareTo(a.D));
            foreach (var (p, u, d, a, v) in desenhar)
                Peca(dc, p, P(u, d), S * Esc(d), a, v, numero.TryGetValue(p, out var nn) ? nn : 0, corFundo, corLetra);

            // a formacao (D-M-A) no canto, num chip de vidro
            string form = porPos[1] + "-" + porPos[2] + "-" + porPos[3];
            var tf = TM(form, 12, MTexto, true);
            var rf = new Rect(r.X + 10, r.Y + 4, tf.WidthIncludingTrailingWhitespace + 18, 20);
            dc.DrawRectangle(Vertical((Alfa(Colors.White, 0.16), 0), (Alfa(Colors.White, 0.05), 1)), new Pen(new SolidColorBrush(Alfa(Colors.White, 0.2)), 1), rf, 10, 10);
            dc.DrawText(tf, new Point(rf.X + 9, rf.Center.Y - tf.Height / 2));
            int res = _e.Jogadores.Count(p => Mem.Escalacao(p) == Mem.EscReserva);
            double xd = r.Right - 10;
            if (!JogarHabilitado())
            {
                // o aviso (o Jogar desligado), num chip laranja
                var ta = TM(T("Escalação incompleta"), 11, MEscuro, true);
                var ra = new Rect(xd - ta.WidthIncludingTrailingWhitespace - 18, rf.Y, ta.WidthIncludingTrailingWhitespace + 18, 20);
                dc.DrawRectangle(Vertical((Cor("#ffc58f"), 0), (Cor("#f29b4c"), 1)), null, ra, 10, 10);
                dc.DrawText(ta, new Point(ra.X + 9, ra.Center.Y - ta.Height / 2));
                xd = ra.X - 10;
            }
            var to = TM(T("Titulares") + " " + tit.Count + "  ·  " + T("Reservas") + " " + res, 11, MTexto, medio: true);
            var ro = new Rect(xd - to.WidthIncludingTrailingWhitespace - 18, rf.Y, to.WidthIncludingTrailingWhitespace + 18, 20);
            if (ro.X > rf.Right + 6)
            {
                dc.DrawRectangle(new SolidColorBrush(Alfa(Cor("#062414"), 0.6)), null, ro, 10, 10);
                dc.DrawText(to, new Point(ro.X + 9, ro.Center.Y - to.Height / 2));
            }
        }

        // os logos do patrocinador master e do pontual da equipa (null sem o contrato)
        (Bitmap? Master, Bitmap? Pontual) LogosPatrocinio()
        {
            if (_s.J.Op.Marcas is not { } marcas) return (null, null);
            Bitmap? Logo(int marca) => marca != 0 && marcas.Registro(marca) is { } m ? TelaPatrocinio.Logo(marcas.CaminhoLogo(m)) : null;
            return (Logo(_e.MarcaMaster()), _e.JogosPontual() != 0 ? Logo(_e.MarcaPontual()) : null);
        }

        // um painel da placa: o logo num fundo branco ou, sem logo, o nome da equipa
        void PainelPlaca(DrawingContext dc, Bitmap? logo, Rect r, string nome, Color corLetra)
        {
            if (r.Width < 4 || r.Height < 3) return;
            if (logo == null)
            {
                double tl = r.Height * 0.62;
                if (tl >= 6) CentroM(dc, nome, r.Center.X, r.Center.Y, tl, new SolidColorBrush(corLetra), true, r.Width - 4);
                return;
            }
            dc.DrawRectangle(Brushes.White, null, r, 1.5, 1.5);
            var sz = logo.Size;
            double e = Math.Min((r.Width - 4) / sz.Width, (r.Height - 2) / sz.Height);
            using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                dc.DrawImage(logo, new Rect(r.Center.X - sz.Width * e / 2, r.Center.Y - sz.Height * e / 2, sz.Width * e, sz.Height * e));
        }

        // a baliza na linha de fundo d (postes, travessao e a rede para fora do campo)
        static void Baliza(DrawingContext dc, Func<double, double, double, Point> P, double d, double S, Func<double, double> esc, double op)
        {
            double fora = d == 0 ? -2 : d + 2, w = 3.66, h = 2.44;
            var rede = new Pen(new SolidColorBrush(Color.FromArgb((byte)(130 * op), 235, 235, 235)), Math.Max(0.5, S * 0.12));
            var poste = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * op), 255, 255, 255)), Math.Max(1.2, S * 0.36 * esc(d)), lineCap: PenLineCap.Round);
            var veu = new SolidColorBrush(Color.FromArgb((byte)(40 * op), 255, 255, 255));
            StreamGeometry Q(params Point[] p)
            {
                var g = new StreamGeometry();
                using var c = g.Open();
                c.BeginFigure(p[0], true);
                for (int i = 1; i < p.Length; i++) c.LineTo(p[i]);
                c.EndFigure(true);
                return g;
            }
            dc.DrawGeometry(veu, rede, Q(P(-w, fora, 0), P(w, fora, 0), P(w, fora, h * 0.8), P(-w, fora, h * 0.8)));
            dc.DrawGeometry(veu, rede, Q(P(-w, d, h), P(w, d, h), P(w, fora, h * 0.8), P(-w, fora, h * 0.8)));
            foreach (double u in new[] { -w, w })
                dc.DrawGeometry(null, rede, Q(P(u, d, 0), P(u, fora, 0), P(u, fora, h * 0.8), P(u, d, h)));
            dc.DrawLine(poste, P(-w, d, 0), P(-w, d, h));
            dc.DrawLine(poste, P(w, d, 0), P(w, d, h));
            dc.DrawLine(poste, P(-w, d, h), P(w, d, h));
        }

        // a peca: sombra, a base redonda, a camisola com o numero e o nome numa etiqueta
        void Peca(DrawingContext dc, SJogador p, Point pe, double esc, double op, double vizinho, int numero, Color corFundo, Color corLetra)
        {
            string chave = "peca:" + RuntimeHelpers.GetHashCode(p);
            double t = Math.Clamp(esc * 4.6, 15, 26);
            var caixa = new Rect(pe.X - t * 0.55, pe.Y - t * 1.45, t * 1.1, t * 1.6);
            bool sobre = op > 0.9 && caixa.Contains(_ponteiro);
            double s = Anim(chave + ":s", sobre ? 1 : 0, 16);
            bool sel = p == _sel;
            double l = Anim(chave + ":l", sel ? 1 : 0, 12);
            double sobe = 3 * s + 2 * l;
            using var _o = dc.PushOpacity(op);

            // a sombra no chao
            dc.DrawEllipse(new SolidColorBrush(Alfa(Colors.Black, 0.38 - 0.1 * s)), null, new Point(pe.X + t * 0.08, pe.Y + 1), t * 0.62, t * 0.22);
            // o anel de escolhido / do rato no chao
            if (l > 0.01) dc.DrawEllipse(null, new Pen(new SolidColorBrush(Alfa(TemaVerde.Cores.Destaque, 0.9 * l)), 2), pe, t * 0.7, t * 0.26);
            if (s > 0.01) dc.DrawEllipse(new SolidColorBrush(Alfa(Cor("#fdd663"), 0.18 * s)), null, pe, t * 0.78, t * 0.3);

            double y = pe.Y - sobe;
            // a base (moeda): a borda escura e o tampo com brilho
            var baseCor = Mist(corLetra, TemaVerde.Cores.Cartao, 0.35);
            double rx = t * 0.5, ry = t * 0.18, hb = t * 0.14;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(pe.X - rx, y - hb), true);
                c.ArcTo(new Point(pe.X + rx, y - hb), new Size(rx, ry), 0, false, SweepDirection.CounterClockwise);
                c.LineTo(new Point(pe.X + rx, y));
                c.ArcTo(new Point(pe.X - rx, y), new Size(rx, ry), 0, false, SweepDirection.Clockwise);
                c.EndFigure(true);
            }
            dc.DrawGeometry(Horizontal((Mist(baseCor, Colors.Black, 0.25), 0), (Mist(baseCor, Colors.White, 0.1), 0.35), (Mist(baseCor, Colors.Black, 0.5), 1)), null, g);
            dc.DrawEllipse(Vertical((Mist(baseCor, Colors.White, 0.35), 0), (baseCor, 1)), null, new Point(pe.X, y - hb), rx, ry);

            // a camisola: o corpo, as mangas e a gola, com a luz da esquerda
            double cy = y - hb - t * 0.55, w = t * 0.36, hm = t * 0.5;
            var cam = new StreamGeometry();
            using (var c = cam.Open())
            {
                c.BeginFigure(new Point(pe.X - w * 0.45, cy - hm), true);
                c.LineTo(new Point(pe.X - w, cy - hm * 0.82));
                c.LineTo(new Point(pe.X - w * 1.55, cy - hm * 0.2));
                c.LineTo(new Point(pe.X - w * 1.15, cy + hm * 0.08));
                c.LineTo(new Point(pe.X - w * 0.82, cy - hm * 0.2));
                c.LineTo(new Point(pe.X - w * 0.82, cy + hm));
                c.LineTo(new Point(pe.X + w * 0.82, cy + hm));
                c.LineTo(new Point(pe.X + w * 0.82, cy - hm * 0.2));
                c.LineTo(new Point(pe.X + w * 1.15, cy + hm * 0.08));
                c.LineTo(new Point(pe.X + w * 1.55, cy - hm * 0.2));
                c.LineTo(new Point(pe.X + w, cy - hm * 0.82));
                c.LineTo(new Point(pe.X + w * 0.45, cy - hm));
                c.QuadraticBezierTo(new Point(pe.X, cy - hm * 0.45), new Point(pe.X - w * 0.45, cy - hm));
                c.EndFigure(true);
            }
            var claro = Mist(corFundo, Colors.White, 0.35 + 0.15 * s);
            var escuro = Mist(corFundo, Colors.Black, 0.35);
            // contorno: escuro nas camisolas claras, claro nas escuras (aparece na relva)
            var contorno = Lum(corFundo) > 0.45 ? Mist(corFundo, Colors.Black, 0.55) : Mist(corFundo, Colors.White, 0.55);
            dc.DrawGeometry(Horizontal((claro, 0), (corFundo, 0.45), (escuro, 1)), new Pen(new SolidColorBrush(contorno), 0.9), cam);
            // a gola e as pontas das mangas na cor da letra
            var letra = new SolidColorBrush(corLetra);
            dc.DrawGeometry(null, new Pen(letra, Math.Max(1, t * 0.06)), Linha2(new Point(pe.X - w * 0.45, cy - hm), new Point(pe.X, cy - hm * 0.62), new Point(pe.X + w * 0.45, cy - hm)));
            // o brilho
            using (dc.PushClip(new Rect(pe.X - w * 0.82, cy - hm, w * 0.8, hm * 2)))
                dc.DrawGeometry(new SolidColorBrush(Alfa(Colors.White, 0.12 + 0.1 * s)), null, cam);
            if (numero > 0) CentroM(dc, numero.ToString(), pe.X, cy + hm * 0.2, Math.Max(7, t * 0.36), letra, true);
            // lesionado ou suspenso no onze: o aviso vermelho
            if (p.JogosLesao > 0 || p.Suspensao > 0)
            {
                var ra = new Point(pe.X + w * 1.2, cy - hm);
                dc.DrawEllipse(p.Suspensao > 0 ? MLaranja : MVermelho, new Pen(MEscuro, 1), ra, t * 0.2, t * 0.2);
                CentroM(dc, p.Suspensao > 0 ? "S" : "L", ra.X, ra.Y, Math.Max(7, t * 0.24), MEscuro, true);
            }

            // o nome numa etiqueta escura por baixo da peca
            double tn = Math.Clamp(t * 0.42, 8.5, 11);
            double maxN = Math.Max(t * 1.6, Math.Min(t * 3.4, vizinho * esc - 14));
            var nomeJ = NomeJogador(p);
            var tx = TM(nomeJ, tn, sel ? MEscuro : MTexto, true, maxN);
            double wn = Math.Min(tx.WidthIncludingTrailingWhitespace, maxN) + 10;
            var rn = new Rect(pe.X - wn / 2, pe.Y + 1, wn, tn + 5);
            dc.DrawRectangle(new SolidColorBrush(sel ? Alfa(TemaVerde.Cores.Destaque, 0.95) : Alfa(Cor("#062414"), 0.72 + 0.2 * s)), null, rn, rn.Height / 2, rn.Height / 2);
            dc.DrawText(tx, new Point(rn.X + 5, rn.Center.Y - tx.Height / 2));

            if (op > 0.9)
            {
                var alvoR = caixa.Union(rn);
                var jog = p;
                _alvos.Add((alvoR, () =>
                {
                    if (_sel == jog) Alternar(jog);
                    else { _sel = jog; MostrarEscolhido(); }
                }));
                _brilhos.Add((alvoR, chave));
                // o nome cortado: a dica com ele inteiro, pela dica do painel (a geral, que
                // procura texto cortado redesenhando a tela, demorava com o campinho)
                if (LarguraM(nomeJ, tn, true) > maxN) _dicas.Add((alvoR, nomeJ));
            }
        }

        static StreamGeometry Linha2(params Point[] pts)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            c.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(false);
            return g;
        }
    }
}
