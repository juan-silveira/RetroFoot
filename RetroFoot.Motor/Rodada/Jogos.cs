using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Rodada.Campos;
using static RetroFoot.Motor.Rodada.X87;

namespace RetroFoot.Motor.Rodada;

// (Traduzido de motor/rodada/jogos.go e jornada.go.)
public sealed partial class Mundo
{
    static readonly Ext C0_1 = Const(0xCD, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xFB, 0x3F);   // seg03:6e34 e seg09:35c7
    static readonly Ext C0_9 = Const(0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0xE6, 0xFE, 0x3F);   // seg03:6f38
    static readonly Ext C0_3 = Const(0x9A, 0x99, 0x99, 0x99, 0x99, 0x99, 0x99, 0x99, 0xFD, 0x3F);   // seg03:b093

    // seg03:4ec3: media arredondada da forca de todos os jogadores; soma em 16 bits com sinal
    public static short MediaForca(Equipa e)
    {
        short soma = 0;
        foreach (var j in e.Jogadores) soma += (short)j.Forca;
        return (short)Arred(Xi(soma).Div(Xi(e.Jogadores.Count)));
    }

    internal static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    // seg03:6e48(divisao, jornada): atualiza o +0x79 de cada equipa da divisao, ordena a
    // lista da divisao pelo lugar de cada equipa no calendario desta jornada e cria os jogos
    // (seg03:0875) emparelhando a lista dois a dois
    public void CriarJogosDivisao(int d)
    {
        var div = S.Divisoes[d];
        int soma = 0;   // o dinheiro das equipas da CPU, em 32 bits
        foreach (var id in div.Equipas)
        {
            var e = Equipa(id)!;
            var t = TreinadorDa(e);
            if (t == null || t.Humano == 0) soma += (int)e.Dinheiro;
        }
        // r = soma / D (+0x1F da divisao), no minimo 0,1
        var r = Xi(soma).Div(Xi((int)div.D1F));
        if (C0_1.Cmp(r) > 0) r = C0_1;
        // +0x79 <- 0,9 (+0x79) + 0,1 r (seg14:115e), em todas as equipas
        foreach (var id in div.Equipas) MediaPonderadaExt(Equipa(id)!.B79, C0_9, r);
        // ordena a divisao pelo lugar da jornada: o byte +0x21+jornada (seg11:1a61, crescente;
        // a bolha troca tambem os iguais); com N > 8, o lugar de Berger (Regras.Lugar)
        int jn = Jornada;
        byte Lugar(ushort id) { var (b, o) = MemEquipa(Equipa(id)!, 0x22); return Regras.Lugar(b.AsSpan(o, 14), jn); }
        Listas.Ordenar(div.Equipas, (a, b) => Lugar(a) < Lugar(b));
        var titulo = Latin1($"{jn}ª Jornada");
        CriarJogos(div.Equipas.Select(id => Equipa(id)!).ToList(), 0, titulo, NomeDivisao(div), TipoJogo.Campeonato);
    }

    static byte[] NomeDivisao(Divisao d) => d.Nome.AsSpan(1, Math.Min((int)d.Nome[0], 20)).ToArray();

    // seg03:0875(lista, n, titulo, nome, tipo): embaralha os arbitros, calcula a media das
    // forcas e do +0x79 das equipas da lista, o preco do bilhete (seg09:35e1) e cria uma
    // linha de cabecalho com o nome (se houver) e n jogos lista[0] x lista[1], ... (n = 0:
    // metade da lista)
    public void CriarJogos(List<Equipa> lista, short n, byte[] titulo, byte[]? nome, byte tipo)
    {
        Listas.Embaralhar(R, S.Arbitros);
        short somaF = 0;
        var somaX = X(0);
        foreach (var e in lista)
        {
            somaF += MediaForca(e);
            somaX = somaX.Add(DeExt80(e.B79));
        }
        var cont = Xi(lista.Count);
        short mediaF = (short)Arred(Xi(somaF).Div(cont));
        var preco = PrecoBilhete(somaX.Div(cont), tipo, mediaF);
        if (n == 0) n = (short)(lista.Count / 2);
        if (nome is { Length: > 0 }) NovoJogo(TipoJogo.SemEquipas, null, null, nome, preco);
        for (int i = 0; i <= 2 * n - 1; i += 2) NovoJogo(tipo, lista[i], lista[i + 1], titulo, preco);
    }

