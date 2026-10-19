using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Patrocinio;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Xunit;
using Xunit.Abstractions;
using static RetroFoot.Motor.Patrocinio.Contrato;
using Equipa = RetroFoot.Motor.Save.Equipa;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes;

// Os patrocinadores do launcher (Patrocinio/). Os dados em Patrocinio/:
//  - MARCAS.TXT e MARCAS.DAT do launcher (tools/patrocinios/SPONSORS, 227 marcas);
//  - l20-2026-j1.e98: jogo novo do launcher (ELIF20, 2026-10-15) gravado na 1a jornada de 2026,
//    com a distribuicao do comeco da epoca feita pelo EXE (inflacao 2,0);
//  - l20-2027-j21.e98: jornada 21 de 2027 (contratos de duas epocas).
public class PatrocinioTestes(ITestOutputHelper saida)
{
    static string Pasta()
    {
        var d = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && d != null; i++, d = Path.GetDirectoryName(d))
        {
            var p = Path.Combine(d, "Patrocinio");
            if (File.Exists(Path.Combine(p, "MARCAS.DAT"))) return p;
        }
        throw new DirectoryNotFoundException("RetroFoot.Motor.Testes/Patrocinio");
    }

    static Marcas Marcas() => new(File.ReadAllBytes(Path.Combine(Pasta(), "MARCAS.DAT")));
    static Jogo98 Save(string nome) => Jogo98.Ler(File.ReadAllBytes(Path.Combine(Pasta(), nome)));

    static Jogo98 J3()
    {
        var d = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && d != null; i++, d = Path.GetDirectoryName(d))
        {
            var p = Path.Combine(d, "Oraculo", "j3-entrada.e98");
            if (File.Exists(p)) return Jogo98.Ler(File.ReadAllBytes(p));
        }
        throw new FileNotFoundException("Oraculo/j3-entrada.e98");
    }

    // ------------------------------------------------------------ MARCAS.DAT
    // gerar_dat.py: a leitura do MARCAS.TXT e o registro de 80 bytes
    static List<(string Nome, string Logo, int Tier, int Verba, int Cota, int DivMax, int Reserva, List<string> Paises, int Modo)> LerTxt(string arq)
    {
        var r = new List<(string, string, int, int, int, int, int, List<string>, int)>();
        foreach (var l0 in File.ReadAllLines(arq, Encoding.Latin1))
        {
            var l = l0.Trim();
            if (l.Length == 0 || l.StartsWith('#')) continue;
            var c = l.Split(';').Select(x => x.Trim()).ToList();
            while (c.Count < 9) c.Add("");
            var paises = c[7] is "" or "*" ? new List<string>() : c[7].Split(',').Select(p => p.Trim().ToUpperInvariant()).Where(p => p.Length > 0).ToList();
            string modo = paises.Count > 0 ? c[8].ToLowerInvariant() : "";
            if (paises.Count > 0 && modo == "") modo = "exclusivo";
            int m = modo switch { "" => 0, "exclusivo" => 1, "preferido" => 2, "exceto" => 3, _ => throw new Exception(modo) };
            r.Add((c[0].ToUpperInvariant(), c[1].ToUpperInvariant(), int.Parse(c[2]), int.Parse(c[3]), int.Parse(c[4]), int.Parse(c[5]),
                c[6] == "" ? 0 : int.Parse(c[6]), paises, m));
        }
        return r;
    }

    [Fact]
    public void MarcasDatIgualAoGeradoDoTxt()
    {
        var txt = LerTxt(Path.Combine(Pasta(), "MARCAS.TXT"));
        var ms = Marcas();
        Assert.True(ms.Assinatura);
        Assert.Equal(txt.Count, ms.Quantidade);
        // o gerar() do gerar_dat.py, byte a byte
        var o = new List<byte>(Encoding.ASCII.GetBytes("PAT1")) { 1, 0, (byte)txt.Count, (byte)(txt.Count >> 8) };
        static byte[] PStr(string s, int n)
        {
            var b = Encoding.Latin1.GetBytes(s).Take(n).ToArray();
            return new[] { (byte)b.Length }.Concat(b).Concat(new byte[n - b.Length]).ToArray();
        }
        foreach (var m in txt)
        {
            var r = new List<byte>();
            r.AddRange(PStr(m.Nome, 24)); r.AddRange(PStr(m.Logo, 12));
            r.Add((byte)m.Tier); r.Add((byte)m.DivMax);
            r.AddRange(BitConverter.GetBytes(m.Verba)); r.AddRange(BitConverter.GetBytes(m.Cota));
            r.Add((byte)m.Reserva); r.Add((byte)m.Modo); r.Add((byte)m.Paises.Count);
            var p = m.Paises.SelectMany(x => Encoding.Latin1.GetBytes(x)).ToList();
            while (p.Count < 24) p.Add(0);
            r.AddRange(p);
            while (r.Count < Marca.Tamanho) r.Add(0);
            o.AddRange(r);
        }
        Assert.Equal(o.ToArray(), ms.Dados);
        // e os campos lidos
        for (int b = 1; b <= ms.Quantidade; b++)
        {
            var mc = ms.Registro(b)!;
            var t = txt[b - 1];
            Assert.Equal(t.Nome, mc.Nome);
            Assert.Equal(t.Logo, mc.Logo);
            Assert.Equal(t.Verba, mc.Verba);
            Assert.Equal(t.Cota, mc.Cota);
            Assert.Equal(t.DivMax, mc.DivisaoMaxima);
            Assert.Equal(t.Reserva, mc.Reserva);
            Assert.Equal(t.Modo, mc.Modo);
            Assert.Equal(t.Paises, Enumerable.Range(0, mc.NumPaises).Select(k => Encoding.Latin1.GetString(mc.Pais(k))).ToList());
        }
        // a leitura de alem do arquivo falha, e a marca 0 tambem (o laco da volta na palavra)
        Assert.Null(ms.Registro(ms.Quantidade + 1));
        Assert.Null(ms.Registro(0));
        Assert.Null(new Marcas(new byte[7]).Registro(1));
        saida.WriteLine($"{ms.Quantidade} marcas");
    }

    // ------------------------------------------------------------ o save
    [Fact]
    public void ContratoNoCabecalhoDaEquipa()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Pasta(), "l20-2026-j1.e98"));
        var s = Jogo98.Ler(bytes);
        Assert.Equal(bytes, s.Gravar());   // o cabecalho "EFa" vai e volta inteiro
        var m = new Mundo(s, 0);
        var e = m.Equipa(s.Divisoes[0].Equipas[0])!;
        // bytes 4.. do cabecalho: 00 03 03 e0391300 (sem ingresso, QATAR AIRWAYS, 3 epocas,
        // 1 260 000)
        Assert.Equal(3, e.MarcaMaster());
        Assert.Equal(3, e.EpocasMaster());
        Assert.Equal(1_260_000, e.ValorMaster());
        Assert.Equal("QATAR AIRWAYS", Marcas().Registro(3)!.Nome);
        var h = s.Equipas.Single(x => MercadoJogo.HumanoAtivo(m.TreinadorDa(x)));
        Assert.Equal("ASSAI ATACADISTA", Marcas().Registro(h.MarcaMaster())!.Nome);
    }

    // ------------------------------------------------------------ contas
    [Fact]
    public void QuotaPorDivisao()
    {
        // cota x inflacao / 2 no milhar (frndint ao par) x (5 - d) / 2: o simular.py
        var s = Save("l20-2026-j1.e98");
        var p = new Patrocinios(new Mundo(s, 0), Marcas());
        foreach (var mc in Marcas().Todas())
        {
            int cota = p.CotaInflacionada(mc);
            Assert.Equal((int)Math.Round(mc.Cota * 2.0 / 2 / 1000, MidpointRounding.ToEven) * 1000, cota);
            for (int d = 0; d < 5; d++) Assert.Equal(cota * (5 - d) / 2, Patrocinios.Quota(cota, d));
        }
        // inflacao 2,5: 120 500 -> 150 625 -> 151 000 (no milhar)
        s.Cabecalho.Real2B46 = Ext.F64(2.5).Bytes();
        var reg = Marcas().Registro(1)!.Registro.ToArray();
        BitConverter.GetBytes(120_500).CopyTo(reg, 44);
        Assert.Equal(151_000, new Patrocinios(new Mundo(s, 0), null).CotaInflacionada(new Marca(reg)));
        // 0,5 de milhar vai ao par: 2 500 x 1 -> 2 000; 3 500 -> 4 000
        s.Cabecalho.Real2B46 = Ext.F64(2).Bytes();
        BitConverter.GetBytes(2_500).CopyTo(reg, 44);
        Assert.Equal(2_000, new Patrocinios(new Mundo(s, 0), null).CotaInflacionada(new Marca(reg)));
        BitConverter.GetBytes(3_500).CopyTo(reg, 44);
        Assert.Equal(4_000, new Patrocinios(new Mundo(s, 0), null).CotaInflacionada(new Marca(reg)));
    }

    // os contratos do save do launcher (feitos pelo EXE no comeco de 2026) batem com a regra da
    // distribuicao: refazendo-a marca a marca, na ordem do arquivo e das listas das divisoes,
    // cada equipa do computador que tem a marca b foi assinada na vez da marca b (sem
    // patrocinador ainda, pais aceito, divisao do nivel da marca, verba suficiente) com o valor
    // da quota; quem nao tem b nunca era obrigado a ter (o Random decide). O humano recebe
    // proposta de toda marca que chega a ele.
    [Fact]
    public void DistribuicaoDoLauncherBateComARegra()
    {
        var s = Save("l20-2026-j1.e98");
        var ms = Marcas();
        var m = new Mundo(s, 0);
        var p = new Patrocinios(m, ms);
        var final = new Dictionary<ushort, (byte B, int V)>();
        foreach (var e in s.Equipas) final[e.ID] = (e.MarcaMaster(), e.ValorMaster());
        var hum = s.Equipas.Single(x => MercadoJogo.HumanoAtivo(m.TreinadorDa(x)));
        // o estado no comeco: ninguem com patrocinador
        foreach (var e in s.Equipas) { e.SetB(MMarca, 0); e.SetD(MValor, 0); e.SetB(MAnos, 0); }
        var assinadas = new HashSet<ushort>();
        var propostas = new List<int>();
        int sorteios = 0;
        for (int b = 1; b <= ms.Quantidade; b++)
        {
            var mc = ms.Registro(b)!;
            int disp = p.Disponivel(mc, b), cota = p.CotaInflacionada(mc);
            bool acabou = false;
            for (int dv = Math.Max(0, mc.DivisaoMaxima - 2); dv < mc.DivisaoMaxima && !acabou; dv++)
            {
                int q = Patrocinios.Quota(cota, dv);
                foreach (var id in s.Divisoes[dv].Equipas)
                {
                    if (disp < q) { acabou = true; break; }
                    var e = m.Equipa(id)!;
                    if (!Patrocinios.PaisOk(mc, e) || e.MarcaMaster() != 0) continue;
                    if (e == hum)
                    {
                        if (propostas.Count < 3) { propostas.Add(b); disp -= q; }
                        continue;
                    }
                    sorteios++;
                    if (final[id].B != b) continue;
                    Assert.Equal(q, final[id].V);
                    e.SetB(MMarca, (byte)b); e.SetD(MValor, q);
                    assinadas.Add(id);
                    disp -= q;
                }
            }
        }
        foreach (var e in s.Equipas)
            if (e != hum && final[e.ID].B != 0) Assert.Contains(e.ID, assinadas);
        Assert.Contains((int)final[hum.ID].B, propostas);
        saida.WriteLine($"{assinadas.Count} contratos do computador, {sorteios} sorteios de 60%, propostas ao humano: {string.Join(", ", propostas.Select(b => ms.Registro(b)!.Nome))}");
    }

    // ------------------------------------------------------------ distribuicao com Random fixo
    // a regra do simular.py (tools/patrocinios) com o Random do Delphi e o humano: o mesmo
    // resultado e a mesma semente no fim
    [Fact]
    public void DistribuicaoIgualAoSimulador()
    {
        foreach (uint semente in new uint[] { 0, 1, 12345, 0xDEADBEEF })
        {
            var s1 = Save("l20-2026-j1.e98");
            foreach (var e in s1.Equipas) { e.SetB(MMarca, 0); e.SetD(MValor, 0); e.SetB(MAnos, 0); }
            var s2 = Jogo98.Ler(s1.Gravar());
            var ms = Marcas();

            // o motor
            var m1 = new Mundo(s1, semente);
            var vistas = new List<Proposta>();
            new Patrocinios(m1, ms) { Perguntar = pr => { vistas.Add(pr); return false; } }.Distribuir();

            // o simulador
            var m2 = new Mundo(s2, semente);
            var g = new Aleatorio(semente);
            var aux = new Patrocinios(m2, ms);
            var hum = s2.Equipas.Single(x => MercadoJogo.HumanoAtivo(m2.TreinadorDa(x)));
            var props = new List<(int B, int Anos, int Q)>();
            foreach (var (mc, b) in ms.Todas().Select((x, i) => (x, i + 1)))
            {
                int disp = (int)Math.Round(mc.Verba * 2.0 / 2, MidpointRounding.ToEven);
                disp -= (int)((long)disp * mc.Reserva / 100);
                int cota = (int)Math.Round(mc.Cota * 2.0 / 2 / 1000, MidpointRounding.ToEven) * 1000;
                bool acabou = false;
                for (int dv = Math.Max(0, mc.DivisaoMaxima - 2); dv < mc.DivisaoMaxima && !acabou; dv++)
                {
                    int quota = cota * (5 - dv) / 2;
                    foreach (var id in s2.Divisoes[dv].Equipas)
                    {
                        if (disp < quota) { acabou = true; break; }
                        var e = m2.Equipa(id)!;
                        if (!Patrocinios.PaisOk(mc, e) || e.MarcaMaster() != 0) continue;
                        if (e != hum && g.Random(100) >= 60) continue;
                        int anos = g.Random(3) + 1;
                        if (e == hum)
                        {
                            if (props.Count < 3) { props.Add((b, anos, quota)); disp -= quota; }
                            continue;
                        }
                        e.SetB(MMarca, (byte)b); e.SetB(MAnos, (byte)anos); e.SetD(MValor, quota);
                        disp -= quota;
                    }
                }
            }
            Assert.Equal(g.Semente, m1.R.Semente);
            Assert.Equal(s2.Gravar(), s1.Gravar());
            Assert.Equal(props, vistas.Select(v => ((int)v.Marca, (int)v.Quantidade, v.Valor)).ToList());
            Assert.All(vistas, v => Assert.Equal(Proposta.Master, v.Tipo));
            int com = s1.Divisoes.Take(4).Sum(d => d.Equipas.Count(id => m1.Equipa(id)!.MarcaMaster() != 0));
            saida.WriteLine($"semente {semente:X}: {com} das 80 com patrocinador, {vistas.Count} propostas");
        }
    }

    // um mundo de 8 equipas (j3, Juan no JUVENTUS-BR, 4a divisao) com o MARCAS.DAT
    static (Mundo M, Patrocinios P, Equipa H, List<Proposta> Vistas) Mundo8(uint semente, Func<Proposta, bool>? resposta = null)
    {
        var s = J3();
        var m = new Mundo(s, semente);
        var vistas = new List<Proposta>();
        var p = new Patrocinios(m, Marcas()) { Perguntar = pr => { vistas.Add(pr); return resposta?.Invoke(pr) ?? false; } };
        var h = s.Equipas.Single(x => MercadoJogo.HumanoAtivo(m.TreinadorDa(x)));
        return (m, p, h, vistas);
    }

    [Fact]
    public void PropostasRenovacaoNaFrenteEAceitarPara()
    {
        // sem renovacao: as 3 primeiras marcas que chegam ao humano, na ordem do arquivo
        var (m, p, h, vistas) = Mundo8(7);
        p.Distribuir();
        Assert.Equal(3, vistas.Count);
        Assert.True(vistas[0].Marca < vistas[1].Marca && vistas[1].Marca < vistas[2].Marca);
        Assert.Equal(0, h.MarcaMaster());
        // com a renovacao da 2a: ela vem primeiro, como tipo 1, e a 1a vai para o lugar dela
        byte ren = vistas[1].Marca, pri = vistas[0].Marca;
        (m, p, h, vistas) = Mundo8(7, pr => pr.Tipo == Proposta.Renovacao);
        h.SetB(UMarca, ren);
        p.Distribuir();
        Assert.Single(vistas);   // aceitou a 1a: as outras ficam de fora
        Assert.Equal(Proposta.Renovacao, vistas[0].Tipo);
        Assert.Equal(ren, vistas[0].Marca);
        Assert.Equal(ren, h.MarcaMaster());
        Assert.Equal(vistas[0].Quantidade, h.EpocasMaster());
        Assert.Equal(vistas[0].Valor, h.ValorMaster());
        Assert.Equal(0, h.MarcaRenovacao());   // so vale nesta epoca
        Assert.Equal(0, vistas[0].Multa);
        _ = pri;
    }

    [Fact]
    public void SemArquivoNaoSorteiaNemPropoe()
    {
        var s = J3();
        var m = new Mundo(s, 99);
        var h = s.Equipas.Single(x => MercadoJogo.HumanoAtivo(m.TreinadorDa(x)));
        h.SetB(UMarca, 5);
        new Patrocinios(m, null).Distribuir();
        Assert.Equal(99u, m.R.Semente);
        Assert.Equal(5, h.MarcaRenovacao());   // abrir falhou: nem chega a apagar a renovacao
        // so o cabecalho: sem marcas, sem sorteio, e a renovacao apagada
        new Patrocinios(m, new Marcas(new byte[] { (byte)'P', (byte)'A', (byte)'T', (byte)'1', 1, 0, 0, 0 })).Distribuir();
        Assert.Equal(99u, m.R.Semente);
        Assert.Equal(0, h.MarcaRenovacao());
    }

    [Fact]
    public void PagarAEpoca()
    {
        var (m, p, h, _) = Mundo8(1);
        var cpu = m.Equipa(m.S.Divisoes[0].Equipas[0])!;
        cpu.SetB(MMarca, 4); cpu.SetB(MAnos, 2); cpu.SetD(MValor, 300_000);
        h.SetB(MMarca, 9); h.SetB(MAnos, 1); h.SetD(MValor, 150_000);
        uint dc = cpu.Dinheiro, dh = h.Dinheiro;
        int pc = MercadoJogo.Balanco(cpu)[2], ph = MercadoJogo.Balanco(h)[2];
        p.Pagar();
        Assert.Equal(dc + 300_000, cpu.Dinheiro);
        Assert.Equal(pc + 300_000, MercadoJogo.Balanco(cpu)[2]);   // linha dos Premios
        Assert.Equal(1, cpu.EpocasMaster());
        Assert.Equal(4, cpu.MarcaMaster());
        Assert.Equal(dh + 150_000, h.Dinheiro);
        Assert.Equal(ph + 150_000, MercadoJogo.Balanco(h)[2]);
        Assert.Equal(0, h.MarcaMaster());
        Assert.Equal(0, h.ValorMaster());
        Assert.Equal(9, h.MarcaRenovacao());   // vira renovacao na proxima epoca
        p.Pagar();
        Assert.Equal(0, cpu.MarcaMaster());
        Assert.Equal(0, cpu.MarcaRenovacao());   // o do computador so acaba
        Assert.Equal(1u, m.R.Semente);
    }

    [Fact]
    public void RodadaForaDosMomentosNaoUsaORandom()
    {
        var (m, p, _, vistas) = Mundo8(1234);
        // 8 equipas: 14 jornadas; master em 3, 7 e 10; pontual nas jornadas 10 a 14 entre os 3
        // primeiros (o humano esta na 4a divisao, lugar > 2 no j3?) e a Taca nunca (> 16 equipas)
        var h = m.S.Equipas.Single(x => MercadoJogo.HumanoAtivo(m.TreinadorDa(x)));
        int lugar = m.S.Divisoes[m.DivisaoDa(h)].Equipas.IndexOf(h.ID);
        foreach (short j in new short[] { 1, 2, 4, 5, 6, 8, 9 })
        {
            m.Jornada = j;
            p.Rodada();
        }
        Assert.Equal(1234u, m.R.Semente);
        Assert.Empty(vistas);
        m.Jornada = 3;
        p.Rodada();
        Assert.NotEqual(1234u, m.R.Semente);
        saida.WriteLine($"lugar {lugar}, jornada 3: {vistas.Count} proposta(s)");
    }

    [Fact]
    public void PontualPagaPorJogo()
    {
        // a 1a semente em que o sorteio (12 tentativas) acha uma marca com reserva que chega a
        // 4a divisao
        Mundo m = null!; Patrocinios p = null!; Equipa h = null!; List<Proposta> vistas = null!;
        for (uint sem = 1; ; sem++)
        {
            (m, p, h, vistas) = Mundo8(sem, pr => true);
            p.Teste = true;   // PATROC_TESTE: proposta em toda rodada
            m.Jornada = 1;
            p.Rodada();
            if (vistas.Any(v => v.Tipo == Proposta.Pontual)) { saida.WriteLine($"semente {sem}"); break; }
            Assert.True(sem < 200);
        }
        var pont = vistas.First(v => v.Tipo == Proposta.Pontual);
        Assert.InRange(pont.Quantidade, 1, 5);
        Assert.Equal(pont.Marca, h.MarcaPontual());
        Assert.Equal(pont.Valor, h.ValorPontual());
        var mc = Marcas().Registro(pont.Marca)!;
        Assert.NotEqual(0, mc.Reserva);
        int div = m.DivisaoDa(h);
        Assert.Equal(Patrocinios.Quota(p.CotaInflacionada(mc), div) / 8, pont.Valor);
        // aceitou tambem a de master (Teste): sem contrato antes, sem multa
        var mst = vistas.First(v => v.Tipo == Proposta.Master);
        Assert.Equal(0, mst.Multa);
        Assert.Equal(mst.Marca, h.MarcaMaster());
        // as proximas rodadas pagam um jogo cada, na linha dos Premios
        int jogos = pont.Quantidade;
        for (int k = 0; k < jogos; k++)
        {
            int pr = MercadoJogo.Balanco(h)[2];
            uint din = h.Dinheiro;
            int antes = vistas.Count;
            p.Rodada();
            Assert.Equal(din + (uint)pont.Valor - (uint)MultasDesde(vistas, antes), h.Dinheiro);
            Assert.Equal(pr + pont.Valor - MultasDesde(vistas, antes), MercadoJogo.Balanco(h)[2]);
            Assert.Equal(jogos - k - 1, h.JogosPontual());
        }
        Assert.Equal(0, h.MarcaPontual());
        Assert.Equal(0, h.ValorPontual());
    }

    static int MultasDesde(List<Proposta> v, int i) => v.Skip(i).Sum(x => x.Multa);

    [Fact]
    public void TrocaDeMasterPagaMulta()
    {
        // com contrato, so proposta maior; a multa e tudo o que falta (epocas x valor)
        var (m, p, h, vistas) = Mundo8(77, pr => pr.Tipo == Proposta.Master);
        h.SetB(MMarca, 1); h.SetB(MAnos, 3); h.SetD(MValor, 10_000);
        p.Teste = true;
        m.Jornada = 2;
        uint din = h.Dinheiro;
        int pr0 = MercadoJogo.Balanco(h)[2];
        p.Rodada();
        var mst = vistas.Single(v => v.Tipo == Proposta.Master);
        Assert.True(mst.Valor > 10_000);
        Assert.Equal(30_000, mst.Multa);
        Assert.Equal(din - 30_000, h.Dinheiro);
        Assert.Equal(pr0 - 30_000, MercadoJogo.Balanco(h)[2]);
        Assert.Equal(mst.Marca, h.MarcaMaster());
        Assert.Equal(mst.Quantidade, h.EpocasMaster());
    }

    // o ciclo com a opcao: o f20 do oraculo (ultima jornada de 2026, fim de temporada e a 1a
    // jornada de 2027, regras do launcher) com os patrocinadores ligados: no fim da epoca nao
    // havia contratos; no comeco de 2027 a distribuicao assina com as equipas do computador e
    // o humano recebe propostas; os amarelos acumulados zeram
    [Fact]
    public void CicloComPatrocinios()
    {
        var d = AppContext.BaseDirectory;
        string? arq = null;
        for (int i = 0; i < 8 && d != null && arq == null; i++, d = Path.GetDirectoryName(d))
            if (File.Exists(Path.Combine(d, "Oraculo", "f20-entrada.e98"))) arq = Path.Combine(d, "Oraculo", "f20-entrada.e98");
        Assert.NotNull(arq);
        var s = Jogo98.Ler(File.ReadAllBytes(arq!));
        int ano = s.Cabecalho.Ano;
        var vistas = new List<Proposta>();
        var h = Ciclo.JogoEmCurso.HumanoDoOraculo();
        h.Patrocinio = pr => { vistas.Add(pr); return true; };
        var op = Ciclo.Opcoes.Padrao with { Regras = TRegras.Launcher(0), Patrocinios = true, Marcas = Marcas() };
        var j = new Ciclo.JogoEmCurso(s, 0, op, h) { Agora = () => default };
        int amarelosAntes = s.Equipas.Sum(e => e.Jogadores.Count(p => p.Historial[14] != 0));
        j.Ciclo();
        // o roteiro sai na preparacao da 1a jornada de 2027: ninguem jogou depois da NewSeason
        int amarelosNaEpoca = s.Equipas.Sum(e => e.Jogadores.Count(p => p.Historial[14] != 0 || p.Historial[15] != 0));
        Assert.True(amarelosAntes > 0);
        Assert.Equal(0, amarelosNaEpoca);
        Assert.Equal(ano + 1, j.M.Ano);
        int com = s.Equipas.Count(e => e.MarcaMaster() != 0);
        var hum = s.Equipas.Single(x => MercadoJogo.HumanoAtivo(j.M.TreinadorDa(x)));
        Assert.True(com > 40, com.ToString());
        Assert.NotEmpty(vistas);
        Assert.Equal(vistas[0].Marca, hum.MarcaMaster());
        saida.WriteLine($"{com} equipas com patrocinador; propostas: {string.Join(", ", vistas.Select(v => $"{v.Nome} ({v.Tipo})"))}; jogadores com amarelos acumulados antes: {amarelosAntes}");
    }

    [Fact]
    public void ZerarAmarelos()
    {
        var s = J3();
        var m = new Mundo(s, 0);
        foreach (var e in s.Equipas) foreach (var j in e.Jogadores) { j.Historial[14] = 2; j.Historial[15] = 7; j.Historial[13] = 1; }
        Patrocinios.ZerarAmarelos(m);
        // so as equipas das divisoes (as 5 listas) e so +0x25 e +0x26
        var nasDivisoes = s.Divisoes.Take(5).SelectMany(d => d.Equipas).ToHashSet();
        foreach (var e in s.Equipas)
            foreach (var j in e.Jogadores)
            {
                Assert.Equal(nasDivisoes.Contains(e.ID) ? 0 : 2, j.Historial[14]);
                Assert.Equal(nasDivisoes.Contains(e.ID) ? 0 : 7, j.Historial[15]);
                Assert.Equal(1, j.Historial[13]);
            }
    }

    [Fact]
    public void TextoDaProposta()
    {
        string Ext(long v) => v == 1_260_000 ? "1 milhão e 260 mil reais" : v + " reais";
        var p = new Proposta { Tipo = Proposta.Master, Quantidade = 3, Valor = 1_260_000 };
        Encoding.Latin1.GetBytes("\x0dQATAR AIRWAYS").CopyTo(p.NomeBruto, 0);
        Assert.Equal("QATAR AIRWAYS oferece 1 milhão e 260 mil reais por temporada, durante 3 temporadas,\r" +
            "para ser o patrocinador master do seu time.\r\rAceita?", Patrocinios.Texto(p, Ext));
        p.Tipo = Proposta.Renovacao; p.Quantidade = 1; p.Multa = 5;
        Assert.Equal("QATAR AIRWAYS oferece 1 milhão e 260 mil reais por temporada, durante 1 temporada,\r" +
            "para renovar o contrato de patrocinador master.\rMulta pela troca de patrocinador: 5 reais\r\rAceita?", Patrocinios.Texto(p, Ext));
        p.Tipo = Proposta.Pontual; p.Quantidade = 2;
        Assert.Equal("QATAR AIRWAYS oferece 1 milhão e 260 mil reais por jogo, durante 2 jogos,\r" +
            "para estampar a marca no uniforme.\r\rAceita?", Patrocinios.Texto(p, Ext));
    }
}
