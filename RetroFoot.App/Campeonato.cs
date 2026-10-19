using System;
using RetroFoot.Motor.Save;
using RetroFoot.Motor.Temporada;

namespace RetroFoot.App;

// Os pontos de entrada das telas de Campeonato, Tecnico, fim de temporada, convite, opcoes e
// Sobre, num lugar so (cada um chama a tela de verdade em TelaListas, TelaTreinadores,
// TelaFimDeTemporada, TelaOpcoes e TelaAcerca).
//
// Ligar (no construtor da Sessao, depois de criar o J):
//     Campeonato.Ligar(this);                       // Humano.Convite -> TinviteCoachDlg
// e o fim de temporada, que o Ciclo recebe como parametro (em Sessao.Comecar):
//     J.Ciclo(Campeonato.AoFim(this));              // TEndOfSeasonDlg
//
// Os itens dos menus da tela da equipa (thread da interface, motor parado; e = a equipa da
// tela; refazer = o que a tela faz para se redesenhar):
//     RetroFoot > Opções...           Campeonato.Opcoes(s, refazer)
//     RetroFoot > Sobre...            Campeonato.Sobre(s)
//     Jogador > Últimas transf...   Campeonato.UltimasTransferencias(s)
//     Campeonato > Artilheiros...   Campeonato.Artilheiros(s)
//     Campeonato > Calendário...    Campeonato.Calendario(s, e, refazer)
//     Campeonato > Últimos venc...  Campeonato.UltimosVencedores(s)
//     Campeonato > Maiores art...   Campeonato.ArtilheirosDeSempre(s)
//     Técnico > História...         Campeonato.Historia(s, e)
//     Técnico > Gestão...           Campeonato.Gestao(s, refazer)
//     Técnico > Ranking...          Campeonato.Ranking(s)
//     Técnico > Perfil...           Campeonato.Perfil(s, e, refazer)
//     (botão Calendário da página Adversário: Campeonato.Calendario(s, adversario, refazer))
public static class Campeonato
{
    public static void Ligar(Sessao s) => TelaTreinadores.Ligar(s);

    public static Action<Resultado> AoFim(Sessao s) => TelaFimDeTemporada.AoFim(s);

    public static void Calendario(Sessao s, Equipa e, Action? refazer = null) => TelaListas.Calendario(s, e, refazer);
    public static void Artilheiros(Sessao s) => TelaListas.Marcadores(s, false);
    public static void ArtilheirosDeSempre(Sessao s) => TelaListas.Marcadores(s, true);
    public static void UltimosVencedores(Sessao s) => TelaListas.UltimosVencedores(s);
    public static void UltimasTransferencias(Sessao s) => TelaListas.UltimasTransferencias(s);

    public static void Historia(Sessao s, Equipa e) => TelaListas.HistoriaTreinador(s, s.TreinadorDa(e));
    public static void Ranking(Sessao s) => TelaListas.RankingTreinadores(s);
    public static void Gestao(Sessao s, Action? refazer = null) => TelaTreinadores.Gestao(s, refazer);
    public static void Perfil(Sessao s, Equipa e, Action? refazer = null) => TelaTreinadores.Perfil(s, s.TreinadorDa(e), refazer);

    // ok recebe as opcoes escolhidas (ja aplicadas a Sessao); o OK grava o arquivo das opcoes
    // (OpcoesDoJogo.Arquivo, o elif98.ini do original) e a velocidade vale da proxima jornada
    public static void Opcoes(Sessao s, Action? refazer = null, Action<OpcoesDoJogo>? ok = null) =>
        TelaOpcoes.Abrir(s, o =>
        {
            try { o.Gravar(); } catch (Exception ex) { Console.Error.WriteLine("opcoes: " + ex.Message); }
            ok?.Invoke(o);
            refazer?.Invoke();
        }, OpcoesDoJogo.Ler()?.Som ?? true);

    public static void Sobre(Sessao s) => TelaAcerca.Abrir(s);
}
