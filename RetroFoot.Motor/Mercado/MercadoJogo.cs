using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Mercado.Constantes;
using static RetroFoot.Motor.Rodada.X87;
using TRegras = RetroFoot.Motor.Regras.Regras;
using Chicotada = RetroFoot.Motor.Rodada.Chicotada;

namespace RetroFoot.Motor.Mercado;

// o registro da moeda escolhida (101 bytes em DS:2D40, gravado no save como
// Cabecalho.G2D40): um dos 5 da tabela DS:0062 (Escudos, Euros, Dolares, Reais, Libras)
public sealed record Moeda(string Nome, string Sufixo, string Prefixo, int D3F, int D43, int Fator, short W4B, short W4D,
    int PremioCampeao, int PremioTaca, int PremioMarcador, Ext Arredonda)
{
    public static Moeda DoSave(Jogo98 s)
    {
        var r = s.Cabecalho.G2D40;
        string Str(int o) => Encoding.Latin1.GetString(r, o + 1, Math.Min((int)r[o], 20));
        int I32(int o) => BinaryPrimitives.ReadInt32LittleEndian(r.AsSpan(o));
        short I16(int o) => BinaryPrimitives.ReadInt16LittleEndian(r.AsSpan(o));
        return new Moeda(Str(0), Str(0x15), Str(0x2A), I32(0x3F), I32(0x43), I32(0x47), I16(0x4B), I16(0x4D),
            I32(0x4F), I32(0x53), I32(0x57), Ext.Bytes(r.AsSpan(0x5B, 10)));
    }
}

// o lance de uma equipa humana no leilao (a caixa textHumanOffer, em milhares). Ok = false
// quando o texto nao e um numero (conta como 0). Chamada de novo enquanto o lance x 1000
// passar do dinheiro da equipa.
public delegate (int Milhares, bool Ok) LanceHumano(Equipa comprador, Equipa vendedor, Jogador p);

// o que o original pergunta ao humano (janelas). Campos null: o padrao de cada um.
public sealed class Interacao
{
    // TaskMoreSalaryDlg.Execute (7:330e): modo 0 Sim/Nao, 2 "Nem pensar! Exijo ...!"; +1
    // quando o jogador nao pode ser vendido (so OK = Sim). true = aceitou. null = aceita.
    public Func<Equipa, Jogador, int, int, bool>? PedirAumento;
    // o lance das equipas humanas nos leiloes. null = nao dao lance.
    public LanceHumano? Lance;
    // TinviteCoachDlg (6:3de2). null = aceita.
    public Func<Treinador, Equipa, bool>? Convite;
    // DS:2B34: 0 nenhuma, 1 so com humanos, 2 todas
    public int MostrarChicotadas;
    // DS:2716, mostrada e limpa por 5:025b
    public List<Chicotada> Chicotadas = new();
}

// Compra e venda de jogadores, leiloes, propostas e transferencias da CPU, dinheiro
// (emprestimos, salarios, estadio) e treinadores, descompilados do jogo classico (98.003).
// Mapa dos objetos em memoria usado nos comentarios:
//   Equipa: +0x0A jogadores, +0x0E treinador, +0x30 eliminada da Taca, +0x5B moral,
//     +0x61 eventos (<= 50), +0x71 dinheiro, +0x79 valor do clube (Extended), +0xC7
//     jogadores a chegar, +0xC9 estrangeiros a chegar.
//   Jogador: +0x04 id, +0x0A forca, +0x14 golos na epoca, +0x16 salario ja revisto,
//     +0x31 salario, +0x36 equipa de destino (transferencia pendente).
//   Treinador: +0x07 humano, +0x12 eventos (<= 100), +0x16 campeonatos, +0x18 tacas.
// (Traduzido de motor/mercado.)
public sealed partial class MercadoJogo
{
    public Jogo98 S;
    public Aleatorio G;
    // o fator da moeda (DS:2D87): Reais, Dolares = 5, Euros, Libras = 4, Escudos = 1
    public int Moeda;
    // CTRGROUP/BOSMAN.TXE e PLOP.TXE (DS:29C4, DS:29C0); null = os originais
    public List<string>? Bosman, PLOP;
    public TRegras Regras;

