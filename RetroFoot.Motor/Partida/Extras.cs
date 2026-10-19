using System;
using System.Collections.Generic;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Partida.Memoria;

namespace RetroFoot.Motor.Partida;

// Os lances que o original nao guarda no jogo (so para a linha do tempo da interface):
// substituicoes (automaticas do computador e as do humano nas janelas) e penaltis sem gol.
// Ficam so na memoria (JogoAtual.Extras), fora do save e da lista de lances, e sao anotados
// sem sortear nada nem mexer no jogo: o motor continua igual ao original byte a byte.
public static class TipoExtra
{
    public const int Substituicao = 1, PenaltiFalhado = 2;
}

public sealed class LanceExtra
{
    public int Lado, Minuto, Tipo;
    // o minuto dos acrescimos (Lance.Acrescimo)
    public int Acrescimo;
    // quantos lances do jogo ja havia (JogoAtual.Lances.Count): a posicao na ordem do jogo
    public int Ordem;
    // feita na janela do intervalo (o minuto e o do intervalo: 45 ou 90)
    public bool Intervalo;
    // substituicao: quem saiu e quem entrou
    public Jogador? Saiu, Entrou;
    // penalti falhado: batedor, goleiro e o resultado (ResultadoPenalti: Defesa, Poste,
    // Barra, AoLado ou PorCima)
    public Jogador? Batedor, Goleiro;
    public int Resultado;
}

public sealed partial class Jornada
{
    // false: nada e anotado (os testes comparam com e sem)
    public bool RegistarExtras = true;

    // a ultima cobranca de Penalti (o goleiro e o resultado), para o lance extra
    Jogador? ultimoGoleiro;
    int ultimoResultado;

    void Extra(JogoAtual jg, LanceExtra x)
    {
        if (!RegistarExtras) return;
        x.Ordem = jg.Lances.Count;
        if (!x.Intervalo) x.Acrescimo = Acrescimo;
        jg.Extras.Add(x);
        Ganchos.Extra?.Invoke(jg, x);
    }

    void ExtraSubstituicao(JogoAtual jg, int lado, int minuto, Jogador saiu, Jogador entrou, bool intervalo = false) =>
        Extra(jg, new LanceExtra { Lado = lado, Minuto = minuto, Tipo = TipoExtra.Substituicao, Saiu = saiu, Entrou = entrou, Intervalo = intervalo });

    // depois de um Penalti sem gol (resultado 0 = sem batedor ou sem goleiro: nada)
    void ExtraPenalti(JogoAtual jg, int lado, int minuto, Jogador? batedor)
    {
        if (batedor == null || ultimoGoleiro == null || ultimoResultado <= ResultadoPenalti.Gol) return;
        Extra(jg, new LanceExtra
        {
            Lado = lado, Minuto = minuto, Tipo = TipoExtra.PenaltiFalhado, Batedor = batedor, Goleiro = ultimoGoleiro, Resultado = ultimoResultado,
        });
    }

    // Uma janela de substituicao do humano (a do motor ou a de detalhes da interface): compara
    // os titulares antes e depois e anota as trocas feitas nela. saiu: o lesionado que ja
    // ficou fora antes da janela (motivo 2), que conta como titular de antes.
    public void ObservarTrocas(JogoAtual jg, int lado, Jogador? saiu, bool intervalo, Action janela)
    {
        var e = jg.Equipa[lado];
        if (e == null || !RegistarExtras) { janela(); return; }
        var antes = TitularesIDs(e);
        if (saiu != null && !antes.Contains(saiu.ID)) antes.Add(saiu.ID);
        int minuto = Minuto;
        janela();
        var depois = TitularesIDs(e);
        var sairam = antes.FindAll(id => !depois.Contains(id));
        var entraram = depois.FindAll(id => !antes.Contains(id));
        foreach (var idEntra in entraram)
        {
            var entra = JogadorPorID(e, idEntra);
            if (entra == null || sairam.Count == 0) continue;
            // de preferencia goleiro por goleiro, linha por linha
            int k = sairam.FindIndex(id => JogadorPorID(e, id) is { } p && (p.Posicao == 0) == (entra.Posicao == 0));
            if (k < 0) k = 0;
            var sai = JogadorPorID(e, sairam[k]);
            sairam.RemoveAt(k);
            if (sai != null) ExtraSubstituicao(jg, lado, minuto, sai, entra, intervalo);
        }
    }

    static List<ushort> TitularesIDs(Equipa e)
    {
        var l = new List<ushort>();
        foreach (var p in e.Jogadores) if (Escalacao(p) == EscTitular) l.Add(p.ID);
        return l;
    }
}
