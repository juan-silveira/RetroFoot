using System.Collections.Generic;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Rodada.Campos;
using static RetroFoot.Motor.Rodada.X87;

namespace RetroFoot.Motor.Rodada;

// (Traduzido de motor/rodada/preparacao.go.)
public sealed partial class Mundo
{
    internal static readonly Ext C1_2 = Const(0x9A, 0x99, 0x99, 0x99, 0x99, 0x99, 0x99, 0x99, 0xFF, 0x3F);   // seg03:3e6f
    internal static readonly Ext C0_8 = Const(0xCD, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xFE, 0x3F);   // seg03:3e79, c026
    internal static readonly Ext C1_3 = Const(0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0x66, 0xA6, 0xFF, 0x3F);   // seg03:3e83
    internal static readonly Ext C1_6 = Const(0xCD, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xFF, 0x3F);   // seg03:3aa3
    internal static readonly Ext C0_2 = Const(0xCD, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xFC, 0x3F);   // seg09:3704
    internal static readonly Ext C0_04 = Const(0x00, 0xD7, 0xA3, 0x70, 0x3D, 0x0A, 0xD7, 0xA3, 0xFA, 0x3F);  // seg03:8063
    internal static readonly Ext C1000 = Const(0, 0, 0, 0, 0, 0, 0x00, 0xFA, 0x08, 0x40);                     // seg03:8057

    // seg03:13e1: para cada jogo, enquanto o jogador nao pediu para sair, prepara as duas
    // equipas (seg03:a67f)
    public void PrepararJogos()
    {
        if (Online) { PrepararOnline(); return; }
        foreach (var j in S.Jogos.ToArray())
            if (!Sair) PrepararJogo(j);
    }

    // Jogo online: logo depois da rodada, a preparacao dos times da CPU (os leiloes deles) e a
    // revisao de salarios dos humanos (o pedido de aumento); so depois os humanos preparam (a
    // barreira: cada um na sua tela, ate o PRONTO). No original tudo vem na ordem dos jogos: online,
    // as perguntas chegavam a quem ja tinha dado PRONTO. A ordem nao muda ao retomar (outro host).
    void PrepararOnline()
    {
        // (o save de um jogo sozinho retomado no meio da preparacao, o modo amigo: as equipas
        // depois da que parou ainda nao prepararam, na ordem dos jogos; esta rodada termina nela)
        if (Retomar > 0 && RetomarNaOrdemDosJogos)
        {
            RetomarNaOrdemDosJogos = false;
            foreach (var j in S.Jogos.ToArray())
                if (!Sair) PrepararJogo(j);
            return;
        }
        RetomarNaOrdemDosJogos = false;
        bool rever = Retomar == 0;
        var times = new List<(Equipa E, Jogo J)>();
        foreach (var j in S.Jogos.ToArray())
            if (Equipa(j.Casa) is { } c && Equipa(j.Fora) is { } f) { times.Add((c, j)); times.Add((f, j)); }
        bool humano((Equipa E, Jogo J) x) => TreinadorDa(x.E) is { } t && HumanoAtivo(t);
        var cpu = times.Where(x => !humano(x)).ToList();
        var humanos = times.Where(humano).ToList();
        // primeiro os times da CPU (os leiloes deles), depois o pedido de aumento dos humanos e so
        // entao a preparacao deles
        foreach (var x in cpu)
            if (!Sair) PrepararEquipa(x.E, x.J);
        if (rever && !Sair) ReverSalariosHumanos();
        foreach (var x in humanos)
            if (!Sair) PrepararEquipa(x.E, x.J);
        // (online o tecnico de um time pode mudar no meio da preparacao: quem desistiu ou foi
        // expulso, quem entrou agora. Um time sem escalacao que feche nao entra em campo vazio
        // (a disputa de penaltis sem titulares parava o jogo): a automatica)
        if (!Sair)
            foreach (var x in times)
                if (!x.E.Jogadores.Any(p => RetroFoot.Motor.Partida.Memoria.Escalacao(p) == RetroFoot.Motor.Partida.Memoria.EscTitular))
                { Escalacao.Automatico(R, x.E); ForcaDosSetores(x.E); }
    }