    public MercadoJogo(Jogo98 s, Aleatorio g)
    {
        S = s; G = g;
        Moeda = Mercado.Moeda.DoSave(s).Fator;
        Regras = TRegras.DoSave(s, TRegras.Original);
    }

    internal MercadoJogo(Jogo98 s, Aleatorio g, int moeda, TRegras? regras = null)
    {
        S = s; G = g; Moeda = moeda;
        Regras = regras ?? TRegras.Original;
    }

    // DS:2B46 (Extended)
    public Ext Inflacao => Ext.Bytes(S.Cabecalho.Real2B46);
    // DS:2B40, gravada em G271C
    public short Jornada => (short)S.Cabecalho.G271C;

    static Ext Ln(Ext x) => Fpu.LnExato(x);
    static Ext Power(Ext b, Ext e) => Fpu.ExpExato(Ln(b).Mul(e));
    static Ext Real48(Ext x) => Rodada.X87.Real48(x);
    static bool Maior(Ext a, Ext b) => a.Cmp(b) > 0;
    static bool Zero(Ext a) => a.Mant == 0;

    public static string Texto(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(Cifra.TextoEFT(b).Texto);

    // ---------- buscas (3:d55c) ----------

    public static Equipa? Equipa(Jogo98 s, ushort id) => s.Equipas.Find(e => e.ID == id);
    public static Treinador? Treinador(Jogo98 s, ushort id) => s.Treinadores.Find(t => t.ID == id);
    public static Equipa? EquipaDoTreinador(Jogo98 s, ushort id) => s.Equipas.Find(e => e.Treinador == id);

    public static (Equipa? E, int K) EquipaDoJogador(Jogo98 s, ushort id)
    {
        foreach (var e in s.Equipas)
        {
            int k = e.Jogadores.FindIndex(j => j.ID == id);
            if (k >= 0) return (e, k);
        }
        return (null, -1);
    }

    public static (int D, int K) Divisao(Jogo98 s, ushort id)
    {
        for (int d = 0; d < s.Divisoes.Count; d++)
        {
            int k = s.Divisoes[d].Equipas.IndexOf(id);
            if (k >= 0) return (d, k);
        }
        return (-1, -1);
    }

    // 14:06e8: acrescenta no fim e, se a lista tem limite e passou dele, tira do comeco
    public static void AcrescentarLimitado<T>(List<T> l, T v, int limite)
    {
        l.Add(v);
        while (limite > 0 && l.Count > limite) l.RemoveAt(0);
    }

    public const int LimiteEventosTreinador = 100, LimiteEventosEquipa = 50;

    // ---------- jogadores ----------

    // 12:0a2f: 0 mesmo pais; 1 os dois na lista Bosman ou os dois na PLOP; 2 estrangeiro
    public byte Estrangeiro(string paisEquipa, string paisJogador)
    {
        if (paisEquipa == paisJogador) return 0;
        IReadOnlyCollection<string> b = (IReadOnlyCollection<string>?)Bosman ?? Rodada.Mundo.BosmanOriginal;
        IReadOnlyCollection<string> p = (IReadOnlyCollection<string>?)PLOP ?? Rodada.Mundo.PLOPOriginal;
        if (b.Contains(paisEquipa) && b.Contains(paisJogador)) return 1;
        if (p.Contains(paisEquipa) && p.Contains(paisJogador)) return 1;
        return 2;
    }

    // 3:7ff4: o salario que o jogador pede numa equipa com valor do clube vc (+0x79):
    //   t = raiz(vc) (moeda inflacao) 35; v = Power(1000; 0,04) (Power(forca; 1,04) t)
    //   s = Round(v / 50 / moeda) 50 moeda, em [50 moeda; 50000 moeda]; lesionado nao desce
    public int Salario(Jogador p, Ext vc)
    {
        var m = Xi(Moeda);
        var t = vc.Sqrt().Mul(m.Mul(Inflacao)).Mul(X(35));
        t = Power(Xi(p.Forca), C1_04).Mul(t);
        t = Power(X(1000), Ext10(0x3ffa, 0xa3d70a3d70a3d700)).Mul(t);
        int s = Arred(t.Div(X(50)).Div(m)) * 50 * Moeda;
        if (50 * Moeda > s) s = 50 * Moeda;
        else if (50000 * Moeda < s) s = 50000 * Moeda;
        if ((short)p.JogosLesao > 0 && (int)p.Salario > s) s = (int)p.Salario;
        return s;
    }

    // 3:8156: grava o salario, zera +0x10 e marca o salario como revisto (+0x16 = 1)
    public static void MudarSalario(Jogador p, int s)
    {
        p.Salario = (uint)s;
        p.B0C[4] = 0; p.B0C[5] = 0;
        p.B14[2] = 1;
    }

    // 3:37b4: no fim da lista, conta a posicao, classifica (+0x35) e conta estrangeiros
    // (+0x87) e total (+0x88)
    public void AcrescentarJogador(Equipa e, Jogador p)
    {
        e.B83[p.Posicao]++;
        p.B35 = Estrangeiro(Texto(e.Pais), Texto(p.Pais));
        if (p.B35 == 2) e.B83[4]++;
        e.B83[5]++;
        e.Jogadores.Add(p);
    }

    // 3:383c: tira o jogador k e desconta os contadores
    public static Jogador TirarJogador(Equipa e, int k)
    {
        var p = e.Jogadores[k];
        e.Jogadores.RemoveAt(k);
        e.B83[p.Posicao]--;
        if (p.B35 == 2) e.B83[4]--;
        e.B83[5]--;
        return p;
    }

    // 9:371a: Round(Power(forca; 1,04) (moeda inflacao) ((1 - comp/5 0,2 - lesao/10 0,2) +
    // nota/10 0,2) 2000)
    public int ValorDoJogador(Jogador p)
    {
        var v = Power(Xi(p.Forca), C1_04).Mul(Xi(Moeda).Mul(Inflacao));
        var a = X(1).Sub(Xi((short)p.Comportamento).Div(X(5)).Mul(C0_2));
        a = a.Sub(Xi((short)p.Lesao).Div(X(10)).Mul(C0_2));
        a = a.Add(Xi((short)p.Nota).Div(X(10)).Mul(C0_2));
        return Arred(v.Mul(a).Mul(X(2000)));
    }

    // motivos de ImpedimentoVenda (3:5a6a; textos em DS:1528 + 71 x motivo)
    public const int PodeVender = 0, NaoVendeMaisJogadores = 1, JaVendido = 2, SoNaProximaEpoca = 3,
        SoNoProximoJogo = 4, UltimoGuardaRedes = 5, PoucosDeCampo = 6;

    // 3:5a6a(equipa, jogador)
    public static int ImpedimentoVenda(Equipa e, Jogador p)
    {
        if (e.B83[5] <= 11 || e.Jogadores.Count <= 11) return 1;
        if ((short)p.W36 > 0) return 2;
        if (p.B14[2] != 0) return 3;
        if (p.B0C[6] != 0) return 4;
        if (p.Posicao == 0 && e.B83[0] <= 1) return 5;
        if (p.Posicao != 0 && e.B83[5] - e.B83[0] <= 10) return 6;
        return 0;
    }

    // 3:4ec3
    public static short ForcaMedia(Equipa e) => Rodada.Mundo.MediaForca(e);

    // 3:5bb1: quanto a equipa quer o jogador (Extended)
    //   f = ForcaMedia(e); k = f div 10
    //   x = forca / (f+5) (ideal[k][pos]+1) / (e.pos+1) ideal[k][total] / (e.total+1)
    //   x = raiz(raiz(x)) Power(f; 0,1); x 1,5 se e goleiro e a equipa so tem um
    //   x y: 0,05 estrangeiros + 1,5 (mesmo pais), + 1,15 (Bosman/PLOP), 1,0 (estrangeiro);
    //   y x 1,1 com estrela
    public Ext Interesse(Equipa e, Jogador p)
    {
        short f = ForcaMedia(e);
        short k = (short)(f / 10);
        int pos = p.Posicao;
        var x = Xi(p.Forca).Div(Xi(f + 5));
        x = x.Mul(Xi(Tabelas.Ideal(k, pos) + 1));
        x = x.Div(Xi(e.B83[pos] + 1));
        x = x.Mul(Xi(Tabelas.Ideal(k, 5)));
        x = x.Div(Xi(e.B83[5] + 1));
        x = x.Sqrt().Sqrt();
        x = Power(Xi(f), C01).Mul(x);
        if (p.Posicao == 0 && e.B83[0] == 1) x = x.Mul(X(1.5));
        var y = Estrangeiro(Texto(e.Pais), Texto(p.Pais)) switch
        {
            0 => Xi(e.B83[4]).Mul(C005).Add(X(1.5)),
            1 => Xi(e.B83[4]).Mul(C005).Add(C115),
            _ => X(1),
        };
        if (p.Estrela != 0) y = y.Mul(C11);
        return x.Mul(y);
    }

    // 3:5de1: quanto a equipa quer manter o jogador (0 sem jogador); c5 = +0xC5
    //   r = raiz(raiz(5 / (comp+1))) (ideal[k][pos]/(e.pos+1) ideal[k][total]/(e.total+1)
    //       forca / (c5+1)), x 1,5 se nacional, x 1,2 se Bosman/PLOP
    public static Ext Permanencia(Equipa e, Jogador? p, short c5)
    {
        if (p == null) return X(0);
        short k = (short)(c5 / 10);
        int pos = p.Posicao;
        var x = X(5).Div(Xi((short)p.Comportamento + 1)).Sqrt().Sqrt();
        var y = Xi(Tabelas.Ideal(k, pos)).Div(Xi(e.B83[pos] + 1));
        var zz = Xi(Tabelas.Ideal(k, 5)).Div(Xi(e.B83[5] + 1));
        var w = y.Mul(zz).Mul(Xi(p.Forca));
        w = w.Div(Xi(c5 + 1));
        var r = x.Mul(w);
        if (p.B35 == 0) r = r.Mul(X(1.5));
        else if (p.B35 == 1) r = r.Mul(C12);
        return r;
    }

    // 3:5f77: quanto a equipa da CPU oferece pelo jogador num leilao; extra = salario que o
    // jogador pediria. 0 = nao quer.
    public int Oferta(Equipa e, int extra, Jogador p)
    {
        short f = ForcaMedia(e);
        if (Tabelas.Ideal((short)(f / 10), p.Posicao) == e.B83[p.Posicao]) return 0;
        var v = Xi(ValorDoJogador(p));
        var q = Real48(v.Mul(Interesse(e, p)));
        int s = FolhaSalarial(e);
        var din = Xi((int)e.Dinheiro);
        var razao = Real48(din.Sub(q).Div(Xi(s + 1 + extra)));
        if (razao.Cmp(X(3)) < 0) q = Real48(Xi((int)e.Dinheiro - (s + extra) * 3));
        else if (razao.Cmp(X(5)) > 0) q = Real48(Ln(razao).Mul(q).Div(C16));
        if (s > 100)
        {
            var d0 = din.Sub(q);
            if (d0.Cmp(Xi(s * 10)) > 0 && d0.Cmp(X(1)) > 0)
            {
                var t = Ln(d0.Div(Xi(s)).Sub(X(9)));
                t = t.Div(X(10)).Add(X(1));
                q = Real48(t.Mul(q));
            }
        }
        // 3:626f: com lesao ou suspensao o codigo divide o resultado (ainda 0) por
        // (Ln(jogos) 0,1 + 1): nao muda nada
        var d = din.Sub(q);
        int a = Arred(d.Sub(Xi(s * 5)));
        int b = Arred(q);
        return Math.Max(Math.Min(a, b), 0);
    }

    // 3:633d: a equipa c pode comprar o jogador p da equipa v? total + a chegar < limite; com
    // 5 ou mais estrangeiros, o jogador nao pode ser estrangeiro para ela. Humano nao compra
    // de humano.
    public bool PodeComprar(Equipa c, Equipa v, Jogador p)
    {
        bool ok = (short)(c.B83[5] + (short)c.WC7) < Regras.Limite;
        if (ok && (short)(c.B83[4] + (short)c.WC9) >= 5) ok = Estrangeiro(Texto(c.Pais), Texto(p.Pais)) != 2;
        var tc = Treinador(S, c.Treinador); var tv = Treinador(S, v.Treinador);
        if (tc != null && tv != null && tc.Humano != 0 && tv.Humano != 0) ok = false;
        return ok;
    }

    // 3:63fa: quanto a equipa v (da CPU) pede pelo jogador; c = quem compra (null = ninguem);
    // c5 = +0xC5 de v. 0 = nao vende.
    //   f = Permanencia (Real48); f > 1,95 -> 0; preco = Round(f (valor 3,5))
    //   comprador do mesmo pais: Round((preco + 30000) 1,6); Bosman/PLOP: Round((preco +
    //   15000) 1,3); arredonda para baixo a 100.000 / 50.000 / 25.000 / 10.000 / 1.000
    public int PrecoDeVenda(Equipa v, Equipa? c, Jogador p, short c5)
    {
        if (ImpedimentoVenda(v, p) != 0) return 0;
        if (Treinador(S, v.Treinador) is { } t && t.Humano != 0) return 0;
        var f = Real48(Permanencia(v, p, c5));
        if (C195.Cmp(f) < 0) return 0;
        int preco = Arred(f.Mul(Xi(ValorDoJogador(p)).Mul(X(3.5))));
        if (c != null)
            switch (Estrangeiro(Texto(c.Pais), Texto(p.Pais)))
            {
                case 0: preco = Arred(Xi(preco + 30000).Mul(C16)); break;
                case 1: preco = Arred(Xi(preco + 15000).Mul(C13)); break;
            }
        if (preco >= 2000000) return preco / 100000 * 100000;
        if (preco >= 1000000) return preco / 50000 * 50000;
        if (preco > 500000) return preco / 25000 * 25000;
        if (preco > 100000) return preco / 10000 * 10000;
        return preco / 1000 * 1000;
    }

    // (RetroFoot) o valor de mercado mostrado ao tecnico: o preco de venda da CPU a um comprador
    // do mesmo pais (valor x 3,5 x 1,6) sem a permanencia (o quanto o clube quer segurar o
    // jogador), arredondado nas mesmas faixas. Em temporadas simuladas os leiloes pagam em
    // mediana perto disso (SoakTestes.PrecosDeMercado)
    public int ValorDeMercado(Jogador p) => Arredondar(Arred(Xi(ValorDoJogador(p)).Mul(X(5.6))));

    static int Arredondar(int preco) =>
        preco >= 2000000 ? preco / 100000 * 100000 : preco >= 1000000 ? preco / 50000 * 50000
        : preco > 500000 ? preco / 25000 * 25000 : preco > 100000 ? preco / 10000 * 10000 : preco / 1000 * 1000;

    // 10:1939 (UpdateBuyBtn): o preco para a equipa humana c, ou 0 com o Comprar desligado
    public int PrecoParaComprar(Equipa? c, Equipa v, Jogador p)
    {
        if (c == null) return 0;
        int preco = PrecoDeVenda(v, c, p, ForcaMedia(v));
        if (preco <= 0 || preco >= (int)c.Dinheiro || !PodeComprar(c, v, p)) return 0;
        return preco;
    }

    public const int LimiteTransferencias = 50;

    // um TLastTransfer (VMT 3:061f) na lista DS:2712 (limite 50); tipo 1 compra direta, 0 leilao
    void RegistrarTransferencia(byte tipo, Jogador p, Equipa comprador, int preco)
    {
        byte h = (byte)(HumanoAtivo(Treinador(S, comprador.Treinador)) ? 1 : 0);
        AcrescentarLimitado(S.Transferencias, new Transferencia { B04 = tipo, B05 = h, W06 = p.ID, W08 = p.Forca, W0A = comprador.ID, D0C = (uint)preco }, LimiteTransferencias);
    }

    // 10:1d66 (buyBtnClick): a equipa humana c compra o jogador k de v. So com o jogo
    // registado. O jogador fica em v ate as transferencias pendentes (5:0aae):
    //   +0x36 = id de c; 3:383c tira-o de v e volta a po-lo no fim da lista; v recebe o
    //   preco, c paga; TLastTransfer tipo 1; c.+0xC7++ (e +0xC9 se estrangeiro); sai dos
    //   observados do treinador de c
    public void Comprar(Equipa c, Equipa v, int k, int preco, bool registado)
    {
        if (!registado) return;
        v.Jogadores[k].W36 = c.ID;
        var p = TirarJogador(v, k);
        v.Jogadores.Add(p);
        Movimento(v, preco, TipoVendidos);
        Movimento(c, -preco, TipoComprados);
        RegistrarTransferencia(1, p, c, preco);
        c.WC7++;
        if (Estrangeiro(Texto(c.Pais), Texto(p.Pais)) == 2) c.WC9++;
        DesobservarJogador(S, c.Treinador, p.ID);
    }

    // a ordem da lista global DS:26FE logo depois de carregar o save
    public static List<ushort> OrdemDeCarga(Jogo98 s) => s.Equipas.SelectMany(e => e.Jogadores.Select(j => j.ID)).ToList();

    // 5:0aae: para cada jogador com +0x36 > 0 (na ordem DS:26FE): sai da equipa onde esta
    // (sem descontar contadores: a compra ja os descontou), MudarSalario(Salario no
    // destino), AcrescentarJogador(destino), +0x36 = 0, destino.+0xC7-- (e +0xC9)
    public void ExecutarTransferenciasPendentes(List<ushort>? ordem = null)
    {
        foreach (var id in ordem ?? OrdemDeCarga(S))
        {
            var (e, k) = EquipaDoJogador(S, id);
            if (e == null || (short)e.Jogadores[k].W36 <= 0) continue;
            var p = e.Jogadores[k];
            e.Jogadores.RemoveAt(k);
            var d = Equipa(S, p.W36);
            if (d == null) continue;   // o original usaria um ponteiro nulo
            MudarSalario(p, Salario(p, Ext.Bytes(d.B79)));
            p.W36 = 0;
            AcrescentarJogador(d, p);
            d.WC7--;
            if (Estrangeiro(Texto(d.Pais), Texto(p.Pais)) == 2) d.WC9--;
        }
    }

    // a parte de 3:6e48 que mexe no +0x79 das equipas da divisao d:
    //   m = soma do dinheiro das equipas sem humano / D1F, no minimo 0,1
    //   +0x79 = +0x79 0,9 + (1 - 0,9) m   (14:115e)
    public void AtualizarValorDoClube(int d)
    {
        var div = S.Divisoes[d];
        int soma = 0;
        foreach (var id in div.Equipas)
        {
            var e = Equipa(S, id)!;
            if (Treinador(S, e.Treinador) is not { Humano: not 0 }) soma += (int)e.Dinheiro;
        }
        var m = Xi(soma).Div(Xi((int)div.D1F));
        if (C01.Cmp(m) > 0) m = C01;
        foreach (var id in div.Equipas)
        {
            var e = Equipa(S, id)!;
            var vc = Ext.Bytes(e.B79);
            vc = vc.Mul(C09).Add(X(1).Sub(C09).Mul(m));
            vc.Bytes().CopyTo(e.B79, 0);
        }
    }
}
