using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using static RetroFoot.Motor.NovoJogo.Tabelas;
using R = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.NovoJogo;

// as escolhas do jogador ao comecar um jogo
public sealed class Opcoes
{
    public int Moeda;                        // 0 Escudos, 1 Euros, 2 Dolares, 3 Reais, 4 Libras
    public int Ano;                          // o original usa o ano do computador (seg05:3e09)
    public List<string> Humanos = new();     // os 6 campos de TgetTeamsDlg; vazios sao pulados
    // quantas vezes a janela principal foi redesenhada antes do sorteio das equipas dos
    // humanos: cada pintura (seg05:27e3) chama Random(20) (0 no Wine; 2 e 3 no Windows)
    public int Pinturas;
    public R Regras;
}

public static class Criacao
{
    // o que seg05:3c2a faz depois do dialogo da moeda, e em seguida TgetTeamsDlg
    // (seg06:0e8f) e seg05:0148. g e o gerador logo depois do Randomize de seg05:3c2a.
    // Sorteios: Random(7) por jogador (forca, seg05:3aac) e Random(6) por humano (equipa,
    // seg06:0d42), repetido se a equipa ja for de outro humano.
    public static Estado Criar(Dados d, Opcoes op, Aleatorio g)
    {
        if (op.Moeda < 0 || op.Moeda >= QuantasMoedas) throw new ArgumentException("moeda inválida");
        if (op.Humanos.Count > 6) throw new ArgumentException("TgetTeamsDlg só tem 6 campos");
        var e = new Estado
        {
            ProximoID = 1,                       // seg03:db69
            Inflacao = ExtInflacao,              // seg11:3a8e
            Real2B50 = Ext2B50,
            Moeda = Moeda(op.Moeda),             // seg02:3e9f
            Regras = op.Regras,
        };
        e.Divisoes = NovasDivisoes();            // seg03:0b60
        LerArbitros(e, d);                       // seg03:15d6
        CarregarEquipas(e, d);
        e.Ano = op.Ano;                          // seg05:3e47
        Distribuir(e);                           // seg05:3b94
        DinheiroEForca(e, g);
        foreach (var dv in e.Divisoes) SomarDinheiro(dv);   // seg05:3e5b
        // seg05:3e83: sem arbitros suficientes o jogo avisa e nao continua
        if (e.Equipas.Count / 2 + 1 > e.Arbitros.Count)
            throw new InvalidOperationException($"árbitros insuficientes: {e.Arbitros.Count} para {e.Equipas.Count} equipas");
        if (e.Divisoes[3].Equipas.Count < e.Regras.N) throw new InvalidOperationException($"menos de {e.Regras.N} equipas na 4ª divisão");
        for (int k = 0; k < op.Pinturas; k++) g.Random(20);   // seg05:27e3
        foreach (var nome in op.Humanos) if (nome.Length > 0) NovoHumano(e, nome, g);   // seg06:0e8f
        HistorialTreinadores(e);                 // seg05:0148
        return e;
    }

    // TListOfDivisions.Create (seg03:0b60): cinco divisoes com W04 = 52 - 14 i e o nome
    // "Na Divisao"; a 5a recebe "Distrital" por cima (o 11o byte fica no campo)
    static List<Divisao> NovasDivisoes()
    {
        var r = new List<Divisao>();
        for (int i = 0; i < 5; i++)
        {
            var d = new Divisao { W04 = (short)(0x34 - 14 * i) };
            var nome = Encoding.ASCII.GetBytes((i + 1).ToString()).Concat(SufixoDivisao).ToArray();
            Guardar(d.Nome, nome);
            if (i == 4) Guardar(d.Nome, NomeDistrital);
            r.Add(d);
        }
        return r;
    }

    // copia uma string Pascal para um campo string[N] (25:1988): o resto fica como estava
    internal static void Guardar(byte[] campo, byte[] s)
    {
        int n = Math.Min(s.Length, campo.Length - 1);
        campo[0] = (byte)n;
        Array.Copy(s, 0, campo, 1, n);
    }

    // TListOfReferees.Create (seg03:15d6) e TReferee.Create (seg03:c6ca)
    static void LerArbitros(Estado e, Dados d)
    {
        foreach (var t in d.Arbitro)
        {
            var (cod, nome) = Dados.PartirTxe(t, 40);
            if (cod.Length == 0) continue;   // seg03:1763
            e.Arbitros.Add(new Arbitro { ID = e.NovoID(), Disponivel = 1, Nome = nome, Pais = cod });
        }
    }