    // seg03:a67f: com as duas equipas, prepara o mandante e, se ninguem pediu para sair, o
    // visitante (seg03:3e9c)
    public void PrepararJogo(Jogo j)
    {
        var casa = Equipa(j.Casa); var fora = Equipa(j.Fora);
        if (casa == null || fora == null) return;
        PrepararEquipa(casa, j);
        if (!Sair) PrepararEquipa(fora, j);
    }

    // seg03:3e9c(equipa, jornada, jogo). Ao retomar um jogo gravado (DS:271E != 0) so a
    // equipa da vez e preparada, sem a parte 1.
    //  1. treinador com mais de 1 jogo na equipa (+0x0C > 1): zera +0x0C..+0x13 de cada
    //     jogador e reve salarios (a partir da 2a jornada, so quem ainda nao teve revisao):
    //     a CPU aumenta o de maior razao salario calculado/salario acima de 1,2 e baixa o de
    //     menor razao abaixo de 0,8; o humano recebe o pedido do de maior razao acima de 1,3
    //     (e aumento > 300). Com dinheiro negativo, o primeiro vendavel vai a venda.
    //  2. zera o estado de jogo da equipa (+0x4D, 14 bytes).
    //  3. humano: TprepareGameDlg (gancho); CPU: seg03:3aad (gestao e escalacao).
    //  4. forca dos setores (seg03:397d).
    public void PrepararEquipa(Equipa e, Jogo j)
    {
        if (Retomar > 0 && Retomar != (short)e.ID) return;
        Traco?.Invoke(e, "preparar");
        // vendeu e a variavel [bp-4] de 3e9c (o jogador posto a venda por divida != nil). So
        // e escrita na parte 1; sem ela, 3aad le o que estiver na pilha, e ali esta sempre o
        // endereco de retorno do StackCheck (nao nulo): a CPU nao vende nem chama o Random(20).
        bool vendeu = true;
        var t = TreinadorDa(e);
        if (_revistas.Remove(e.ID, out var v)) vendeu = v;
        else if (Online && t != null && HumanoAtivo(t)) { }
        else if (Retomar == 0 && t != null && (short)t.W0C > 1) vendeu = ReverSalarios(e, t) != null;
        Retomar = 0;
        System.Array.Clear(e.B4D);
        if (t != null && HumanoAtivo(t)) G.PrepararHumano?.Invoke(this, e, j);
        else GestaoCPU(e, vendeu);   // sem treinador o original mostra "coach is nil" e segue como CPU
        if (!Sair) ForcaDosSetores(e);
        Traco?.Invoke(e, "fim");
    }

    // Jogo online (PrepararOnline): a parte 1 dos times humanos (o pedido de aumento) antes de
    // todos prepararem. Ao retomar no meio da preparacao (outro host), os humanos nao revem de novo.
    public bool Online;
    // o save veio de um jogo sozinho (o modo amigo): retomado no meio da preparacao, a rodada
    // termina na ordem dos jogos (no jogo sozinho, as equipas depois da que parou nao prepararam)
    public bool RetomarNaOrdemDosJogos;
    readonly Dictionary<ushort, bool> _revistas = new();

    void ReverSalariosHumanos()
    {
        _revistas.Clear();
        foreach (var j in S.Jogos.ToArray())
            foreach (var e in Equipa(j.Casa) is { } c && Equipa(j.Fora) is { } f ? new[] { c, f } : [])
            {
                if (Sair) return;
                if (_revistas.ContainsKey(e.ID) || TreinadorDa(e) is not { } t || !HumanoAtivo(t)) continue;
                _revistas[e.ID] = (short)t.W0C > 1 ? ReverSalarios(e, t) != null : true;
            }
    }