    // seg09:35e1(x, tipo, media de forca), x = media do +0x79 (no minimo 0,1):
    //   p = arred(moeda inflacao / raiz(x) media / 5 (1 + 0,5 [Taca])) / 2
    // no minimo moeda/2 e, se DS:2D9B > 1, arredondado para multiplo de DS:2D9B
    public Ext PrecoBilhete(Ext x, byte tipo, short media)
    {
        if (C0_1.Cmp(x) > 0) x = C0_1;
        var s = x.Sqrt();
        var moeda = Xi(Moeda);
        var v = moeda.Mul(Inflacao).Div(s);
        v = v.Mul(Xi(media));
        v = v.Div(X(5));
        v = v.Mul(Xi(tipo == TipoJogo.Taca ? 1 : 0).Mul(X(0.5)).Add(X(1)));
        var p = Xi(Arred(v)).Div(X(2));
        var minimo = moeda.Mul(X(0.5));
        if (p.Cmp(minimo) < 0) p = minimo;
        var u = ArredBilhete;
        if (u.Cmp(X(1)) > 0) p = Xi(Arred(p.Div(u))).Mul(u);
        return p;
    }

    // o construtor do TGame, seg03:8db2: tipo, equipas, titulo (string[40]) e preco; o
    // publico (seg03:b0a1) e, com duas equipas, o arbitro (seg03:1a66) e a influencia dele
    // (seg03:c95c, +2 para o mandante). Nos jogos da Taca, o jogo no calendario das duas
    // (seg03:1df4).
    public Jogo NovoJogo(byte tipo, Equipa? casa, Equipa? fora, byte[] titulo, Ext preco)
    {
        var j = new Jogo { Taca = tipo, Casa = casa?.ID ?? 0, Fora = fora?.ID ?? 0 };
        S.Jogos.Add(j);
        int n = Math.Min(titulo.Length, 40);
        j.B04[0] = (byte)n;
        Array.Copy(titulo, 0, j.B04, 1, n);
        ParaExt80(preco, j.B36);
        j.B50 = 0;
        Publico(j);
        if (casa != null && fora != null)
        {
            var a = PreJogo.EscolherArbitro(S.Arbitros, casa, fora);
            if (a != null)
            {
                j.Arbitro = a.ID;
                j.W4C = (ushort)(PreJogo.InfluenciaDoArbitro(a, casa) + BonusDaTorcida(casa, j));
                j.W4E = (ushort)PreJogo.InfluenciaDoArbitro(a, fora);
            }
            else j.Arbitro = 0;
        }
        if (tipo == TipoJogo.Taca)
        {
            JogoTacaNoCalendario(casa!, 0, fora!);
            JogoTacaNoCalendario(fora!, 1, casa!);
        }
        return j;
    }

    // seg03:b0a1, F = MediaForca, W = moral:
    //   base = 3 F(casa) + F(fora) + 3 RandomReal
    //   publico = arred((base raiz(W casa) raiz(W fora) 200)^1,04 (1 + 0,3 [Taca]))
    // (o produto passa por um Real de 6 bytes antes da potencia) e o limite do estadio do
    // mandante (seg03:c0c0). Sem equipas ou tipo 2: 0.
    public void Publico(Jogo j)
    {
        var casa = Equipa(j.Casa); var fora = Equipa(j.Fora);
        if (casa == null || fora == null || j.Taca == TipoJogo.SemEquipas) { j.D40 = 0; return; }
        short a = MediaForca(fora);
        short h = MediaForca(casa);
        var @base = Xi((short)(h * 3 + a));
        var b = X(R.Real()).Mul(X(3)).Add(@base);
        var c = DeReal48(casa.Moral).Sqrt().Mul(b);
        c = DeReal48(fora.Moral).Sqrt().Mul(c).Mul(X(200));
        c = Real48(c);
        var p = Pot(c, Mercado.Constantes.C1_04);
        p = p.Mul(Xi(j.Taca == TipoJogo.Taca ? 1 : 0).Mul(C0_3).Add(X(1)));
        // (a procura antes do preco e a ocupacao media antes deste jogo: o preco escolhido na
        // tela da equipa refaz o publico deste jogo, RecalcularPublico)
        _demanda[j] = (p, (byte[])casa.EstadioReal.Clone());
        p = Ingressos.Publico(p, casa);   // launcher: o preco escolhido pelo mandante (seg03:B1EC)
        j.D40 = (uint)PreJogo.LotacaoDoEstadio(casa, Arred(p));
    }

