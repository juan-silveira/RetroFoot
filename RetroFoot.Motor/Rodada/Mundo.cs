using System;
using System.Collections.Generic;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Rodada;

// As partes da jornada que nao estao aqui: as telas (o que o humano decide) e rotinas de
// outros pacotes (o minuto a minuto, o mercado). Um gancho null faz o que o computador
// faria, ou nada.
public sealed class Ganchos
{
    // a TjourneyDlg (seg10:0200) para todos os jogos de S.Jogos; taca nos dias de Taca.
    // Devolve o byte DS:2888 > 0 (seg10:0483), que faz mostrar a classificacao depois.
    public Func<Mundo, byte[], bool, bool>? Partida;
    // a TprepareGameDlg (seg04:11f3) para a equipa de um humano. null: fica como esta.
    public Action<Mundo, Equipa, Jogo>? PrepararHumano;
    // seg11:0d46 (TPenaltiesDlg) num jogo da Taca empatado: true se passa o visitante.
    // null: passa o mandante.
    public Func<Mundo, Jogo, bool>? Penaltis;
    // seg03:5b37 -> seg02:3002: poe o jogador a venda (mercado; consome o Random)
    public Action<Mundo, Equipa, int, Jogador>? ColocarAVenda;
    // seg09:371a
    public Func<Mundo, Jogador, int>? ValorJogador;
    // seg03:5de1 (Extended)
    public Func<Mundo, Equipa, Jogador, Ext>? UtilidadeJogador;
    // seg03:66dc: false poe o jogador a venda (seg03:41c4)
    public Func<Mundo, Equipa, Jogador, bool>? PedidoDeAumento;
    // seg06:3de2 (TinviteCoachDlg): o humano convidado aceita? null: nao
    public Func<Mundo, Treinador, Equipa, bool>? AceitaConvite;
    // seg05:038e
    public Action<Mundo>? FimDaTemporada;
    // seg05:11FF, logo antes de cada jornada do campeonato (o "call 0C41" que o patch dos
    // patrocinios do launcher troca por um trampolim): so quando a jornada vai ser jogada
    public Action<Mundo>? AntesDaJornada;
    // a TclassificationDlg depois de uma jornada do campeonato (seg05:0d27), com os jogos ja
    // fechados e a classificacao ordenada; o bool e o "mostrar" do original (DS:2888 ou a
    // ultima jornada)
    public Action<Mundo, bool>? Classificacao;
    // a TsorteioDlg da Taca (opcao DS:2B3A), logo depois de OrdenarTacaPorNome em JogarTaca:
    // os jogos ja criados; so mostra (nao muda o Random nem os dados)
    public Action<Mundo>? Sorteio;
    // a janela "Chicotadas Psicologicas" (seg05:025a, TPrintListDlg) com a lista Chicotadas,
    // logo antes de ela ser esvaziada; o original so abre com a lista nao vazia
    public Action<Mundo>? Chicotadas;
}

// um TLash (seg03:b228): treinador que sai, equipa, tipo
public sealed record Chicotada(ushort Treinador, ushort Equipa, byte Tipo = 0, byte Flag = 0);

// O estado do jogo em memoria durante uma jornada: o save (as listas globais), o Random e
// as variaveis globais que o save nao guarda. As listas globais e onde estao aqui:
//   DS:26F6 todas as equipas -> S.Equipas      DS:26F2 vivas na Taca -> S.OutrasEquipas
//   DS:26FA divisoes -> S.Divisoes            DS:26FE jogadores -> os de todas as equipas
//   DS:2702 treinadores -> S.Treinadores      DS:2706 arbitros -> S.Arbitros
//   DS:270A jogos da jornada -> S.Jogos       DS:2716 chicotadas -> Chicotadas
// (Traduzido de motor/rodada/mundo.go.)
public sealed partial class Mundo
{
    public Jogo98 S;
    public Aleatorio R;
    public short Jornada;          // DS:2B40
    public TRegras Regras;
    public bool Sair;              // DS:2724: o jogador pediu para sair
    public Action<Equipa, string>? Traco;
    public Action<Mundo>? AntesEvolucao;
    public short Retomar;          // DS:271E: equipa cuja preparacao estava em curso
    public bool JornadaJogada;     // DS:271A
    // seg11:30bc(0x13): opcao do jogo que deixa a CPU pagar e pedir emprestimos
    public bool OpcaoGestaoCPU;
    // DS:2B34: 0 nunca mostra chicotadas, 1 so com humanos, 2 todas
    public short MostrarChicotadas;
    // DS:2B3A: 1 ou 2 mudam o sorteio da Taca
    public short TacaModo;
    // CTRGROUP/BOSMAN.TXE e PLOP.TXE (codigos de pais); null = os originais
    public List<string>? Bosman, PLOP;
    public List<Chicotada> Chicotadas = new();
    public Ganchos G = new();

    readonly Dictionary<ushort, short> forcaMedia = new();   // +0xC5 de cada equipa
    // a ordem da lista DS:26FE: a de leitura do save quando o mundo foi criado
    internal List<ushort> OrdemJogadores = new();
    Dictionary<ushort, Equipa> eqPorID = new();
    Dictionary<ushort, Treinador> trPorID = new();
    Dictionary<ushort, Arbitro> arPorID = new();

