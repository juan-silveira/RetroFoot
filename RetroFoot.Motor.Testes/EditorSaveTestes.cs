using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Save;
using Xunit;
using Xunit.Abstractions;

namespace RetroFoot.Motor.Testes;

// O Editor de Save (Motor/Save/EditorSave.cs): abrir e gravar sem mudar nada da os mesmos
// bytes; o que se muda volta lido igual; os limites de 16 bits.
public class EditorSaveTestes
{
    readonly ITestOutputHelper saida;
    public EditorSaveTestes(ITestOutputHelper s) => saida = s;

    // os saves do repositorio (Oraculo) e os reais que existirem (Arquivos.Saves)
    static List<string> Todos()
    {
        var l = new List<string>();
        var d = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && d != null; i++, d = Path.GetDirectoryName(d))
        {
            var p = Path.Combine(d, "Oraculo");
            if (Directory.Exists(p)) { l.AddRange(Directory.GetFiles(p, "*.e98")); break; }
        }
        l.AddRange(Arquivos.Saves());
        return l;
    }

    static void TocarEmTudo(SaveEditavel s)
    {
        // ler e repor os mesmos valores nao pode mudar nada
        s.Inflacao = s.Inflacao;
        s.HumanoDaVez = s.HumanoDaVez;
        foreach (var e in s.Equipas)
        {
            e.Dinheiro = e.Dinheiro; e.Moral = e.Moral; e.Estadio = e.Estadio; e.TreinadorId = e.TreinadorId;
            foreach (var j in e.Jogadores)
            {
                j.AmarelosAcumulados = j.AmarelosAcumulados; j.Estrela = j.Estrela;
                j.Forca = j.Forca; j.Salario = j.Salario; j.Nota = j.Nota; j.Lesao = j.Lesao;
                j.Comportamento = j.Comportamento; j.Suspensao = j.Suspensao; j.JogosLesionado = j.JogosLesionado;
            }
        }
    }

    [Fact]
    public void AbrirEGravarSemMudarDaOsMesmosBytes()
    {
        int n = 0;
        foreach (var a in Todos())
        {
            var b = File.ReadAllBytes(a);
            var s = SaveEditavel.Ler(b);
            Assert.True(s.Gravar().AsSpan().SequenceEqual(b), $"{a}: regravado diferente");
            TocarEmTudo(s);
            Assert.True(s.Gravar().AsSpan().SequenceEqual(b), $"{a}: regravado diferente depois de repor os valores");
            Assert.InRange(s.Ano, 1990, 3000);
            Assert.InRange(s.Inflacao, 0.3, 12);
            Assert.All(s.Equipas, e => Assert.False(string.IsNullOrWhiteSpace(e.Nome)));
            Assert.All(s.Equipas.SelectMany(e => e.Jogadores), j => Assert.False(string.IsNullOrWhiteSpace(j.Nome)));
            n++;
        }
        saida.WriteLine($"{n} saves abertos e gravados iguais");
        Assert.True(n >= 10);
    }

    static string Oraculo(string nome) => Todos().First(p => Path.GetFileName(p) == nome);

    [Fact]
    public void MudancasVoltamLidasIguais()
    {
        var b = File.ReadAllBytes(Oraculo("j3-entrada.e98"));
        var s = SaveEditavel.Ler(b);
        var e = s.Equipas.First(x => x.Jogadores.Count > 0 && x.PodeTerHumano);
        var j = e.OrdemDoJogo().First(x => x.Posicao == 3);
        s.Inflacao = 2.5;
        e.Dinheiro = 123_456_789;
        e.Moral = 1.5;
        e.Estadio = 20;
        j.Forca = 60; j.Salario = 7777; j.Nota = 9; j.Suspensao = 3; j.JogosLesionado = 5; j.AmarelosAcumulados = 2; j.Comportamento = 4; j.Lesao = 2;
        var novo = s.Gravar();
        Assert.Equal(b.Length, novo.Length);   // so campos de tamanho fixo
        var r = SaveEditavel.Ler(novo);
        var e2 = r.Equipas.Single(x => x.Id == e.Id);
        var j2 = e2.Jogadores.Single(x => x.J.ID == j.J.ID);
        Assert.Equal(2.5, r.Inflacao, 9);
        Assert.Equal(123_456_789, e2.Dinheiro);
        Assert.Equal(1.5, e2.Moral, 9);
        Assert.Equal(20, e2.Estadio);
        Assert.Equal((60, 7777L, 9, 3, 5, 2, 4, 2), (j2.Forca, j2.Salario, j2.Nota, j2.Suspensao, j2.JogosLesionado, j2.AmarelosAcumulados, j2.Comportamento, j2.Lesao));
        // as outras equipas ficam iguais
        foreach (var o in r.Equipas.Where(x => x.Id != e.Id))
        {
            var antes = s.Equipas.Single(x => x.Id == o.Id);
            Assert.Equal(antes.Dinheiro, o.Dinheiro);
        }
        // os limites cortam
        e.Estadio = 99; e.Dinheiro = long.MaxValue; j.Suspensao = 9;
        Assert.Equal((LimitesSave.EstadioMax, LimitesSave.DinheiroMax, LimitesSave.SuspensaoMax), (e.Estadio, e.Dinheiro, j.Suspensao));
        // dinheiro com sinal: uma divida volta igual (o jogo faz (int)e.Dinheiro)
        e.Dinheiro = -1_234_567;
        Assert.Equal(-1_234_567, SaveEditavel.Ler(s.Gravar()).Equipas.Single(x => x.Id == e.Id).Dinheiro);
        Assert.Equal(unchecked((uint)-1_234_567), e.E.Dinheiro);
    }

    [Fact]
    public void GravarEmFazBackupETrocaOArquivo()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "retrofoot-editor-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var arq = Path.Combine(pasta, "TESTE.e98");
            var orig = File.ReadAllBytes(Oraculo("j4-entrada.e98"));
            File.WriteAllBytes(arq, orig);
            var s = SaveEditavel.Abrir(arq);
            s.Equipas[0].Dinheiro = 42;
            var bak = s.GravarEm(arq);
            Assert.Equal(orig, File.ReadAllBytes(bak));
            Assert.Equal(42, SaveEditavel.Abrir(arq).Equipas[0].Dinheiro);
            // 2a gravacao: o .bak continua o original de antes do editor
            s.Equipas[0].Dinheiro = 43;
            s.GravarEm(arq);
            Assert.Equal(orig, File.ReadAllBytes(bak));
            Assert.False(File.Exists(arq + ".tmp"));
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void ForcaMaximaNoElencoNaoEstouraNenhumLimite()
    {
        int conferidos = 0;
        foreach (var a in Todos().Take(12))
        {
            var s = SaveEditavel.Ler(File.ReadAllBytes(a));
            foreach (var e in s.Equipas.Take(30))
            {
                var (p, f, esc) = LimitesSave.Contas(e.Jogadores);
                if (p > LimitesSave.SomaPesosMax || f > LimitesSave.SomaForcaMax || esc > LimitesSave.EscalacaoMax) continue;
                foreach (var j in e.Jogadores)
                {
                    int max = LimitesSave.ForcaMaximaNoElenco(e.Jogadores, j, j.Nota, j.Estrela);
                    Assert.True(max >= j.Forca, $"{e.Nome}/{j.Nome}: maximo {max} abaixo da forca {j.Forca}");
                    Assert.Equal(LimitesSave.Limite.Nenhum, LimitesSave.Estouro(e.Jogadores, j, max, j.Nota, j.Estrela).Qual);
                    if (max < LimitesSave.ForcaMax)
                        Assert.NotEqual(LimitesSave.Limite.Nenhum, LimitesSave.Estouro(e.Jogadores, j, max + 1, j.Nota, j.Estrela).Qual);
                    conferidos++;
                }
            }
        }
        saida.WriteLine($"{conferidos} jogadores conferidos");
        Assert.True(conferidos > 100);
    }

    [Fact]
    public void LimitesDoLauncher()
    {
        // avancado com Nota 10: 32767 / (9 x 10)
        Assert.Equal(364, LimitesSave.ForcaMaxima(3, 10));
        Assert.Equal(LimitesSave.ForcaMax, LimitesSave.ForcaMaxima(0, 10));
        Assert.True(LimitesSave.TemEstrela(2, 8));
        Assert.False(LimitesSave.TemEstrela(1, 10));
        Assert.Equal(3 * (40 + 2 + 3), LimitesSave.PesoEscalacao(0, 40, true));
    }

    [Fact]
    public void TrocarEquipaComoChicotada()
    {
        foreach (var a in Todos())
        {
            var s = SaveEditavel.Ler(File.ReadAllBytes(a));
            var tec = s.Treinadores.FirstOrDefault(t => t.Humano && s.EquipaDoTreinador(t) != null);
            if (tec == null) continue;
            var origem = s.EquipaDoTreinador(tec)!;
            var destino = s.Equipas.First(e => e != origem && e.PodeTerHumano);
            var outro = destino.TreinadorId;
            s.TrocarEquipa(tec, destino);
            var r = SaveEditavel.Ler(s.Gravar());
            Assert.Equal(tec.Id, r.Equipas.Single(e => e.Id == destino.Id).TreinadorId);
            Assert.Equal(outro, r.Equipas.Single(e => e.Id == origem.Id).TreinadorId);
            Assert.Equal(1.0, r.Equipas.Single(e => e.Id == destino.Id).Moral, 9);
            // a historia: o humano "promovido" para o destino (7) e o antigo do destino
            // "rebaixado" para a origem (6), no ano do save, como a troca feita pelo jogo
            var ev = r.Treinador(tec.Id)!.T.Eventos.Last();
            Assert.Equal(7, ev.B04);
            Assert.Equal(destino.Id, ev.W09);
            Assert.Equal((uint)s.Ano, ev.D05);
            var ev2 = r.Treinador(outro)!.T.Eventos.Last();
            Assert.Equal(6, ev2.B04);
            Assert.Equal(origem.Id, ev2.W09);
            return;
        }
        Assert.Fail("nenhum save com treinador humano");
    }
}
