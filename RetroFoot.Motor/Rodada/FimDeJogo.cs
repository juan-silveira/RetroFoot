using System.Collections.Generic;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Rodada.Campos;
using static RetroFoot.Motor.Rodada.X87;
using TipoJogo = RetroFoot.Motor.Partida.TipoJogo;

namespace RetroFoot.Motor.Rodada;

// (Traduzido de motor/rodada/fimdejogo.go, classificacao.go e chicotada.go.)
public sealed partial class Mundo
{
    // tipos de lancamento do balanco da temporada (seg03:58bf): o valor tambem soma num
    // LongInt do bloco +0x31..+0x4C da equipa
    public const byte BalBilheteira = 0x01, Bal02 = 0x02, Bal03 = 0x03, Bal15 = 0x15,
        BalSalarios = 0x16, BalEstadio = 0x17, BalJuros = 0x18;

    static readonly Dictionary<byte, int> Bal = new()
    {
        [BalBilheteira] = 0x31, [Bal02] = 0x35, [Bal03] = 0x39, [Bal15] = 0x3D,
        [BalSalarios] = 0x41, [BalEstadio] = 0x45, [BalJuros] = 0x49,
    };

    // seg03:58bf(equipa, valor, tipo): soma ao dinheiro (+0x71) e, nos tipos do balanco, a
    // linha correspondente (32 bits com sinal, estoura como o original)
    public static void Lancamento(Equipa e, int valor, byte tipo)
    {
        e.Dinheiro = (uint)((int)e.Dinheiro + valor);
        if (Bal.TryGetValue(tipo, out int off)) SetEqD(e, off, EqD(e, off) + valor);
    }

    public static int Saldo(Equipa e, byte tipo) => EqD(e, Bal[tipo]);

    // seg03:5992: soma dos salarios dos jogadores que nao estao de saida (+0x36 = 0)
    public static int FolhaSalarial(Equipa e)
    {
        int s = 0;
        foreach (var j in e.Jogadores) if (j.W36 == 0) s += (int)j.Salario;
        return s;
    }

    // seg03:5a0a: depois de cada jogo da equipa
    public static void PagarSalarios(Equipa e) => Lancamento(e, -FolhaSalarial(e), BalSalarios);

    static readonly Ext C0_05 = Const(0xCD, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xFA, 0x3F);   // seg09:385b

    // seg09:3865: arred(emprestimo x 0,05)
    public static int Juros(int emprestimo) => Arred(Xi(emprestimo).Mul(C0_05));

    // seg03:5a37: os juros do emprestimo (+0x6D)
    public static void PagarJuros(Equipa e) => Lancamento(e, -Juros((int)e.D6D), BalJuros);

    // seg03:56e4: suspensao (+0x27) e jogos de lesao (+0x29) caem 1, no minimo 0
    public static void DescontarSuspensoesELesoes(Equipa e)
    {
        foreach (var j in e.Jogadores)
        {
            j.Suspensao = (ushort)System.Math.Max((short)j.Suspensao - 1, 0);
            j.JogosLesao = (ushort)System.Math.Max((short)j.JogosLesao - 1, 0);
        }
    }

    // seg03:56e4 com os cartoes do launcher (cartoes.asm, parte 9): so desce se > 0 e, com 3
    // ou mais amarelos acumulados (+0x25), o contador volta a 0 e, se o jogador nao esta
    // suspenso (por um vermelho), fica suspenso 1 jogo e sai da escalacao
    public static void DescontarComAmarelos(Equipa e)
    {
        foreach (var j in e.Jogadores)
        {
            if ((short)j.Suspensao > 0) j.Suspensao--;
            if ((short)j.JogosLesao > 0) j.JogosLesao--;
            if (JgB(j, JgAmarelosAcumulados) < 3) continue;
            SetJgB(j, JgAmarelosAcumulados, 0);
            if ((short)j.Suspensao > 0) continue;
            SetJgW(j, JgEscalacao, 0);
            j.Suspensao = 1;
        }
    }

