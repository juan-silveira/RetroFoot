using System;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Xunit;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes;

// O preco do ingresso escolhido na tela da equipa vale ja para o jogo da jornada em casa
// (Mundo.RecalcularPublico): o publico e refeito com a mesma procura sorteada ao criar o jogo,
// e a torcida pesa no arbitro (Mundo.BonusDaTorcida, versao Atual). Antes o publico ficava o do
// preco antigo e o ingresso no maximo dava uma renda absurda.
public class IngressosPublicoTestes
{
    static (JogoEmCurso J, Jogo Jogo, Equipa Casa) Cenario(bool torcida = true)
    {
        string? arq = null;
        for (var d = AppContext.BaseDirectory; d != null && arq == null; d = Path.GetDirectoryName(d))
        {
            var f = Path.Combine(d, "Oraculo", "j3-entrada.e98");
            if (File.Exists(f)) arq = f;
        }
        Assert.NotNull(arq);
        var s = Jogo98.Ler(File.ReadAllBytes(arq!));
        var j = new JogoEmCurso(s, 1, Opcoes.Padrao with { Regras = TRegras.Launcher(8), TorcidaNoArbitro = torcida }, new Humano());
        // o estadio grande (o publico nao bate no limite) e o preco de tabela
        foreach (var e in s.Equipas) { e.Estadio = 24; Ingressos.Escolher(e, 100); }
        j.M.LimparJogos();
        j.M.CriarJogosDivisao(0);
        var jogo = s.Jogos.First(x => x.Casa != 0 && x.Fora != 0 && x.Arbitro != 0);
        return (j, jogo, s.Equipas.First(e => e.ID == jogo.Casa));
    }

    [Fact]
    public void PrecoMudaOPublicoDoJogoDaJornada()
    {
        var (j, jogo, casa) = Cenario();
        uint normal = jogo.D40;
        Assert.True(normal > 0);
        Ingressos.Escolher(casa, 300);
        j.M.RecalcularPublico(jogo);
        uint caro = jogo.D40;
        Ingressos.Escolher(casa, 50);
        j.M.RecalcularPublico(jogo);
        uint barato = jogo.D40;
        Assert.True(caro < normal, $"caro {caro} >= normal {normal}");
        Assert.True(barato > normal, $"barato {barato} <= normal {normal}");
        // de volta ao preco de tabela: o mesmo publico do comeco (o sorteio nao muda)
        Ingressos.Escolher(casa, 100);
        j.M.RecalcularPublico(jogo);
        Assert.Equal(normal, jogo.D40);
    }

    [Fact]
    public void TorcidaPesaNoArbitro()
    {
        var (j, jogo, casa) = Cenario();
        Ingressos.Escolher(casa, 300);
        j.M.RecalcularPublico(jogo);
        int vazio = jogo.W4C;
        Ingressos.Escolher(casa, 50);
        j.M.RecalcularPublico(jogo);
        int cheio = jogo.W4C;
        Assert.Equal(3, cheio - vazio);   // +3 com mais gente que o preco de tabela, +0 com 1/4 dele
        // sem a opcao (o jogo classico, o oraculo): o +2 fixo, o preco nao mexe no arbitro
        var (j2, jogo2, casa2) = Cenario(torcida: false);
        int antes = jogo2.W4C;
        Ingressos.Escolher(casa2, 300);
        j2.M.RecalcularPublico(jogo2);
        Assert.Equal(antes, jogo2.W4C);
    }

    // sem mexer no preco: o +2 do classico em todos os jogos
    [Fact]
    public void PrecoDeTabelaDaOMesmoArbitroDoClassico()
    {
        var (j, _, _) = Cenario();
        var (j2, _, _) = Cenario(torcida: false);
        var com = j.M.S.Jogos.Where(x => x.Arbitro != 0).Select(x => x.W4C).ToArray();
        var sem = j2.M.S.Jogos.Where(x => x.Arbitro != 0).Select(x => x.W4C).ToArray();
        Assert.NotEmpty(com);
        Assert.Equal(sem, com);
    }

    // o publico com o preco escolhido sobre o do preco de tabela: ate 1/4 +0, ate 2/4 +1,
    // ate igual +2, mais +3
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2500, 0)]
    [InlineData(2501, 1)]
    [InlineData(5000, 1)]
    [InlineData(5001, 2)]
    [InlineData(10000, 2)]
    [InlineData(10001, 3)]
    public void BonusPeloPublicoDoPrecoDeTabela(int publico, int bonus) =>
        Assert.Equal(bonus, Mundo.BonusPeloPublico(publico, 10000));
}
