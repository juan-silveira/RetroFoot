using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using RetroFoot.Motor.Save;
using Xunit;
using Xunit.Abstractions;
using R = RetroFoot.Motor.Regras.Regras;
using Dados = RetroFoot.Motor.NovoJogo.Dados;
using Equipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.Motor.Testes;

public class NovoJogoTestes(ITestOutputHelper saida)
{
    static byte[] Decifrar(byte[] p) => Cifra.TextoEFT(p).Texto;
    static string Txt(byte[] p) => Encoding.Latin1.GetString(Decifrar(p));

    static Dados DadosDoJogo() => Dados.Ler(Dados0());
    static string Dados0() => Arquivos.Raiz() ?? throw new InvalidOperationException("jogo original não encontrado");

    // um jogo com todos os paises (mais de 128 equipas: o OK tira vagas por sorteio) e dois humanos
    static (Estado, SelecaoPaises) JogoNovo()
    {
        var d = DadosDoJogo();
        var sel = d.ContarEquipas();
        sel.Todos();
        sel.Confirmar(new Aleatorio(0x12345678));
        var g = new Aleatorio(0x2A0B1307);
        var e = Criacao.Criar(d, new Opcoes { Moeda = Tabelas.MoedaReais, Ano = 2026, Humanos = new() { "Teste", "", "Outro" } }, g);
        e.IniciarTemporada();
        e.PrepararJornada(1, g);
        return (e, sel);
    }

    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    static string J(object? o) => JsonSerializer.Serialize(o, Json);

    [Fact]
    public void GravarELer()
    {
        var (e, _) = JogoNovo();
        var s = e.Save(Exportar.DataHora(new DateTime(2026, 10, 11, 9, 41, 0)));
        var b = s.Gravar();
        var s2 = Jogo98.Ler(b);
        Assert.True(s2.Gravar().AsSpan().SequenceEqual(b));
        Assert.Equal(J(s), J(s2));
        var t1 = Decifrar(s.Cabecalho.Texto);
        var txt = Encoding.Latin1.GetString(Decifrar(new[] { (byte)t1.Length }.Concat(t1).ToArray()));
        Assert.Equal("Teste e Outro\r10/11/2026 09:41", txt);
    }

    [Fact]
    public void Estrutura()
    {
        var (e, sel) = JogoNovo();
        var s = e.Save("x");
        Assert.Equal(SelecaoPaises.MaximoEquipas, sel.Escolhidas);
        Assert.Equal(SelecaoPaises.MaximoEquipas, s.Equipas.Count);
        var vistos = new HashSet<ushort>();
        ushort maior = 0;
        void Marcar(ushort id) { Assert.True(vistos.Add(id), $"ID {id} repetido"); maior = Math.Max(maior, id); }
        foreach (var a in s.Arbitros) Marcar(a.ID);
        foreach (var c in s.Treinadores) Marcar(c.ID);
        var porID = new Dictionary<ushort, Equipa>();
        foreach (var q in s.Equipas)
        {
            Marcar(q.ID);
            porID[q.ID] = q;
            foreach (var j in q.Jogadores)
            {
                Marcar(j.ID);
                Assert.True(j.Forca >= 1 && j.Forca <= 50 && j.Salario % 250 == 0, $"jogador {j.ID}: força {j.Forca} salário {j.Salario}");
            }
        }
        Assert.Equal(maior, vistos.Count);
        Assert.Equal(maior + 1, s.Cabecalho.G2722);
        Assert.Equal(s.Equipas.Count + 2, s.Treinadores.Count);
        int[] tam = { 8, 8, 8, 8, s.Equipas.Count - 32 };
        string[] nomes = { "1\xaa Divis\xe3o", "2\xaa Divis\xe3o", "3\xaa Divis\xe3o", "4\xaa Divis\xe3o", "Distrital" };
        for (int i = 0; i < s.Divisoes.Count; i++)
        {
            var d = s.Divisoes[i];
            Assert.Equal(tam[i], d.Equipas.Count);
            Assert.Equal(nomes[i], Encoding.Latin1.GetString(d.Nome, 1, d.Nome[0]));
            Assert.Equal(52 - 14 * i, (short)d.W04);
            Assert.Equal(d.D1F, (uint)d.Equipas.Sum(id => (long)porID[id].Dinheiro));
        }
        foreach (var c in s.Treinadores.Where(c => c.Humano == 1))
        {
            var id = c.Eventos[0].W09;
            Assert.True(porID[id].Treinador == c.ID && s.Divisoes[3].Equipas.Contains(id), $"humano {Txt(c.Nome)} fora da 4ª divisão");
        }
        for (int i = 0; i < 4; i++)
            foreach (var id in s.Divisoes[i].Equipas)
            {
                var q = porID[id];
                Assert.Equal(14, q.Calendario.Count);
                for (int j = 0; j < q.Calendario.Count; j++)
                {
                    var c = q.Calendario[j];
                    Assert.True(c.W04 == j + 1 && s.Divisoes[i].Equipas.Contains(c.W08) && c.W08 != id);
                    var oc = porID[c.W08].Calendario[j];
                    Assert.True(oc.W08 == id && oc.W0E != c.W0E, $"calendário não simétrico na jornada {j + 1}");
                }
            }
        Assert.Equal(20, s.Jogos.Count);
        for (int k = 0; k < s.Jogos.Count; k++)
        {
            var jg = s.Jogos[k];
            if (k % 5 == 0) { Assert.True(jg.Taca == 2 && jg.Casa == 0); continue; }
            var c = porID[jg.Casa].Calendario[0];
            Assert.True(c.W08 == jg.Fora && c.W0E == 0 && jg.Arbitro != 0 && jg.D40 != 0, $"jogo {k}");
        }
        Assert.Equal(2, Reais.Ext80(s.Cabecalho.Real2B46));
        Assert.Equal(1, s.Cabecalho.G271C);
    }

