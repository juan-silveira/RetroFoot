using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Mercado.Constantes;
using static RetroFoot.Motor.Rodada.X87;

namespace RetroFoot.Motor.Mercado;

// o estado da janela de emprestimos (TloanDlg): Limite fixado ao abrir, Max(divida,
// LimiteEmprestimo); nao e recalculado ao pedir ou pagar
public sealed record Banco(int Limite)
{
    // borrowBtn ativo se divida < limite (7:395a)
    public bool PodePedir(Equipa e) => (int)e.D6D < Limite;
}

// Dinheiro: balanco da temporada, salarios, juros, emprestimos, estadio; os salarios dos
// humanos; os jogadores observados. (Traduzido de motor/mercado/dinheiro.go e observados.go.)
public sealed partial class MercadoJogo
{
    // tipos de movimento do balanco (3:58bf) e a linha do TThreeBalances onde cada um soma
    public const int TipoBilhetes = 1, TipoVendidos = 2, TipoPremios = 3, TipoComprados = 0x15,
        TipoSalarios = 0x16, TipoBancadas = 0x17, TipoJuros = 0x18;
    const int BalancoNoSave = 0x31 - 0x16, BalancoLinhas = 7;

    static readonly byte[] OcupacaoInicial = { 0x80, 0x9a, 0x99, 0x99, 0x99, 0x19 };   // 0,6 (3:beb9, 3:c081)

    static int BalancoSlot(int tipo) => tipo switch
    {
        TipoBilhetes => 0, TipoVendidos => 1, TipoPremios => 2, TipoComprados => 3,
        TipoSalarios => 4, TipoBancadas => 5, TipoJuros => 6, _ => -1,
    };

    // 3:58bf: soma ao dinheiro (+0x71) e, nos sete tipos do balanco, a linha +0x31 + 4k
    public static void Movimento(Equipa e, int valor, int tipo)
    {
        e.Dinheiro = (uint)((int)e.Dinheiro + valor);
        int k = BalancoSlot(tipo);
        if (k < 0) return;
        var b = e.B16.AsSpan(BalancoNoSave + 4 * k);
        BinaryPrimitives.WriteInt32LittleEndian(b, BinaryPrimitives.ReadInt32LittleEndian(b) + valor);
    }

    // as 7 linhas do balanco: bilhetes, vendidos, premios, comprados, salarios, bancadas, juros
    public static int[] Balanco(Equipa e) =>
        Enumerable.Range(0, BalancoLinhas).Select(k => BinaryPrimitives.ReadInt32LittleEndian(e.B16.AsSpan(BalancoNoSave + 4 * k))).ToArray();

    public static int FolhaSalarial(Equipa e) => Rodada.Mundo.FolhaSalarial(e);
    public static void PagarSalarios(Equipa e) => Movimento(e, -FolhaSalarial(e), TipoSalarios);
    public static void PagarJuros(Equipa e) => Movimento(e, -Juros((int)e.D6D), TipoJuros);
    // 9:3865: Round(divida x 0,05), "5% do capital em divida por cada jogo"
    public static int Juros(int divida) => Arred(Xi(divida).Mul(Ext10(0x3ffa, 0xcccccccccccccccd)));

    // DS:2D7F: o valor de cada botao do TloanDlg
    public int PassoEmprestimo => BinaryPrimitives.ReadInt32LittleEndian(S.Cabecalho.G2D40.AsSpan(0x3F));

    // 9:3899 ("confianca do banco"): v = Round(forca media moral inflacao moeda 20000);
    // limite = (v div passo) passo, ou passo se der 0
    public int LimiteEmprestimo(Equipa e)
    {
        var v = Ext.DeReal48(e.Moral).Mul(Xi(ForcaMedia(e)));
        v = v.Mul(Inflacao).Mul(Xi(Moeda)).Mul(X(20000));
        int p = PassoEmprestimo;
        int r = Arred(v) / p * p;
        return r == 0 ? p : r;
    }

    // 3:665e e 3:668d
    public void PedirEmprestimo(Equipa e) { int p = PassoEmprestimo; e.Dinheiro = (uint)((int)e.Dinheiro + p); e.D6D = (uint)((int)e.D6D + p); }
    public void PagarEmprestimo(Equipa e) { int p = PassoEmprestimo; e.Dinheiro = (uint)((int)e.Dinheiro - p); e.D6D = (uint)((int)e.D6D - p); }

    // 7:3826 (TloanDlg.Execute); os botoes que a abrem exigem o add-in 0x13
    public Banco AbrirBanco(Equipa e) => new(Math.Max(LimiteEmprestimo(e), (int)e.D6D));