    // o +2 do mandante na influencia do arbitro (seg03:1a66). Na versao Atual (as regras do
    // launcher, com o preco do ingresso) a torcida pesa pelo publico com o preco escolhido
    // comparado ao publico com o preco de tabela (a mesma procura sorteada): ate 1/4 dele +0,
    // ate 2/4 +1, ate igual +2 e mais que ele (o ingresso barato trouxe mais gente) +3. Sem
    // mexer no preco, sempre +2, como no jogo classico (que fica sempre com +2)
    public bool TorcidaNoArbitro;

    int BonusDaTorcida(Equipa casa, Jogo j)
    {
        if (!Regras.CartoesAmarelos || !TorcidaNoArbitro || !_demanda.TryGetValue(j, out var d)) return 2;
        long tabela = Math.Min(Arred(d.Procura), PreJogo.CapacidadeDoEstadio(casa));
        return BonusPeloPublico(j.D40, tabela);
    }

    public static int BonusPeloPublico(long publico, long publicoTabela) =>
        publicoTabela <= 0 ? 2
        : publico > publicoTabela ? 3
        : 4 * publico <= publicoTabela ? 0
        : 4 * publico <= 2 * publicoTabela ? 1 : 2;

    // a procura de cada jogo criado nesta execucao (o publico antes do preco do ingresso) e a
    // ocupacao media do estadio antes dele; so na memoria (nao vai para o save)
    readonly Dictionary<Jogo, (Ext Procura, byte[]? Ocupacao)> _demanda = new(ReferenceEqualityComparer.Instance);

    // O preco do ingresso mudado na tela da equipa (a versao Atual) vale ja para o jogo desta
    // jornada: o publico e refeito com a mesma procura sorteada ao criar o jogo (o sorteio nao
    // muda) e o fator do preco novo, limitado ao estadio, e a ocupacao media volta a de antes
    // dele. Sem isto o publico ficava o do preco antigo e o preco no maximo dava uma renda
    // absurda. Um jogo de um save carregado no meio da jornada (sem a procura guardada): a
    // procura media (o sorteio no meio), sem mexer na ocupacao.
    public void RecalcularPublico(Jogo j)
    {
        var casa = Equipa(j.Casa); var fora = Equipa(j.Fora);
        if (casa == null || fora == null || j.Taca == TipoJogo.SemEquipas) return;
        // (um save carregado: a procura media, com o publico guardado no peso de antes)
        if (!_demanda.ContainsKey(j)) _demanda[j] = (ProcuraMedia(j, casa, fora), null);
        int antes = BonusDaTorcida(casa, j);
        try { RecalcularSo(j, casa); }
        finally
        {
            // a torcida nova muda o peso dela no arbitro
            if (j.Arbitro != 0) j.W4C = (ushort)Math.Max(0, j.W4C - antes + BonusDaTorcida(casa, j));
        }
    }

    // a procura com o sorteio no meio (0,5), sem mexer no gerador
    Ext ProcuraMedia(Jogo j, Equipa casa, Equipa fora)
    {
        var b = X(0.5).Mul(X(3)).Add(Xi((short)(MediaForca(casa) * 3 + MediaForca(fora))));
        var c = DeReal48(fora.Moral).Sqrt().Mul(DeReal48(casa.Moral).Sqrt().Mul(b)).Mul(X(200));
        return Pot(Real48(c), Mercado.Constantes.C1_04).Mul(Xi(j.Taca == TipoJogo.Taca ? 1 : 0).Mul(C0_3).Add(X(1)));
    }

