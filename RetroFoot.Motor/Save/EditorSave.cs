using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;

namespace RetroFoot.Motor.Save;

// O Editor de Save do RetroFoot: um .e98 aberto como Jogo98 (Save.cs) com o que o editor mostra
// e muda (inflacao, dinheiro, moral e estadio do clube, os jogadores, a troca de equipa de um
// treinador humano). Porte do SaveCodec do launcher do jogo classico (src/SaveCodec.cs, Juan
// Silveira): as regras e os limites sao os de la; os campos aqui vem do Jogo98 em vez de
// deslocamentos procurados no arquivo, e o que nao foi mexido volta igual, byte a byte.
// Limites de 16 bits: docs internos do launcher, "Limites de 16 bits".
public sealed class SaveEditavel
{
    public Jogo98 Jogo { get; }
    public List<EquipaEditavel> Equipas { get; } = new();
    public List<TreinadorEditavel> Treinadores { get; } = new();

    SaveEditavel(Jogo98 j)
    {
        Jogo = j;
        var div = new Dictionary<int, string>();
        foreach (var d in j.Divisoes)
        {
            var nome = Latin1(d.Nome.AsSpan(1, Math.Min((int)d.Nome[0], d.Nome.Length - 1)));
            foreach (var id in d.Equipas) div.TryAdd(id, nome);
        }
        foreach (var e in j.Equipas)
            Equipas.Add(new EquipaEditavel(this, e, div.GetValueOrDefault(e.ID, "")));
        foreach (var t in j.Treinadores) Treinadores.Add(new TreinadorEditavel(t));
    }

    public static SaveEditavel Ler(byte[] b) => new(Jogo98.Ler(b));
    public static SaveEditavel Abrir(string caminho) => Ler(File.ReadAllBytes(caminho));

    // os bytes do .e98 (sem mudancas = os mesmos bytes lidos)
    public byte[] Gravar() => Jogo.Gravar();

    // Grava com seguranca: .bak do arquivo como estava (so na 1a vez, guarda o original de
    // antes do editor), os bytes novos conferidos (lidos de novo) num temporario e so entao
    // trocados pelo arquivo. Devolve o caminho do .bak.
    public string GravarEm(string caminho)
    {
        var novo = Gravar();
        var conferido = Jogo98.Ler(novo).Gravar();
        if (!conferido.AsSpan().SequenceEqual(novo)) throw new InvalidDataException("o save gravado nao le de volta igual");
        var bak = caminho + ".bak";
        if (File.Exists(caminho) && !File.Exists(bak)) File.Copy(caminho, bak);
        var tmp = caminho + ".tmp";
        File.WriteAllBytes(tmp, novo);
        File.Move(tmp, caminho, true);
        return bak;
    }

    public int Ano => Jogo.Cabecalho.Ano;

    // Inflacao (o jogo mostra x 10). So regrava se mudou: o Extended tem mais precisao que double
    public double Inflacao
    {
        get => Reais.Ext80(Jogo.Cabecalho.Real2B46);
        set
        {
            if (Math.Abs(value - Inflacao) <= 1e-9) return;
            Reais.PorExt80(Jogo.Cabecalho.Real2B46, Math.Clamp(value, LimitesSave.InflacaoMin, LimitesSave.InflacaoMax));
        }
    }

    // a equipa do humano "da vez" (DS:271E): se apontar para uma equipa do computador, o jogo
    // carrega e ja disputa a rodada; acompanha a troca de equipa
    public ushort HumanoDaVez { get => Jogo.Cabecalho.G271E; set => Jogo.Cabecalho.G271E = value; }

    public EquipaEditavel? EquipaDoTreinador(TreinadorEditavel t) => Equipas.FirstOrDefault(e => e.TreinadorId == t.Id);
    public TreinadorEditavel? Treinador(int id) => Treinadores.FirstOrDefault(t => t.Id == id);

