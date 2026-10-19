using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using Equipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.App;

// Os logos das equipas e das ligas (Assets/Escudos, gerados por tools/escudos/gerar.py do
// launcher, a partir do football-logos.cc). Sem logo = null: a tela desenha o escudo nas
// cores da equipa, como antes.
//  - escudos.tsv: pais, nome normalizado, arquivo ("-" = sabidamente sem logo). A equipa
//    procura o nome completo e, se ele nao estiver no indice, o curto;
//  - ligas.tsv: pais, nivel (1 a 4, T = taca), arquivo. So vale quando todas as equipas
//    das divisoes sao do mesmo pais.
// Os logos do jogador (DadosJogo: <pasta de dados>/logos) vem primeiro: logos.tsv (pais, nome
// da equipa, arquivo) e leagues.tsv (pais, nivel, arquivo) com o mesmo formato (o nome pode ir
// escrito normalmente, com acentos); sem indice, logos/<PAIS>_<slug do nome>.png; e um arquivo
// com o mesmo nome de um do jogo (ex.: brazil_flamengo.png) substitui o do jogo.
public static class Escudos
{
    const string Pasta = "avares://RetroFoot.App/Assets/Escudos/";
    static readonly object Trava = new();
    static Dictionary<(string, string), string>? _indice, _ligas;
    static readonly Dictionary<string, Bitmap?> _cache = new();