    void RecalcularSo(Jogo j, Equipa casa)
    {
        var d = _demanda[j];
        if (d.Ocupacao != null)
        {
            d.Ocupacao.CopyTo(casa.EstadioReal, 0);
            j.D40 = (uint)PreJogo.LotacaoDoEstadio(casa, Arred(Ingressos.Publico(d.Procura, casa)));
            return;
        }
        j.D40 = (uint)Math.Min(Arred(Ingressos.Publico(d.Procura, casa)), PreJogo.CapacidadeDoEstadio(casa));
    }

    // seg03:1df4(calendario, fora, adversario): entrada da Taca (+0x07 = 1, +0x08 =
    // adversario, +0x0E = 0 em casa / 1 fora, golos e publico -1) antes da primeira entrada
    // ainda nao jogada (seg03:1c9d), ou no fim
    static void JogoTacaNoCalendario(Equipa e, ushort fora, Equipa adv)
    {
        var nova = new EntradaCalendario { W08 = adv.ID, B07 = 1, W0E = fora, W0A = 0xFFFF, W0C = 0xFFFF, D10 = 0xFFFFFFFF };
        int i = ProximaEntrada(e);
        if (i < 0) e.Calendario.Add(nova);
        else e.Calendario.Insert(i, nova);
    }

    // seg03:1c9d: a primeira entrada do calendario (+0x69) com golos (+0x0A) < 0; -1 se nao ha
    public static int ProximaEntrada(Equipa e) => e.Calendario.FindIndex(c => (short)c.W0A < 0);

    // ---------- o laco da temporada e as jornadas (seg05) ----------

    // seg05:11b8: com DS:271C = jornada onde comecar:
    //   se nao esta a retomar: seg05:0002 (comeco da temporada, outro pacote)
    //   para jornada = DS:271C ate R: se ninguem pediu para sair: se a jornada ainda nao foi
    //   jogada, JogarJornada; se a jornada e par, JogarTaca; DS:271A = 0
    //   fim da temporada (seg05:038e) ou saida
    public void Temporada(Action<Mundo>? inicio)
    {
        if (Retomar == 0) inicio?.Invoke(this);
        short ini = (short)S.Cabecalho.G271C;
        short r = Regras.Jornadas;
        for (Jornada = ini; Jornada <= r; Jornada++)
        {
            if (Sair) continue;
            if (!JornadaJogada)
            {
                G.AntesDaJornada?.Invoke(this);
                JogarJornada();
            }
            if (!Sair && Jornada % 2 == 0) JogarTaca();
            JornadaJogada = false;
        }
        Jornada = ini > r ? ini : r;
        if (!Sair) G.FimDaTemporada?.Invoke(this);
    }

    // seg05:0c41, uma jornada do campeonato:
    //  1. (nao retomando) fecha os jogos que sobraram (seg05:01f6) e cria os das 4 divisoes;
    //  2. classificacao (a criacao dos jogos desordena as listas);
    //  3. prepara as equipas (seg03:13e1);
    //  4. se ninguem pediu para sair: transferencias (seg05:0aae), +1 jogo a cada titular
    //     (seg10:03ea), a jornada (gancho Partida), fecha cada jogo, chicotadas, classificacao
    //     e evolucao de forca (seg03:0d9c);
    //  5. sempre: inflacao (seg05:0d3c) e escalacao zerada em todos os jogadores.
    // Devolve true se o original mostraria a classificacao depois (seg05:0cf1).
    public bool JogarJornada()
    {
        if (Retomar == 0)
        {
            LimparJogos();
            for (int d = 0; d <= 3 && d < S.Divisoes.Count; d++) CriarJogosDivisao(d);
        }
        OrdenarClassificacao();
        PrepararJogos();
        bool mostrar = false;
        if (!Sair)
        {
            ExecutarTransferencias();
            var titulo = Latin1($"{Jornada}ª Jornada - {Ano}");
            ContarJogos();
            bool pediu = G.Partida?.Invoke(this, titulo, false) ?? false;
            mostrar = pediu || Jornada == Regras.Jornadas;
            LimparJogos();
            JanelaChicotadas();   // seg05:025a: mostra e esvazia
            OrdenarClassificacao();
            G.Classificacao?.Invoke(this, mostrar);
            AntesEvolucao?.Invoke(this);
            EvolucaoForca();
        }
        AtualizarInflacao();
        ZerarEscalacoes();
        return mostrar;
    }

