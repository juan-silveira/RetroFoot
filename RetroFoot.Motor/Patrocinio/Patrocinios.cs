using System;
using System.Collections.Generic;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Patrocinio.Contrato;
using static RetroFoot.Motor.Rodada.X87;

namespace RetroFoot.Motor.Patrocinio;

// uma proposta ao humano (os 32 bytes do quadro do patch): o nome da marca (string[24]
// inteiro), as epocas (ou os jogos, no pontual), o valor (por epoca ou por jogo), o tipo e o
// numero da marca. Multa: o que sai do caixa se aceitar (a troca de patrocinador no meio da
// epoca), calculada na hora de perguntar.
public sealed class Proposta
{
    public const byte Master = 0, Renovacao = 1, Pontual = 2;

    public byte[] NomeBruto = new byte[25];
    public byte Quantidade;      // +25: epocas (1 a 3) ou jogos (1 a 5)
    public int Valor;            // +26
    public byte Tipo;            // +30
    public byte Marca;           // +31
    public int Multa;
    public Marca? Registro;      // a marca lida (para o logo); null se a leitura falhou

    public string Nome => System.Text.Encoding.Latin1.GetString(NomeBruto, 1, Math.Min((int)NomeBruto[0], 24));
}

// Os patrocinadores do launcher (launcher/tools/divisoes/patrocinio.asm e
// patrocinio.py; docs internos do launcher, "Patrocinadores e a janela de Sim/Nao"), com a
// mesma ordem de sorteios do Random do jogo. Os pontos de entrada sao os do patch:
//  - InicioDaEpoca: no lugar de "call far NewSeason" (seg05:11CE), depois dela: os amarelos
//    acumulados zeram (com ou sem a opcao) e, com a opcao, a distribuicao e as propostas;
//  - FimDaEpoca: no lugar de "call far premios" (seg05:0AA8), antes dos Premios: cada
//    contrato paga a epoca;
//  - Rodada: antes de cada jornada do campeonato (seg05:11FF, "call 0C41"): o jogo do
//    patrocinio pontual, a proposta pontual e a de master no meio da epoca.
public sealed class Patrocinios
{
    readonly Mundo m;
    public Marcas? Marcas;
    // a janela de Sim/Nao: true = aceitou. null = recusa (nao ha quem responda)
    public Func<Proposta, bool>? Perguntar;
    // PATROC_TESTE=1 do patch: propostas em toda rodada (chances de 100%, momento pontual
    // sempre) e os amarelos zerando em toda rodada
    public bool Teste;

    public Patrocinios(Mundo m, Marcas? marcas)
    {
        this.m = m;
        Marcas = marcas;
    }

    Aleatorio R => m.R;
    Jogo98 S => m.S;
    // RODADAS do patch: 2 x (N - 1)
    int Rodadas => m.Regras.Jornadas;

    // ------------------------------------------------------------ listas
    // lista_div: as equipas das divisoes 0 a 4 (o patch percorre sempre 5)
    IEnumerable<(int D, int K, Equipa E)> Equipas()
    {
        for (int d = 0; d < 5 && d < S.Divisoes.Count; d++)
        {
            var l = S.Divisoes[d].Equipas;
            for (int k = 0; k < l.Count; k++)
                if (m.Equipa(l[k]) is { } e) yield return (d, k, e);
        }
    }

    // humano (seg03:4351): treinador com +7 <> 0 e +6 = 0
    bool Humano(Equipa e) => MercadoJogo.HumanoAtivo(m.TreinadorDa(e));

    // time_humano: a primeira equipa humana, a divisao dela e o lugar na lista da divisao
    // (a classificacao: 0 = lider)
    (Equipa E, int Div, int Lugar)? TimeHumano()
    {
        foreach (var (d, k, e) in Equipas())
            if (Humano(e)) return (e, d, k);
        return null;
    }

    // ------------------------------------------------------------ contas
    // orcamento/cota_div: COTA = cota x inflacao / 2, no milhar (frndint ao par)
    internal int CotaInflacionada(Marca mc) =>
        (int)(Xi(mc.Cota).Mul(m.Inflacao).Mul(X(0.5)).Div(X(1000)).Round() * 1000);

    // dx:ax x cx (sem sinal, 32 bits) e o shr 1: a quota da divisao d
    internal static int Quota(int cota, int d) => (int)(unchecked((uint)cota * (uint)(ushort)(5 - d)) >> 1);

