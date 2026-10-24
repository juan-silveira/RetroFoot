using System;
using System.Collections.Generic;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Partida.Memoria;

namespace RetroFoot.Motor.Partida;

// a TjourneyDlg (seg10) sem o desenho: o relogio e a lista de jogos da jornada (DS:270A)
public sealed partial class Jornada
{
    public required Aleatorio R;
    public List<JogoAtual> Jogos = new();   // na ordem da lista do original

    // +0x190 da janela: 1 nas jornadas da Taca (seg05:1119), 0 no campeonato (seg05:0cb3).
    // Com Taca, no minuto 90 os empatados vao a prorrogacao ate 120.
    public bool Taca;
    // a opcao DS:2B32: no intervalo, a janela de substituicao dos times humanos
    public bool SubstituirNoIntervalo;

    // os cartoes amarelos do launcher (Regras.CartoesAmarelos, Cartoes.cs). Numero (DS:2B40)
    // e Ano (DS:2B42) entram na marca do jogo de cada amarelo.
    public bool Amarelos;
    public short Numero;
    public int Ano;

    public Ganchos Ganchos = new();

    // seg03:9396 (fim do jogo: classificacao, bilheteria, treinadores, moral, eliminacao na
    // Taca com DesempateTaca, suspensoes, arbitro livre). Fica fora daqui; e chamado na mesma
    // hora que no original: no minuto 90 da Taca para os jogos decididos e, no fim, para os
    // restantes, na ordem da lista.
    public Action<JogoAtual>? Finalizar;

    public int Minuto;   // +0x18E
    public int Fim;      // +0x18C: 90, ou 120 com prorrogacao
    bool fechar;

    // Os acrescimos (Regras.Acrescimos; fora do original, por isso desligados por padrao):
    // no fim de cada tempo (45, 90 e 120), cada jogo sorteia os seus (Acrescimos.cs) e o
    // relogio para no minuto enquanto Acrescimo anda de 1 ao maior deles.
    public bool Acrescimos;
    public int Acrescimo;      // o n de "45'+n'"; 0 fora dos acrescimos
    public int AcrescimoMax;   // o maior acrescimo deste tempo entre os jogos
    bool emAcrescimos;
    // (o jogo online, Online/AoVivo: a jornada mostrada nos outros aparelhos copia o do host)
    public bool EmAcrescimos { get => emAcrescimos; set => emAcrescimos = value; }

    // o relogio parado no intervalo: o 45 ja jogado (e os acrescimos dele)
    public bool NoIntervalo => Minuto == 45 && !emAcrescimos;

    // a parte do motor de seg10:03ea, antes de abrir a janela: todos os titulares (a lista
    // global DS:26FE) somam um jogo (+0x17). O original percorre todos os jogadores, inclusive
    // os de equipas que nao jogam nesta jornada (a escalacao deles e a da ultima vez).
    public static void ContarJogos(IEnumerable<Equipa> equipas)
    {
        foreach (var e in equipas)
            foreach (var j in e.Jogadores)
                if (Escalacao(j) == EscTitular) SomaHistorial(j, HistJogos, 1);
    }

    // seg10:0200 (Run): relogio em -5, fim em 90
    public void Iniciar() { Minuto = -5; Fim = 90; fechar = false; Acrescimo = 0; AcrescimoMax = 0; emAcrescimos = false; }

    // a jornada inteira: Iniciar, MinutoPassado ate a janela fechar e, depois, Finalizar para
    // cada jogo que restou (seg05:01f6), na ordem. Antes, como o original (seg10:03ea), quem
    // chama deve fazer ContarJogos com todas as equipas.
    public void Jogar()
    {
        Iniciar();
        while (MinutoPassado()) { }
        while (Jogos.Count > 0)
        {
            Finalizar?.Invoke(Jogos[0]);
            Jogos.RemoveAt(0);
        }
    }

    // seg10:069b (TjourneyDlg.MinutePassed), um tique do relogio; false quando a janela fecha.
    // De 1 ate Fim, para cada jogo, na ordem: gol/penalti (a6eb), expulsao (aa11) e lesao
    // (ac72), parando no 1o que acontecer; se nada aconteceu e o minuto nao e 46 nem 91, gol
    // anulado (aea7); e sempre a evolucao (afff). No 45, intervalo. No 90 das jornadas da
    // Taca, os empatados vao a prorrogacao (+0x50 = 1) e os outros sao finalizados
    // (seg03:143e); se sobrar algum, novo intervalo e Fim = 120. Fecha depois de Fim + 5.
    public bool MinutoPassado()
    {
        if (Acrescimos) return MinutoComAcrescimos();
        int m = ++Minuto;
        if (m > 0 && m <= Fim)
        {
            int n = Jogos.Count;
            for (int i = 0; i < n; i++) JogarMinuto(Jogos[i], m);
            FimDoTempo(m);
        }
        if (Fim + 5 < m) fechar = true;
        return !fechar;
    }