    // a parte 1 de seg03:3e9c (seg03:3edf-42d7)
    Jogador? ReverSalarios(Equipa e, Treinador t)
    {
        Ext maior = C1_2, menor = C0_8, pedido = X(0);
        Jogador? candMaior = null, candMenor = null, candPedido = null;
        foreach (var p in e.Jogadores)
        {
            System.Array.Clear(p.B0C);
            if (JgB(p, JgSalarioRevisto) != 0 || Jornada <= 1) continue;
            int novo = SalarioCalculado(p, e);
            var razao = Xi(novo).Div(Xi((int)p.Salario));
            if (!(p.B35 < t.B1A.Length && t.B1A[p.B35] == 0 && HumanoAtivo(t)))
            {
                // CPU (ou opcao do treinador: +0x1A+[+0x35 do jogador] != 0)
                if (maior.Cmp(razao) < 0 && C1_2.Cmp(razao) < 0) { maior = razao; candMaior = p; }
                if (menor.Cmp(razao) > 0 && C0_8.Cmp(razao) > 0) { menor = razao; candMenor = p; }
                continue;
            }
            if (C1_3.Cmp(razao) < 0 && novo - (int)p.Salario > 300)
                if (pedido.Cmp(razao) < 0 || pedido.Mant == 0) { pedido = razao; candPedido = p; }
        }
        foreach (var p in new[] { candMaior, candMenor })
            if (p != null)
            {
                p.Salario = (uint)SalarioCalculado(p, e);
                SetJgB(p, JgSalarioRevisto, 1);
            }
        if (candPedido != null)
        {
            bool aceita = G.PedidoDeAumento?.Invoke(this, e, candPedido) ?? false;
            if (!aceita) ColocarAVenda(e, 0, candPedido);
        }
        Jogador? venda = null;
        if ((int)e.Dinheiro < 0)
        {
            venda = e.Jogadores.Find(p => PodeVender(e, p) == 0);
            if (venda != null) ColocarAVenda(e, 0, venda);
        }
        return venda;
    }

    // seg03:5b37 arredonda o preco para baixo, a milhares
    void ColocarAVenda(Equipa e, int preco, Jogador j) => G.ColocarAVenda?.Invoke(this, e, preco / 1000 * 1000, j);

    // seg03:7ff4(jogador, +0x79 da equipa):
    //   s = raiz(x) moeda inflacao 35 forca^1,04 1000^0,04 / 50 / moeda
    //   salario = arred(s) 50 moeda, entre 50 moeda e 50000 moeda
    // e quem esta lesionado (+0x29 > 0) nao tem o salario reduzido
    public int SalarioCalculado(Jogador p, Equipa e)
    {
        int moeda = Moeda;
        var a = DeExt80(e.B79).Sqrt().Mul(Xi(moeda).Mul(Inflacao)).Mul(X(35));
        var b = Pot(Xi((short)p.Forca), Mercado.Constantes.C1_04).Mul(a);
        var c = Pot(C1000, C0_04).Mul(b);
        int d = Arred(c.Div(X(50)).Div(Xi(moeda))) * 50 * moeda;
        if (50 * moeda > d) d = 50 * moeda;
        else if (50000 * moeda < d) d = 50000 * moeda;
        if ((short)p.JogosLesao > 0 && (int)p.Salario > d) d = (int)p.Salario;
        return d;
    }

    // seg09:371a:
    //   arred(forca^1,04 moeda inflacao (1 - comp/5 0,2 - Lesao/10 0,2 + Nota/10 0,2) 2000)
    public int ValorJogador(Jogador p)
    {
        if (G.ValorJogador != null) return G.ValorJogador(this, p);
        var v = Pot(Xi((short)p.Forca), Mercado.Constantes.C1_04);
        v = v.Mul(Xi(Moeda).Mul(Inflacao));
        var f = X(1).Sub(Xi((short)p.Comportamento).Div(X(5)).Mul(C0_2));
        f = f.Sub(Xi((short)p.Lesao).Div(X(10)).Mul(C0_2));
        f = f.Add(Xi((short)p.Nota).Div(X(10)).Mul(C0_2));
        return Arred(v.Mul(f).Mul(X(2000)));
    }

