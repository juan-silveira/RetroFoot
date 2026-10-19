using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using static RetroFoot.Motor.NovoJogo.Tabelas;

namespace RetroFoot.Motor.NovoJogo;

// O inicio da temporada e a 1a jornada do jogo novo. (Traduzido de motor/novojogo/temporada.go.)
public static class TemporadaNova
{
    // seg05:0002 (comeco da temporada, com DS:0x271E = 0): a Taca recebe todas as equipas
    // (+0x30 de cada uma vai a 0); cada divisao prepara as suas (seg03:6dc3 -> seg03:38a9):
    // +0xA5 <- +0x89, +0x89 <- +0x31 (28 bytes), +0x16..+0x4C zerados; nas 4 divisoes, o
    // lugar de cada jornada (+0x22) e o calendario (seg03:1d20); no Distrital o calendario e
    // esvaziado; +0x14 (3 bytes) de cada jogador vai a 0; DS:0x271E = 0, 0x271C = 1, 0x271A = 0
    public static void IniciarTemporada(this Estado e)
    {
        e.NaTaca = new List<Equipa>(e.Equipas);
        foreach (var t in e.Equipas) t.B16[0x30 - 0x16] = 0;
        for (int i = 0; i < e.Divisoes.Count; i++)
        {
            var d = e.Divisoes[i];
            for (int p = 0; p < d.Equipas.Count; p++)
            {
                var t = d.Equipas[p];
                t.BA5 = (byte[])t.B89.Clone();
                Array.Copy(t.B16, 0x31 - 0x16, t.B89, 0, t.B89.Length);
                t.B16 = new byte[55];
                if (i < 4)
                {
                    var (l, ok) = e.Regras.LugaresIniciais(p);
                    if (ok) Array.Copy(l, 0, t.B16, 0x22 - 0x16, l.Length);
                    t.Calendario = Calendario(e, d.Equipas, p);
                }
                else t.Calendario = new List<EntradaCalendario>();
            }
        }
        foreach (var j in e.Jogadores) j.B14 = new byte[3];
        e.Vez = 0; e.Inicio = 1; e.Taca271A = 0;
    }

    // TCalendar.Montar (seg03:1d20): uma entrada por jornada
    static List<EntradaCalendario> Calendario(Estado e, List<Equipa> equipas, int p)
    {
        var r = new List<EntradaCalendario>();
        for (int j = 1; j <= e.Regras.Jornadas; j++)
        {
            var (a, fora, ok) = e.Regras.Adversario(p, j);
            if (!ok) break;
            r.Add(new EntradaCalendario { Adversario = equipas[a].ID, Jornada = (ushort)j, Fora = (ushort)(fora ? 1 : 0) });
        }
        return r;
    }

    // a parte de seg05:0c41 que monta a jornada j (com DS:0x271E = 0): esvazia a lista de
    // jogos (seg05:01f6), cria os jogos de cada uma das 4 divisoes (seg03:6e48) e ordena as
    // divisoes pela classificacao (seg06:0378). Sorteios: em cada divisao, 3 x (arbitros)
    // Random(arbitros) para embaralhar os arbitros e um Random real por jogo (publico).
    public static void PrepararJornada(this Estado e, int j, Aleatorio g)
    {
        e.Jornada = j;
        e.Partidas = new List<Partida>();
        for (int i = 0; i < 4; i++) JogosDaDivisao(e, e.Divisoes[i], j, g);
        for (int i = 0; i < 4; i++) Bolha(e.Divisoes[i].Equipas, AntesNaClassificacao);   // seg06:0378
        e.Vez = PrimeiroHumano(e);
    }

    // a equipa da 1a partida (mandante antes do visitante) com treinador humano
    static ushort PrimeiroHumano(Estado e)
    {
        foreach (var p in e.Partidas)
            foreach (var t in new[] { p.Casa, p.Fora })
                if (t != null && t.Treinador.Humano != 0 && t.Treinador.B06 == 0) return t.ID;
        return 0;
    }