    // seg05:025a: a janela das chicotadas (so com a lista nao vazia) e a lista esvaziada
    public void JanelaChicotadas()
    {
        if (Chicotadas.Count > 0) G.Chicotadas?.Invoke(this);
        Chicotadas.Clear();
    }

    // seg05:01f6: fecha cada jogo da lista (seg03:9396) e o tira, do primeiro ao ultimo
    public void LimparJogos()
    {
        while (S.Jogos.Count > 0)
        {
            var j = S.Jogos[0];
            FimDeJogo(j);
            S.Jogos.RemoveAt(0);
        }
    }

    // a primeira parte de seg10:03ea: cada titular ganha 1 jogo no historico (+0x17)
    public void ContarJogos() =>
        TodosJogadores((_, p) => { if (JgW(p, JgEscalacao) == 1) SetJgD(p, JgJogos, JgD(p, JgJogos) + 1); });

    // seg05:0e22 (e 1166 na Taca)
    void ZerarEscalacoes() => TodosJogadores((_, p) => SetJgW(p, JgEscalacao, 0));

    // seg05:0eef: ha fase da Taca se o numero de equipas do jogo (DS:26F6, todas) e maior que
    // arred(2^((R - jornada) div 2)). Do LongInt o jogo guarda so a palavra de baixo e compara
    // com sinal: com R = 38, 2^16 a 2^18 viram 0 e 2^15 vira -32768.
    public short LimiteTaca()
    {
        int n = (Regras.Jornadas - Jornada) / 2;
        return (short)Arred(Pot(X(2), Xi(n)));
    }

    // seg05:0fae, pelo numero P de equipas que passam
    public static byte[] NomeFaseTaca(short p) => Latin1(p switch
    {
        1 => "FINAL",
        2 or 3 => "Meias-finais",
        >= 4 and <= 7 => "Quartos de final",
        >= 8 and <= 15 => "Oitavos de final",
        _ => $"{p} avos de final",
    });

    // O titulo de uma jornada ou de um jogo ("3ª Jornada - 1998", "TAÇA - Oitavos de final",
    // "Taça - FINAL - 1998") no idioma escolhido, so para mostrar: os bytes gravados no save
    // (Jogo.B04) e os passados aos ganchos ficam como o original.
    public static string TituloNaTela(string t)
    {
        var m = System.Text.RegularExpressions.Regex.Match(t, @"^(\d+)ª Jornada(?: - (\d+))?$");
        if (m.Success)
            return m.Groups[2].Success ? Textos.T("{0}ª Jornada - {1}", m.Groups[1].Value, m.Groups[2].Value)
                                       : Textos.T("{0}ª Jornada", m.Groups[1].Value);
        m = System.Text.RegularExpressions.Regex.Match(t, @"^(Taça|TAÇA) - (.+?)(?: - (\d+))?$");
        if (!m.Success) return t;
        var fase = FaseNaTela(m.Groups[2].Value);
        if (m.Groups[1].Value == "TAÇA") return Textos.T("TAÇA - {0}", fase);
        return m.Groups[3].Success ? Textos.T("Taça - {0} - {1}", fase, m.Groups[3].Value) : Textos.T("Taça - {0}", fase);
    }

    // o nome da fase (NomeFaseTaca) no idioma escolhido ({0} jogos, {1} equipas)
    public static string FaseNaTela(string fase)
    {
        var m = System.Text.RegularExpressions.Regex.Match(fase, @"^(\d+) avos de final$");
        return m.Success ? Textos.T("{0} avos de final", m.Groups[1].Value, 2 * int.Parse(m.Groups[1].Value)) : Textos.T(fase);
    }