    // ---------------------------------------------------------------- saves reais
    static bool InicioDeJogo(Jogo98 s) =>
        s.Cabecalho.G271C == 1 && s.Campeoes.Count == 0 && s.Transferencias.Count == 0 &&
        Reais.Ext80(s.Cabecalho.Real2B46) == 2 && s.Divisoes.Count == 5 &&
        s.Divisoes[0].Equipas.Count >= 8 && s.Divisoes[0].Equipas.Count == s.Divisoes[3].Equipas.Count;

    // a semente do Randomize de seg05:3c2a (hora do DOS) pela forca dos jogadores, o 1o
    // sorteio depois dele (Random(7) por jogador, seg05:39c3)
    static (uint Semente, bool Ok) SementeDoSave(Jogo98 s)
    {
        int n = s.Divisoes[0].Equipas.Count;
        var esp = new List<int>();
        for (int i = 0; i < s.Equipas.Count; i++)
        {
            var x = Ext.Int(i).Div(Ext.F32(4)).Div(Ext.F32(n));
            int v = (int)Ext.F32(1).Sub(x).Mul(Ext.F32(50)).Round();
            v = i <= 4 * n ? Math.Max(v - 1, 5) : 1;
            foreach (var j in s.Equipas[i].Jogadores.OrderBy(j => j.ID))
                esp.Add(j.Forca > 1 && j.Forca < 50 ? v + 3 - j.Forca : -1);
        }
        var alvo = esp.Take(80).ToArray();
        for (uint hh = 0; hh < 24; hh++)
            for (uint mm = 0; mm < 60; mm++)
                for (uint ss = 0; ss < 60; ss++)
                    for (uint cs = 0; cs < 100; cs++)
                    {
                        uint sd = (ss << 8 | cs) << 16 | hh << 8 | mm;
                        var g = new Aleatorio(sd);
                        bool ok = true;
                        foreach (var x in alvo)
                            if (g.Random(7) is int r && x >= 0 && x != r) { ok = false; break; }
                        if (ok) return (sd, true);
                    }
        return (0, false);
    }

    static readonly string[] BosmanOriginal = "POR ESP FRA ITA ALE AUT GRE ING ESC WAL ILN IRL DIN BEL HOL LUX FIN SUE".Split(' ');

    [Fact]
    public void SavesReais()
    {
        int n = 0;
        foreach (var arq in Arquivos.Saves())
        {
            Jogo98 s;
            try { s = Jogo98.Ler(File.ReadAllBytes(arq)); } catch { continue; }
            // so saves do jogo original (8 equipas): os do launcher (ELIFnn.EXE) tem mais coisas
            if (!InicioDeJogo(s) || s.Divisoes[0].Equipas.Count != 8) continue;
            var pasta = Path.GetDirectoryName(Path.GetDirectoryName(arq))!;
            if (!Directory.Exists(Dados.Achar(pasta, "EQUIPAS"))) pasta = Dados0();
            var d = Dados.Ler(pasta);
            var motivo = PrepararDados(d, s);
            if (motivo.Length > 0) { saida.WriteLine($"{Path.GetFileName(arq)}: {motivo}"); continue; }
            var (sd, ok) = SementeDoSave(s);
            Assert.True(ok, $"{arq}: semente não encontrada");
            Jogo98? achou = null;
            string info = "";
            foreach (var bosman in new[] { false, true })
            {
                if (bosman) d.Bosman = BosmanOriginal.Select(c => d.Paises.FirstOrDefault(p => p.Codigo == c)).Where(p => p != null).ToList()!;
                for (int pinturas = 0; pinturas <= 20 && achou == null; pinturas++)
                {
                    var novo = Recriar(d, s, sd, pinturas);
                    if (novo != null && Iguais(novo, s, null)) { achou = novo; info = $"semente {sd:x8}, {pinturas} pinturas, Bosman original {bosman}"; }
                }
                if (achou != null) break;
            }
            Assert.True(achou != null, $"{Path.GetFileName(arq)}: nenhuma combinação reproduz o save (semente {sd:x8})");
            Iguais(achou!, s, saida);
            n++;
            saida.WriteLine($"{Path.GetFileName(arq)}: igual ({info})");
        }
        saida.WriteLine($"{n} saves de início de jogo recriados iguais");
    }

