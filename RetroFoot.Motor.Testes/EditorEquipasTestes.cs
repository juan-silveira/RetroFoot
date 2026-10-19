using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RetroFoot.Motor.Arquivos;
using Xunit;
using Xunit.Abstractions;

namespace RetroFoot.Motor.Testes;

// O Editor de Equipas do RetroFoot (Motor/Arquivos: EquipaEditavel, RegrasEquipa, LogosDados,
// PatchEquipas, EscudoProcessador): a equipa aberta e gravada sem mudar um byte, as regras do
// original, o patch de equipas e logos e os escudos adaptados de qualquer imagem.
public class EditorEquipasTestes(ITestOutputHelper saida)
{
    static string Jogo() => Arquivos.Raiz() ?? throw new InvalidOperationException("jogo original não encontrado");

    static string Temp()
    {
        var p = Path.Combine(Path.GetTempPath(), "retrofoot-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    // uma pasta de dados com as equipas e listas originais
    static string PastaOriginal()
    {
        var raiz = Temp();
        var r = PastaDados.Importar(Jogo(), raiz);
        Assert.True(r.Equipas >= 200);
        return raiz;
    }

    [Fact]
    public void OriginaisAbremGravamIguaisEValidas()
    {
        var raiz = PastaOriginal();
        try
        {
            var regras = RegrasEquipa.DaPasta(raiz);
            int n = 0, ruins = 0;
            foreach (var j in PastaDados.JsonsDeEquipas(raiz))
            {
                var e = EquipaEditavel.Ler(j);
                var l = EquipaJson.LerArquivo(j);
                Assert.True(e.ParaEft().AsSpan().SequenceEqual(l.Bytes), j);
                Assert.Equal(File.ReadAllText(j), e.ParaJson());
                Assert.True(e.MesmoConteudo(e.Copia()));
                var erros = regras.Validar(e);
                // duas originais (CRUZEIRO EC, INTER DE LIMEIRA-BR) vem sem treinador
                if (erros.Count > 0) { ruins++; saida.WriteLine($"{e.NomeCompleto}: {string.Join("; ", erros)}"); }
                Assert.True(erros.All(x => x.Contains("treinador")), $"{e.NomeCompleto}: {string.Join("; ", erros)}");
                n++;
            }
            saida.WriteLine($"{n} equipas originais: abertas e gravadas iguais; {ruins} recusadas pelas regras");
            Assert.True(ruins <= 2);
        }
        finally { Directory.Delete(raiz, true); }
    }

    static EquipaEditavel Nova(string pais = "BRA")
    {
        var e = new EquipaEditavel { NomeCompleto = "Clube Teste da Várzea", NomeAbreviado = "Várzea", Pais = pais, Nivel = 7, Treinador = "Zé Treinador", CorLetra = 0xFFFF00, CorFundo = 0x000080 };
        e.Jogadores.Add(new JogadorEditavel { Nome = "Goleiro", Posicao = 0, Pais = pais });
        for (int i = 0; i < 13; i++) e.Jogadores.Add(new JogadorEditavel { Nome = "Jogador " + (char)('A' + i), Posicao = 1 + i % 3, Pais = pais });
        return e;
    }

    [Fact]
    public void RegrasDoOriginal()
    {
        var r = new RegrasEquipa(new[] { "BRA", "POR", "ESP", "FRA", "ARG" }, new[] { "POR", "ESP", "FRA" }, new[] { "BRA", "POR" });
        var e = Nova();
        Assert.Empty(r.Validar(e));
        e.Jogadores.RemoveAt(13);
        Assert.Contains(r.Validar(e), m => m.Contains("suficientes"));
        e = Nova();
        while (e.Jogadores.Count < 21) e.Jogadores.Add(new JogadorEditavel { Nome = "Extra", Posicao = 2, Pais = "BRA" });
        Assert.Contains(r.Validar(e), m => m.Contains("demasiados jogadores (máximo 20)"));
        e = Nova();
        e.Jogadores[0].Posicao = 1;
        Assert.Contains(r.Validar(e), m => m.Contains("guarda-redes"));
        e = Nova();
        for (int i = 1; i <= 6; i++) e.Jogadores[i].Pais = "ARG";
        Assert.Equal(6, r.Estrangeiros(e));
        Assert.Contains(r.Validar(e), m => m.Contains("estrangeiros"));
        // PLOP: portugues no Brasil nao conta; Bosman: espanhol em Portugal nao conta
        Assert.Equal(RegrasEquipa.Situacao.Plop, r.Classificar("POR", "BRA"));
        Assert.Equal(RegrasEquipa.Situacao.Bosman, r.Classificar("esp", "POR"));
        Assert.Equal(RegrasEquipa.Situacao.Estrangeiro, r.Classificar("ESP", "BRA"));
        e = Nova();
        e.Jogadores[3].Nome = "Jogador 10";
        Assert.Contains(r.Validar(e), m => m.Contains("inválidos"));
        e = Nova("XYZ");
        Assert.Contains(r.Validar(e), m => m.Contains("país"));
        Assert.Equal("VARZEA.EFT", EquipaEditavel.SugerirArquivo("Várzea", new[] { "PALMEIRA.EFT" }));
        Assert.Equal("VARZEA2.EFT", EquipaEditavel.SugerirArquivo("Várzea", new[] { "varzea.eft" }));
        Assert.Equal("CLUBEDE3.EFT", EquipaEditavel.SugerirArquivo("Clube de Teste", new[] { "CLUBEDET.EFT", "CLUBEDE2.EFT" }));
    }

    [Fact]
    public void GravarNovaMudarPaisEApagar()
    {
        var raiz = Temp();
        try
        {
            var e = Nova();
            var cam = e.Gravar(raiz);
            Assert.Equal("VARZEA.EFT", e.Arquivo);
            Assert.Equal(Path.Combine(raiz, "teams", "BRA", "clube-teste-da-varzea.json"), cam);
            var lida = EquipaEditavel.Ler(cam);
            Assert.True(lida.MesmoConteudo(e));
            Assert.Equal("Várzea", lida.NomeAbreviado);
            // o pais muda: o JSON vai para a outra pasta e a velha some
            lida.Pais = "POR";
            var cam2 = lida.Gravar(raiz);
            Assert.Equal(Path.Combine(raiz, "teams", "POR", "clube-teste-da-varzea.json"), cam2);
            Assert.False(File.Exists(cam));
            Assert.Single(PastaDados.JsonsDeEquipas(raiz));
            // outra equipa com o mesmo nome curto ganha outro .EFT
            var b = Nova();
            b.Gravar(raiz);
            Assert.Equal("VARZEA2.EFT", b.Arquivo);
            Assert.Equal(2, PastaDados.Ler(raiz).Equipas.Count);
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void IndiceDeLogos()
    {
        var raiz = Temp();
        try
        {
            LogosDados.Definir(raiz, LogosDados.Indice, "bra", "Clube Teste da Várzea", "BRA_varzea.png");
            Assert.Equal("BRA_varzea.png", LogosDados.Achar(raiz, LogosDados.Indice, "BRA", "CLUBE TESTE DA VARZEA")!.Arquivo);
            // renomeada: a linha velha sai
            LogosDados.Definir(raiz, LogosDados.Indice, "BRA", "Novo Nome", "BRA_novo.png", "Clube Teste da Várzea");
            Assert.Single(LogosDados.Ler(raiz, LogosDados.Indice));
            LogosDados.Definir(raiz, LogosDados.Ligas, "BRA", "1", "BRA_league-1.png");
            LogosDados.Definir(raiz, LogosDados.Ligas, "BRA", "t", "BRA_cup.png");
            Assert.Equal(2, LogosDados.Ler(raiz, LogosDados.Ligas).Count);
            LogosDados.Remover(raiz, LogosDados.Ligas, "BRA", "T");
            Assert.Single(LogosDados.Ler(raiz, LogosDados.Ligas));
            Assert.Equal(1, LogosDados.Mesclar(raiz, LogosDados.Indice, "country\tteam\tfile\nBRA\tnovo nome\t-\n"));
            Assert.Equal("-", LogosDados.Achar(raiz, LogosDados.Indice, "BRA", "Novo Nome")!.Arquivo);
            Assert.Equal("BRA_cup.png", LogosDados.ArquivoLiga("bra", "T"));
            Assert.Equal("BRA_sc-palmeiras.png", LogosDados.ArquivoEquipa("BRA", "S.C. Palmeiras"));
        }
        finally { Directory.Delete(raiz, true); }
    }

    [Fact]
    public void PatchDeEquipasELogosIdaEVolta()
    {
        var a = Temp();
        var b = Temp();
        try
        {
            var e1 = Nova();
            e1.Gravar(a);
            var e2 = Nova("POR");
            e2.NomeCompleto = "Sporting Teste";
            e2.NomeAbreviado = "Sporting T";
            e2.Gravar(a);
            Directory.CreateDirectory(Path.Combine(a, "logos"));
            File.WriteAllBytes(Path.Combine(a, "logos", "BRA_clube-teste-da-varzea.png"), new byte[] { 1, 2, 3 });
            LogosDados.Definir(a, LogosDados.Indice, "BRA", e1.NomeCompleto, "BRA_clube-teste-da-varzea.png");
            File.WriteAllBytes(Path.Combine(a, "logos", "POR_league-1.png"), new byte[] { 4, 5 });
            LogosDados.Definir(a, LogosDados.Ligas, "POR", "1", "POR_league-1.png");
            LogosDados.Definir(a, LogosDados.Ligas, "ESP", "1", "ESP_league-1.png");   // de outro pais: nao vai

            var zip = Path.Combine(a, "patches", "teste.zip");
            var res = PatchEquipas.Exportar(a, PastaDados.JsonsDeEquipas(a), zip, "Teste", comEft: true);
            Assert.Equal(new PatchEquipas.Resumo(2, 2, 1), res);
            using (var z = ZipFile.OpenRead(zip))
            {
                var nomes = z.Entries.Select(x => x.FullName).ToList();
                Assert.Contains("retrofoot-patch.json", nomes);
                Assert.Contains("EQUIPAS/VARZEA.EFT", nomes);
                Assert.Contains("logos/logos.tsv", nomes);
                Assert.Contains("teams/POR/sporting-teste.json", nomes);
            }
            Assert.NotNull(PatchEquipas.LerManifesto(zip));

            var r = PastaDados.Importar(zip, b);
            Assert.Equal(2, r.Equipas);          // o JSON vale; o .EFT da mesma equipa nao conta outra vez
            Assert.Empty(r.Erros);
            Assert.Equal(2, r.Copiados["logos"]);
            Assert.True(File.Exists(Path.Combine(b, "logos", "BRA_clube-teste-da-varzea.png")));
            Assert.NotNull(LogosDados.Achar(b, LogosDados.Indice, "BRA", e1.NomeCompleto));
            Assert.Single(LogosDados.Ler(b, LogosDados.Ligas));
            var lidas = PastaDados.JsonsDeEquipas(b).Select(EquipaEditavel.Ler).OrderBy(x => x.Arquivo).ToList();
            Assert.True(lidas[0].MesmoConteudo(EquipaEditavel.Ler(PastaDados.IndiceEquipas(a)[lidas[0].Arquivo])));
            // so os .EFT (um patch do jogo classico) tambem entram
            var c = Temp();
            var soEft = Path.Combine(c, "EQUIPAS");
            PastaDados.ExportarEquipas(a, soEft);
            Assert.Equal(2, PastaDados.Importar(soEft, c).Equipas);
            Directory.Delete(c, true);
        }
        finally { Directory.Delete(a, true); Directory.Delete(b, true); }
    }

    // ---------------------------------------------------------------- escudos
    static EscudoProcessador.Rgba Imagem(int w, int h, Func<int, int, (byte R, byte G, byte B, byte A)> px)
    {
        var r = new EscudoProcessador.Rgba(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (cr, cg, cb, ca) = px(x, y);
                int i = (y * w + x) * 4;
                r.Px[i] = cr; r.Px[i + 1] = cg; r.Px[i + 2] = cb; r.Px[i + 3] = ca;
            }
        return r;
    }

    static (byte, byte, byte, byte) Px(EscudoProcessador.Rgba r, int x, int y)
    {
        int i = (y * r.Largura + x) * 4;
        return (r.Px[i], r.Px[i + 1], r.Px[i + 2], r.Px[i + 3]);
    }

    [Fact]
    public void EscudoDeJpgComFundoBranco()
    {
        // 400x300, fundo branco, um circulo vermelho de raio 100 com um ponto branco no meio
        var src = Imagem(400, 300, (x, y) =>
        {
            double d = Math.Sqrt((x - 200) * (x - 200) + (y - 150) * (y - 150));
            if (d < 10) return (255, 255, 255, 255);
            return d < 100 ? ((byte)200, (byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255);
        });
        var r = EscudoProcessador.Processar(src, true);
        Assert.Null(r.Erro);
        Assert.True(r.FundoUniforme && r.FundoRemovido);
        Assert.Equal(0xFFFFFFu, r.CorDoFundo);
        var img = r.Imagem!;
        Assert.Equal(256, img.Largura);
        Assert.Equal(256, img.Altura);
        Assert.Equal(0, Px(img, 2, 2).Item4);                 // canto transparente
        Assert.Equal((200, 0, 0, 255), ((int)Px(img, 128, 40).Item1, (int)Px(img, 128, 40).Item2, (int)Px(img, 128, 40).Item3, (int)Px(img, 128, 40).Item4));
        Assert.Equal(255, Px(img, 128, 128).Item4);           // o branco de dentro fica
        Assert.True(Px(img, 1, 128).Item4 > 0);               // encostado nos lados (sem margem)
        Assert.True(Px(img, 128, 1).Item4 > 0);
        // sem tirar o fundo: fica o quadrado branco recortado, com aviso
        var s = EscudoProcessador.Processar(src, false);
        Assert.True(s.FundoUniforme && !s.FundoRemovido);
        Assert.Equal(255, Px(s.Imagem!, 2, 2).Item4);
        Assert.NotEmpty(s.Avisos);
    }

    [Fact]
    public void EscudoTransparenteERecusas()
    {
        // PNG transparente com margem grande: recortado e centrado
        var png = Imagem(500, 500, (x, y) => x is >= 200 and < 300 && y is >= 150 and < 350 ? ((byte)0, (byte)0, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        var r = EscudoProcessador.Processar(png);
        Assert.Null(r.Erro);
        Assert.False(r.FundoUniforme);
        Assert.Equal((100, 200), (r.LarguraUtil, r.AlturaUtil));
        Assert.Equal(255, Px(r.Imagem!, 128, 0).Item4);
        Assert.Equal(0, Px(r.Imagem!, 10, 128).Item4);        // estreito: sobra dos lados
        Assert.Contains(r.Avisos, a => a.Contains("quadrada"));

        Assert.NotNull(EscudoProcessador.Processar(Imagem(200, 200, (_, _) => (0, 0, 0, 0))).Erro);           // vazia
        Assert.NotNull(EscudoProcessador.Processar(Imagem(20, 20, (_, _) => (9, 9, 9, 255))).Erro);           // minuscula
        var faixa = Imagem(600, 100, (x, y) => y is > 10 and < 90 ? ((byte)0, (byte)128, (byte)0, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        Assert.Contains("comprida", EscudoProcessador.Processar(faixa).Erro);
        var pequena = EscudoProcessador.Processar(Imagem(64, 64, (x, y) => (x + y) % 2 == 0 ? ((byte)255, (byte)0, (byte)0, (byte)255) : ((byte)0, (byte)0, (byte)255, (byte)255)));
        Assert.Null(pequena.Erro);
        Assert.Contains(pequena.Avisos, a => a.Contains("pequena"));

        var cores = EscudoProcessador.Sugestoes(r.Imagem!);
        Assert.Contains(cores, c => c.Fundo == 0x0000FF);
    }
}