    // seg03:6e48: r = dinheiro das equipas do computador / +0x1F da divisao (>= 0,1); o
    // valor do clube vira 0,9 x valor + 0,1 x r (seg14:115e); ordena pelo lugar da jornada
    // (seg11:1a61) e cria a linha de titulo e os jogos "ja Jornada" (seg03:0875)
    static void JogosDaDivisao(Estado e, Divisao d, int j, Aleatorio g)
    {
        int soma = d.Equipas.Where(t => t.Treinador.Humano == 0).Sum(t => t.Dinheiro);
        var r = Ext.Int(soma).Div(Ext.Int(d.DinheiroTotal));
        if (Ext01.Cmp(r) > 0) r = Ext01;
        foreach (var t in d.Equipas) t.ValorClube = t.ValorClube.Mul(Ext09).Add(ExtUm.Sub(Ext09).Mul(r));
        Bolha(d.Equipas, (a, b) => e.Regras.Lugar(a.B16.AsSpan(0x22 - 0x16), j) < e.Regras.Lugar(b.B16.AsSpan(0x22 - 0x16), j));
        var nome = Encoding.ASCII.GetBytes(j.ToString()).Concat(SufixoJornada).ToArray();
        var titulo = d.Nome.AsSpan(1, d.Nome[0]).ToArray();
        CriarJogos(e, d.Equipas, 0, titulo, nome, 0, g);
    }

    // seg14:0e0d: para i de 1 a n-1 e k de n-1 ate i: se nao antes(l[k-1], l[k]), troca
    internal static void Bolha<T>(List<T> l, Func<T, T, bool> antes)
    {
        int n = l.Count;
        for (int i = 1; i <= n - 1; i++)
            for (int k = n - 1; k >= i; k--)
                if (!antes(l[k - 1], l[k])) (l[k - 1], l[k]) = (l[k], l[k - 1]);
    }

    // seg11:1ab6: por pontos (+0x20), saldo (+0x1C - +0x1E) e gols marcados (+0x1C);
    // empate em tudo = antes
    internal static bool AntesNaClassificacao(Equipa a, Equipa b)
    {
        short W(Equipa t, int o) => BinaryPrimitives.ReadInt16LittleEndian(t.B16.AsSpan(o - 0x16));
        int s = (short)(W(a, 0x20) - W(b, 0x20));
        if (s != 0) return s > 0;
        s = (short)((short)(W(a, 0x1C) - W(a, 0x1E)) - (short)(W(b, 0x1C) - W(b, 0x1E)));
        if (s != 0) return s > 0;
        return (short)(W(a, 0x1C) - W(b, 0x1C)) >= 0;
    }

    // seg03:0875 (tambem usada pela Taca, com taca = 1): embaralha os arbitros (seg14:0f68);
    // media das forcas medias (seg03:4ec3) e do valor do clube -> preco do bilhete
    // (seg09:35e1); se houver titulo, uma linha com Taca = 2; um jogo por par (k, k+1)
    static void CriarJogos(Estado e, List<Equipa> l, int n, byte[] titulo, byte[] nome, byte taca, Aleatorio g)
    {
        Embaralhar(e.Arbitros, g);
        short somaMedias = 0;
        var soma = default(Ext);
        foreach (var t in l)
        {
            somaMedias += ForcaMedia(t);
            soma = soma.Add(t.ValorClube);
        }
        short media = (short)Ext.Int(somaMedias).Div(Ext.Int(l.Count)).Round();
        var valor = soma.Div(Ext.Int(l.Count));
        var bilhete = PrecoBilhete(e, valor, taca, media);
        if (n == 0) n = l.Count / 2;
        if (titulo.Length > 0) e.Partidas.Add(NovaPartida(e, 2, null, null, titulo, bilhete, g));
        for (int k = 0; k <= 2 * n - 1; k += 2) e.Partidas.Add(NovaPartida(e, taca, l[k], l[k + 1], nome, bilhete, g));
    }