    static string PrepararDados(Dados d, Jogo98 s)
    {
        var treinador = s.Treinadores.ToDictionary(c => c.ID, c => Decifrar(c.Nome));
        var porNome = new Dictionary<string, Arquivo>();
        foreach (var a in d.Equipas) porNome[Encoding.Latin1.GetString(a.Eft.NomeCompleto) + "|" + Encoding.Latin1.GetString(a.Eft.NomeAbreviado)] = a;
        var lista = new List<Arquivo>();
        foreach (var q in s.Equipas)
        {
            if (!porNome.TryGetValue(Txt(q.NomeCompleto) + "|" + Txt(q.NomeCurto), out var a)) return $"equipa {Txt(q.NomeCompleto)} não está na pasta";
            bool mudou = !treinador.TryGetValue((ushort)(q.ID + 1), out var tn) || !tn.SequenceEqual(a.Eft.Treinador) || q.Jogadores.Count != a.Eft.Jogadores.Count;
            var js = q.Jogadores.OrderBy(j => j.ID).ToList();
            for (int k = 0; !mudou && k < js.Count; k++)
                if (!Decifrar(js[k].NomeCifrado).SequenceEqual(a.Eft.Jogadores[k].Nome)) mudou = true;
            if (mudou) return $"o arquivo de {Txt(q.NomeCompleto)} mudou desde o save";
            lista.Add(a);
        }
        d.Equipas = lista;
        d.ContarEquipas().Todos();
        return "";
    }

    static Jogo98? Recriar(Dados d, Jogo98 s, uint semente, int pinturas)
    {
        var humanos = s.Treinadores.Where(c => c.Humano != 0).Select(c => Txt(c.Nome)).ToList();
        int moeda = -1;
        for (int i = 0; i < Tabelas.QuantasMoedas; i++) if (Tabelas.Moeda(i).SequenceEqual(s.Cabecalho.G2D40)) moeda = i;
        var g = new Aleatorio(semente);
        Estado e;
        try { e = Criacao.Criar(d, new Opcoes { Moeda = moeda, Ano = s.Cabecalho.Ano, Humanos = humanos, Pinturas = pinturas, Regras = R.DoSave(s, R.Original) }, g); }
        catch { return null; }
        e.IniciarTemporada();
        e.PrepararJornada(1, g);
        var novo = e.Save("");
        novo.Cabecalho.Texto = s.Cabecalho.Texto;   // a data e a hora da gravacao
        return novo;
    }

    // compara o recriado com o real (sem os efeitos de seg03:13e1 nas equipas que o jogo
    // ja preparou antes da janela do humano)
    static bool Iguais(Jogo98 a, Jogo98 r, ITestOutputHelper? t)
    {
        bool ok = true;
        void Dif(string o, object x, object y)
        {
            if (J(x) != J(y)) { ok = false; t?.WriteLine($"{o}: diferente"); }
        }
        Dif("cabeçalho", a.Cabecalho, r.Cabecalho);
        Dif("treinadores", a.Treinadores, r.Treinadores);
        Dif("árbitros", a.Arbitros, r.Arbitros);
        Dif("lista da Taça", a.OutrasEquipas, r.OutrasEquipas);
        Dif("divisões", a.Divisoes, r.Divisoes);
        Dif("jogos", a.Jogos, r.Jogos);
        if (a.Equipas.Count != r.Equipas.Count) { Dif("equipas", a.Equipas.Count, r.Equipas.Count); return false; }
        var processadas = ProcessadasAntes(r);
        for (int i = 0; i < r.Equipas.Count; i++)
        {
            var x = a.Equipas[i]; var y = r.Equipas[i];
            if (processadas.Contains(y.ID))
            {
                x = Normalizar(x); y = Normalizar(y);
            }
            Dif($"equipa {y.ID} ({Txt(y.NomeCompleto)})", x, y);
        }
        return ok;
    }

    // copia a equipa com os jogadores na ordem dos IDs, +0x0C dos jogadores e +0x4D zerados
    static Equipa Normalizar(Equipa e)
    {
        var c = JsonSerializer.Deserialize<Equipa>(J(e), Json)!;
        c.Jogadores = c.Jogadores.OrderBy(j => j.ID).ToList();
        foreach (var j in c.Jogadores) j.B0C = new byte[8];
        c.B4D = new byte[14];
        return c;
    }

    static HashSet<ushort> ProcessadasAntes(Jogo98 s)
    {
        var m = new HashSet<ushort>();
        foreach (var j in s.Jogos)
            foreach (var id in new[] { j.Casa, j.Fora })
            {
                if (id == 0) continue;
                m.Add(id);
                if (id == s.Cabecalho.G271E) return m;
            }
        return m;
    }
}