    // seg05:367a: para cada nivel de 20 a 1, as equipas desse nivel cujo pais foi
    // escolhido e ainda tem vaga; a vaga cai 1 por equipa
    static void CarregarEquipas(Estado e, Dados d)
    {
        var vagas = d.Paises.ToDictionary(p => p, p => p.Vagas);
        for (int nivel = 20; nivel >= 1; nivel--)
            foreach (var a in d.Equipas)
            {
                if (a.Eft.Nivel != nivel) continue;
                var p = Dados.AcharPais(d.Paises, d.PaisDoArquivo(a.Eft.Pais));
                if (p == null || !p.Selecionado || vagas[p] <= 0) continue;
                vagas[p]--;
                NovaEquipa(e, d, a);
            }
    }

    // TTeam.Create (seg03:25e5) e TTeam.LerEFT (seg03:27fb). IDs: a equipa, o treinador,
    // os jogadores na ordem do arquivo.
    static void NovaEquipa(Estado e, Dados d, Arquivo a)
    {
        var f = a.Eft;
        var t = new Equipa
        {
            ID = e.NovoID(),
            Moral = (byte[])Real48De1.Clone(),        // seg03:26ea e seg03:2916
            ValorClube = ExtUm,                        // seg03:2814
            Estadio = 1,                               // TStadium.Create (seg03:beb9)
            EstadioReal = (byte[])Real48De06.Clone(),
        };
        t.Treinador = new Treinador { ID = e.NovoID(), B1A = new byte[] { 0, 0, 0, 1, 0 } };   // seg03:827c
        t.NomeCompleto = (byte[])f.NomeCompleto.Clone();
        t.NomeCurto = (byte[])f.NomeAbreviado.Clone();
        t.CorLetra = new[] { (byte)(f.CorLetra >> 16), (byte)(f.CorLetra >> 8), (byte)f.CorLetra, (byte)0 };
        t.CorFundo = new[] { (byte)(f.CorFundo >> 16), (byte)(f.CorFundo >> 8), (byte)f.CorFundo, (byte)0 };
        t.Pais = d.PaisDoArquivo(f.Pais);
        t.Nivel = (byte)f.Nivel;
        foreach (var fj in f.Jogadores)
        {
            var j = new Jogador { ID = e.NovoID() };   // seg03:70d9
            j.Pais = d.PaisDoArquivo(fj.Pais);
            j.NomeCifrado = Cifra.PorTextoEFT(fj.Nome); // lido cru do arquivo (seg12:12b2)
            j.Posicao = (byte)fj.Posicao;               // os 2 bytes da posicao: +0x19 e +0x1A
            j.Estrela = (byte)(fj.Posicao >> 8);
            AtributosDoNome(j);
            Adicionar(t, d, j);                         // seg03:37b4
            e.Jogadores.Add(j);
        }
        t.Treinador.Nome = (byte[])f.Treinador.Clone(); // seg12:1524
        e.Treinadores.Add(t.Treinador);
        e.Equipas.Add(t);
    }

    // seg03:7516: a soma sobre as primeiras 20 posicoes do texto cifrado, decifradas a
    // partir do tamanho ja cortado, com "*" no fim se +0x1A (a estrela) nao for 0
    static void AtributosDoNome(Jogador j)
    {
        int n = Math.Min((int)j.NomeCifrado[0], 20);
        var nome = new List<byte>(n + 1);
        byte ant = (byte)n;
        for (int i = 0; i < n; i++)
        {
            byte c = j.NomeCifrado[1 + i];
            nome.Add((byte)(c - ant));
            ant = c;
        }
        if (j.Estrela != 0 && nome.Count < 20) nome.Add((byte)'*');
        int soma = nome.Sum(c => (int)c);
        var a = Atributos.DoNome(soma, j.Posicao);
        j.Nota = (short)a.Nota; j.Lesao = (short)a.Lesao; j.Comportamento = (short)a.Comportamento;
        // seg03:760c: estrela = Nota >= 8 e posicao >= 2
        j.Estrela = (byte)(a.Nota >= 8 && j.Posicao >= 2 ? 1 : 0);
    }

    // TTeam.AdicionarJogador (seg03:37b4)
    static void Adicionar(Equipa t, Dados d, Jogador j)
    {
        t.Jogadores.Add(j);
        if (j.Posicao < 4) t.PorPosicao[j.Posicao]++;
        else throw new InvalidOperationException($"posição {j.Posicao} fora de 0..3");
        j.Estrangeiro = d.Estrangeiro(t.Pais, j.Pais);
        if (j.Estrangeiro == 2) t.Estrangeiros++;
        t.NJogadores++;
    }