    // seg14:0f68: 3 x n vezes, tira o elemento Random(n) e o poe no fim
    internal static void Embaralhar<T>(List<T> l, Aleatorio g)
    {
        int n = l.Count;
        for (int k = 1; k <= 3 * n; k++)
        {
            int r = g.Random(n);
            var x = l[r];
            l.RemoveAt(r);
            l.Add(x);
        }
    }

    // seg03:4ec3: Round(soma das forcas / jogadores)
    internal static short ForcaMedia(Equipa t)
    {
        short s = 0;
        foreach (var j in t.Jogadores) s += j.Forca;
        return (short)Ext.Int(s).Div(Ext.Int(t.Jogadores.Count)).Round();
    }

    // seg09:35e1:
    //   p = Round(moeda x inflacao / raiz(max(valor, 0,1)) x media / 5 x (1 + 0,5 x Taca)) / 2
    //   no minimo 0,5 x moeda; com passo da moeda (DS:0x2D9B) > 1, p = Round(p/passo) x passo
    static Ext PrecoBilhete(Estado e, Ext valor, byte taca, short media)
    {
        if (Ext01.Cmp(valor) > 0) valor = Ext01;
        var moeda = Ext.Int(e.FatorMoeda());
        var v = moeda.Mul(e.Inflacao).Div(valor.Sqrt());
        v = v.Mul(Ext.Int(media)).Div(Ext.F32(5));
        long t = taca == 1 ? 1 : 0;
        v = v.Mul(Ext.Int(t).Mul(Ext.F32(0.5f)).Add(Ext.F32(1)));
        var p = Ext.Int(v.Round()).Div(Ext.F32(2));
        var minimo = moeda.Mul(Ext.F32(0.5f));
        if (p.Cmp(minimo) < 0) p = minimo;
        var passo = e.PassoBilhete();
        if (passo.Cmp(Ext.F32(1)) > 0) p = Ext.Int(p.Div(passo).Round()).Mul(passo);
        return p;
    }

    // TGame.Create (seg03:8db2): publico (seg03:b0a1) e, se houver duas equipas, arbitro
    // (seg03:1a66) e a influencia dele (seg03:c95c)
    static Partida NovaPartida(Estado e, byte taca, Equipa? casa, Equipa? fora, byte[] nome, Ext bilhete, Aleatorio g)
    {
        var p = new Partida { Taca = taca, Casa = casa, Fora = fora, Bilhete = bilhete };
        Criacao.Guardar(p.Nome, nome);
        p.Publico = Publico(p, g);
        if (casa != null && fora != null)
        {
            p.Arbitro = EscolherArbitro(e, casa, fora);
            if (p.Arbitro != null)
            {
                p.W4C = (ushort)(Influencia(p.Arbitro, casa) + 2);
                p.W4E = (ushort)Influencia(p.Arbitro, fora);
            }
        }
        if (taca == 1) throw new NotImplementedException("jogos da Taça não implementados aqui (seg03:1df4)");
        return p;
    }

    // seg03:b0a1:
    //   s = 3 x forcaMedia(mandante) + forcaMedia(visitante) + Random x 3
    //   x = Real(s x raiz(moral mandante) x raiz(moral visitante) x 200)
    //   publico = Round(x^1,04 x (1 + 0,3 x Taca)), limitado a capacidade (seg03:c0c0)
    static int Publico(Partida p, Aleatorio g)
    {
        if (p.Casa == null || p.Fora == null || p.Taca == 2) return 0;
        int s = 3 * ForcaMedia(p.Casa) + ForcaMedia(p.Fora);
        var t = Ext.F64(g.Real()).Mul(Ext.F32(3)).Add(Ext.Int(s));
        var u = Ext.DeReal48(p.Casa.Moral).Sqrt().Mul(t);
        var w = Ext.DeReal48(p.Fora.Moral).Sqrt().Mul(u).Mul(Ext.F32(200));
        w = Ext.DeReal48(w.Real48());
        long k = p.Taca == 1 ? 1 : 0;
        var f = Ext.Int(k).Mul(Ext03).Add(Ext.F32(1));
        int v = (int)Ext.Power(w, Ext104).Mul(f).Round();
        return Lotacao(p.Casa, v);
    }