    // orcamento: verba x inflacao / 2 (fistp ao par), menos a reserva dos pontuais (DISP x pct
    // / 100, sem sinal, 32 bits) e menos o que ja esta contratado com a marca b
    internal int Disponivel(Marca mc, int b)
    {
        int disp = (int)Xi(mc.Verba).Mul(m.Inflacao).Mul(X(0.5)).Round();
        uint res = unchecked((uint)disp * mc.Reserva) / 100;
        disp = unchecked(disp - (int)res);
        foreach (var (_, _, e) in Equipas())
            if (e.MarcaMaster() == (byte)b) disp = unchecked(disp - e.ValorMaster());
        return disp;
    }

    // pais_ok: modo 0 e 2 aceitam todos; 1 so os da lista; 3 todos menos os da lista. O pais
    // da equipa: os 3 bytes do string[3] (+0x4B..+0x4D do registro do .EFT)
    internal static bool PaisOk(Marca mc, Equipa e)
    {
        byte modo = mc.Modo;
        if (modo != Marca.Exclusivo && modo != Marca.Exceto) return true;
        Span<byte> p = stackalloc byte[3];
        if (e.Pais.Length > 0)
        {
            var t = Cifra.TextoEFT(e.Pais).Texto;
            for (int i = 0; i < 3 && i < t.Length; i++) p[i] = t[i];
        }
        int n = mc.NumPaises;
        if (n == 0) return false;   // jcxz .nao (nos dois modos)
        for (int k = 0; k < n; k++)
            if (mc.Pais(k).SequenceEqual(p)) return modo != Marca.Exceto;
        return modo == Marca.Exceto;
    }

    // ------------------------------------------------------------ inicio da epoca
    // (depois da NewSeason) os amarelos e, com a opcao, a distribuicao
    public static void ZerarAmarelos(Mundo m)
    {
        for (int d = 0; d < 5 && d < m.S.Divisoes.Count; d++)
            foreach (var id in m.S.Divisoes[d].Equipas)
                if (m.Equipa(id) is { } e)
                    foreach (var j in e.Jogadores)
                    {
                        // mov word [es:di+0x25], 0: os acumulados e a marca do jogo do ultimo
                        Campos.SetJgW(j, Campos.JgAmarelosAcumulados, 0);
                    }
    }

    // distribuir: cada marca (na ordem do arquivo) fecha contratos com as equipas sem
    // patrocinador das 2 divisoes do nivel dela; com o humano guarda uma proposta (ate 3). No
    // fim, as propostas.
    public void Distribuir()
    {
        if (Marcas == null) return;   // abrir falhou: nem as propostas
        var ofs = new List<Proposta>();
        if (Marcas.Assinatura)
        {
            int n = Marcas.Quantidade;
            for (int b = 1; b <= n; b++)
            {
                var mc = Marcas.Registro(b);
                if (mc == null) break;
                int disp = Disponivel(mc, b);
                int cota = CotaInflacionada(mc);
                int divmax = mc.DivisaoMaxima;
                int dv0 = divmax - 2 < 0 ? 0 : divmax - 2;
                bool acabou = false;
                for (int dv = dv0; dv < divmax && !acabou; dv++)
                {
                    int quota = Quota(cota, dv);
                    var lista = dv < S.Divisoes.Count ? S.Divisoes[dv].Equipas : new List<ushort>();
                    for (int k = 0; k < lista.Count; k++)
                    {
                        if (disp < quota) { acabou = true; break; }
                        if (m.Equipa(lista[k]) is not { } e) continue;
                        if (!PaisOk(mc, e)) continue;
                        if (e.MarcaMaster() != 0) continue;
                        if (!Humano(e) && R.Random(100) >= 60) continue;
                        Contratar(mc, b, e, quota, ref disp, ofs);
                    }
                }
            }
        }
        Propostas(ofs);
    }

    // contratar: epocas Random(3) + 1; o computador assina, o humano ganha uma proposta (a
    // renovacao, da marca do contrato que acabou, vai para a frente)
    void Contratar(Marca mc, int b, Equipa e, int quota, ref int disp, List<Proposta> ofs)
    {
        byte anos = (byte)(R.Random(3) + 1);
        if (!Humano(e))
        {
            e.SetB(MMarca, (byte)b);
            e.SetB(MAnos, anos);
            e.SetD(MValor, quota);
            disp = unchecked(disp - quota);
            return;
        }
        byte tipo = (byte)b == e.MarcaRenovacao() ? Proposta.Renovacao : Proposta.Master;
        int slot;
        if (ofs.Count < 3) { slot = ofs.Count; ofs.Add(new Proposta()); }
        else if (tipo == Proposta.Master) return;
        else slot = 2;   // cheia: a renovacao fica no lugar da ultima
        var p = ofs[slot];
        p.Valor = quota;
        p.Tipo = tipo;
        p.Marca = (byte)b;
        p.Quantidade = anos;
        mc.NomeBruto.CopyTo(p.NomeBruto);
        p.Registro = mc;
        disp = unchecked(disp - quota);   // a verba fica reservada para a proposta
        if (tipo == Proposta.Renovacao) (ofs[0], ofs[slot]) = (ofs[slot], ofs[0]);
    }