    // alvos do moral (Real48, como os registos empurrados em seg03:95a1...)
    static readonly Ext MoralPeso = Real48Regs(0x9A80, 0x9999, 0x5999);   // 0,85
    internal static readonly Ext Moral0 = Real48Regs(0, 0, 0);
    static readonly Ext Moral0_8 = Real48Regs(0xCD80, 0xCCCC, 0x4CCC);
    static readonly Ext Moral1_3 = Real48Regs(0x6681, 0x6666, 0x2666);
    static readonly Ext Moral1_5 = Real48Regs(0x0081, 0, 0x4000);
    internal static readonly Ext Moral2 = Real48Regs(0x0082, 0, 0);
    static readonly Ext Moral2_5 = Real48Regs(0x0082, 0, 0x2000);

    // seg14:10d1 com peso 0,85: moral <- 0,85 moral + 0,15 alvo
    internal static void MudarMoral(Equipa e, Ext alvo) => MediaPonderada(e.Moral, MoralPeso, alvo);

    // seg03:9396: o fim de cada jogo (seg05:01f6 para cada jogo da lista, e seg03:143e para
    // os que nao vao a prolongamento):
    //  - campeonato: bilheteira (publico x preco) para o mandante; resultado no calendario
    //    das duas; vitorias/empates/derrotas, pontos (3/1/0), pontos dos treinadores (vitoria
    //    em casa 2, empate 1 e 2, vitoria fora 3), chicotada (seg03:8b5d -> 4f40) e moral;
    //    golos pro e contra.
    //  - Taca: bilheteira ao meio para as duas; quem perde (penaltis se empatar) e eliminado
    //    (+0x30 = 1, sai de DS:26F2); pontos dos treinadores (3 para quem passa) e moral.
    //  - as duas: suspensoes e lesoes -1, salarios, juros, +1 jogo do treinador; o mandante
    //    guarda o ultimo resultado contra o visitante (+0x65, seg03:151d). O arbitro fica livre.
    public void FimDeJogo(Jogo j)
    {
        var casa = Equipa(j.Casa); var fora = Equipa(j.Fora);
        if (j.Taca == TipoJogo.Campeonato)
        {
            Lancamento(casa!, Arred(Xi((int)j.D40).Mul(Ingressos.Preco(DeExt80(j.B36), casa))), BalBilheteira);
            short dif = (short)(EqW(casa!, EqGolosJogo) - EqW(fora!, EqGolosJogo));
            ResultadoNoCalendario(casa!, fora!, j, false);
            var tc = TreinadorDa(casa)!; var tf = TreinadorDa(fora)!;
            if (dif > 0)
            {
                AddW(casa!, EqVitorias, 1); AddW(fora!, EqDerrotas, 1); AddW(casa!, EqPontos, 3);
                tc.D0E += 2;
                if (Despedir(tf, fora!)) Chicotada(fora!, 0);
                MudarMoral(casa!, Moral2); MudarMoral(fora!, Moral0);
            }
            else if (dif == 0)
            {
                AddW(casa!, EqEmpates, 1); AddW(fora!, EqEmpates, 1);
                AddW(casa!, EqPontos, 1); AddW(fora!, EqPontos, 1);
                tc.D0E += 1; tf.D0E += 2;
                if (Despedir(tc, casa!)) Chicotada(casa!, 0);
                if (Despedir(tf, fora!)) Chicotada(fora!, 0);
                MudarMoral(casa!, Moral0_8); MudarMoral(fora!, Moral1_3);
            }
            else
            {
                AddW(fora!, EqVitorias, 1); AddW(casa!, EqDerrotas, 1); AddW(fora!, EqPontos, 3);
                tf.D0E += 3;
                if (Despedir(tc, casa!)) Chicotada(casa!, 0);
                MudarMoral(casa!, Moral0); MudarMoral(fora!, Moral2_5);
            }
            AddW(casa!, EqGolosPro, EqW(casa!, EqGolosJogo));
            AddW(fora!, EqGolosPro, EqW(fora!, EqGolosJogo));
            AddW(casa!, EqGolosContra, EqW(fora!, EqGolosJogo));
            AddW(fora!, EqGolosContra, EqW(casa!, EqGolosJogo));
        }
        else if (j.Taca == TipoJogo.Taca)
        {
            int Metade() => Arred(Xi((int)j.D40).Mul(Ingressos.Preco(DeExt80(j.B36), casa)).Div(X(2)));
            Lancamento(casa!, Metade(), BalBilheteira);
            Lancamento(fora!, Metade(), BalBilheteira);
            short dif = (short)(EqW(casa!, EqGolosJogo) - EqW(fora!, EqGolosJogo));
            var (ic, iF) = ResultadoNoCalendario(casa!, fora!, j, true);
            bool passaCasa = dif > 0;
            if (dif == 0)
            {
                passaCasa = true;
                if (G.Penaltis != null)
                {
                    // seg11:0d46; no fim, 0cf2 marca +0x06 das entradas do calendario: 2 para
                    // quem passa, 3 para quem cai
                    passaCasa = !G.Penaltis(this, j);
                    byte vc = (byte)(passaCasa ? 2 : 3), vf = (byte)(passaCasa ? 3 : 2);
                    if (ic >= 0) casa!.Calendario[ic].B06 = vc;
                    if (iF >= 0) fora!.Calendario[iF].B06 = vf;
                }
            }
            var tc = TreinadorDa(casa)!; var tf = TreinadorDa(fora)!;
            if (passaCasa)
            {
                SetEqB(fora!, EqEliminadoTaca, 1);
                TirarDaTaca(fora!.ID);
                tc.D0E += 3;
                if (Despedir(tf, fora)) Chicotada(fora, ChicSemHumanos);
                MudarMoral(casa!, Moral1_5); MudarMoral(fora, Moral0);
            }
            else
            {
                SetEqB(casa!, EqEliminadoTaca, 1);
                TirarDaTaca(casa!.ID);
                tf.D0E += 3;
                if (Despedir(tc, casa)) Chicotada(casa, ChicSemHumanos);
                MudarMoral(casa, Moral0); MudarMoral(fora!, Moral2);
            }
        }
        if (casa != null)
        {
            DepoisDoJogo(casa);
            if (fora != null) ConfrontoDireto(casa, fora);
        }
        if (fora != null) DepoisDoJogo(fora);
        if (j.Arbitro != 0 && Arbitro(j.Arbitro) is { } a) a.B06 = 1;
        j.Casa = 0; j.Fora = 0;
    }

