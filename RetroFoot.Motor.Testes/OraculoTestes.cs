using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Save;
using Xunit;
using Xunit.Abstractions;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes;

// Os casos de Oraculo/: o jogo original (com o Randomize fixo em 0) jogou a partir de
// NOME-entrada.e98 e gravou NOME-original.e98 (ver Oraculo/LEIA.md). O motor faz o mesmo e
// o save tem de sair igual byte a byte (sem o texto do cabecalho, que tem a hora).
public class OraculoTestes(ITestOutputHelper saida)
{
    static string? Pasta()
    {
        var d = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && d != null; i++, d = Path.GetDirectoryName(d))
        {
            var p = Path.Combine(d, "Oraculo");
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    public static IEnumerable<object[]> Casos()
    {
        var p = Pasta();
        if (p == null) yield break;
        foreach (var o in Directory.GetFiles(p, "*-original.e98").OrderBy(x => x))
            yield return new object[] { Path.GetFileName(o)[..^"-original.e98".Length] };
    }

    static byte[] SemTexto(byte[] b)
    {
        var s = Jogo98.Ler(b);
        s.Cabecalho.Texto = Array.Empty<byte>();
        return s.Gravar();
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public void Oraculo(string nome)
    {
        // (o save igual ao do jogo classico: sem os jogos da temporada do RetroFoot)
        RetroFoot.Motor.Partida.Memoria.SemJogosDaTemporada = true;
        var p = Pasta()!;
        var orig = File.ReadAllBytes(Path.Combine(p, nome + "-original.e98"));
        var ent = Jogo98.Ler(File.ReadAllBytes(Path.Combine(p, nome + "-entrada.e98")));
        // NOME-exe.txt: gravado por um ORACnn.EXE (o ELIFnn.EXE do launcher): regras do launcher
        var rg = File.Exists(Path.Combine(p, nome + "-exe.txt")) ? TRegras.Launcher(0) : TRegras.Original;
        List<string>? bosman = null, plop = null;
        if (Arquivos.Raiz() is { } r) (bosman, plop) = NovoJogo.Dados.ListasDoJogo(r);
        var g = JogoEmCurso.JogarComoOraculo(ent, rg, bosman, plop);
        var a = SemTexto(orig); var b = SemTexto(g);
        if (!a.AsSpan().SequenceEqual(b))
        {
            int i = 0;
            while (i < Math.Min(a.Length, b.Length) && a[i] == b[i]) i++;
            Assert.Fail($"{nome}: {a.Length} x {b.Length} bytes, primeira diferenca no byte {i}");
        }
    }
}