    // propostas: uma de cada vez ate o humano aceitar uma; a renovacao so vale nesta epoca
    void Propostas(List<Proposta> ofs)
    {
        foreach (var p in ofs)
            if (Pergunta(p)) break;
        if (TimeHumano() is { } h) h.E.SetB(UMarca, 0);
    }

    // perguntar: a multa (com contrato em andamento, so nas de master: epocas que faltam x
    // valor, 32 bits sem sinal), a janela e, se aceitou, a assinatura com a equipa do humano
    bool Pergunta(Proposta p)
    {
        p.Multa = 0;
        var h = TimeHumano();
        if (p.Tipo != Proposta.Pontual && h is { } t && t.E.MarcaMaster() != 0)
            p.Multa = (int)unchecked((uint)t.E.ValorMaster() * t.E.EpocasMaster());
        p.Registro ??= Marcas?.Registro(p.Marca);
        bool sim = Perguntar?.Invoke(p) ?? false;
        if (!sim) return false;
        h = TimeHumano();
        if (h is not { } th) return false;
        var e = th.E;
        if (p.Tipo == Proposta.Pontual)
        {
            e.SetB(PMarca, p.Marca);
            e.SetB(PJogos, p.Quantidade);
            e.SetD(PValor, p.Valor);
            return true;
        }
        if (p.Multa != 0) MercadoJogo.Movimento(e, unchecked(-p.Multa), MercadoJogo.TipoPremios);
        e.SetB(MMarca, p.Marca);
        e.SetB(MAnos, p.Quantidade);
        e.SetD(MValor, p.Valor);
        return true;
    }

    // ------------------------------------------------------------ fim da epoca
    // pagar: cada contrato (divisoes 0 a 4, na ordem das listas) paga a epoca na linha dos
    // Premios e conta uma epoca a menos; o do humano que acaba vira renovacao
    public void Pagar()
    {
        foreach (var (_, _, e) in Equipas())
        {
            if (e.MarcaMaster() == 0) continue;
            MercadoJogo.Movimento(e, e.ValorMaster(), MercadoJogo.TipoPremios);
            byte anos = unchecked((byte)(e.EpocasMaster() - 1));
            e.SetB(MAnos, anos);
            if (anos != 0) continue;
            if (Humano(e)) e.SetB(UMarca, e.MarcaMaster());
            e.SetB(MMarca, 0);
            e.SetD(MValor, 0);
        }
    }

    // ------------------------------------------------------------ cada rodada
    // rodada: o jogo do pontual em andamento (pago antes da jornada); senao, no momento certo,
    // a proposta pontual (25% na 1a divisao, 50% nas outras); em 1/4, 1/2 e 3/4 das jornadas,
    // a de master (70% sem patrocinador, 25% com; so maior que a atual)
    public void Rodada()
    {
        if (Teste) ZerarAmarelos(m);
        if (TimeHumano() is not { } h) return;
        var e = h.E;
        if (e.JogosPontual() != 0)
        {
            MercadoJogo.Movimento(e, e.ValorPontual(), MercadoJogo.TipoPremios);
            byte j = (byte)(e.JogosPontual() - 1);
            e.SetB(PJogos, j);
            if (j == 0)
            {
                e.SetB(PMarca, 0);
                e.SetD(PValor, 0);
            }
        }
        else if (MomentoPontual(h.E, h.Lugar))
        {
            int chance = Teste ? 100 : h.Div == 0 ? 25 : 50;
            if (R.Random(100) < chance && Sortear(true, e, h.Div) is { } mc)
            {
                var p = NovaProposta(mc.Marca, mc.B, Proposta.Pontual);
                p.Quantidade = (byte)(R.Random(5) + 1);
                // por jogo: a quota da epoca / 8 (div32 sem sinal)
                p.Valor = (int)((uint)Quota(CotaInflacionada(mc.Marca), h.Div) / 8);
                Pergunta(p);
            }
        }
        // master no meio da epoca
        int jr = m.Jornada;
        if (!Teste && jr != Rodadas / 4 && jr != Rodadas / 2 && jr != 3 * Rodadas / 4) return;
        int ch = Teste ? 100 : e.MarcaMaster() == 0 ? 70 : 25;
        if (R.Random(100) >= ch) return;
        for (int tent = 3; tent > 0; tent--)
        {
            if (Sortear(false, e, h.Div) is not { } s) continue;
            int v = Quota(CotaInflacionada(s.Marca), h.Div);
            if (e.MarcaMaster() != 0 && (uint)v <= (uint)e.ValorMaster()) continue;
            var p = NovaProposta(s.Marca, s.B, Proposta.Master);
            p.Valor = v;
            p.Quantidade = (byte)(R.Random(3) + 1);
            Pergunta(p);
            return;
        }
    }