    void DepoisDoJogo(Equipa e)
    {
        if (Regras.CartoesAmarelos) DescontarComAmarelos(e);
        else DescontarSuspensoesELesoes(e);
        PagarSalarios(e);
        PagarJuros(e);
        if (TreinadorDa(e) is { } t && (short)t.W0C < 0x7FFF) t.W0C++;
    }

    void TirarDaTaca(ushort id) => S.OutrasEquipas.Remove(id);

    // seg03:9403-94d9 (e 9925-9a11 na Taca): a proxima entrada nao jogada do calendario de
    // cada equipa (seg03:1c9d) recebe os golos dela (+0x0A), os do adversario (+0x0C), o
    // publico (+0x10) e, na Taca, o +0x50 do jogo em +0x06
    (int Ic, int IF) ResultadoNoCalendario(Equipa casa, Equipa fora, Jogo j, bool taca)
    {
        ushort gc = (ushort)EqW(casa, EqGolosJogo), gf = (ushort)EqW(fora, EqGolosJogo);
        int ic = ProximaEntrada(casa), iF = ProximaEntrada(fora);
        if (ic >= 0)
        {
            var c = casa.Calendario[ic];
            c.W0A = gc; c.W0C = gf; c.D10 = j.D40;
            if (taca) c.B06 = j.B50;
        }
        if (iF >= 0)
        {
            var c = fora.Calendario[iF];
            c.W0A = gf; c.W0C = gc; c.D10 = j.D40;
            if (taca) c.B06 = j.B50;
        }
        return (ic, iF);
    }