    public Mundo(Jogo98 s, uint semente)
    {
        S = s;
        R = new Aleatorio(semente);
        Jornada = (short)s.Cabecalho.G271C;
        Retomar = (short)s.Cabecalho.G271E;
        JornadaJogada = s.Cabecalho.G271A != 0;
        Regras = TRegras.DoSave(s, TRegras.Original);
        Reindexar();
        foreach (var e in s.Equipas)
            foreach (var j in e.Jogadores) OrdemJogadores.Add(j.ID);
    }

    // um mundo vazio (para testes de rotinas soltas)
    internal Mundo(Aleatorio r, Jogo98? s = null, TRegras? regras = null)
    {
        S = s ?? new Jogo98();
        R = r;
        Regras = regras ?? TRegras.Original;
        Reindexar();
    }

    // refaz os indices por id (depois de mudar as listas)
    public void Reindexar()
    {
        eqPorID = new();
        foreach (var e in S.Equipas) eqPorID[e.ID] = e;
        trPorID = new();
        foreach (var t in S.Treinadores) trPorID[t.ID] = t;
        arPorID = new();
        foreach (var a in S.Arbitros) arPorID[a.ID] = a;
    }

    // seg03:d55c(id, 1)
    public Equipa? Equipa(ushort id) => id == 0 ? null : eqPorID.GetValueOrDefault(id);
    // seg03:d635(id, 1)
    public Treinador? Treinador(ushort id) => id == 0 ? null : trPorID.GetValueOrDefault(id);
    public Treinador? TreinadorDa(Equipa? e) => e == null ? null : Treinador(e.Treinador);
    internal Arbitro? Arbitro(ushort id) => arPorID.GetValueOrDefault(id);

    // seg03:d55c(id, 2): a equipa cujo treinador tem esse id
    public Equipa? EquipaDoTreinador(ushort id) => S.Equipas.Find(e => e.Treinador == id);

    // seg03:d55c(id, 3): a equipa que tem o jogador, e o indice dele
    public (Equipa? E, int K) EquipaDoJogador(ushort id)
    {
        foreach (var e in S.Equipas)
        {
            int k = e.Jogadores.FindIndex(j => j.ID == id);
            if (k >= 0) return (e, k);
        }
        return (null, -1);
    }

    // seg03:d749(id, 1): indice da divisao da equipa, ou -1
    public int DivisaoDa(Equipa e)
    {
        for (int i = 0; i < S.Divisoes.Count; i++)
            if (S.Divisoes[i].Equipas.Contains(e.ID)) return i;
        return -1;
    }

    // seg03:576f: a equipa esta na divisao de indice 4 (compara o nome da divisao)
    public bool NoDistrital(Equipa e)
    {
        int d = DivisaoDa(e);
        return d >= 0 && S.Divisoes.Count > 4 && S.Divisoes[d].Nome.AsSpan().SequenceEqual(S.Divisoes[4].Nome);
    }

    int G32(int ds) => BitConverter.ToInt32(S.Cabecalho.G2D40, ds - 0x2D40);

    // DS:2D7F: o passo dos emprestimos (500 000 com reais) e do valor do clube
    public int UnidadeEmprestimo => G32(0x2D7F);
    // DS:2D87: o fator da moeda (5 com reais)
    public int Moeda => G32(0x2D87);
    // DS:2D9B (Extended): o passo do preco do bilhete
    public Ext ArredBilhete => X87.DeExt80(S.Cabecalho.G2D40, 0x2D9B - 0x2D40);
    // DS:2B46 (Extended)
    public Ext Inflacao => X87.DeExt80(S.Cabecalho.Real2B46);
    // DS:2B50 (Extended, 1,0 nos saves vistos): multiplica o preco pedido pela CPU
    public Ext Fator2B50 => X87.DeExt80(S.Cabecalho.Real2B50);
    // DS:2B42
    public int Ano => S.Cabecalho.Ano;

    // percorre a lista DS:26FE na ordem dela; f pode tirar o jogador da equipa ou passa-lo
    // para outra
    public void TodosJogadores(Action<Equipa, Jogador> f)
    {
        var dono = new Dictionary<ushort, (Equipa E, Jogador J)>();
        void Indexar()
        {
            dono.Clear();
            foreach (var e in S.Equipas)
                foreach (var j in e.Jogadores) dono[j.ID] = (e, j);
        }
        Indexar();
        foreach (var id in OrdemJogadores.ToList())
        {
            if (!dono.TryGetValue(id, out var p)) continue;
            if (!p.E.Jogadores.Contains(p.J))
            {
                Indexar();
                if (!dono.TryGetValue(id, out p)) continue;
            }
            f(p.E, p.J);
        }
    }

    internal static byte[] Nome(byte[] b) => b.Length == 0 ? Array.Empty<byte>() : Cifra.TextoEFT(b).Texto;

    internal void SetForcaMedia(Equipa e, short v) => forcaMedia[e.ID] = v;

    // (o retrato do jogo online, Online/Retrato) o +0xC5 de cada equipa, que o save nao guarda
    internal Dictionary<ushort, short> ForcasMedias => forcaMedia;
    // o +0xC5 da equipa (nao vai para o save): a forca media guardada por GestaoCPU
    public short ForcaMediaGuardada(Equipa e) => forcaMedia.GetValueOrDefault(e.ID);
}
