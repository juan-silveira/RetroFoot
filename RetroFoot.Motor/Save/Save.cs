using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace RetroFoot.Motor.Save;

// O save do jogo classico (.e98), campo a campo, na ordem das rotinas de leitura do
// jogo classico (seg05:1d90 e as que ela chama). Os nomes "+0xNN" sao os deslocamentos dos
// campos nos objetos do jogo original; os que ainda nao tem significado conhecido ficam em
// bytes crus, para que ler e regravar de o mesmo arquivo.
// (Traduzido de versao em Go: motor/save.)
public sealed class Jogo98
{
    public Cabecalho Cabecalho = new();
    public List<Treinador> Treinadores = new();
    public List<Arbitro> Arbitros = new();
    public List<Equipa> Equipas = new();          // todas, completas (DS:26F6)
    public List<ushort> OutrasEquipas = new();    // outra lista de equipas, so os ids (DS:26F2)
    public List<Divisao> Divisoes = new();
    public List<Jogo> Jogos = new();
    public List<Ligacao> Ligacoes = new();        // seg05:1b1b: +0x1F de cada equipa
    public List<Campeao> Campeoes = new();
    public List<Transferencia> Transferencias = new();

    // ---------------------------------------------------------------- leitura
    sealed class Leitor
    {
        readonly byte[] _b;
        public int Pos;
        public Leitor(byte[] b) => _b = b;
        public int Tamanho => _b.Length;
        public ReadOnlySpan<byte> Bytes(int n)
        {
            if (Pos + n > _b.Length) throw new InvalidDataException($"fim do arquivo em {Pos} (faltam {Pos + n - _b.Length} bytes)");
            var p = _b.AsSpan(Pos, n);
            Pos += n;
            return p;
        }
        public byte U8() => Bytes(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2));
        public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));
        public void Fixo(byte[] dst) => Bytes(dst.Length).CopyTo(dst);
        // texto: 1 byte de tamanho e as letras (14:086d), como esta no arquivo (cifrado)
        public byte[] Texto() { int n = _b[Pos]; return Bytes(1 + n).ToArray(); }
    }

    // interpreta um .e98 inteiro; erro se sobrar ou faltar algum byte
    public static Jogo98 Ler(byte[] b)
    {
        var l = new Leitor(b);
        var s = new Jogo98();
        var c = s.Cabecalho;
        c.Texto = l.Texto();
        c.G271C = l.U16(); c.G271E = l.U16(); c.G271A = l.U8(); c.G2722 = l.U16();
        c.Ano = (int)l.U32();
        l.Fixo(c.Real2B46);
        l.Fixo(c.Real2B50);
        l.Fixo(c.G2D40);

        for (int n = l.U16(); n > 0; n--) s.Treinadores.Add(LerTreinador(l));
        for (int n = l.U16(); n > 0; n--)
        {
            var a = new Arbitro { ID = l.U16(), B06 = l.U8() };
            a.Nome = l.Texto(); a.Pais = l.Texto();
            s.Arbitros.Add(a);
        }
        for (int n = l.U16(); n > 0; n--) s.Equipas.Add(LerEquipa(l));
        for (int n = l.U16(); n > 0; n--) s.OutrasEquipas.Add(l.U16());
        for (int n = l.U16(); n > 0; n--)
        {
            var d = new Divisao { D1F = l.U32(), W04 = l.U16() };
            l.Fixo(d.Nome);
            for (int k = l.U16(); k > 0; k--) d.Equipas.Add(l.U16());
            s.Divisoes.Add(d);
        }
        for (int n = l.U16(); n > 0; n--)
        {
            var j = new Jogo { Taca = l.U8() };
            l.Fixo(j.B04);
            j.Casa = l.U16(); j.Fora = l.U16();
            l.Fixo(j.B36);
            j.D40 = l.U32(); j.Arbitro = l.U16(); j.W4C = l.U16(); j.W4E = l.U16(); j.B50 = l.U8();
            s.Jogos.Add(j);
        }
        while (true)
        {
            ushort id = l.U16();
            if ((short)id <= 0) break;
            var g = new Ligacao { Equipa = id };
            for (int k = l.U16(); k > 0; k--) g.Outras.Add(l.U16());
            s.Ligacoes.Add(g);
        }
        for (int n = l.U16(); n > 0; n--)
        {
            var cp = new Campeao();
            for (int i = 0; i < cp.W.Length; i++) cp.W[i] = l.U16();
            s.Campeoes.Add(cp);
        }
        for (int n = l.U16(); n > 0; n--)
            s.Transferencias.Add(new Transferencia { W06 = l.U16(), W0A = l.U16(), D0C = l.U32(), W08 = l.U16(), B05 = l.U8(), B04 = l.U8() });
        if (l.Pos != b.Length) throw new InvalidDataException($"sobraram {b.Length - l.Pos} bytes depois do fim (em {l.Pos})");
        return s;
    }

    static Treinador LerTreinador(Leitor l)
    {
        var t = new Treinador { ID = l.U16() };
        t.Nome = l.Texto();
        t.Humano = l.U8(); t.B06 = l.U8(); t.W0C = l.U16(); t.D0E = l.U32(); t.D16 = l.U32();
        l.Fixo(t.B1A);
        for (int n = l.U16(); n > 0; n--) t.Eventos.Add(new EventoTreinador { B04 = l.U8(), D05 = l.U32(), W09 = l.U16() });
        return t;
    }

    static Equipa LerEquipa(Leitor l)
    {
        var e = new Equipa { ID = l.U16() };
        l.Fixo(e.Cabeca);
        e.NomeCompleto = l.Texto(); e.NomeCurto = l.Texto();
        l.Fixo(e.CorLetra);
        l.Fixo(e.CorFundo);
        e.Pais = l.Texto();
        e.Nivel = l.U8();
        l.Fixo(e.B16);
        l.Fixo(e.B4D);
        l.Fixo(e.Moral);
        e.Treinador = l.U16();
        l.Fixo(e.B89);
        l.Fixo(e.BA5);
        e.D6D = l.U32(); e.Dinheiro = l.U32(); e.D75 = l.U32();
        l.Fixo(e.B79);
        l.Fixo(e.B83);
        e.DC1 = l.U32(); e.WC7 = l.U16(); e.WC9 = l.U16();
        for (int n = l.U16(); n > 0; n--) e.Jogadores.Add(LerJogador(l));
        for (int n = l.U16(); n > 0; n--) e.Eventos.Add(new EventoEquipa { B04 = l.U8(), D05 = l.U32(), W09 = l.U16() });
        for (int n = l.U16(); n > 0; n--) e.Eventos2.Add(new EventoEquipa2 { D06 = l.U32(), W0A = l.U16(), B04 = l.U8(), B05 = l.U8() });
        for (int n = l.U16(); n > 0; n--)
            e.Calendario.Add(new EntradaCalendario { W08 = l.U16(), W0E = l.U16(), W0A = l.U16(), W0C = l.U16(), D10 = l.U32(), B07 = l.U8(), B06 = l.U8(), W04 = l.U16() });
        e.Estadio = l.U8();
        l.Fixo(e.EstadioReal);
        return e;
    }

    static Jogador LerJogador(Leitor l)
    {
        var j = new Jogador { ID = l.U16() };
        j.Pais = l.Texto();
        j.NomeCifrado = l.Texto();
        j.Posicao = l.U8(); j.Estrela = l.U8();
        j.Forca = l.U16();
        l.Fixo(j.B0C);
        l.Fixo(j.B14);
        j.Suspensao = l.U16(); j.Comportamento = l.U16(); j.JogosLesao = l.U16(); j.Lesao = l.U16(); j.Nota = l.U16();
        j.Salario = l.U32(); j.B35 = l.U8(); j.W36 = l.U16();
        l.Fixo(j.Historial);
        return j;
    }

    // ---------------------------------------------------------------- gravacao
    sealed class Gravador
    {
        readonly MemoryStream _m = new();
        public void Bytes(ReadOnlySpan<byte> b) => _m.Write(b);
        public void U8(byte v) => _m.WriteByte(v);
        public void U16(ushort v) { Span<byte> t = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(t, v); _m.Write(t); }
        public void U32(uint v) { Span<byte> t = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(t, v); _m.Write(t); }
        public byte[] Resultado() => _m.ToArray();
    }

    // os bytes do .e98, na mesma ordem em que Ler os le
    public byte[] Gravar()
    {
        var g = new Gravador();
        var c = Cabecalho;
        g.Bytes(c.Texto);
        g.U16(c.G271C); g.U16(c.G271E); g.U8(c.G271A); g.U16(c.G2722);
        g.U32((uint)c.Ano);
        g.Bytes(c.Real2B46); g.Bytes(c.Real2B50); g.Bytes(c.G2D40);

        g.U16((ushort)Treinadores.Count);
        foreach (var t in Treinadores)
        {
            g.U16(t.ID); g.Bytes(t.Nome); g.U8(t.Humano); g.U8(t.B06); g.U16(t.W0C); g.U32(t.D0E); g.U32(t.D16);
            g.Bytes(t.B1A);
            g.U16((ushort)t.Eventos.Count);
            foreach (var e in t.Eventos) { g.U8(e.B04); g.U32(e.D05); g.U16(e.W09); }
        }
        g.U16((ushort)Arbitros.Count);
        foreach (var a in Arbitros) { g.U16(a.ID); g.U8(a.B06); g.Bytes(a.Nome); g.Bytes(a.Pais); }
        g.U16((ushort)Equipas.Count);
        foreach (var e in Equipas) GravarEquipa(g, e);
        g.U16((ushort)OutrasEquipas.Count);
        foreach (var id in OutrasEquipas) g.U16(id);
        g.U16((ushort)Divisoes.Count);
        foreach (var d in Divisoes)
        {
            g.U32(d.D1F); g.U16(d.W04); g.Bytes(d.Nome);
            g.U16((ushort)d.Equipas.Count);
            foreach (var id in d.Equipas) g.U16(id);
        }
        g.U16((ushort)Jogos.Count);
        foreach (var j in Jogos)
        {
            g.U8(j.Taca); g.Bytes(j.B04); g.U16(j.Casa); g.U16(j.Fora); g.Bytes(j.B36);
            g.U32(j.D40); g.U16(j.Arbitro); g.U16(j.W4C); g.U16(j.W4E); g.U8(j.B50);
        }
        foreach (var l in Ligacoes)
        {
            g.U16(l.Equipa);
            g.U16((ushort)l.Outras.Count);
            foreach (var id in l.Outras) g.U16(id);
        }
        g.U16(0);
        g.U16((ushort)Campeoes.Count);
        foreach (var cp in Campeoes) foreach (var w in cp.W) g.U16(w);
        g.U16((ushort)Transferencias.Count);
        foreach (var t in Transferencias) { g.U16(t.W06); g.U16(t.W0A); g.U32(t.D0C); g.U16(t.W08); g.U8(t.B05); g.U8(t.B04); }
        return g.Resultado();
    }

    static void GravarEquipa(Gravador g, Equipa e)
    {
        g.U16(e.ID); g.Bytes(e.Cabeca); g.Bytes(e.NomeCompleto); g.Bytes(e.NomeCurto);
        g.Bytes(e.CorLetra); g.Bytes(e.CorFundo); g.Bytes(e.Pais); g.U8(e.Nivel);
        g.Bytes(e.B16); g.Bytes(e.B4D); g.Bytes(e.Moral); g.U16(e.Treinador);
        g.Bytes(e.B89); g.Bytes(e.BA5);
        g.U32(e.D6D); g.U32(e.Dinheiro); g.U32(e.D75);
        g.Bytes(e.B79); g.Bytes(e.B83);
        g.U32(e.DC1); g.U16(e.WC7); g.U16(e.WC9);
        g.U16((ushort)e.Jogadores.Count);
        foreach (var j in e.Jogadores)
        {
            g.U16(j.ID); g.Bytes(j.Pais); g.Bytes(j.NomeCifrado); g.U8(j.Posicao); g.U8(j.Estrela);
            g.U16(j.Forca); g.Bytes(j.B0C); g.Bytes(j.B14);
            g.U16(j.Suspensao); g.U16(j.Comportamento); g.U16(j.JogosLesao); g.U16(j.Lesao); g.U16(j.Nota);
            g.U32(j.Salario); g.U8(j.B35); g.U16(j.W36); g.Bytes(j.Historial);
        }
        g.U16((ushort)e.Eventos.Count);
        foreach (var v in e.Eventos) { g.U8(v.B04); g.U32(v.D05); g.U16(v.W09); }
        g.U16((ushort)e.Eventos2.Count);
        foreach (var v in e.Eventos2) { g.U32(v.D06); g.U16(v.W0A); g.U8(v.B04); g.U8(v.B05); }
        g.U16((ushort)e.Calendario.Count);
        foreach (var c in e.Calendario) { g.U16(c.W08); g.U16(c.W0E); g.U16(c.W0A); g.U16(c.W0C); g.U32(c.D10); g.U8(c.B07); g.U8(c.B06); g.U16(c.W04); }
        g.U8(e.Estadio); g.Bytes(e.EstadioReal);
    }
}