    // seg03:151d: na lista do mandante (+0x65), o ultimo resultado em casa contra o
    // visitante (ano +0x06, golos +0x04 e +0x05); se nao houver, cria no fim
    void ConfrontoDireto(Equipa casa, Equipa fora)
    {
        byte gc = (byte)EqW(casa, EqGolosJogo), gf = (byte)EqW(fora, EqGolosJogo);
        uint ano = (uint)Ano;
        var ev = casa.Eventos2.Find(x => x.W0A == fora.ID);
        if (ev != null) { ev.D06 = ano; ev.B04 = gc; ev.B05 = gf; return; }
        casa.Eventos2.Add(new EventoEquipa2 { D06 = ano, W0A = fora.ID, B04 = gc, B05 = gf });
    }

    static readonly Ext C0_65 = Const(0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0xA6, 0xFE, 0x3F);   // seg03:8b53

    // seg03:8b5d: cai se ja tem 10 jogos na equipa e o moral esta em 0,65 ou menos; ou,
    // sendo humano, se a equipa esta no Distrital
    public bool Despedir(Treinador t, Equipa e)
    {
        if ((short)t.W0C >= 10 && C0_65.Cmp(DeReal48(e.Moral)) >= 0) return true;
        return t.Humano != 0 && NoDistrital(e);
    }

    // ---------- classificacao (seg11:1ab6, seg06:0378) e evolucao ----------

    internal static bool EmOrdemClassificacao(Equipa a, Equipa b, bool vitorias = false)
    {
        int s = System.Math.Sign((short)(EqW(a, EqPontos) - EqW(b, EqPontos)));
        if (s != 0) return s > 0;
        if (vitorias)
        {
            s = System.Math.Sign((short)(EqW(a, EqVitorias) - EqW(b, EqVitorias)));
            if (s != 0) return s > 0;
        }
        short sa = (short)(EqW(a, EqGolosPro) - EqW(a, EqGolosContra));
        short sb = (short)(EqW(b, EqGolosPro) - EqW(b, EqGolosContra));
        s = System.Math.Sign((short)(sa - sb));
        if (s != 0) return s > 0;
        return System.Math.Sign((short)(EqW(a, EqGolosPro) - EqW(b, EqGolosPro))) >= 0;
    }

    // seg06:0378: ordena as divisoes 0 a 3 (o Distrital nao) com a bolha; estavel
    public void OrdenarClassificacao()
    {
        for (int d = 0; d <= 3 && d < S.Divisoes.Count; d++)
            Listas.Ordenar(S.Divisoes[d].Equipas, (a, b) => EmOrdemClassificacao(Equipa(a)!, Equipa(b)!, Regras.DesempateVitorias));
    }

    // seg03:0d9c -> 6fb8 -> 4a41: nas divisoes 0 a 3, a equipa na posicao i tem o alvo
    // B = Escada(D, i) e, para cada jogador, pelo contador v (+0x0C):
    //   v > 0: goleiro so com Random(2) = 0; forca +1 se < 50, desfeito se passar de B + 5
    //          ou de 50;
    //   v < 0: Random(2) = 0; forca -1 se > 1, desfeito se ficar abaixo de B - 5 ou de 1.
    public void EvolucaoForca()
    {
        for (int d = 0; d <= 3 && d < S.Divisoes.Count; d++)
        {
            var div = S.Divisoes[d];
            for (int i = 0; i < div.Equipas.Count; i++)
                EvolucaoEquipa(Equipa(div.Equipas[i])!, Regras.Escada((short)div.W04, (short)i));
        }
    }

    internal void EvolucaoEquipa(Equipa e, short b)
    {
        foreach (var p in e.Jogadores)
        {
            short f = (short)p.Forca;
            short v = JgW(p, JgContadorV);
            if (v > 0)
            {
                if (!(p.Posicao == 0 && R.Random(2) != 0) && f < 50)
                {
                    f++;
                    if (b + 5 < f || f > 50) f--;
                }
            }
            else if (v < 0)
            {
                if (R.Random(2) == 0 && f > 1)
                {
                    f--;
                    if (b - 5 > f || f < 1) f++;
                }
            }
            p.Forca = (ushort)f;
        }
    }

