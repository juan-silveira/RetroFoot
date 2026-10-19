using System;
using System.Collections.Generic;
using System.Linq;
using RetroFoot.Motor.Base;

namespace RetroFoot.Motor.Arquivos;

// Qualquer imagem vira um logo no padrao do jogo (os de Assets/Escudos): o que nao e fundo,
// recortado e centrado num quadrado transparente, encostado nos lados no sentido mais comprido
// (como os do jogo, sem margem), aqui com 256x256. Pixels puros (RGBA sem pre-multiplicar); a
// App le e grava os arquivos (PNG, JPG, WEBP, BMP, SVG). Portado e ampliado de
// src-cross/EscudoImagem.cs do launcher do jogo classico (ramo editor-escudos): reamostragem
// ponderada pelo alfa e as cores sugeridas pelo escudo.
//   - borda transparente ou de uma cor so: cortada;
//   - fundo de uma cor so (branco de um JPG...): pode ser tirado (o que encosta na borda e tem
//     a cor do fundo vira transparente; o branco dentro do escudo fica);
//   - imagem inutil (vazia, minuscula, comprida demais): recusada com o motivo.
public static class EscudoProcessador
{
    public const int Tamanho = 256, Recomendado = 128, Minimo = 32;
    public const double ProporcaoMaxima = 3.0, ProporcaoBoa = 1.6;
    const int Tolerancia = 40;          // distancia RGB ate a cor do fundo que ainda e fundo

    public sealed class Rgba
    {
        public int Largura, Altura;
        public byte[] Px = Array.Empty<byte>();   // R, G, B, A por pixel

        public Rgba() { }
        public Rgba(int l, int a) { Largura = l; Altura = a; Px = new byte[l * a * 4]; }
        public Rgba Copia() => new() { Largura = Largura, Altura = Altura, Px = (byte[])Px.Clone() };
    }

    public sealed class Resultado
    {
        public Rgba? Imagem;                      // 256x256 (null = recusada)
        public string? Erro;                      // o motivo da recusa
        public List<string> Avisos = new();
        public bool TemTransparencia;             // a imagem ja tinha fundo transparente
        public bool FundoUniforme;                // o fundo e de uma cor so (pode ser tirado)
        public bool FundoRemovido;
        public uint CorDoFundo;                   // 0xRRGGBB, quando FundoUniforme
        public int Largura, Altura;               // da imagem original
        public int LarguraUtil, AlturaUtil;       // do que sobrou depois de cortar o fundo
    }