    // o mesmo de casar.norm (python): maiusculas, sem acentos, so A-Z 0-9 e espacos simples
    public static string Normalizar(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            char u = char.ToUpperInvariant(c);
            sb.Append(u is >= 'A' and <= 'Z' or >= '0' and <= '9' ? u : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    static string Texto(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(Cifra.TextoEFT(b).Texto);

    // no visual Original nao ha logos (as telas ficam como o original); a TelaCelular (que
    // usa a outra Escudo, por pais e nomes) e moderna de qualquer jeito
    public static Bitmap? Escudo(Equipa e) =>
        Visual.Moderno ? Escudo(Texto(e.Pais), Texto(e.NomeCompleto), Texto(e.NomeCurto)) : null;

    public static Bitmap? Escudo(string pais, string completo, string curto)
    {
        lock (Trava)
        {
            pais = pais.Trim().ToUpperInvariant();
            // os do jogador: o indice e depois o nome do arquivo pela convencao
            var meu = _meuIndice ??= LerDoJogador("logos.tsv");
            foreach (var nome in new[] { completo, curto })
                if (meu.TryGetValue((pais, Normalizar(nome)), out var a0))
                    return a0 == "-" ? null : DoJogador(a0);
            foreach (var nome in new[] { completo, curto })
                if (nome.Trim().Length > 0)
                    foreach (var ext in new[] { ".png", ".jpg", ".bmp" })
                        if (DoJogador(pais + "_" + RetroFoot.Motor.Arquivos.EquipaJson.Slug(nome) + ext, false) is Bitmap b0) return b0;
            var ind = _indice ??= Ler("escudos.tsv");
            foreach (var nome in new[] { completo, curto })
                if (ind.TryGetValue((pais, Normalizar(nome)), out var arq) && arq != "-")
                    return DoJogador(arq, false) ?? Imagem(arq);
            // sem indice: pelo nome do arquivo, <prefixo do pais>_<nome>, com e sem a UF
            // ("GOIATUBA ESPORTE CLUBE (GO)" / "GOIATUBA" -> brazil_goiatuba.png)
            if (PrefixoPais(ind, pais) is { } pre)
                foreach (var slug in SlugsDe(completo, curto))
                    if (Imagem(pre + "_" + slug + ".png") is Bitmap b1) return b1;
            return null;
        }
    }

    static Dictionary<string, string>? _prefixos;

    // o prefixo dos arquivos de cada pais ("BRA" -> "brazil"), tirado do proprio indice
    static string? PrefixoPais(Dictionary<(string, string), string> ind, string pais)
    {
        if (_prefixos == null)
        {
            _prefixos = new();
            foreach (var ((p, _), arq) in ind)
            {
                int i = arq.IndexOf('_');
                if (arq != "-" && i > 0 && !_prefixos.ContainsKey(p)) _prefixos[p] = arq[..i];
            }
        }
        return _prefixos.TryGetValue(pais, out var x) ? x : null;
    }

    // os nomes de arquivo possiveis: o curto e o completo, sem e com a UF (a do nome completo
    // entre parenteses vale para os dois), e sem as siglas soltas (FC, EC, SC...)
    static readonly HashSet<string> Siglas = new() { "fc", "ec", "sc", "ac", "ad", "aa", "se", "sd", "ge", "cr", "afc", "esporte", "clube", "futebol", "sport", "club", "sociedade", "esportiva", "associacao", "atletica", "desportiva", "de", "do", "da", "e" };

    static IEnumerable<string> SlugsDe(string completo, string curto)
    {
        var vistos = new HashSet<string>();
        var m = System.Text.RegularExpressions.Regex.Match(completo.Trim(), @"^(.*?)\s*\(([A-Za-z]{2})\)\s*$");
        string uf = m.Success ? m.Groups[2].Value.ToLowerInvariant() : "";
        foreach (var nome in new[] { curto, m.Success ? m.Groups[1].Value : completo })
        {
            var s = RetroFoot.Motor.Arquivos.EquipaJson.Slug(nome.Trim());
            var limpo = string.Join('-', s.Split('-').Where(t => !Siglas.Contains(t)));
            foreach (var x in new[] { s, limpo })
            {
                if (x.Length == 0) continue;
                if (vistos.Add(x)) yield return x;
                if (uf.Length > 0 && !x.EndsWith("-" + uf) && vistos.Add(x + "-" + uf)) yield return x + "-" + uf;
            }
        }
    }

    // divisao: 0 = 1a divisao ... 3 = 4a (o indice de Jogo98.Divisoes); o Distrital nao tem
    public static Bitmap? Liga(Jogo98 s, int divisao) =>
        divisao is >= 0 and <= 3 ? DaLiga(s, (divisao + 1).ToString()) : null;

    public static Bitmap? Taca(Jogo98 s) => DaLiga(s, "T");

    static Bitmap? DaLiga(Jogo98 s, string nivel)
    {
        if (!Visual.Moderno) return null;
        var pais = PaisUnico(s);
        if (pais == null) return null;
        lock (Trava)
        {
            var meu = _minhasLigas ??= LerDoJogador("leagues.tsv", false);
            if (meu.TryGetValue((pais, nivel), out var a0)) return a0 == "-" ? null : DoJogador(a0);
            var l = _ligas ??= Ler("ligas.tsv");
            return l.TryGetValue((pais, nivel), out var arq) && arq != "-" ? DoJogador(arq, false) ?? Imagem(arq) : null;
        }
    }

    // o pais de todas as equipas das divisoes (1a a 4a e Distrital), ou null se ha mais de um
    public static string? PaisUnico(Jogo98 s)
    {
        var ids = s.Divisoes.SelectMany(d => d.Equipas).ToHashSet();
        var paises = s.Equipas.Where(e => ids.Contains(e.ID)).Select(e => Texto(e.Pais).Trim().ToUpperInvariant()).Distinct().ToList();
        return paises.Count == 1 ? paises[0] : null;
    }

    // um controle pronto: o logo em tam x tam (ou null, para a tela manter o que tinha)
    public static Control? Controle(Bitmap? b, double tam) => b == null ? null : new Image
    {
        Source = b, Width = tam, Height = tam, Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
    }.Com(i => RenderOptions.SetBitmapInterpolationMode(i, BitmapInterpolationMode.HighQuality));

    public static Control? Controle(Equipa e, double tam) => Controle(Escudo(e), tam);

    // o logo do Moderno: com o circulo claro por tras quando some no verde (PrecisaDisco)
    public static Control? ControleComDisco(Bitmap? b, double tam)
    {
        if (Controle(b, tam) is not { } im) return null;
        if (!PrecisaDisco(b!)) return im;
        im.HorizontalAlignment = HorizontalAlignment.Center;
        im.VerticalAlignment = VerticalAlignment.Center;
        return new Panel
        {
            Width = tam, Height = tam, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Avalonia.Controls.Shapes.Ellipse { Width = tam + 4, Height = tam + 4, Margin = new Thickness(-2), Fill = TemaVerde.DiscoLogo },
                im,
            },
        };
    }

    static T Com<T>(this T c, Action<T> f) { f(c); return c; }

    // o logo e escuro (media dos pontos visiveis abaixo de ~22% de brilho e menos de 1/4
    // deles claros ou de cor viva): no visual Moderno (o verde do TemaVerde) ganha um circulo
    // claro por tras para nao sumir. Os de preto com cor forte (o Flamengo, rubro-negro)
    // leem-se bem no escuro e ficam sem o circulo.
    static readonly Dictionary<Bitmap, bool> _escuros = new(), _verdes = new();
    public static bool Escuro(Bitmap b)
    {
        lock (Trava)
        {
            if (_escuros.TryGetValue(b, out var r)) return r;
            r = false;
            var buf = Pixels(b);
            if (buf != null)
            {
                double soma = 0; long n = 0, vivos = 0;
                for (int i = 0; i + 3 < buf.Length; i += 4)
                {
                    if (buf[i + 3] < 128) continue;
                    int az = buf[i], vd = buf[i + 1], vm = buf[i + 2];   // BGRA
                    soma += az + vd + vm;
                    n++;
                    int mx = Math.Max(vm, Math.Max(vd, az)), mn = Math.Min(vm, Math.Min(vd, az));
                    if (0.2126 * vm + 0.7152 * vd + 0.0722 * az > 0.3 * 255 || mx - mn > 90 && mx > 120) vivos++;
                }
                r = n > 0 && soma / (n * 3 * 255.0) < 0.22 && vivos < n / 4.0;
            }
            return _escuros[b] = r;
        }
    }

    // o logo some no verde do Moderno (TemaVerde): escuro, ou com a borda (os pontos opacos
    // encostados no transparente) quase toda verde (Coritiba, Guarani, Palmeiras...). Esses
    // ganham o circulo claro (TemaVerde.DiscoLogo) por tras.
    // (o usuario nao quer fundo atras dos logos: o circulo claro fica desligado)
    public static bool PrecisaDisco(Bitmap b)
    {
        return false;
#pragma warning disable CS0162
        if (Escuro(b)) return true;
        lock (Trava)
        {
            if (_verdes.TryGetValue(b, out var r)) return r;
            r = false;
            var buf = Pixels(b);
            if (buf != null)
            {
                int w = b.PixelSize.Width, h = b.PixelSize.Height;
                bool Opaco(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && buf[(y * w + x) * 4 + 3] >= 128;
                long borda = 0, verdes = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (!Opaco(x, y) || Opaco(x - 1, y) && Opaco(x + 1, y) && Opaco(x, y - 1) && Opaco(x, y + 1)) continue;
                        int i = (y * w + x) * 4, az = buf[i], vd = buf[i + 1], vm = buf[i + 2];   // BGRA
                        borda++;
                        double lum = (0.2126 * vm + 0.7152 * vd + 0.0722 * az) / 255;
                        if (vd > vm + 20 && vd > az + 5 && lum < 0.6) verdes++;
                    }
                r = borda > 0 && verdes > borda * 0.5;
            }
            return _verdes[b] = r;
        }
    }

    // os pontos BGRA do bitmap (null se nao der para ler)
    static byte[]? Pixels(Bitmap b)
    {
        try
        {
            int w = b.PixelSize.Width, h = b.PixelSize.Height, stride = w * 4;
            var buf = new byte[stride * h];
            var p = System.Runtime.InteropServices.Marshal.AllocHGlobal(buf.Length);
            try
            {
                b.CopyPixels(new PixelRect(0, 0, w, h), p, buf.Length, stride);
                System.Runtime.InteropServices.Marshal.Copy(p, buf, 0, buf.Length);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(p); }
            return buf;
        }
        catch { return null; }
    }

    static Dictionary<(string, string), string>? _meuIndice, _minhasLigas;

    // os logos do jogador mudaram (a pasta de dados): le de novo na proxima vez
    public static void Recarregar()
    {
        lock (Trava)
        {
            _meuIndice = _minhasLigas = null;
            foreach (var k in _cache.Keys.Where(k => k.StartsWith("@")).ToList()) _cache.Remove(k);
        }
    }

    // um arquivo de logos/ (null se nao existe); avisar = escrever no erro se nao abrir
    static Bitmap? DoJogador(string arq, bool avisar = true)
    {
        var k = "@" + arq;
        if (_cache.TryGetValue(k, out var b)) return b;
        b = null;
        try
        {
            var cam = Path.Combine(DadosJogo.Logos, arq);
            if (File.Exists(cam)) b = new Bitmap(cam);
            else if (avisar) Console.Error.WriteLine("logos: falta " + cam);
        }
        catch (Exception x) { Console.Error.WriteLine("logos: " + arq + ": " + x.Message); }
        return _cache[k] = b;
    }

    // logos.tsv / leagues.tsv do jogador: pais, nome (ou nivel), arquivo
    static Dictionary<(string, string), string> LerDoJogador(string nome, bool normalizar = true)
    {
        var d = new Dictionary<(string, string), string>();
        try
        {
            var arq = Path.Combine(DadosJogo.Logos, nome);
            if (!File.Exists(arq)) return d;
            foreach (var l in File.ReadAllLines(arq, Encoding.UTF8))
            {
                if (l.Trim().Length == 0 || l.TrimStart()[0] == '#') continue;
                var c = l.Split('\t');
                if (c.Length < 3 || c[0].Trim().Equals("country", StringComparison.OrdinalIgnoreCase)) continue;
                var chave = normalizar ? Normalizar(c[1]) : c[1].Trim().ToUpperInvariant();
                d[(c[0].Trim().ToUpperInvariant(), chave)] = c[2].Trim();
            }
        }
        catch (Exception x) { Console.Error.WriteLine("logos: " + nome + ": " + x.Message); }
        return d;
    }

    // ---------------------------------------------------------------- Editor de Equipas
    // o logo que o jogo acharia sozinho (o "Automatico" do editor): so a colecao do jogo, sem
    // os do jogador; null = sem logo (o jogo desenha o escudo nas cores)
    public static string? ArquivoDoPacote(string pais, string completo, string curto)
    {
        lock (Trava)
        {
            var ind = _indice ??= Ler("escudos.tsv");
            pais = pais.Trim().ToUpperInvariant();
            foreach (var nome in new[] { completo, curto })
                if (ind.TryGetValue((pais, Normalizar(nome)), out var arq)) return arq == "-" ? null : arq;
            return null;
        }
    }

    public static string? LigaDoPacote(string pais, string nivel)
    {
        lock (Trava)
        {
            var l = _ligas ??= Ler("ligas.tsv");
            return l.TryGetValue((pais.Trim().ToUpperInvariant(), nivel), out var arq) && arq != "-" ? arq : null;
        }
    }

    public static Bitmap? DoPacote(string arq) { lock (Trava) return Imagem(arq); }

    // um arquivo de logos/ do jogador, sem guardar no cache (o editor troca os arquivos)
    public static Bitmap? DoJogadorAgora(string arq)
    {
        try
        {
            var cam = Path.Combine(DadosJogo.Logos, arq);
            return File.Exists(cam) ? new Bitmap(new MemoryStream(File.ReadAllBytes(cam))) : null;
        }
        catch { return null; }
    }

    // a colecao do jogo (todos os PNG de Assets/Escudos) e o pais de cada um pelo indice
    // (o prefixo do nome: "brazil_" = BRA)
    public sealed record ItemColecao(string Arquivo, string Nome, string? Pais);

    static List<ItemColecao>? _colecao;
    public static IReadOnlyList<ItemColecao> Colecao()
    {
        lock (Trava)
        {
            if (_colecao != null) return _colecao;
            var prefixos = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var ((pais, _), arq) in (_indice ??= Ler("escudos.tsv")).Concat(_ligas ??= Ler("ligas.tsv")))
                if (arq != "-" && arq.IndexOf('_') is int k and > 0) prefixos.TryAdd(arq[..k], pais);
            var r = new List<ItemColecao>();
            try
            {
                foreach (var u in AssetLoader.GetAssets(new Uri(Pasta), null))
                {
                    var arq = Path.GetFileName(Uri.UnescapeDataString(u.AbsolutePath));
                    if (!arq.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    var b = Path.GetFileNameWithoutExtension(arq);
                    int k = b.IndexOf('_');
                    var nome = (k > 0 ? b[(k + 1)..] : b).Replace('-', ' ');
                    r.Add(new ItemColecao(arq, nome, k > 0 ? prefixos.GetValueOrDefault(b[..k]) : null));
                }
            }
            catch (Exception x) { Console.Error.WriteLine("escudos: colecao: " + x.Message); }
            return _colecao = r.OrderBy(i => i.Arquivo, StringComparer.Ordinal).ToList();
        }
    }

    static Bitmap? Imagem(string arq)
    {
        if (_cache.TryGetValue(arq, out var b)) return b;
        try { b = new Bitmap(AssetLoader.Open(new Uri(Pasta + arq))); }
        catch { b = null; }
        return _cache[arq] = b;
    }

    static Dictionary<(string, string), string> Ler(string nome)
    {
        var d = new Dictionary<(string, string), string>();
        try
        {
            using var r = new StreamReader(AssetLoader.Open(new Uri(Pasta + nome)), Encoding.UTF8);
            for (string? l; (l = r.ReadLine()) != null;)
            {
                if (l.Length == 0 || l[0] == '#') continue;
                var c = l.Split('\t');
                if (c.Length >= 3) d[(c[0], c[1])] = c[2];
            }
        }
        catch (Exception x) { Console.Error.WriteLine("escudos: " + nome + ": " + x.Message); }
        return d;
    }
}