    // payBtn ativo se divida > 0 e dinheiro > passo (7:398b)
    public bool PodePagar(Equipa e) => (int)e.D6D > 0 && (int)e.Dinheiro > PassoEmprestimo;

    // 3:3cd3-3dd6 (equipa da CPU, so com o add-in 0x13): paga enquanto sobra; pede so se a
    // condicao contraria a da entrada vale (no original a CPU nunca pede)
    public void EmprestimoCPU(Equipa e, int folha)
    {
        int p = PassoEmprestimo;
        while ((int)e.D6D > 0 && (int)e.Dinheiro - folha - Juros((int)e.D6D) > p) PagarEmprestimo(e);
        if (Juros((int)e.D6D) + folha > (int)e.Dinheiro)
        {
            int lim = LimiteEmprestimo(e);
            while ((int)e.D6D < lim && Juros((int)e.D6D) + folha < (int)e.Dinheiro) PedirEmprestimo(e);
        }
    }

    // ---------- estadio ----------

    public static int Capacidade(Equipa e) => e.Estadio * 5000;   // 3:bf35

    // 3:c172 -> 9:37ee: k = 3 se N >= 14, 2 se N >= 6, senao 1;
    //   Max(Round(raiz(k) (inflacao 35000 moeda) / 50000) 50000, 50000)
    public int PrecoBancada(Equipa e)
    {
        long k = e.Estadio >= 14 ? 3 : e.Estadio >= 6 ? 2 : 1;
        var t = Inflacao.Mul(X(35000)).Mul(Xi(Moeda));
        int v = Arred(Xi(k).Sqrt().Mul(t).Div(X(50000))) * 50000;
        return Math.Max(v, 50000);
    }

    // 3:c081: N < 24: N++ e ocupacao 0,6
    public static bool Ampliar(Equipa e)
    {
        if (e.Estadio >= 24) return false;
        e.Estadio++;
        OcupacaoInicial.CopyTo(e.EstadioReal, 0);
        return true;
    }

    // o botao "Construir" (7:29a2): preco <= dinheiro e N < 24
    public bool PodeConstruir(Equipa e) => PrecoBancada(e) <= (int)e.Dinheiro && e.Estadio < 24;

    // 7:3036: Ampliar e cobra o preco calculado DEPOIS de aumentar N (ao passar de 5 para 6
    // ou de 13 para 14 a janela mostra o preco da faixa antiga e cobra o da nova)
    public void ConstruirBancada(Equipa e)
    {
        Ampliar(e);
        Movimento(e, -PrecoBancada(e), TipoBancadas);
    }

    // 3:c030
    public static bool QuerAmpliar(Equipa e) => Ext.DeReal48(e.EstadioReal).Cmp(C08) >= 0 && e.Estadio < 24;

    // 3:3dd6-3e54
    public void EstadioCPU(Equipa e, int folha)
    {
        if (!QuerAmpliar(e)) return;
        if (PrecoBancada(e) + folha * 2 < (int)e.Dinheiro)
        {
            Ampliar(e);
            Movimento(e, -PrecoBancada(e), TipoBancadas);
        }
    }

    // ---------- add-ins (registo) ----------

    // TAddInsList (DS:29B4): ativo se o id <= DS:29DE (nivel de registo; 0x64 registado)
    public const int AddInPlantelNoConvite = 0x0B, AddInGestaoTreinadores = 0x0E, AddInBalanco = 0x11,
        AddInCompraDirecta = 0x10, AddInEmprestimo = 0x13, AddInFerias = 0x28, AddInTodosDeFerias = 0x3C,
        AddInPontuacaoTreinador = 0x51;

    public static bool AddInAtivo(int nivel, int id) => id <= nivel;

    // ---------- salarios ----------

    // 3:66dc: o aumento que o jogador pede,
    //   novo = Round(s ((jornada 0,2) / R + 1) 1,1 / 50 / moeda) 50 moeda
    public int SalarioPedido(Equipa e, Jogador p)
    {
        var s = Xi(Salario(p, Ext.Bytes(e.B79)));
        var f = Xi(Jornada).Mul(Ext10(0x3ffc, 0xcccccccccccccccd)).Div(X(Regras.Jornadas)).Add(X(1));
        var v = s.Mul(f).Mul(Ext10(0x3fff, 0x8ccccccccccccccd));
        return Arred(v.Div(X(50)).Div(Xi(Moeda))) * 50 * Moeda;
    }