    // seg09:3899: arred(forca media moral inflacao moeda 20000), truncado para multiplo de
    // DS:2D7F (no minimo DS:2D7F). E o limite de emprestimo da CPU (seg03:3d71).
    public int ValorClube(Equipa e)
    {
        var v = DeReal48(e.Moral).Mul(Xi(MediaForca(e)));
        v = v.Mul(Inflacao).Mul(Xi(Moeda)).Mul(X(20000));
        int u = UnidadeEmprestimo;
        int r = Arred(v) / u * u;
        return r == 0 ? u : r;
    }

    // seg03:c172 -> seg09:37ee(nivel), nivel 1 ate 5 blocos, 2 de 6 a 13, 3 a partir de 14:
    //   Max(arred(raiz(nivel) inflacao 35000 moeda / 50000) 50000, 50000)
    public int PrecoObraEstadio(Equipa e)
    {
        long nivel = e.Estadio >= 14 ? 3 : e.Estadio >= 6 ? 2 : 1;
        var v = Inflacao.Mul(X(35000)).Mul(Xi(Moeda));
        v = Xi(nivel).Sqrt().Mul(v).Div(X(50000));
        int r = Arred(v) * 50000;
        return r < 50000 ? 50000 : r;
    }

    // seg03:c030: ocupacao (Real48) >= 0,8 e N < 24
    public static bool PodeAmpliarEstadio(Equipa e) => !(C0_8.Cmp(DeReal48(e.EstadioReal)) > 0) && e.Estadio < 24;

    // seg03:c081: N + 1 e ocupacao 0,6
    public static bool AmpliarEstadio(Equipa e)
    {
        if (e.Estadio >= 24) return false;
        e.Estadio++;
        new byte[] { 0x80, 0x9A, 0x99, 0x99, 0x99, 0x19 }.CopyTo(e.EstadioReal, 0);
        return true;
    }

    // seg03:5a6a(equipa, jogador). 0 = pode; senao o motivo: 1 elenco com 11 ou menos, 2 ja
    // vendido (+0x36), 3 salario revisto nesta temporada (+0x16), 4 +0x12 != 0 (ja tentou
    // vender neste jogo), 5 ultimo goleiro, 6 ficariam menos de 10 de linha
    public static short PodeVender(Equipa e, Jogador p)
    {
        byte total = e.B83[5];   // +0x88
        if (total <= 11 || e.Jogadores.Count <= 11) return 1;
        if ((short)p.W36 > 0) return 2;
        if (JgB(p, 0x16) != 0) return 3;
        if (JgB(p, 0x12) != 0) return 4;
        if (p.Posicao == 0 && e.B83[0] <= 1) return 5;
        if (p.Posicao != 0 && (short)(total - e.B83[0]) <= 10) return 6;
        return 0;
    }

