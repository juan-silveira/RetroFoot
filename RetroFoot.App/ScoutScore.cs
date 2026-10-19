using System;
using System.Collections.Generic;
using System.Globalization;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// Score e Impacto do Scout: copiados do launcher do jogo classico (launcher/src/Score.cs,
// metodo em docs/score.md de la). Os coeficientes sairam de simulacoes de temporadas com as
// regras do jogo (docs internos do launcher), com a forca igual para todos.
public static class ScoutScore
{
    // jogos perdidos por temporada (14 jogos) por ponto de lesao e de comportamento, por posicao G/D/M/A
    static readonly double[] ForaPorLesao = { 0.105, 0.13, 0.13, 0.13 };
    static readonly double[] ForaPorComportamento = { 0.17, 0.21, 0.21, 0.21 };
    const int JogosTemporada = 14;

    public static double JogosFora(int pos, int lesao, int comportamento) =>
        pos is < 0 or > 3 ? 0 : ForaPorLesao[pos] * Math.Max(0, lesao) + ForaPorComportamento[pos] * Math.Max(0, comportamento);

    // Score (0 a 1000), uma escala so para todas as posicoes: Nota x 100, descontando a parte da
    // temporada que ele deve perder por lesao e suspensao. Nao depende da forca.
    public static int Rendimento(int pos, int nota, int lesao, int comportamento)
    {
        if (pos is < 0 or > 3) return 0;
        double disp = Math.Max(0, 1 - JogosFora(pos, lesao, comportamento) / JogosTemporada);
        return (int)Math.Round(100 * Math.Max(0, Math.Min(10, nota)) * disp);
    }

    // Impacto no time: pontos por temporada a mais (ou a menos) que um jogador de referencia da
    // mesma posicao (nota sem estrela, lesao 0, Fair Play). Inclui o efeito "para-raios": o numero
    // de lesoes e expulsoes do time por jogo e fixo, e lesao/comportamento so decidem quem sofre.
    public static double Impacto(int pos, int nota, int lesao, int comportamento)
    {
        bool estrela = pos >= 2 && nota >= 8;
        return pos switch
        {
            0 => -0.016 * lesao - 0.065 * comportamento,
            1 => 0.02 * lesao,
            2 => (estrela ? 0.26 : 0) + 0.019 * comportamento,
            3 => (estrela ? 0.27 : 0) - 0.013 * lesao + 0.021 * comportamento,
            _ => 0,
        };
    }

    public static string TextoImpacto(double v)
    {
        var n = Math.Abs(v).ToString("0.00", CultureInfo.InvariantCulture);
        if (Atual != EN) n = n.Replace('.', ',');
        return (v > 0.004 ? "+" : v < -0.004 ? "−" : "") + n;
    }

    // o "?" do Scout: o que cada coluna quer dizer (titulo, texto), traduzidos ao mostrar
    public static IEnumerable<(string Titulo, string Texto)> Ajuda()
    {
        yield return (T("Score (0 a 1000)"), T("Nota × 100, descontando os jogos que ele deve perder na época por lesão e por suspensão (cerca de 0,13 jogo por ponto de Lesão e 0,21 por ponto de Comportamento, em 14 jogos). Não depende da força."));
        yield return (T("Impacto"), T("Pontos por época (14 jogos) que a equipa ganha ou perde com ele, comparado a um jogador da mesma posição sem estrela, Lesão 0 e Fair Play (medido em simulações). O jogo sorteia por minuto se a equipa terá uma lesão ou expulsão; Lesão e Comportamento só escolhem quem sofre. Por isso um avançado violento ou um defesa frágil podem até ajudar: atraem os problemas que iriam para o guarda-redes ou para as estrelas."));
        yield return (T("✱ Estrela"), T("Nota 8 ou mais num médio ou avançado. Soma 2 à força do sector dele enquanto está em campo."));
        yield return (T("Nota (1 a 10)"), T("Decide quem marca os golos da equipa (avançado pesa 9, médio 4, defesa 1; o guarda-redes nunca marca) e quem bate os penáltis. Não muda quantos golos a equipa faz: isso vem da força dos sectores."));
        yield return (T("Lesão (0 a 10)"), T("Chance de ser ele o lesionado quando a equipa sofre uma lesão. 0 nunca se lesiona. Cada lesão: 1 a 20 jogos fora e 10 de força a menos."));
        yield return (T("Comportamento"), T("Chance de ser ele o expulso. Fair Play nunca é expulso. Suspensão de 1 a 4 jogos; guarda-redes expulso dá penálti certo, defesa dá em 1 de 3."));
        yield return (T("Valor"), T("O valor do jogador para o jogo (força, nota, lesão, comportamento, moeda e inflação). O preço base dos leilões e os pedidos das equipas partem dele."));
        yield return (T("Jogos, Golos, Lesões, Amar., Expuls."), T("O historial do jogador no save (golos desta época; Amar. = cartões amarelos na carreira, só com as regras do RetroFoot)."));
        yield return (T("Sit."), T("S = suspenso por N jogos, A = N amarelos acumulados (o 3º suspende por 1 jogo), L = lesionado por N jogos; Estrang. = conta como estrangeiro (Bosman e PLOP não contam)."));
        yield return (T("Idade"), T("O jogo gravado não guarda a idade dos jogadores, por isso o Scout não tem filtro de idade."));
    }
}