    // seg05:0ed4, uma fase da Taca (nas jornadas pares):
    //  1. so se ha mais equipas no jogo (DS:26F6) que LimiteTaca;
    //  2. (nao retomando) fecha os jogos que sobraram, embaralha as equipas, acha P (a menor
    //     potencia de 2 com P >= N/2) e cria N - P jogos "TACA - <fase>" (tipo 1); ordena os
    //     jogos com os de humanos primeiro (seg11:1c3e);
    //  3. prepara as equipas, joga (TjourneyDlg), fecha os jogos, chicotadas, transferencias
    //     e escalacao zerada.
    public void JogarTaca()
    {
        if ((short)S.Equipas.Count <= LimiteTaca()) return;   // 5:0f23: Count de DS:26F6
        byte[] fase = Array.Empty<byte>();
        if (Retomar == 0)
        {
            LimparJogos();
            Listas.Embaralhar(R, S.OutrasEquipas);
            short n = (short)S.OutrasEquipas.Count;
            if (n <= 1) return;
            short p = 1;
            while (Xi(n).Div(X(2)).Cmp(Xi(p)) > 0) p *= 2;
            fase = NomeFaseTaca(p);
            var titulo = Latin1("TAÇA - ").Concat(fase).ToArray();
            CriarJogos(S.OutrasEquipas.Select(id => Equipa(id)!).ToList(), (short)(n - p), titulo, null, TipoJogo.Taca);
            // a janela do sorteio (TsorteioDlg, opcao DS:2B3A: 2 sempre, 1 so com humano na
            // Taca) ordena a propria lista DS:26F2 pelo nome, o que muda os proximos sorteios
            int humanos = S.OutrasEquipas.Count(id => TreinadorDa(Equipa(id)) is { } t && HumanoAtivo(t));
            if ((TacaModo == 2 || (TacaModo == 1 && humanos > 0)) && S.OutrasEquipas.Count > 2)
            {
                Motor.Temporada.Epoca.OrdenarTacaPorNome(S);
                G.Sorteio?.Invoke(this);
            }
            Listas.Ordenar(S.Jogos, (a, b) => ChaveJogoHumano(a) >= ChaveJogoHumano(b));
        }
        else if (S.Jogos.Count > 0)
        {
            // ao retomar, o original usa o titulo inteiro do 1o jogo ("TACA - ...") (seg05:10d2)
            var t = S.Jogos[0].B04;
            fase = t.AsSpan(1, t[0]).ToArray();
        }
        PrepararJogos();
        if (Sair) return;
        var tit = Latin1("Taça - ").Concat(fase).Concat(Latin1($" - {Ano}")).ToArray();
        ContarJogos();
        G.Partida?.Invoke(this, tit, true);
        LimparJogos();
        JanelaChicotadas();
        ExecutarTransferencias();
        ZerarEscalacoes();
    }

    // seg11:1c3e: para cada treinador do jogo, 3 humano (+0x07) + (1 se +0x06 = 0)
    short ChaveJogoHumano(Jogo j)
    {
        short k = 0;
        foreach (var id in new[] { j.Casa, j.Fora })
        {
            var t = TreinadorDa(Equipa(id));
            if (t == null) continue;
            if (t.B06 == 0) k++;
            k += (short)(3 * (sbyte)t.Humano);
        }
        return k;
    }

    // seg03:b03b: as duas equipas e os golos (+0x4F) iguais
    public bool Empatado(Jogo j)
    {
        var c = Equipa(j.Casa); var f = Equipa(j.Fora);
        return c != null && f != null && EqW(c, EqGolosJogo) == EqW(f, EqGolosJogo);
    }

    // seg03:143e, no fim dos 90 minutos de um dia de Taca: os empatados ficam (+0x50 = 1); os
    // outros (inclusive as linhas de cabecalho) sao fechados e saem
    public void FecharJogosSemProlongamento()
    {
        for (int i = 0; i < S.Jogos.Count;)
        {
            var j = S.Jogos[i];
            if (Empatado(j)) { j.B50 = 1; i++; continue; }
            FimDeJogo(j);
            S.Jogos.RemoveAt(i);
        }
    }
}