    // removerFundo: tirar o fundo de uma cor so (quando ha)
    public static Resultado Processar(Rgba src, bool removerFundo = true)
    {
        var r = new Resultado { Largura = src.Largura, Altura = src.Altura };
        if (src.Largura < 1 || src.Altura < 1 || src.Px.Length < src.Largura * src.Altura * 4)
        {
            r.Erro = Textos.T("A imagem está vazia.");
            return r;
        }
        var img = src.Copia();
        int w = img.Largura, h = img.Altura;

        // a borda: transparente, de uma cor so ou um desenho que encosta na borda
        var borda = Borda(w, h).ToList();
        int transp = borda.Count(i => img.Px[i * 4 + 3] <= 8);
        r.TemTransparencia = transp >= borda.Count / 2 || Enumerable.Range(0, w * h).Any(i => img.Px[i * 4 + 3] < 250);
        if (transp < borda.Count / 2)
        {
            var (cor, fracao) = CorDaBorda(img, borda);
            if (fracao >= 0.85)
            {
                r.FundoUniforme = true;
                r.CorDoFundo = cor;
            }
        }
        if (r.FundoUniforme && removerFundo)
        {
            TirarFundo(img, r.CorDoFundo);
            r.FundoRemovido = true;
        }

        // o retangulo do que nao e fundo
        bool Fundo(int i)
        {
            if (img.Px[i * 4 + 3] <= 8) return true;
            return r.FundoUniforme && !r.FundoRemovido && Distancia(img, i, r.CorDoFundo) <= Tolerancia;
        }
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (!Fundo(y * w + x))
                {
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
        if (x1 < 0)
        {
            r.Erro = Textos.T("A imagem não tem nada além do fundo.");
            return r;
        }
        int uw = x1 - x0 + 1, uh = y1 - y0 + 1;
        r.LarguraUtil = uw;
        r.AlturaUtil = uh;
        int maior = Math.Max(uw, uh), menor = Math.Min(uw, uh);
        if (maior < Minimo)
        {
            r.Erro = Textos.T("A imagem é pequena demais ({0}x{1} px). Use uma com pelo menos {2} px.", uw, uh, Recomendado);
            return r;
        }
        double prop = (double)maior / Math.Max(1, menor);
        if (prop > ProporcaoMaxima)
        {
            r.Erro = Textos.T("A imagem é comprida demais ({0}x{1} px): um escudo precisa ser quadrado ou quase.", uw, uh);
            return r;
        }
        if (maior < Recomendado) r.Avisos.Add(Textos.T("A imagem é pequena ({0}x{1} px): vai ficar desfocada. O ideal é pelo menos {2} px.", uw, uh, Recomendado));
        if (prop > ProporcaoBoa) r.Avisos.Add(Textos.T("A imagem não é quadrada: o escudo vai ficar estreito."));
        if (r.FundoUniforme && !r.FundoRemovido) r.Avisos.Add(Textos.T("O fundo ficou: o escudo vai aparecer num quadrado de cor."));
        if (!r.TemTransparencia && !r.FundoUniforme) r.Avisos.Add(Textos.T("A imagem não tem fundo transparente: o escudo vai aparecer num quadrado. Prefira um PNG ou SVG com fundo transparente."));

        // no quadrado, encostado nos lados no sentido mais comprido, centrado no outro
        double esc = (double)Tamanho / maior;
        int nw = Math.Clamp((int)Math.Round(uw * esc), 1, Tamanho), nh = Math.Clamp((int)Math.Round(uh * esc), 1, Tamanho);
        var dst = new Rgba(Tamanho, Tamanho);
        Reamostrar(img, x0, y0, uw, uh, dst, (Tamanho - nw) / 2, (Tamanho - nh) / 2, nw, nh);
        r.Imagem = dst;
        return r;
    }

    static IEnumerable<int> Borda(int w, int h)
    {
        for (int x = 0; x < w; x++) { yield return x; if (h > 1) yield return (h - 1) * w + x; }
        for (int y = 1; y < h - 1; y++) { yield return y * w; if (w > 1) yield return y * w + w - 1; }
    }

    static int Distancia(Rgba img, int i, uint cor)
    {
        int dr = img.Px[i * 4] - (int)(cor >> 16 & 0xFF), dg = img.Px[i * 4 + 1] - (int)(cor >> 8 & 0xFF), db = img.Px[i * 4 + 2] - (int)(cor & 0xFF);
        return (int)Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    // a cor mais comum na borda (em grupos de 16 tons por canal, a media do grupo) e a fracao
    // dos pixels opacos da borda perto dela
    static (uint Cor, double Fracao) CorDaBorda(Rgba img, List<int> borda)
    {
        var grupos = new Dictionary<int, (long R, long G, long B, int N)>();
        int opacos = 0;
        foreach (var i in borda)
        {
            if (img.Px[i * 4 + 3] <= 8) continue;
            opacos++;
            int k = (img.Px[i * 4] >> 4) << 8 | (img.Px[i * 4 + 1] >> 4) << 4 | img.Px[i * 4 + 2] >> 4;
            var g = grupos.GetValueOrDefault(k);
            grupos[k] = (g.R + img.Px[i * 4], g.G + img.Px[i * 4 + 1], g.B + img.Px[i * 4 + 2], g.N + 1);
        }
        if (opacos == 0) return (0, 0);
        var m = grupos.Values.MaxBy(g => g.N);
        uint cor = (uint)((m.R / m.N) << 16 | (m.G / m.N) << 8 | (m.B / m.N));
        int perto = borda.Count(i => img.Px[i * 4 + 3] > 8 && Distancia(img, i, cor) <= Tolerancia);
        return (cor, (double)perto / borda.Count);
    }

    // o que encosta na borda com a cor do fundo vira transparente (busca a partir da borda; o
    // branco dentro do escudo fica). Nos contornos, o alfa cai aos poucos e a cor do fundo sai
    // da mistura, para nao sobrar um halo.
    static void TirarFundo(Rgba img, uint cor)
    {
        int w = img.Largura, h = img.Altura;
        var fundo = new bool[w * h];
        var fila = new Queue<int>();
        foreach (var i in Borda(w, h))
            if (!fundo[i] && Distancia(img, i, cor) <= Tolerancia) { fundo[i] = true; fila.Enqueue(i); }
        while (fila.Count > 0)
        {
            int i = fila.Dequeue(), x = i % w, y = i / w;
            void Ir(int j) { if (!fundo[j] && Distancia(img, j, cor) <= Tolerancia) { fundo[j] = true; fila.Enqueue(j); } }
            if (x > 0) Ir(i - 1);
            if (x < w - 1) Ir(i + 1);
            if (y > 0) Ir(i - w);
            if (y < h - 1) Ir(i + w);
        }
        double br = cor >> 16 & 0xFF, bg = cor >> 8 & 0xFF, bb = cor & 0xFF;
        for (int i = 0; i < w * h; i++)
        {
            if (fundo[i]) { img.Px[i * 4 + 3] = 0; continue; }
            int x = i % w, y = i / w;
            bool vizinho = x > 0 && fundo[i - 1] || x < w - 1 && fundo[i + 1] || y > 0 && fundo[i - w] || y < h - 1 && fundo[i + w];
            if (!vizinho) continue;
            int d = Distancia(img, i, cor);
            if (d >= Tolerancia * 3) continue;
            double a = Math.Clamp((d - Tolerancia) / (2.0 * Tolerancia), 0.15, 1.0);
            for (int k = 0; k < 3; k++)
            {
                double b = k == 0 ? br : k == 1 ? bg : bb;
                img.Px[i * 4 + k] = (byte)Math.Clamp(Math.Round((img.Px[i * 4 + k] - b * (1 - a)) / a), 0, 255);
            }
            img.Px[i * 4 + 3] = (byte)Math.Round(img.Px[i * 4 + 3] * a);
        }
    }

    // reduz para lado x lado (miniaturas)
    public static Rgba Reduzir(Rgba src, int lado)
    {
        var dst = new Rgba(lado, lado);
        Reamostrar(src, 0, 0, src.Largura, src.Altura, dst, 0, 0, lado, lado);
        return dst;
    }

    // media por area ao reduzir, bilinear ao ampliar, com a cor ponderada pelo alfa (as bordas
    // transparentes nao escurecem)
    static void Reamostrar(Rgba s, int sx, int sy, int sw, int sh, Rgba d, int dx, int dy, int dw, int dh)
    {
        double fx = (double)sw / dw, fy = (double)sh / dh;
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                double ax = sx + x * fx, bx = sx + (x + 1) * fx, ay = sy + y * fy, by = sy + (y + 1) * fy;
                if (fx < 1) { double c = sx + (x + 0.5) * fx - 0.5; ax = c; bx = c + 1; }
                if (fy < 1) { double c = sy + (y + 0.5) * fy - 0.5; ay = c; by = c + 1; }
                double sr = 0, sg = 0, sb = 0, sa = 0, pesos = 0;
                for (int py = (int)Math.Floor(ay); py < (int)Math.Ceiling(by); py++)
                {
                    double wy = Math.Min(by, py + 1) - Math.Max(ay, py);
                    if (wy <= 0) continue;
                    int cy = Math.Max(sy, Math.Min(sy + sh - 1, py));
                    for (int px = (int)Math.Floor(ax); px < (int)Math.Ceiling(bx); px++)
                    {
                        double wx = Math.Min(bx, px + 1) - Math.Max(ax, px);
                        if (wx <= 0) continue;
                        int cx = Math.Max(sx, Math.Min(sx + sw - 1, px));
                        int i = (cy * s.Largura + cx) * 4;
                        double peso = wx * wy, a = s.Px[i + 3] * peso;
                        sr += s.Px[i] * a; sg += s.Px[i + 1] * a; sb += s.Px[i + 2] * a; sa += a; pesos += peso;
                    }
                }
                int o = ((dy + y) * d.Largura + dx + x) * 4;
                if (sa > 0)
                {
                    d.Px[o] = (byte)Math.Round(sr / sa);
                    d.Px[o + 1] = (byte)Math.Round(sg / sa);
                    d.Px[o + 2] = (byte)Math.Round(sb / sa);
                }
                d.Px[o + 3] = (byte)Math.Round(pesos > 0 ? sa / pesos : 0);
            }
    }

    // ---------------------------------------------------------------- cores
    // a cor das 16 do jogo para um pixel: sem cor (cinzas) pela claridade; com cor pelo matiz,
    // na versao escura (0x80) ou clara (0xFF)
    public static uint CorDoJogo(int r, int g, int b)
    {
        int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        double croma = (mx - mn) / 255.0, l = (mx + mn) / 510.0;
        if (croma < 0.18) return l < 0.25 ? 0x000000u : l < 0.63 ? 0x808080u : l < 0.88 ? 0xC0C0C0u : 0xFFFFFFu;
        double h;
        if (mx == r) h = 60 * (((g - b) / (double)(mx - mn) + 6) % 6);
        else if (mx == g) h = 60 * ((b - r) / (double)(mx - mn) + 2);
        else h = 60 * ((r - g) / (double)(mx - mn) + 4);
        uint escura, clara;
        if (h < 40 || h >= 330) { escura = 0x800000; clara = 0xFF0000; }
        else if (h < 75) { escura = 0x808000; clara = 0xFFFF00; }
        else if (h < 165) { escura = 0x008000; clara = 0x00FF00; }
        else if (h < 195) { escura = 0x008080; clara = 0x00FFFF; }
        else if (h < 270) { escura = 0x000080; clara = 0x0000FF; }
        else { escura = 0x800080; clara = 0xFF00FF; }
        return l < 0.36 ? escura : clara;
    }

    static bool TemCor(uint c) => !((c >> 16 & 0xFF) == (c >> 8 & 0xFF) && (c >> 8 & 0xFF) == (c & 0xFF));

    static uint Irma(uint c)
    {
        static uint Troca(uint v) => v == 0x80 ? 0xFFu : v == 0xFF ? 0x80u : v;
        return Troca(c >> 16 & 0xFF) << 16 | Troca(c >> 8 & 0xFF) << 8 | Troca(c & 0xFF);
    }

    public static List<uint> CoresDominantes(Rgba r)
    {
        var conta = new Dictionary<uint, int>();
        int total = 0;
        for (int i = 0; i + 3 < r.Px.Length; i += 4)
        {
            if (r.Px[i + 3] < 200) continue;
            var c = CorDoJogo(r.Px[i], r.Px[i + 1], r.Px[i + 2]);
            conta[c] = conta.GetValueOrDefault(c) + 1;
            total++;
        }
        foreach (var c in conta.Keys.Where(TemCor).ToList())
            if (conta.ContainsKey(c) && conta.TryGetValue(Irma(c), out var n2) && n2 <= conta[c])
            {
                conta[c] += n2;
                conta.Remove(Irma(c));
            }
        return conta.Where(kv => kv.Value >= total * 0.04).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
    }

    public static double Contraste(uint a, uint b)
    {
        static double L(uint c)
        {
            static double C(uint v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * C(c >> 16 & 0xFF) + 0.7152 * C(c >> 8 & 0xFF) + 0.0722 * C(c & 0xFF);
        }
        double la = L(a), lb = L(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // pares (letra, fundo) sugeridos pelo escudo: fundo = uma cor dominante, letra = outra
    // legivel sobre ela (ou branco/preto)
    public static List<(uint Letra, uint Fundo)> Sugestoes(Rgba r)
    {
        var dom = CoresDominantes(r);
        var res = new List<(uint, uint)>();
        void Por(uint letra, uint fundo) { if (letra != fundo && !res.Contains((letra, fundo))) res.Add((letra, fundo)); }
        foreach (var fundo in dom.Where(TemCor).Concat(dom.Where(c => !TemCor(c))).Take(3))
        {
            foreach (var letra in dom.Where(c => c != fundo && Contraste(c, fundo) >= 2.5).Take(2)) Por(letra, fundo);
            Por(Contraste(0xFFFFFF, fundo) >= Contraste(0x000000, fundo) ? 0xFFFFFFu : 0x000000u, fundo);
        }
        if (res.Count == 0) Por(0x000000, 0xFFFFFF);
        return res.Take(6).ToList();
    }
}
