using System;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Mercado.Constantes;
using static RetroFoot.Motor.Rodada.X87;

namespace RetroFoot.Motor.Mercado;

// O mercado dentro da preparacao de cada equipa para a jornada (3:3e9c), e o leilao
// (TsellPlayerDlg, seg02, aberto por 3:5b37). (Traduzido de motor/mercado/cpu.go e leilao.go.)
public sealed partial class MercadoJogo
{
    // 3:3ecb-41dc. Para cada jogador (na ordem): zera +0x0C..+0x13; salta se +0x16 != 0 ou
    // jornada <= 1; s = Salario; r = s / salario atual.
    //   o treinador tem a opcao da categoria do jogador (+0x1A + [+0x35]) desligada e e
    //   humano ativo: candidato a pedir aumento: r > 1,3 e s - salario > 300 (o de maior r)
    //   senao: aumento r > 1,2 (o de maior r); reducao r < 0,8 (o de menor r)
    // O do aumento e o da reducao recebem Salario e +0x16 = 1 (sem zerar +0x10). O candidato
    // pergunta ao humano (3:66dc); se ele recusa, leilao com base 0.
    public void RevisaoDeSalarios(ushort id, short jornada, Interacao? it)
    {
        var e = Equipa(S, id)!;
        var t = Treinador(S, e.Treinador)!;
        var vc = Ext.Bytes(e.B79);
        Ext melhor1 = X(0), melhor2 = C12, melhor3 = C08;
        Jogador? sel1 = null, sel2 = null, sel3 = null;
        foreach (var p in e.Jogadores)
        {
            System.Array.Clear(p.B0C);
            if (p.B14[2] != 0 || jornada <= 1) continue;
            int s = Salario(p, vc);
            var r = Xi(s).Div(Xi((int)p.Salario));
            if (t.B1A[(sbyte)p.B35] == 0 && HumanoAtivo(t))
            {
                if (Maior(r, C13) && s - (int)p.Salario > 300 && (Maior(r, melhor1) || Zero(melhor1))) { melhor1 = r; sel1 = p; }
                continue;
            }
            if (Maior(r, melhor2) && Maior(r, C12)) { melhor2 = r; sel2 = p; }
            if (Maior(melhor3, r) && Maior(C08, r)) { melhor3 = r; sel3 = p; }
        }
        foreach (var p in new[] { sel2, sel3 })
            if (p != null) { p.Salario = (uint)Salario(p, vc); p.B14[2] = 1; }
        if (sel1 != null && !PedidoAumento(e, sel1, it)) Leiloar(e, sel1, it);
    }

    // 3:41e1-42d7: com dinheiro negativo, leiloa com base 0 o primeiro jogador que pode
    // vender (o laco calcula Permanencia mas, como o melhor comeca em 0 e para no primeiro,
    // fica sempre o primeiro vendavel). Devolve se houve jogador escolhido.
    public bool VendaPorDividas(ushort id, short c5, Interacao? it)
    {
        var e = Equipa(S, id)!;
        if ((int)e.Dinheiro >= 0) return false;
        var melhor = X(0);
        Jogador? sel = null;
        for (int i = 0; sel == null && i < e.Jogadores.Count; i++)
        {
            var p = e.Jogadores[i];
            var perm = Permanencia(e, p, c5);
            if (ImpedimentoVenda(e, p) == 0 && (Maior(melhor, perm) || Zero(melhor))) { sel = p; melhor = perm; }
        }
        if (sel == null) return false;
        Leiloar(e, sel, it);
        return true;
    }

    // o comeco de 3:3aad (equipa da CPU). Devolve o novo +0xC5 (ForcaMedia).
    //   so se VendaPorDividas nao escolheu ninguem e jornada > 1: se dinheiro 3 >= folha:
    //   Random(20); se != 0, so continua com 5 estrangeiros e total >= 21; o de menor
    //   Permanencia que pode vender (e estrangeiro, se cheio) vai a leilao com base
    //   Round(Valor DS:2B50 1,6)
    public short VendaCPU(ushort id, short jornada, bool vendeuAntes, Interacao? it)
    {
        var e = Equipa(S, id)!;
        int s = FolhaSalarial(e);
        short c5 = ForcaMedia(e);
        bool cheio = e.B83[4] == 5;
        if (vendeuAntes || jornada <= 1) return c5;
        if ((int)e.Dinheiro * 3 >= s && G.Random(20) != 0 && (!cheio || e.B83[5] < 21)) return c5;
        var melhor = X(0);
        Jogador? sel = null;
        foreach (var p in e.Jogadores)
        {
            var perm = Permanencia(e, p, c5);
            if ((Maior(melhor, perm) || Zero(melhor)) && ImpedimentoVenda(e, p) == 0 && (!cheio || p.B35 == 2)) { sel = p; melhor = perm; }
        }
        if (sel == null) return c5;
        int preco = Arred(Xi(ValorDoJogador(sel)).Mul(Ext.Bytes(S.Cabecalho.Real2B50)).Mul(C16));
        Leiloar(id, sel.ID, preco, it?.Lance);
        return c5;
    }