    // seg05:0d3c, com r1, r2, r3 = RandomReal nessa ordem:
    //   ano mod 100 < 50: inflacao += p ((r1 + r2) - r3); senao: inflacao += p ((r1 - r2) - r3)
    // e depois fica entre 0,5 e 10; p = 0,01 x 14 / jornadas (Regras.PassoInflacao)
    public void AtualizarInflacao()
    {
        var inf = Inflacao;
        var r1 = X(R.Real());
        var r2 = X(R.Real());
        var t = Ano % 100 < 50 ? r2.Add(r1) : r1.Sub(r2);
        var r3 = X(R.Real());
        t = t.Sub(r3);
        inf = t.Mul(DeExt80(Regras.PassoInflacao())).Add(inf);
        if (inf.Cmp(X(10)) > 0) inf = X(10);
        else if (inf.Cmp(X(0.5)) < 0) inf = X(0.5);
        ParaExt80(inf, S.Cabecalho.Real2B46);
    }

    // ---------- chicotadas (seg03:4f40, seg03:5343) ----------

    public const int ChicOutrasEquipas = 0x100;   // candidatos: treinadores de outras equipas (seg03:5661)
    public const int ChicDesempregados = 0x200;   // candidatos: desempregados (forca media < 15)
    public const int ChicSemHumanos = 0x400;      // humanos ficam de fora (Taca, Distrital, ultima jornada)

    const int LimiteEventosTreinador = 100;
    public const byte EvTreinadorEntrou = 1, EvTreinadorDespedido = 2, EvTreinadorTrocouSaiu = 6, EvTreinadorTrocouEntrou = 7;

    // seg03:4f40(equipa, flags): a equipa troca de treinador:
    //  1. o novo por seg03:5343 (flags | 0x100, e | 0x200 se a forca media < 15); nenhum: nada;
    //  2. se a opcao DS:2B34 manda, tres linhas na lista de chicotadas;
    //  3. o novo vai para a equipa (moral 1,0, 0 jogos); se ele treinava outra equipa, o
    //     antigo vai para la (moral 1,0, 0 jogos);
    //  4. eventos no historico dos dois;
    //  5. se o antigo era humano e o novo nao, seg03:6a08 (salarios e dinheiro).
    // Devolve sempre false (o original zera o resultado no fim, seg03:5338).
    public bool Chicotada(Equipa e, int flags)
    {
        var antigo = TreinadorDa(e);
        flags |= ChicOutrasEquipas;
        if (MediaForca(e) < 15) flags |= ChicDesempregados;
        var novo = EscolherTreinador(e, flags);
        if (novo != null)
        {
            antigo = TreinadorDa(e)!;
            var outra = EquipaDoTreinador(novo.ID);
            bool mostrar = MostrarChicotadas == 2 || ((antigo.Humano != 0 || novo.Humano != 0) && MostrarChicotadas == 1);
            if (mostrar)
            {
                Chicotadas.Add(new Chicotada(antigo.ID, e.ID, 1));
                Chicotadas.Add(new Chicotada(novo.ID, outra?.ID ?? 0));
                Chicotadas.Add(new Chicotada(0, 0));
            }
            e.Treinador = novo.ID;
            novo.W0C = 0;
            Moral10(e);
            uint ano = (uint)Ano;
            if (outra != null)
            {
                outra.Treinador = antigo.ID;
                Moral10(outra);
                antigo.W0C = 0;
                EventoTreinador(antigo, EvTreinadorTrocouSaiu, ano, outra.ID);
                EventoTreinador(novo, EvTreinadorTrocouEntrou, ano, e.ID);
            }
            else
            {
                EventoTreinador(antigo, EvTreinadorDespedido, ano, 0);
                EventoTreinador(novo, EvTreinadorEntrou, ano, e.ID);
            }
        }
        if (antigo != null && novo != null && antigo.Humano != 0 && novo.Humano == 0) HumanoSaiu(e);
        return false;
    }

    static void Moral10(Equipa e) => e.Moral = new byte[] { 0x81, 0, 0, 0, 0, 0 };

    // seg03:b4cf + seg14:06e8 (lista limitada)
    static void EventoTreinador(Treinador t, byte tipo, uint ano, ushort equipa)
    {
        t.Eventos.Add(new EventoTreinador { B04 = tipo, D05 = ano, W09 = equipa });
        while (t.Eventos.Count > LimiteEventosTreinador) t.Eventos.RemoveAt(0);
    }