    // seg05:3b94: a equipa na posicao i vai para a divisao min(i div N, 4)
    static void Distribuir(Estado e)
    {
        int n = e.Regras.N;
        for (int i = 0; i < e.Equipas.Count; i++) e.Divisoes[Math.Min(i / n, 4)].Equipas.Add(e.Equipas[i]);
    }

    // seg05:39c3. Para a equipa na posicao i:
    //   v = Round((1 - i/4/N) x 50); v = max(v - 1, 5) se i <= 4N, senao v = 1
    //   forca de cada jogador = min(max(v + 3 - Random(7), 1), 50); salario (seg03:7ff4)
    //   dinheiro = v x 50000 x moeda (tambem em +0x75); estadio = max(Round(v x 0,3), 1)
    static void DinheiroEForca(Estado e, Aleatorio g)
    {
        int moeda = e.FatorMoeda();
        int n = e.Regras.N;
        for (int i = 0; i < e.Equipas.Count; i++)
        {
            var t = e.Equipas[i];
            var x = Ext.Int(i).Div(Ext.F32(4)).Div(Ext.F32(n));
            int v = (int)ExtUm.Sub(x).Mul(Ext.F32(50)).Round();
            v = i <= 4 * n ? Math.Max(v - 1, 5) : 1;
            foreach (var j in t.Jogadores)
            {
                int f = Math.Max(v + 3 - g.Random(7), 1);
                j.Forca = (short)Math.Min(f, 50);
                j.Salario = Salario(e, j, t.ValorClube);
            }
            t.Dinheiro = v * 50000 * moeda;   // DS:0x09AE = 50000
            t.D75 = t.Dinheiro;
            t.Estadio = (byte)Math.Max((int)Ext.Int(v).Mul(Ext03).Round(), 1);
        }
    }

    // seg03:7ff4:
    //   s = raiz(valor) x (moeda x inflacao) x 35 x forca^1,04 x 1000^0,04
    //   salario = Round(s / 50 / moeda) x 50 x moeda, entre 50 x moeda e 50000 x moeda;
    //   com jogos de lesao nao fica abaixo do salario atual
    internal static int Salario(Estado e, Jogador j, Ext valor)
    {
        int moeda = e.FatorMoeda();
        var a = valor.Sqrt().Mul(Ext.Int(moeda).Mul(e.Inflacao)).Mul(Ext.F32(35));
        var b = Ext.Power(Ext.Int(j.Forca), Ext104).Mul(a);
        var c = Ext.Power(Ext1000, Ext004).Mul(b);
        int k = (int)c.Div(Ext.F32(50)).Div(Ext.Int(moeda)).Round();
        int s = k * 50 * moeda;
        int lo = moeda * 50, hi = moeda * 50000;
        if (lo > s) s = lo;
        else if (hi < s) s = hi;
        if (j.JogosLesao > 0) s = Math.Max(s, j.Salario);
        return s;
    }

    // seg03:7067: +0x1F = soma do dinheiro das equipas da divisao
    static void SomarDinheiro(Divisao d) => d.DinheiroTotal = d.Equipas.Sum(t => t.Dinheiro);

    // TgetTeamsDlg (seg06:0d0f): sorteia a posicao 7 - Random(6) (2 a 7) da 4a divisao ate
    // achar uma equipa sem treinador humano; cria um treinador humano novo e o poe nela
    static Equipa NovoHumano(Estado e, string nome, Aleatorio g)
    {
        Equipa t;
        while (true)
        {
            int p = 7 - g.Random(6);
            t = e.Divisoes[3].Equipas[p];
            if (t.Treinador.Humano == 0) break;
        }
        var n = Encoding.Latin1.GetBytes(nome);
        if (n.Length > 20) n = n[..20];
        var c = new Treinador { ID = e.NovoID(), Humano = 1, Nome = n, B1A = new byte[] { 0, 0, 0, 1, 0 } };
        t.Treinador = c;
        e.Treinadores.Add(c);
        return t;
    }

    // seg05:0148: cada equipa acrescenta ao seu treinador o evento 5 (entrou na equipa)
    static void HistorialTreinadores(Estado e)
    {
        foreach (var t in e.Equipas)
        {
            var c = t.Treinador;
            if (c.Eventos.Count < 100) c.Eventos.Add(new EventoTreinador { Tipo = 5, Ano = (uint)e.Ano, Equipa = t.ID });
        }
    }
}