    // Poe o treinador na equipa destino como a "chicotada psicologica" do jogo (seg03:4f40): as
    // duas equipas trocam de treinador e ficam com moral 1,0 e 0 jogos; o humano "da vez"
    // acompanha. E, como o jogo quando um time chama o treinador de outro, a historia dos dois
    // treinadores ganha a troca (o novo "foi chamado e entrou" no destino; o antigo do destino
    // "saiu" para a equipa de origem): antes a troca do editor nao ficava na historia.
    public void TrocarEquipa(TreinadorEditavel tec, EquipaEditavel destino)
    {
        var origem = EquipaDoTreinador(tec) ?? throw new InvalidOperationException("sem equipa");
        if (origem == destino) return;
        if (!destino.PodeTerHumano) throw new InvalidOperationException("distrital");
        ushort outro = destino.TreinadorId;
        destino.TreinadorId = tec.Id;
        origem.TreinadorId = outro;
        origem.Moral = 1.0;
        destino.Moral = 1.0;
        uint ano = (uint)Ano;
        tec.T.W0C = 0;
        Evento(tec.T, Rodada.Mundo.EvTreinadorTrocouEntrou, ano, destino.Id);
        if (Treinador(outro) is { } antigo)
        {
            antigo.T.W0C = 0;
            Evento(antigo.T, Rodada.Mundo.EvTreinadorTrocouSaiu, ano, origem.Id);
        }
        if (HumanoDaVez == origem.Id) HumanoDaVez = destino.Id;
        else if (HumanoDaVez == destino.Id) HumanoDaVez = origem.Id;
    }

    // seg03:b4cf + seg14:06e8: o evento no fim da historia do treinador (lista limitada)
    static void Evento(Treinador t, byte tipo, uint ano, ushort equipa)
    {
        t.Eventos.Add(new EventoTreinador { B04 = tipo, D05 = ano, W09 = equipa });
        while (t.Eventos.Count > Mercado.MercadoJogo.LimiteEventosTreinador) t.Eventos.RemoveAt(0);
    }

    internal static string Latin1(ReadOnlySpan<byte> b) => Encoding.Latin1.GetString(b);
    internal static string TextoCifrado(byte[] t) => t.Length == 0 ? "" : Latin1(Cifra.TextoEFT(t).Texto).Trim();
}

public sealed class TreinadorEditavel
{
    public Treinador T { get; }
    public TreinadorEditavel(Treinador t) { T = t; Nome = SaveEditavel.TextoCifrado(t.Nome); }
    public ushort Id => T.ID;
    public string Nome { get; }
    public bool Humano => T.Humano == 1;
}

public sealed class EquipaEditavel
{
    readonly SaveEditavel _save;
    public Equipa E { get; }
    public string Nome { get; }
    public string NomeCurto { get; }
    public string NomeExibido => NomeCurto.Length > 0 ? NomeCurto : Nome;
    public string Pais { get; }
    public string Divisao { get; }        // "1ª Divisão" ... ou "Distrital"; vazio se em nenhuma
    public List<JogadorEditavel> Jogadores { get; }

    internal EquipaEditavel(SaveEditavel s, Equipa e, string divisao)
    {
        _save = s;
        E = e;
        Nome = SaveEditavel.TextoCifrado(e.NomeCompleto);
        NomeCurto = SaveEditavel.TextoCifrado(e.NomeCurto);
        Pais = SaveEditavel.TextoCifrado(e.Pais).ToUpperInvariant();
        Divisao = divisao;
        Jogadores = e.Jogadores.Select(j => new JogadorEditavel(j)).ToList();
    }

    public ushort Id => E.ID;
    public ushort TreinadorId { get => E.Treinador; set => E.Treinador = value; }
    public TreinadorEditavel? Treinador => _save.Treinador(E.Treinador);

    // So equipas das divisoes podem ter treinador humano: no Distrital o jogo da "List index out
    // of bounds" (testado no Android com o launcher)
    public bool PodeTerHumano => Divisao.Length > 0 && !Divisao.StartsWith("Distrital", StringComparison.OrdinalIgnoreCase);