// seg05:1a01
public sealed class Cabecalho
{
    public byte[] Texto = Array.Empty<byte>();   // texto cifrado do inicio (ate 255)
    public ushort G271C, G271E, G2722;
    public byte G271A;
    public int Ano;                              // DS:2B42
    public byte[] Real2B46 = new byte[10];       // Extended (inflacao ou outro)
    public byte[] Real2B50 = new byte[10];
    public byte[] G2D40 = new byte[101];
}

// seg03:8382
public sealed class Treinador
{
    public ushort ID;                            // +0x04
    public byte[] Nome = Array.Empty<byte>();    // texto ate 20
    public byte Humano;                          // +0x07
    public byte B06;                             // +0x06
    public ushort W0C;                           // +0x0C
    public uint D0E, D16;                        // +0x0E, +0x16
    public byte[] B1A = new byte[5];
    public List<EventoTreinador> Eventos = new();  // +0x12, ate 100
}

public sealed class EventoTreinador { public byte B04; public uint D05; public ushort W09; }   // seg03:b52a

// seg03:c770
public sealed class Arbitro
{
    public ushort ID;
    public byte B06;
    public byte[] Nome = Array.Empty<byte>();
    public byte[] Pais = Array.Empty<byte>();
}

// seg03:2bca
public sealed class Equipa
{
    public ushort ID;                            // +0x08 (os 2 bytes antes do "EFa")
    public byte[] Cabeca = new byte[50];         // "EFa\0" e zeros
    public byte[] NomeCompleto = Array.Empty<byte>(), NomeCurto = Array.Empty<byte>();
    public byte[] CorLetra = new byte[4], CorFundo = new byte[4];
    public byte[] Pais = Array.Empty<byte>();
    public byte Nivel;
    public byte[] B16 = new byte[55], B4D = new byte[14];
    public byte[] Moral = new byte[6];           // Real de 6 bytes
    public ushort Treinador;                     // +0x0E
    public byte[] B89 = new byte[28], BA5 = new byte[28];
    public uint D6D, Dinheiro, D75;              // Dinheiro: +0x71
    public byte[] B79 = new byte[10], B83 = new byte[6];
    public uint DC1;
    public ushort WC7, WC9;
    public List<Jogador> Jogadores = new();
    public List<EventoEquipa> Eventos = new();   // +0x61, ate 50
    public List<EventoEquipa2> Eventos2 = new();
    public List<EntradaCalendario> Calendario = new();
    public byte Estadio;                         // capacidade = 5000 x N
    public byte[] EstadioReal = new byte[6];
}

