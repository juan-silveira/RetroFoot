namespace RetroFoot.Motor.Partida;

// Os acrescimos (Regras.Acrescimos), uma adicao fora do original: com a opcao desligada nada
// daqui roda e o motor e o 98.003 byte a byte.
//
// No ultimo minuto de cada tempo (45, 90 e, com prorrogacao, 120), depois dos lances desse
// minuto em todos os jogos, cada jogo da lista (na ordem, so os com as duas equipas) sorteia
// os seus acrescimos com o Random da jornada: Random(5) + 1 no 1o tempo, Random(10) + 1 no 2o
// e Random(3) + 1 no fim da prorrogacao. O relogio fica no minuto (Minuto = 45, 90 ou 120) e
// Acrescimo anda de 1 ate o maior deles; em cada tique, so os jogos com acrescimos ate ali
// jogam o minuto, com o mesmo laco dos outros (golo/penalti, expulsao ou cartao, lesao,
// golo anulado, evolucao). Os lances ficam com Minuto = 45/90/120 e Acrescimo = n.
// Depois do ultimo tique vem o que viria no fim do tempo: o intervalo, ou a prorrogacao da
// Taca (os empatados depois dos acrescimos do 2o tempo). Nos acrescimos:
//  - o golo usa o minuto corrido (45 + n, 90 + n): "gol so vale se minuto > Random(45)"
//    vale sempre, como nos minutos finais, e o autor sorteado sem golo e so no 45 de verdade;
//  - o "minuto anterior" (penalti por expulsao, golo anulado) e o acrescimo anterior, e o
//    penalti por expulsao nao tem a excecao do 1o minuto do tempo (minuto mod 45 <= 1);
//  - o fecho da janela continua em Fim + 5 minutos depois dos acrescimos do fim.
public sealed partial class Jornada
{
    static int MaximoDoTempo(int m) => m == 45 ? 5 : m == 90 ? 10 : 3;
    static int IndiceDoTempo(int m) => m == 45 ? 0 : m == 90 ? 1 : 2;

    bool MinutoComAcrescimos()
    {
        if (emAcrescimos)
        {
            int m = Minuto, a = ++Acrescimo, k = IndiceDoTempo(m);
            int n = Jogos.Count;
            for (int i = 0; i < n; i++)
                if (a <= Jogos[i].Acrescimos[k]) JogarMinuto(Jogos[i], m);
            if (a >= AcrescimoMax) { emAcrescimos = false; FimDoTempo(m); }
            return true;
        }
        Acrescimo = 0;
        int mm = ++Minuto;
        if (mm > 0 && mm <= Fim)
        {
            int n = Jogos.Count;
            for (int i = 0; i < n; i++) JogarMinuto(Jogos[i], mm);
            if (mm == 45 || mm == 90 || mm == 120) SortearAcrescimos(mm);
            if (emAcrescimos) return true;
            FimDoTempo(mm);
        }
        if (Fim + 5 < mm) fechar = true;
        return !fechar;
    }

    // os acrescimos de cada jogo no fim do tempo m
    void SortearAcrescimos(int m)
    {
        int k = IndiceDoTempo(m), max = MaximoDoTempo(m);
        AcrescimoMax = 0;
        foreach (var jg in Jogos)
        {
            jg.Acrescimos[k] = jg.TemEquipas ? R.Random(max) + 1 : 0;
            if (jg.Acrescimos[k] > AcrescimoMax) AcrescimoMax = jg.Acrescimos[k];
        }
        emAcrescimos = AcrescimoMax > 0;
    }

    // o acrescimo que o jogo tem no tempo em que o relogio esta (0 fora dos acrescimos)
    public int AcrescimosDoJogo(JogoAtual jg) =>
        Minuto is 45 or 90 or 120 && Acrescimo > 0 ? jg.Acrescimos[IndiceDoTempo(Minuto)] : 0;
}