    // Dinheiro: inteiro de 4 bytes COM sinal (o jogo faz (int)e.Dinheiro: as dividas ficam
    // negativas). O SaveCodec do launcher lia sem sinal e, ao gravar, trocava uma divida por
    // 999.999.999; aqui uma divida fica como esta e pode ser editada.
    public long Dinheiro
    {
        get => (int)E.Dinheiro;
        set { if (value != Dinheiro) E.Dinheiro = (uint)(int)Math.Clamp(value, LimitesSave.DinheiroMin, LimitesSave.DinheiroMax); }
    }

    // Moral: Real de 6 bytes, 0 a 2 (a barra do jogo: moral x 10 / 20); so regrava se mudou
    public double Moral
    {
        get => Reais.Real48(E.Moral);
        set
        {
            if (Math.Abs(value - Moral) <= 1e-9) return;
            Reais.PorReal48(E.Moral, Math.Clamp(value, 0, LimitesSave.MoralMax));
        }
    }

    // N: capacidade = 5000 x N; o jogo so constroi ate 24 (seg07:29dd). Um valor maior ja no
    // save (editado antes) fica como esta ate ser mudado aqui.
    public int Estadio
    {
        get => E.Estadio;
        set { if (value != E.Estadio) E.Estadio = (byte)Math.Clamp(value, 1, LimitesSave.EstadioMax); }
    }

    public int Capacidade => Estadio * 5000;

    // cores RGB (0xRRGGBB) da letra e do fundo
    public int CorLetra => E.CorLetra[0] << 16 | E.CorLetra[1] << 8 | E.CorLetra[2];
    public int CorFundo => E.CorFundo[0] << 16 | E.CorFundo[1] << 8 | E.CorFundo[2];

    // ordem da tela do time no jogo: G, D, M, A e, em cada posicao, o nome em ordem alfabetica
    public List<JogadorEditavel> OrdemDoJogo() =>
        Jogadores.OrderBy(j => j.Posicao).ThenBy(j => LimitesSave.SemAcento(j.Nome), StringComparer.Ordinal).ToList();
}

public sealed class JogadorEditavel
{
    public Jogador J { get; }
    public string Nome { get; }
    public string Pais { get; }

    public JogadorEditavel(Jogador j)
    {
        J = j;
        Nome = SaveEditavel.TextoCifrado(j.NomeCifrado);
        Pais = SaveEditavel.TextoCifrado(j.Pais).ToUpperInvariant();
    }

    public int Posicao => J.Posicao;                            // 0 G, 1 D, 2 M, 3 A
    public char Letra => J.Posicao < 4 ? "GDMA"[J.Posicao] : '?';
    public bool Estrela { get => J.Estrela != 0; set => J.Estrela = (byte)(value ? 1 : 0); }
    public int Forca { get => J.Forca; set { if (value != Forca) J.Forca = (ushort)Math.Clamp(value, LimitesSave.ForcaMin, LimitesSave.ForcaMax); } }
    public long Salario { get => J.Salario; set { if (value != Salario) J.Salario = (uint)Math.Clamp(value, LimitesSave.SalarioMin, LimitesSave.SalarioMax); } }
    public int Nota { get => (short)J.Nota; set { if (value != Nota) J.Nota = (ushort)Math.Clamp(value, 1, 10); } }
    public int Lesao { get => (short)J.Lesao; set { if (value != Lesao) J.Lesao = (ushort)Math.Clamp(value, 0, 10); } }
    public int Comportamento { get => (short)J.Comportamento; set { if (value != Comportamento) J.Comportamento = (ushort)Math.Clamp(value, 0, 5); } }
    public int Suspensao { get => (short)J.Suspensao; set { if (value != Suspensao) J.Suspensao = (ushort)Math.Clamp(value, 0, LimitesSave.SuspensaoMax); } }
    public int JogosLesionado { get => (short)J.JogosLesao; set { if (value != JogosLesionado) J.JogosLesao = (ushort)Math.Clamp(value, 0, LimitesSave.JogosLesionadoMax); } }