// seg03:7184 (registro de NL + 55 bytes, contando o identificador)
public sealed class Jogador
{
    public ushort ID;                            // +0x04
    public byte[] Pais = Array.Empty<byte>();    // texto ate 3
    public byte[] NomeCifrado = Array.Empty<byte>();
    public byte Posicao;                         // +0x19 dos atributos: 0 G, 1 D, 2 M, 3 A
    public byte Estrela;                         // +0x1A
    public ushort Forca;                         // +0x0A
    public byte[] B0C = new byte[8];
    public byte[] B14 = new byte[3];
    public ushort Suspensao;                     // +0x27
    public ushort Comportamento;                 // +0x2D
    public ushort JogosLesao;                    // +0x29
    public ushort Lesao;                         // +0x2F
    public ushort Nota;                          // +0x2B
    public uint Salario;                         // +0x31
    public byte B35;
    public ushort W36;
    public byte[] Historial = new byte[16];      // +0x17: jogos, golos, lesoes, vermelhos (4 bytes cada)
}

public sealed class EventoEquipa { public byte B04; public uint D05; public ushort W09; }       // seg03:ba06
public sealed class EventoEquipa2 { public uint D06; public ushort W0A; public byte B04, B05; } // seg03:bd8a

// seg03:1f12 (16 bytes)
public sealed class EntradaCalendario
{
    public ushort W08, W0E, W0A, W0C;
    public uint D10;
    public byte B07, B06;
    public ushort W04;
}

// seg03:6b37
public sealed class Divisao
{
    public uint D1F;
    public ushort W04;
    public byte[] Nome = new byte[21];           // string[20] do Pascal
    public List<ushort> Equipas = new();
}

// seg03:8f74
public sealed class Jogo
{
    public byte Taca;                            // +0x2D
    public byte[] B04 = new byte[41];
    public ushort Casa, Fora;                    // id da equipa (0 = nenhuma)
    public byte[] B36 = new byte[10];
    public uint D40;
    public ushort Arbitro, W4C, W4E;
    public byte B50;
}

public sealed class Ligacao { public ushort Equipa; public List<ushort> Outras = new(); }      // seg05:1b1b
public sealed class Campeao { public ushort[] W = new ushort[5]; }                             // seg03:ca77

// seg03:cf1f
public sealed class Transferencia
{
    public ushort W06, W0A, W08;
    public uint D0C;
    public byte B05, B04;
}
