using System;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Arquivos;
using Xunit;

namespace RetroFoot.Motor.Testes;

// A ferramenta Arbitros (referees.tsv da pasta de dados): ler e gravar com os desativados
// (comentados) nao muda a lista que o jogo novo usa.
public class ArbitrosEditaveisTestes
{
    static string Pacote()
    {
        for (var d = AppContext.BaseDirectory; d != null; d = Path.GetDirectoryName(d))
        {
            var p = Path.Combine(d, "RetroFoot.App", "Assets", "Dados", "referees.tsv");
            if (File.Exists(p)) return p;
        }
        throw new InvalidOperationException("Assets/Dados/referees.tsv nao encontrado");
    }

    [Fact]
    public void PacoteIdaEVoltaIgual()
    {
        var orig = Pacote();
        var l = TextosDados.LerArbitrosEditaveis(orig);
        var ativos = TextosDados.LerArbitros(orig);
        Assert.Equal(ativos.Count, l.Count(a => a.Ativo));
        Assert.True(l.Count(a => !a.Ativo) > 100);   // os do original que a FIFA substituiu
        Assert.All(l.Where(a => !a.Ativo), a => Assert.Equal("1998", a.Origem));
        var tmp = Path.Combine(Path.GetTempPath(), "retrofoot-arb-" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            TextosDados.GravarArbitrosEditaveis(tmp, l);
            Assert.Equal(ativos, TextosDados.LerArbitros(tmp));
            Assert.Equal(l, TextosDados.LerArbitrosEditaveis(tmp));
        }
        finally { File.Delete(tmp); if (File.Exists(tmp + ".bak")) File.Delete(tmp + ".bak"); }
    }

    [Fact]
    public void AcrescentarDesativarERemover()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "retrofoot-arb-" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            var l = new[]
            {
                new TextosDados.ArbitroEditavel("BRA", "Wilton Sampaio", "fifa", true),
                new TextosDados.ArbitroEditavel("POR", "Fulano de Tal", "user", true),
                new TextosDados.ArbitroEditavel("ESC", "Hugh Dallas", "1998", false),
            };
            TextosDados.GravarArbitrosEditaveis(tmp, l);
            Assert.Equal(new[] { "BRA Wilton Sampaio", "POR Fulano de Tal" },
                TextosDados.LerArbitros(tmp).Select(a => a.Pais + " " + a.Nome));
            Assert.Equal(l, TextosDados.LerArbitrosEditaveis(tmp));
            // o desativado volta a valer: entra na lista do jogo
            TextosDados.GravarArbitrosEditaveis(tmp, new[] { l[0], l[2] with { Ativo = true } });
            Assert.Equal(new[] { "BRA Wilton Sampaio", "ESC Hugh Dallas" },
                TextosDados.LerArbitros(tmp).Select(a => a.Pais + " " + a.Nome));
        }
        finally { File.Delete(tmp); if (File.Exists(tmp + ".bak")) File.Delete(tmp + ".bak"); }
    }

    // pacote novo: entram so os que o jogador nao tem; os dele (e os que desativou) ficam
    [Fact]
    public void AcrescentarOsNovosDoPacote()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "retrofoot-arb-" + Guid.NewGuid().ToString("N") + ".tsv");
        try
        {
            TextosDados.GravarArbitrosEditaveis(tmp, new[]
            {
                new TextosDados.ArbitroEditavel("BRA", "Wilton Sampaio", "fifa", true),
                new TextosDados.ArbitroEditavel("BRA", "Raphael Claus", "fifa", false),
                new TextosDados.ArbitroEditavel("POR", "Fulano de Tal", "user", true),
            });
            var pacote = new[]
            {
                new TextosDados.ArbitroEditavel("BRA", "Wilton Sampaio", "fifa", true),
                new TextosDados.ArbitroEditavel("BRA", "Raphael Claus", "fifa", true),
                new TextosDados.ArbitroEditavel("BRA", "Anderson Daronco", "fifa", true),
                new TextosDados.ArbitroEditavel("BRA", "Wilton Pereira Sampaio", "fifa", true),
                new TextosDados.ArbitroEditavel("BRA", "Raphael Cláus", "fifa", true),
                new TextosDados.ArbitroEditavel("POR", "Fulano Beltrano de Tal", "cbf", true),
                new TextosDados.ArbitroEditavel("ESC", "Hugh Dallas", "1998", false),
            };
            Assert.Equal(1, TextosDados.AcrescentarArbitros(tmp, pacote));
            Assert.Equal(new[] { "BRA Wilton Sampaio", "POR Fulano de Tal", "BRA Anderson Daronco" },
                TextosDados.LerArbitros(tmp).Select(a => a.Pais + " " + a.Nome));
            Assert.Equal(0, TextosDados.AcrescentarArbitros(tmp, pacote));
        }
        finally { File.Delete(tmp); if (File.Exists(tmp + ".bak")) File.Delete(tmp + ".bak"); }
    }
}
