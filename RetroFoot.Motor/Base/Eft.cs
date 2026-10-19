using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RetroFoot.Motor.Base;

// Arquivos de equipe (.EFT, pasta EQUIPAS) e listas .TXE (COUNTRY, REFEREE, CTRGROUP).
// Formato em docs internos do launcher, secao 7. (Traduzido de motor/equipa.)
public sealed class Eft
{
    public static readonly byte[] Marca = "EFa\0"u8.ToArray();

    // jogador de um .EFT: so o que o editor guarda; o resto o jogo calcula no jogo novo
    public sealed class Jogador
    {
        public string Pais = "";                     // codigo de 3 letras (ex.: "bra")
        public byte[] Nome = Array.Empty<byte>();    // Latin-1
        public int Posicao;                          // 0 G, 1 D, 2 M, 3 A
    }

    public byte[] NomeCompleto = Array.Empty<byte>(), NomeAbreviado = Array.Empty<byte>();
    public uint CorLetra, CorFundo;                  // 0xRRGGBB
    public string Pais = "";
    public int Nivel;                                // 1 a 20
    public List<Jogador> Jogadores = new();
    public byte[] Treinador = Array.Empty<byte>();

    static readonly Encoding Latin1 = Encoding.Latin1;

    public static Eft Ler(string caminho) => Decodificar(File.ReadAllBytes(caminho));

    // marca, zeros ate 0x32, nome completo, nome abreviado, cores (RGB0 cada), pais, nivel
    // (1 byte), quantidade de jogadores (2 bytes), cada jogador (pais, nome, posicao em 2
    // bytes) e o treinador
    public static Eft Decodificar(byte[] b)
    {
        if (b.Length < 0x33 || !b.AsSpan(0, 4).SequenceEqual(Marca)) throw new InvalidDataException("não é um arquivo de equipe válido (EFa)");
        try
        {
            int o = 0x32;
            byte[] Texto() { var (p, n) = Cifra.TextoEFT(b.AsSpan(o)); o += n; return p; }
            var e = new Eft();
            e.NomeCompleto = Texto();
            e.NomeAbreviado = Texto();
            e.CorLetra = Rgb(b, o);
            e.CorFundo = Rgb(b, o + 4);
            o += 8;
            e.Pais = Latin1.GetString(Texto());
            e.Nivel = b[o];
            int n = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o + 1));
            o += 3;
            for (int k = 0; k < n; k++)
            {
                var j = new Jogador { Pais = Latin1.GetString(Texto()), Nome = Texto() };
                j.Posicao = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
                o += 2;
                e.Jogadores.Add(j);
            }
            e.Treinador = Texto();
            return e;
        }
        catch (Exception x) when (x is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("arquivo de equipe truncado", x);
        }
    }

    public byte[] Codificar()
    {
        var s = new MemoryStream();
        s.Write(Marca);
        s.Write(new byte[0x32 - 4]);
        s.Write(Cifra.PorTextoEFT(NomeCompleto));
        s.Write(Cifra.PorTextoEFT(NomeAbreviado));
        PorRgb(s, CorLetra);
        PorRgb(s, CorFundo);
        s.Write(Cifra.PorTextoEFT(Latin1.GetBytes(Pais)));
        s.WriteByte((byte)Nivel);
        Span<byte> w = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)Jogadores.Count);
        s.Write(w);
        foreach (var j in Jogadores)
        {
            s.Write(Cifra.PorTextoEFT(Latin1.GetBytes(j.Pais)));
            s.Write(Cifra.PorTextoEFT(j.Nome));
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)j.Posicao);
            s.Write(w);
        }
        s.Write(Cifra.PorTextoEFT(Treinador));
        return s.ToArray();
    }

    static uint Rgb(byte[] b, int o) => (uint)(b[o] << 16 | b[o + 1] << 8 | b[o + 2]);
    static void PorRgb(Stream s, uint c) => s.Write(new[] { (byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)0 });

    // lista .TXE (textos cifrados um atras do outro)
    public static List<byte[]> LerTxe(string caminho)
    {
        var b = File.ReadAllBytes(caminho);
        var lista = new List<byte[]>();
        for (int o = 0; o < b.Length;)
        {
            if (o + 1 + b[o] > b.Length) break;
            var (p, n) = Cifra.TextoTXE(b.AsSpan(o));
            lista.Add(p);
            o += n;
        }
        return lista;
    }
}