    // 3:66dc: SalarioPedido e TaskMoreSalaryDlg no modo 0 (+1 se nao pode vender). Ao
    // aceitar, +0x31 = novo e +0x16 = 1 (7:363e).
    public bool PedidoAumento(Equipa e, Jogador p, Interacao? it) => DialogoSalario(e, p, SalarioPedido(e, p), 0, it);

    bool DialogoSalario(Equipa e, Jogador p, int novo, int modo, Interacao? it)
    {
        if (ImpedimentoVenda(e, p) != 0) modo++;
        bool ok = it?.PedirAumento?.Invoke(e, p, novo, modo) ?? true;
        if (ok) { p.Salario = (uint)novo; p.B14[2] = 1; }
        return ok;
    }

    // 3:5b37 com preco base 0 (pedido de salario recusado, dividas)
    bool Leiloar(Equipa e, Jogador p, Interacao? it) => Leiloar(e.ID, p.ID, 0, it?.Lance);

    // TprepareGameDlg.ChangeSalaryBtnClick (4:3f87): o salario ainda nao foi revisto
    public static bool PodeMudarSalario(Jogador p) => p.B14[2] == 0;

    // 4:470c (newSalaryBtnClick): o humano propoe um salario. Com s = Salario: se proposta >
    // 0,8 s, aceita. Senao exige Round(s 1,25 / 50 / moeda) 50 moeda (modo 2, ou 3). Aceito:
    // +0x16 = 1 e o salario. Recusado: leilao com preco 0. Devolve se ficou com o novo.
    public bool ProporSalario(Equipa e, Jogador p, int proposta, Interacao? it)
    {
        int s = Salario(p, Ext.Bytes(e.B79));
        int novo = proposta;
        bool ok = true;
        if (Xi(s).Mul(C08).Cmp(Xi(proposta)) >= 0)
        {
            novo = Arred(Xi(s).Mul(X(1.25)).Div(X(50)).Div(Xi(Moeda))) * 50 * Moeda;
            ok = DialogoSalario(e, p, novo, 2, it);
        }
        if (ok) { p.B14[2] = 1; p.Salario = (uint)novo; return true; }
        Leiloar(e, p, it);
        return false;
    }

    // 3:6a08: a equipa deixa de ter humano: cada salario vira Min(salario, Salario) e o
    // dinheiro Max(dinheiro, 0)
    public void SalariosSemHumano(Equipa e)
    {
        var vc = Ext.Bytes(e.B79);
        foreach (var p in e.Jogadores)
        {
            int s = Salario(p, vc);
            if (s < (int)p.Salario) p.Salario = (uint)s;
        }
        if ((int)e.Dinheiro < 0) e.Dinheiro = 0;
    }

    // ---------- observados (a lista +0x1F de cada treinador; no save, Ligacoes) ----------

    // com mais de 15, ObservarJogador abre a janela dos observados (3:8cbe -> 3:8d4e)
    public const int LimiteObservados = 15;

    public static List<ushort> Observados(Jogo98 s, ushort treinador) =>
        s.Ligacoes.Find(l => l.Equipa == treinador)?.Outras ?? new List<ushort>();

    // 3:8d7c
    public static bool Observado(Jogo98 s, ushort treinador, ushort jogador) => Observados(s, treinador).Contains(jogador);

    // 3:8cbe: acrescenta no fim (mesmo que ja esteja); true se passou do limite
    public static bool ObservarJogador(Jogo98 s, ushort treinador, ushort jogador)
    {
        var l = new List<ushort>(Observados(s, treinador)) { jogador };
        DefinirObservados(s, treinador, l);
        return l.Count > LimiteObservados;
    }

    // 3:8d18: tira a 1a ocorrencia
    public static void DesobservarJogador(Jogo98 s, ushort treinador, ushort jogador)
    {
        var l = new List<ushort>(Observados(s, treinador));
        l.Remove(jogador);
        DefinirObservados(s, treinador, l);
    }

    public static void LimparObservados(Jogo98 s, ushort treinador) => DefinirObservados(s, treinador, new List<ushort>());

    // refaz Ligacoes como o original grava: so listas nao vazias, na ordem dos treinadores
    static void DefinirObservados(Jogo98 s, ushort treinador, List<ushort> l)
    {
        var listas = new Dictionary<ushort, List<ushort>>();
        foreach (var g in s.Ligacoes) listas[g.Equipa] = g.Outras;
        listas[treinador] = l;
        s.Ligacoes.Clear();
        foreach (var t in s.Treinadores)
            if (listas.TryGetValue(t.ID, out var o) && o.Count > 0) s.Ligacoes.Add(new Ligacao { Equipa = t.ID, Outras = o });
    }
}