    // Historial (+0x17): jogos, golos (4 bytes cada), lesoes e amarelos na carreira, vermelhos,
    // amarelos acumulados e a marca do jogo do ultimo amarelo (Partida/Cartoes.cs)
    // (os 16 bits de cima sao os jogos da temporada do RetroFoot)
    public int Jogos => BitConverter.ToUInt16(J.Historial, 0);
    public int Golos => BitConverter.ToInt32(J.Historial, 4);
    public int AmarelosAcumulados
    {
        get => J.Historial[14];
        // so grava se mudou: um valor fora de 0..2 ja no save fica como esta; a marca nunca muda
        set { if (value != J.Historial[14]) J.Historial[14] = (byte)Math.Clamp(value, 0, LimitesSave.AmarelosAcumuladosMax); }
    }
}

// Os limites do editor (SaveCodec do launcher). Os de 16 bits sao contas que o jogo faz numa
// word; passando delas o valor "da a volta" e a partida sai errada.
public static class LimitesSave
{
    public const int EstadioMax = 24;                 // 120.000 lugares
    public const long DinheiroMin = -999_999_999, DinheiroMax = 999_999_999;
    public const int SuspensaoMax = 4, JogosLesionadoMax = 20;   // sorteados 1-4 e 1-20 (seg03:48be, 4940)
    public const int AmarelosAcumuladosMax = 2;       // o 3o vira 1 jogo de suspensao
    public const double InflacaoMin = 0.5, InflacaoMax = 10.0;
    public const double MoralMax = 2.0;
    public const int ForcaMin = 1, ForcaMax = 32767, ForcaNormal = 50;
    public const long SalarioMin = 50, SalarioMax = 9_999_999;

    public const int SomaPesosMax = 65535;   // soma dos pesos de gol (seg03:106c, Random da word baixa)
    public const int SomaForcaMax = 32767;   // soma da forca do elenco (seg03:4f20, fild word)
    public const int EscalacaoMax = 32766;   // 3G + D + M + A do time; o do adversario soma junto (seg03:4686)

    // os nomes do comportamento no original (0 a 5)
    public static readonly string[] Comportamentos = { "Fair Play", "Cordeirinho", "Cavalheiro", "Caneleiro", "Caceteiro", "Sarrafeiro" };

    // Regra do jogo (seg03:7516): estrela = Nota >= 8 e posicao Medio ou Avancado
    public static bool TemEstrela(int posicao, int nota) => nota >= 8 && posicao >= 2;

    // Maior forca que o jogador usa direito: o peso do sorteio do autor do gol (seg03:0f69) e
    // posicao^2 x forca x Nota em 16 bits com sinal. Acima de 32767 o peso fica negativo e ele
    // nunca marca. Guarda-redes (posicao 0) tem peso 0.
    // E o penalti (seg03:818c): (posicao + 1) x forca tambem em 16 bits com sinal; acima de
    // 32767 o valor do batedor fica negativo e ele chuta para fora ou o goleiro defende
    // (um defensor com forca 31998 perdia penaltis). Vale o menor dos dois limites.
    public static int ForcaMaxima(int posicao, int nota)
    {
        int penalti = 32767 / (Math.Max(0, posicao) + 1);
        if (posicao <= 0) return Math.Min(ForcaMax, penalti);
        return Math.Min(penalti, Math.Min(ForcaMax, 32767 / (posicao * posicao * Math.Clamp(nota, 1, 10))));
    }

    public static long PesoGol(int posicao, int forca, int nota) => (long)Math.Max(0, posicao) * Math.Max(0, posicao) * forca * nota;

    // o que o jogador soma no setor em campo (seg03:397d): forca + 2 se estrela + 3 se e do pais
    // do clube (contado sempre, pior caso); o guarda-redes vale 3 vezes
    public static long PesoEscalacao(int posicao, int forca, bool estrela) => (forca + (estrela ? 2 : 0) + 3L) * (posicao == 0 ? 3 : 1);