    // ---------- leilao ----------

    // o estado do dialogo (DS:26CE-26F0)
    public sealed class Leilao
    {
        public ushort Vendedor, Jogador, Vencedor;
        public int Base, Salario, Melhor, Licitante;

        // 2:3563: arredonda o lance para baixo aos milhares; vale se >= base e maior que o
        // melhor (ou ainda sem melhor). Empate nao troca o vencedor.
        internal void RegistrarLance(ushort equipa, int lance)
        {
            lance = lance / 1000 * 1000;
            if (lance >= Base && (lance > Melhor || Melhor == 0)) { Melhor = lance; Vencedor = equipa; }
        }
    }

    // 3:5b37 -> 2:3002: a equipa vendedor leiloa o jogador com preco base (aos milhares).
    // Devolve true se foi vendido. Ordem (Timer1Timer, 2:3649):
    //  1. salario = Salario(jogador, +0x79 do vendedor)
    //  2. embaralha DS:26F6 (3n Random(n)): muda a ordem das equipas no save
    //  3. licitam pela ordem nova, a partir do indice 1 (o indice 0 nunca licita)
    //  4. com vencedor, 2:3a10 faz a venda.
    public bool Leiloar(ushort vendedor, ushort jogador, int preco, LanceHumano? humano)
    {
        var v = Equipa(S, vendedor)!;
        var (_, k) = EquipaDoJogador(S, jogador);
        var l = new Leilao { Vendedor = vendedor, Jogador = jogador, Base = preco / 1000 * 1000, Licitante = -1 };
        l.Salario = Salario(v.Jogadores[k], Ext.Bytes(v.B79));
        bool Rodar()
        {
            Rodada.Listas.Embaralhar(G, S.Equipas);
            for (l.Licitante = 1; l.Licitante < S.Equipas.Count; l.Licitante++) Licitar(l, S.Equipas[l.Licitante], humano);
            if (l.Vencedor == 0) return false;   // "Nao houve ofertas"
            Vender(l);
            return true;
        }
        return Janela != null ? Janela(l, Rodar) : Rodar();
    }

    // a janela do leilao (TsellPlayerDlg, que o original abre em todo leilao, 3:5b37): a
    // interface mostra a janela e chama o leilao (embaralhar, licitar, vender) dentro dela,
    // devolvendo o que ele devolveu. Tem de chamar exatamente uma vez. null: sem janela.
    public Func<Leilao, Func<bool>, bool>? Janela;

    // 2:3787: nao pode ser o vendedor; PodeComprar e dinheiro >= base. CPU: Oferta. Humano,
    // pela opcao +0x1D: 0 pergunta sempre; 1 se Interesse > 1,0 (2:3885: fcomp com o single
    // 1,0 de seg02:376f, ja); outro valor nao licita.
    void Licitar(Leilao l, Equipa c, LanceHumano? humano)
    {
        if (c.ID == l.Vendedor) return;
        var v = Equipa(S, l.Vendedor)!;
        var (ve, k) = EquipaDoJogador(S, l.Jogador);
        var p = ve!.Jogadores[k];
        if (!PodeComprar(c, v, p)) return;
        if ((int)c.Dinheiro < l.Base) return;
        var t = Treinador(S, c.Treinador);
        if (!HumanoAtivo(t)) { l.RegistrarLance(c.ID, Oferta(c, l.Salario, p)); return; }
        switch (t!.B1A[3])
        {
            case 0: break;
            case 1: if (!Maior(Interesse(c, p), Ext.F32(1))) return; break;
            default: return;
        }
        if (humano == null) return;
        while (true)   // 2:3bda (OKBtnClick)
        {
            var (m, ok) = humano(c, v, p);
            if (!ok) m = 0;
            // (fora do original) o lance vazio ou 0 nao e oferta: com a base 0, contava como
            // lance valido e o humano levava o jogador de graca so por apertar Enter
            if (m <= 0) return;
            if (m * 1000 <= (int)c.Dinheiro) { l.RegistrarLance(c.ID, m * 1000); return; }
        }
    }

    // 2:3a10: MudarSalario; tira do vendedor (3:383c), poe no vencedor (3:37b4); vendedor
    // recebe, vencedor paga; sai dos observados do vencedor; TLastTransfer tipo 0
    void Vender(Leilao l)
    {
        var v = Equipa(S, l.Vendedor)!;
        var (_, k) = EquipaDoJogador(S, l.Jogador);
        MudarSalario(v.Jogadores[k], l.Salario);
        var p = TirarJogador(v, k);
        var c = Equipa(S, l.Vencedor)!;
        AcrescentarJogador(c, p);
        Movimento(v, l.Melhor, TipoVendidos);
        Movimento(c, -l.Melhor, TipoComprados);
        DesobservarJogador(S, c.Treinador, p.ID);
        RegistrarTransferencia(0, p, c, l.Melhor);
    }
}