    // seg03:6a08: a equipa deixada por um humano para a CPU: cada salario vira
    // Min(salario, salario calculado) e o dinheiro negativo e zerado
    void HumanoSaiu(Equipa e)
    {
        foreach (var j in e.Jogadores)
            j.Salario = (uint)System.Math.Min((int)j.Salario, SalarioCalculado(j, e));
        if ((int)e.Dinheiro < 0) e.Dinheiro = 0;
    }

    // seg03:5343(equipa, flags): candidatos percorrendo os treinadores (DS:2702), ordenados
    // (seg11:1e3b com 0x200, seg11:1d59 sem), e o primeiro que aceita (seg03:8bcc).
    // No original "aceito" nao e zerado a cada treinador, so antes do laco: um treinador que
    // nao passa por nenhum teste herda o valor do anterior (reproduzido aqui).
    public Treinador? EscolherTreinador(Equipa e, int flags)
    {
        if (NoDistrital(e)) flags |= ChicSemHumanos;
        if (Jornada == Regras.Jornadas) flags |= ChicSemHumanos;   // seg03:53f3 (14 no original)
        var lista = new List<Treinador>();
        bool aceito = false;
        foreach (var t in S.Treinadores)
        {
            var eq = EquipaDoTreinador(t.ID);
            if ((flags & ChicDesempregados) != 0 && eq == null) aceito = true;
            // o original compara o ponteiro da equipa do candidato com o endereco do
            // parametro na pilha (seg03:54a9), nunca iguais: o proprio treinador da equipa
            // tambem passa por 5661 (e e recusado, diferenca de forca 0)
            if ((flags & ChicOutrasEquipas) != 0 && eq != null) aceito = CandidatoDeOutraEquipa(eq, e);
            if ((flags & ChicSemHumanos) != 0) aceito = aceito && t.Humano == 0;
            if (eq != null && NoDistrital(eq)) aceito = aceito && t.Humano == 0;
            if (aceito) lista.Add(t);
        }
        if ((flags & ChicDesempregados) != 0) Listas.Ordenar(lista, HumanosPrimeiro);
        else Listas.Ordenar(lista, PorHistorial);
        foreach (var t in lista)
            if (AceitaConvite(t, e)) return t;
        return null;
    }

    // seg03:5661: so se a equipa do candidato tem moral >= 1,25 e a que contrata e de 11 a
    // 19 pontos de forca media mais forte
    static bool CandidatoDeOutraEquipa(Equipa dele, Equipa nossa)
    {
        if (DeReal48(dele.Moral).Cmp(X(1.25)) < 0) return false;
        int d = MediaForca(nossa) - MediaForca(dele);
        return d > 10 && d < 20;
    }

    // seg11:1e3b: humanos antes; entre iguais, mais pontos (+0x0E) antes
    static bool HumanosPrimeiro(Treinador a, Treinador b)
    {
        if ((a.Humano != 0) != (b.Humano != 0)) return a.Humano != 0;
        return (int)a.D0E >= (int)b.D0E;
    }

    // seg11:1d59: decrescente pela palavra +0x16, depois +0x18, depois pontos (+0x0E)
    static bool PorHistorial(Treinador a, Treinador b)
    {
        short a16 = (short)a.D16, b16 = (short)b.D16;
        if (a16 != b16) return a16 > b16;
        short a18 = (short)(a.D16 >> 16), b18 = (short)(b.D16 >> 16);
        if (a18 != b18) return a18 > b18;
        return (int)a.D0E >= (int)b.D0E;
    }

    // seg03:8bcc: a CPU aceita sempre; humano sem equipa aceita; humano com equipa decide na
    // janela TinviteCoachDlg (seg06:3de2)
    bool AceitaConvite(Treinador t, Equipa e)
    {
        if (!HumanoAtivo(t)) return true;
        if (EquipaDoTreinador(t.ID) == null) return true;
        return G.AceitaConvite?.Invoke(this, t, e) ?? false;
    }

    // seg03:8c67: humano (+0x07) que nao esta em modo automatico (+0x06 = 0)
    public static bool HumanoAtivo(Treinador t) => t.Humano != 0 && t.B06 == 0;
}