    // seg03:3aad (rotina interna de 3e9c), para equipas da CPU:
    //  1. guarda a forca media em +0xC5;
    //  2. se nao houve venda por divida e ja passou a 1a jornada: vende quando 3 dinheiro <
    //     folha, ou com 1 chance em 20 (Random(20) = 0), ou com 5 estrangeiros e o limite de
    //     jogadores menos 3 ou mais; o escolhido e o de menor utilidade (seg03:5de1) entre os
    //     vendaveis (so estrangeiros, se ha 5), pelo valor x DS:2B50 x 1,6;
    //  3. com a opcao 0x13: paga o emprestimo enquanto sobra mais que uma unidade depois de
    //     folha e juros; pede emprestimo (codigo que nunca roda); amplia o estadio se a
    //     ocupacao >= 0,8 e sobra o preco + 2 folhas;
    //  4. escalacao da CPU (seg03:4388).
    public void GestaoCPU(Equipa e, bool vendeu)
    {
        int folha = FolhaSalarial(e);
        SetForcaMedia(e, MediaForca(e));
        bool estrangeirosCheio = e.B83[4] == 5;   // +0x87
        if (!vendeu && Jornada > 1)
        {
            bool vender = (int)e.Dinheiro * 3 < folha || R.Random(20) == 0
                || (estrangeirosCheio && (short)e.B83[5] >= Regras.LimiteVendaCPU);
            if (vender)
            {
                var melhor = X(0);
                Jogador? cand = null;
                foreach (var p in e.Jogadores)
                {
                    var v = Utilidade(e, p);
                    if (!(melhor.Cmp(v) > 0 || melhor.Mant == 0)) continue;
                    if (PodeVender(e, p) != 0) continue;
                    if (estrangeirosCheio && p.B35 != 2) continue;
                    cand = p; melhor = v;
                }
                if (cand != null) ColocarAVenda(e, Arred(Xi(ValorJogador(cand)).Mul(Fator2B50).Mul(C1_6)), cand);
            }
        }
        if (OpcaoGestaoCPU)
        {
            int u = UnidadeEmprestimo;
            while ((int)e.D6D > 0 && (int)e.Dinheiro - folha - Juros((int)e.D6D) > u)
            {
                e.Dinheiro = (uint)((int)e.Dinheiro - u);   // seg03:668d
                e.D6D = (uint)((int)e.D6D - u);
            }
            if (Juros((int)e.D6D) + folha > (int)e.Dinheiro)
            {
                int credito = ValorClube(e);
                // o original so pede emprestimo se juros+folha < dinheiro, o contrario da
                // condicao de entrada: este laco nunca empresta
                while ((int)e.D6D < credito && Juros((int)e.D6D) + folha < (int)e.Dinheiro)
                {
                    e.Dinheiro = (uint)((int)e.Dinheiro + u);   // seg03:665e
                    e.D6D = (uint)((int)e.D6D + u);
                }
            }
        }
        // o estadio nao depende do add-in: sem ele, 3:3cd0 salta para 3dd6, que e esta parte
        if (PodeAmpliarEstadio(e) && PrecoObraEstadio(e) + folha * 2 < (int)e.Dinheiro)
        {
            AmpliarEstadio(e);
            Lancamento(e, -PrecoObraEstadio(e), BalEstadio);   // recalculado depois (seg03:3e3d)
        }
        Traco?.Invoke(e, "escalar");
        Escalacao.EscalacaoCPU(R, e);
    }

    static readonly Ext C1_2_5dd7 = C1_2;   // seg03:5dd7 (1,2)

    // seg03:5de1(equipa, jogador): quanto o jogador vale para a equipa, com F = forca media
    // guardada (+0xC5) e a linha F div 10 da tabela DS:0300:
    //   (5/(comportamento+1))^(1/4) tab[posicao]/(n na posicao+1) tab[total]/(n de
    //   jogadores+1) forca / (F+1)
    // x 1,5 se nacional (+0x35 = 0), x 1,2 se Bosman/PLOP (+0x35 = 1)
    public Ext Utilidade(Equipa e, Jogador p)
    {
        if (G.UtilidadeJogador != null) return G.UtilidadeJogador(this, e, p);
        short f = ForcaMediaGuardada(e);
        var lin = Mercado.Tabelas.LinhaPlantelIdeal((short)(f / 10));
        var x = X(5).Div(Xi((short)((short)p.Comportamento + 1))).Sqrt().Sqrt();
        var q1 = Xi(lin[p.Posicao]).Div(Xi(e.B83[p.Posicao] + 1));
        var q2 = Xi(lin[5]).Div(Xi(e.B83[5] + 1));
        var r = q1.Mul(q2).Mul(Xi((short)p.Forca));
        r = r.Div(Xi(f + 1)).Mul(x);
        if (p.B35 == 0) r = r.Mul(X(1.5));
        else if (p.B35 == 1) r = r.Mul(C1_2_5dd7);
        return r;
    }

