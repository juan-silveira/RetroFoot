using System;
using System.IO;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using Xunit;
using Xunit.Abstractions;

namespace RetroFoot.Motor.Testes;

public class BaseTestes(ITestOutputHelper saida)
{
    [Fact]
    public void AleatorioSequencia()
    {
        var g = new Aleatorio(0);
        uint[] quer = { 1, 0x08088406, 0xDC6DAC1F };
        foreach (var q in quer) Assert.Equal(q, g.Avanca());
    }

    [Fact]
    public void AleatorioIgualAoAsm()
    {
        // seg25:212d: dx:ax = semente; resultado = alto16(alto x n + alto16(baixo x n))
        var g = new Aleatorio(0xDEADBEEF);
        var h = new Aleatorio(0xDEADBEEF);
        for (int i = 0; i < 1000; i++)
        {
            int n = 1 + i * 37 % 5000;
            uint s = h.Avanca();
            uint lo = s & 0xFFFF, hi = s >> 16;
            uint m1 = lo * (uint)n;
            uint m2 = hi * (uint)n + (m1 >> 16);
            Assert.Equal((int)(m2 >> 16), g.Random(n));
        }
    }

    [Fact]
    public void Real48IdaEVolta()
    {
        foreach (var v in new[] { 0, 1, 0.6, 1.5, 2, -3.25, 123456.75, 0.001 })
        {
            var b = new byte[6];
            Reais.PorReal48(b, v);
            Assert.True(Math.Abs(Reais.Real48(b) - v) <= Math.Abs(v) * 1e-11, $"Real48({v})");
        }
        var u = new byte[6];
        Reais.PorReal48(u, 1);
        Assert.Equal(0x81, u[0]);
        Assert.Equal(0, u[5]);
    }

    [Fact]
    public void Ext80IdaEVolta()
    {
        foreach (var v in new[] { 0, 1, 1.05, 2.5, -7, 1e9 })
        {
            var b = new byte[10];
            Reais.PorExt80(b, v);
            Assert.Equal(v, Reais.Ext80(b));
        }
    }

    [Fact]
    public void CifrasIdaEVolta()
    {
        foreach (var s in new[] { "", "Brasil", "São Paulo", "Ryan Giggs" })
        {
            var p = Encoding.Latin1.GetBytes(s);
            var (t1, n1) = Cifra.TextoEFT(Cifra.PorTextoEFT(p));
            Assert.Equal(p, t1); Assert.Equal(p.Length + 1, n1);
            var (t2, n2) = Cifra.TextoTXE(Cifra.PorTextoTXE(p));
            Assert.Equal(p, t2); Assert.Equal(p.Length + 1, n2);
            Assert.Equal(p, Cifra.Rolante(Cifra.PorRolante(p)));
        }
    }

    // exemplo do documento (validado contra o TurboScore): Ryan Giggs, avancado
    [Fact]
    public void RyanGiggs()
    {
        int s = Atributos.Soma(Encoding.Latin1.GetBytes("Ryan Giggs"));
        Assert.Equal(939, s);
        Assert.Equal(new Atributos(10, 4, 4, true), Atributos.DoNome(s, Posicao.Avancado));
        Assert.Equal(939, Atributos.Soma(Encoding.Latin1.GetBytes("ryan Giggs")));
    }

    [Fact]
    public void TodasAsEquipes()
    {
        var raiz = Arquivos.Raiz();
        Assert.NotNull(raiz);
        var arqs = Directory.GetFiles(Path.Combine(raiz!, "EQUIPAS"), "*.EFT");
        Assert.True(arqs.Length >= 200, $"só {arqs.Length} equipes");
        foreach (var a in arqs)
        {
            var b = File.ReadAllBytes(a);
            var e = Eft.Decodificar(b);
            Assert.True(e.Jogadores.Count >= 14 && e.Nivel >= 1 && e.Nivel <= 20, a);
            Assert.True(e.Codificar().AsSpan().SequenceEqual(b), $"{a}: regravado diferente");
        }
        saida.WriteLine($"{arqs.Length} equipes lidas e regravadas iguais");
        var paises = Eft.LerTxe(Path.Combine(raiz!, "COUNTRY.TXE"));
        Assert.True(paises.Count >= 20);
    }

    [Fact]
    public void SavesLidosEGravadosIguais()
    {
        int n = 0;
        foreach (var a in Arquivos.Saves())
        {
            var b = File.ReadAllBytes(a);
            var s = Jogo98.Ler(b);
            Assert.True(s.Gravar().AsSpan().SequenceEqual(b), $"{a}: regravado diferente");
            n++;
            saida.WriteLine($"{Path.GetFileName(a)}: ano {s.Cabecalho.Ano}, {s.Treinadores.Count} treinadores, {s.Equipas.Count} equipas ({s.Equipas.Sum(e => e.Jogadores.Count)} jogadores), {s.Divisoes.Count} divisoes, {s.Jogos.Count} jogos");
        }
        Assert.True(n > 0, "nenhum save encontrado");
    }
}