    // o minuto de um jogo (o laco de seg10:069b)
    void JogarMinuto(JogoAtual jg, int m)
    {
        if (!MinutoGol(jg, m) && !MinutoExpulsao(jg, m) && !MinutoLesao(jg, m) && m != 46 && m != 91 && m > 0)
            GolAnulado(jg, m);
        MinutoEvolucao(jg);
    }

    // o que vem depois do ultimo minuto de cada tempo: intervalo no 45; no 90 da Taca, a
    // prorrogacao dos empatados
    void FimDoTempo(int m)
    {
        if (m == 45) Intervalo();
        if (m == 90 && Taca)
        {
            Prorrogacao();
            if (Jogos.Count > 0) { Intervalo(); Fim = 120; }
        }
    }

    // seg03:143e: empatados ficam (+0x50 = 1), os outros sao finalizados e saem da lista
    public bool SubstituicaoExtra;

    void Prorrogacao()
    {
        for (int i = 0; i < Jogos.Count;)
        {
            var jg = Jogos[i];
            if (jg.Empatado)
            {
                jg.Dados.B50 = 1;
                // (versao Atual) mais uma substituicao para cada equipa na prorrogacao
                if (SubstituicaoExtra)
                    foreach (var eq in jg.Equipa)
                        if (eq != null) Memoria.MudarSubstituicoes(eq, (short)(Memoria.Substituicoes(eq) + 1));
                i++; continue;
            }
            Finalizar?.Invoke(jg);
            Jogos.RemoveAt(i);
        }
    }

    // seg10:0cef (HalfTime): com a opcao DS:2B32, a janela de substituicao (motivo 4) para
    // cada time humano, casa e depois visitante. Nao ha sorteio, mas a janela reordena o elenco.
    public void Intervalo()
    {
        if (!SubstituirNoIntervalo) return;
        foreach (var jg in Jogos)
        {
            if (!jg.TemEquipas) continue;   // seg03:a5ad: so com as duas equipas
            if (jg.HumanoNoLado(Lado.Casa)) JanelaSubstituicao(jg, Lado.Casa, null, 4);
            if (jg.HumanoNoLado(Lado.Visit)) JanelaSubstituicao(jg, Lado.Visit, null, 4);
        }
    }

    // seg03:a5ad para um time humano: com os motivos 1, 3 (goleiro expulso) e 4 (intervalo),
    // TsubstitutionDlg (seg02:21cb), que ao abrir ORDENA o elenco por posicao e nome (14:0e0d
    // com 11:1826), mesmo que ninguem troque; com o motivo 2 (lesao), TforcedSubstitutionDlg
    // (seg06:3969), que nao reordena. Depois, o gancho Substituicao. Os lances e quem saiu sao
    // reencontrados pelo identificador depois da reordenacao.
    void JanelaSubstituicao(JogoAtual jg, int lado, Jogador? saiu, int motivo)
    {
        if (motivo != 2)
        {
            var e = jg.Equipa[lado]!;
            ushort id = saiu?.ID ?? 0;
            OrdenarPorPosicaoNome(e);
            foreach (var l in jg.Lances)
                if (l.Lado == lado && JogadorPorID(e, l.JogadorID) is { } p) l.Jogador = p;
            if (saiu != null) saiu = JogadorPorID(e, id);
        }
        if (Ganchos.Substituicao is { } g)
            ObservarTrocas(jg, lado, motivo == 2 ? saiu : null, motivo == 4, () => g(jg, lado, saiu, motivo));
    }

    internal static Jogador? JogadorPorID(Equipa e, ushort id) => e.Jogadores.Find(j => j.ID == id);

    // a bolha seg14:0e0d com o comparador seg11:1826: G, D, M, A e, em cada posicao, pelo
    // nome mostrado (seg12:13fe, com "*" da estrela), comparado como string do Pascal; nomes
    // iguais na mesma posicao trocam a cada passada
    public static void OrdenarPorPosicaoNome(Equipa e)
    {
        var l = e.Jogadores;
        int n = l.Count;
        for (int i = 1; i <= n - 1; i++)
            for (int j = n - 1; j >= i; j--)
                if (!PorPosicaoNome(l[j - 1], l[j])) (l[j - 1], l[j]) = (l[j], l[j - 1]);
    }

    static bool PorPosicaoNome(Jogador a, Jogador b)
    {
        if (a.Posicao != b.Posicao) return a.Posicao < b.Posicao;
        return NomeNaTela(a).AsSpan().SequenceCompareTo(NomeNaTela(b)) < 0;
    }

    // seg12:13fe
    public static byte[] NomeNaTela(Jogador j)
    {
        var cif = j.NomeCifrado;
        if (cif.Length > 0 && cif[0] > 20)
        {
            var c = new byte[21];
            c[0] = 20;
            Array.Copy(cif, 1, c, 1, 20);
            cif = c;
        }
        var nome = cif.Length == 0 ? Array.Empty<byte>() : Cifra.TextoEFT(cif).Texto;
        if (j.Estrela != 0)
        {
            var s = new byte[Math.Min(nome.Length + 1, 20)];
            Array.Copy(nome, s, Math.Min(nome.Length, s.Length));
            if (nome.Length < 20) s[nome.Length] = (byte)'*';
            nome = s;
        }
        return nome;
    }
}