    // seg03:397d: zera os 4 setores e soma, para cada titular, forca + 3 (pais do jogador
    // = pais da equipa) + 2 estrela no setor da posicao dele
    public static void ForcaDosSetores(Equipa e) => Partida.Sorteio.ForcaDosSetores(e);

    // as listas da Lei Bosman e do PLOP originais (CTRGROUP/*.TXE)
    public static readonly string[] BosmanOriginal = "POR ESP FRA ITA ALE AUT GRE ING ESC WAL ILN IRL DIN BEL HOL LUX FIN SUE".Split(' ');
    public static readonly string[] PLOPOriginal = "POR BRA ANG MOC CAV STP GBI GNE TIM".Split(' ');

    // seg12:0a2f(pais do jogador, pais da equipa): 0 mesmo pais, 1 paises diferentes mas os
    // dois na lista Bosman ou os dois no PLOP, 2 estrangeiro
    public static byte Estrangeiro(string pj, string pe, IReadOnlyCollection<string> bosman, IReadOnlyCollection<string> plop)
    {
        if (pj == pe) return 0;
        if ((bosman.Contains(pj) && bosman.Contains(pe)) || (plop.Contains(pj) && plop.Contains(pe))) return 1;
        return 2;
    }

    IReadOnlyCollection<string> ListaBosman => (IReadOnlyCollection<string>?)Bosman ?? BosmanOriginal;
    IReadOnlyCollection<string> ListaPLOP => (IReadOnlyCollection<string>?)PLOP ?? PLOPOriginal;

    static string Latin1(byte[] b) => System.Text.Encoding.Latin1.GetString(b);

    // seg03:37b4(equipa, jogador): poe no fim da lista, soma 1 na contagem da posicao
    // (+0x83+posicao), recalcula +0x35 (estrangeiro) e soma 1 em +0x87 (se estrangeiro) e
    // +0x88 (total)
    public void AdicionarJogador(Equipa e, Jogador p)
    {
        e.B83[p.Posicao]++;
        p.B35 = Estrangeiro(Latin1(Nome(p.Pais)), Latin1(Nome(e.Pais)), ListaBosman, ListaPLOP);
        if (p.B35 == 2) e.B83[4]++;
        e.B83[5]++;
        e.Jogadores.Add(p);
    }

    // seg05:0aae: para cada jogador (lista DS:26FE) com transferencia pendente (+0x36 = id da
    // compradora > 0): sai da lista da equipa atual (sem mexer nas contagens dela), recebe o
    // salario calculado para a nova (seg03:7ff4 -> 8156: escalacao 0 e salario revisto),
    // entra na nova (seg03:37b4) e a nova tem -1 em +0xC7 (e em +0xC9 se estrangeiro)
    public void ExecutarTransferencias()
    {
        TodosJogadores((_, p) =>
        {
            if ((short)p.W36 <= 0) return;
            var (antiga, k) = EquipaDoJogador(p.ID);
            var nova = Equipa(p.W36)!;
            antiga!.Jogadores.RemoveAt(k);
            p.Salario = (uint)SalarioCalculado(p, nova);
            SetJgW(p, JgEscalacao, 0);
            SetJgB(p, JgSalarioRevisto, 1);
            p.W36 = 0;
            AdicionarJogador(nova, p);
            nova.WC7--;
            if (Estrangeiro(Latin1(Nome(p.Pais)), Latin1(Nome(nova.Pais)), ListaBosman, ListaPLOP) == 2) nova.WC9--;
        });
    }
}