    static Proposta NovaProposta(Marca mc, int b, byte tipo)
    {
        var p = new Proposta { Tipo = tipo, Marca = (byte)b, Registro = mc };
        mc.NomeBruto.CopyTo(p.NomeBruto);
        return p;
    }

    // momento_pontual: nas 5 ultimas jornadas (jornada >= R - 4) entre os 3 primeiros da
    // divisao; ou o humano vivo na Taca (DS:0x26F2) com 16 equipas ou menos na Taca. (Ate
    // 16/10/2026 o patch lia DS:0x26F6, a lista de TODAS as equipas, e a condicao da Taca
    // nunca valia; corrigido nos dois.)
    bool MomentoPontual(Equipa e, int lugar)
    {
        if (Teste) return true;
        if (m.Jornada >= Rodadas - 4 && (uint)lugar <= 2) return true;
        int n = S.OutrasEquipas.Count;
        if (n > 16 || n < 2) return false;
        return S.OutrasEquipas.Contains(e.ID);
    }

    // sorteia: ate 12 marcas ao acaso (Random(N) + 1) para o humano. Master: a divisao dele
    // entre (divisao maxima - 2) e (divisao maxima - 1) da marca; pontual: ate a divisao
    // maxima e com reserva. Os dois com o pais. O arquivo e o cabecalho tem de ler.
    (Marca Marca, int B)? Sortear(bool pontual, Equipa e, int div)
    {
        if (Marcas == null || !Marcas.CabecalhoLe) return null;
        int n = Marcas.Quantidade;
        if (n == 0) return null;
        for (int tent = 12; tent > 0; tent--)
        {
            int b = R.Random(n) + 1;
            var mc = Marcas.Registro(b);
            if (mc == null) continue;
            if (Serve(mc, pontual, e, div)) return (mc, b);
        }
        return null;
    }

    // marca_serve
    static bool Serve(Marca mc, bool pontual, Equipa e, int div)
    {
        int baixa = mc.DivisaoMaxima - 1;   // a divisao mais baixa em que atua (0 = 1a)
        if (div > baixa) return false;
        if (pontual) return mc.Reserva != 0 && PaisOk(mc, e);
        if (div < baixa - 1) return false;
        return PaisOk(mc, e);
    }

    // ------------------------------------------------------------ a aba e a janela
    // os textos do patch (com a traducao pt-BR do launcher, IDIOMAS/pt-BR.txt)
    public static string Texto(Proposta p, Func<long, string> extenso)
    {
        // (no idioma escolhido, Textos.T; sem traducao, o texto do launcher)
        var t = new System.Text.StringBuilder();
        string n = ((char)('0' + p.Quantidade)).ToString();
        if (p.Tipo != Proposta.Pontual)
        {
            t.Append(Textos.T(p.Quantidade == 1 ? "{0} oferece {1} por temporada, durante {2} temporada" : "{0} oferece {1} por temporada, durante {2} temporadas",
                p.Nome, extenso(p.Valor), n));
            t.Append(Textos.T(p.Tipo == Proposta.Renovacao
                ? ",\rpara renovar o contrato de patrocinador master."
                : ",\rpara ser o patrocinador master do seu time."));
            if (p.Multa != 0) t.Append(Textos.T("\rMulta pela troca de patrocinador: {0}", extenso(p.Multa)));
        }
        else
        {
            t.Append(Textos.T(p.Quantidade == 1 ? "{0} oferece {1} por jogo, durante {2} jogo" : "{0} oferece {1} por jogo, durante {2} jogos",
                p.Nome, extenso(p.Valor), n));
            t.Append(Textos.T(",\rpara estampar a marca no uniforme."));
        }
        t.Append(Textos.T("\r\rAceita?"));
        // anexar: o texto para em 250 letras
        var s = t.ToString();
        return s.Length > 250 ? s[..250] : s;
    }
}