    // seg03:c0c0: limita o publico a capacidade (5000 x N) e atualiza o real do estadio:
    // r = 0,9 r + 0,1 publico/(capacidade + 1), em Real de 6 bytes (seg14:10d1)
    static int Lotacao(Equipa t, int publico)
    {
        int cap = t.Estadio * 5000;
        int pub = Math.Min(publico, cap);
        var ocupacao = Ext.Int(pub).Div(Ext.Int(cap + 1)).Real48();
        t.EstadioReal = MediaReal(t.EstadioReal, Real48De09, ocupacao);
        return pub;
    }

    // seg14:10d1: x := Real(x a + (1 - a) b)
    internal static byte[] MediaReal(byte[] x, byte[] a, byte[] b)
    {
        var ra = Ext.DeReal48(a);
        var t1 = Ext.DeReal48(x).Mul(ra);
        var t2 = ExtUm.Sub(ra).Mul(Ext.DeReal48(b));
        return t1.Add(t2).Real48();
    }

    // seg03:1a66: 1a passada, o 1o disponivel do mesmo pais das duas equipas; 2a, o 1o de
    // um pais diferente das duas. O escolhido fica indisponivel. Sem nenhum, sem arbitro.
    static Arbitro? EscolherArbitro(Estado e, Equipa casa, Equipa fora)
    {
        for (int passada = 0; passada < 2; passada++)
            foreach (var a in e.Arbitros)
            {
                if (a.Disponivel == 0) continue;
                bool ok = passada == 0 ? a.Pais == casa.Pais && a.Pais == fora.Pais : a.Pais != casa.Pais && a.Pais != fora.Pais;
                if (ok) { a.Disponivel = 0; return a; }
            }
        return null;
    }

    // seg03:c95c: soma, da posicao n ate 1 (o maior dos dois tamanhos), letra do nome do
    // arbitro - letra do nome curto da equipa; |soma mod 11|, mais 3 se o arbitro for do pais
    // da equipa. Depois do fim de cada texto o jogo le a memoria do registro (zeros no
    // arbitro; na equipa, os zeros do campo de 21 bytes, as cores, o pais e o nivel).
    static int Influencia(Arbitro a, Equipa t)
    {
        var mem = new byte[64];   // TTeamAttr a partir de +0x2D
        mem[0] = (byte)t.NomeCurto.Length;
        Array.Copy(t.NomeCurto, 0, mem, 1, Math.Min(20, t.NomeCurto.Length));
        Array.Copy(t.CorLetra, 0, mem, 21, 4);
        Array.Copy(t.CorFundo, 0, mem, 25, 4);
        mem[29] = (byte)t.Pais.Length;
        var pais = Encoding.Latin1.GetBytes(t.Pais);
        Array.Copy(pais, 0, mem, 30, Math.Min(3, pais.Length));
        mem[33] = t.Nivel;
        var nome = new byte[64];
        nome[0] = (byte)a.Nome.Length;
        Array.Copy(a.Nome, 0, nome, 1, Math.Min(40, a.Nome.Length));
        int n = Math.Max(nome[0], mem[0]);
        short soma = 0;
        for (int i = n; i >= 1; i--) soma += (short)(nome[i] - mem[i]);
        int v = soma % 11;
        if (v < 0) v = -v;
        if (a.Pais == t.Pais) v += 3;
        return v;
    }
}
