using System;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Online;

// O modo "jogar com um amigo" (docs/multiplayer-online.md): um save single player vira um jogo
// online por um tempo. O amigo que entra assume um time da 4a divisao (o tecnico do computador
// e despedido); quando ele sai, o tecnico dele e despedido e sai do jogo (RemoverTreinador: um
// tecnico desempregado do computador assume o time), e o save volta a ser single player.
public static class Convidado
{
    // um time da 4a divisao com tecnico do computador, sorteado; null se nao ha
    public static Equipa? TimeLivre(JogoEmCurso j, Aleatorio r)
    {
        var s = j.M.S;
        if (s.Divisoes.Count < 4) return null;
        var livres = s.Divisoes[3].Equipas.Select(id => j.M.Equipa(id)).OfType<Equipa>()
            .Where(e => j.M.TreinadorDa(e) is not { Humano: not 0 }).ToList();
        return livres.Count == 0 ? null : livres[r.Random(livres.Count)];
    }

    // o amigo entra com o nome dele: o tecnico novo e o time
    public static (Treinador T, Equipa E)? Entrar(JogoEmCurso j, string nome, Aleatorio r)
    {
        if (TimeLivre(j, r) is not { } e) return null;
        var t = j.Mc.ContratarConvidado(e, Cifra.PorTextoEFT(System.Text.Encoding.Latin1.GetBytes(new string(nome.Where(c => c < 256).Take(20).ToArray()))));
        j.M.Reindexar();
        return (t, e);
    }

    // o amigo sai: despedido e fora do jogo; um tecnico do computador assume o time dele
    public static void Sair(JogoEmCurso j, ushort treinador)
    {
        j.Mc.RemoverTreinador(treinador);
        j.M.Reindexar();
    }
}