    public enum Limite { Nenhum, ForcaJogador, SomaPesos, SomaForca, Escalacao }

    // As contas do elenco como estao (para mostrar ao lado dos maximos)
    public static (long Pesos, long Forca, long Escalacao) Contas(IEnumerable<JogadorEditavel> elenco)
    {
        var l = elenco.ToList();
        return (l.Sum(x => PesoGol(x.Posicao, x.Forca, x.Nota)), l.Sum(x => (long)x.Forca),
            l.Select(x => PesoEscalacao(x.Posicao, x.Forca, x.Estrela)).OrderByDescending(v => v).Take(11).Sum());
    }

    // Maior forca que o jogador pode ter com a Nota dada sem estourar nenhum limite (o dele e os
    // do elenco, com os outros como estao). 0 = nenhuma serve.
    public static int ForcaMaximaNoElenco(IList<JogadorEditavel> elenco, JogadorEditavel editado, int nota, bool estrela)
    {
        var outros = elenco.Where(x => x != editado).ToList();
        long max = ForcaMaxima(editado.Posicao, nota);
        long peso1 = PesoGol(editado.Posicao, 1, nota);
        if (peso1 > 0)
            max = Math.Min(max, (SomaPesosMax - outros.Sum(x => PesoGol(x.Posicao, x.Forca, x.Nota))) / peso1);
        max = Math.Min(max, SomaForcaMax - outros.Sum(x => (long)x.Forca));
        // na escalacao mais forte ele entra com os 10 maiores dos outros
        long top10 = outros.Select(x => PesoEscalacao(x.Posicao, x.Forca, x.Estrela)).OrderByDescending(v => v).Take(10).Sum();
        max = Math.Min(max, (EscalacaoMax - top10) / (editado.Posicao == 0 ? 3 : 1) - (estrela ? 2 : 0) - 3);
        return (int)Math.Max(0, max);
    }

    // Confere o elenco com o jogador editado nos valores novos: o 1o limite estourado, o valor
    // a que a conta chegaria e o maximo dela
    public static (Limite Qual, long Valor, long Maximo) Estouro(IList<JogadorEditavel> elenco, JogadorEditavel editado, int forca, int nota, bool estrela)
    {
        if (forca > ForcaMaxima(editado.Posicao, nota)) return (Limite.ForcaJogador, forca, ForcaMaxima(editado.Posicao, nota));
        int F(JogadorEditavel x) => x == editado ? forca : x.Forca;
        int N(JogadorEditavel x) => x == editado ? nota : x.Nota;
        bool E(JogadorEditavel x) => x == editado ? estrela : x.Estrela;
        long pesos = elenco.Sum(x => PesoGol(x.Posicao, F(x), N(x)));
        if (pesos > SomaPesosMax) return (Limite.SomaPesos, pesos, SomaPesosMax);
        long soma = elenco.Sum(x => (long)F(x));
        if (soma > SomaForcaMax) return (Limite.SomaForca, soma, SomaForcaMax);
        long escal = elenco.Select(x => PesoEscalacao(x.Posicao, F(x), E(x))).OrderByDescending(v => v).Take(11).Sum();
        if (escal > EscalacaoMax) return (Limite.Escalacao, escal, EscalacaoMax);
        return (Limite.Nenhum, 0, 0);
    }

    // sem depender de cultura (o build pode rodar em globalizacao invariante)
    public static string SemAcento(string s)
    {
        const string com = "ÁÀÂÃÄÉÈÊËÍÌÎÏÓÒÔÕÖÚÙÛÜÇÑáàâãäéèêëíìîïóòôõöúùûüçñ", sem = "AAAAAEEEEIIIIOOOOOUUUUCNaaaaaeeeeiiiiooooouuuucn";
        var c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++) { int k = com.IndexOf(c[i]); if (k >= 0) c[i] = sem[k]; }
        return new string(c).ToUpperInvariant();
    }
}
