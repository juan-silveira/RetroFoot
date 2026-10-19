using System;
using RetroFoot.Motor.Base;

namespace RetroFoot.Motor.NovoJogo;

// Tabelas e constantes do codigo. (Traduzido de motor/novojogo/tabelas.go.)
public static class Tabelas
{
    // os 5 registros de 101 bytes em DS:0x0062 (+ i x 0x65), oferecidos por TcurrencyDlg
    // (seg02:3dc7); o escolhido vai inteiro para DS:0x2D40 e para o cabecalho do save.
    // +0x00 nome, +0x15 sufixo curto, +0x2A sufixo da tela, +0x47 o fator (4 bytes),
    // +0x5B o passo do preco do bilhete (Extended)
    static readonly string[] Moedas =
    {
        "1045736375646f732028636f6e746f7329000000000720636f6e746f730000000000000000000000000005206573632e000000000000000000000000000000a08601000100000001000000e8030000c0c62d0040420f0020a107000000000000000080fe3f",
        "054575726f7300000000000000000000000000000006206575726f73000000000000000000000000000000000000000000000000000000000000000000000020a10700040000000400000001000000c0e1e400404b4c00c0c62d0000000000000000800040",
        "0744f36c6172657300000000000000000000000000082064f36c6172657300000000000000000000000000000000000000000000000000000000000000000020a10700040000000500000001000000002d3101808d5b00c0c62d0000000000000000800040",
        "05526561697300000000000000000000000000000006207265616973000000000000000000000000000000000000000000000000000000000000000000000020a10700040000000500000001000000002d3101808d5b00c0c62d0000000000000000800040",
        "064c6962726173000000000000000000000000000007206c69627261730000000000000000000000000000000000000000000000000000000000000000000090d003000400000004000000010000008096980000093d0080841e0000000000000000800040",
    };

    public const int MoedaEscudos = 0, MoedaEuros = 1, MoedaDolares = 2, MoedaReais = 3, MoedaLibras = 4;
    public static int QuantasMoedas => Moedas.Length;
    // as moedas oferecidas no jogo novo: os Escudos (0) sairam (so os saves antigos os tem)
    public static readonly int[] MoedasDoJogoNovo = { MoedaEuros, MoedaDolares, MoedaReais, MoedaLibras };

    public static byte[] Moeda(int i)
    {
        var r = new byte[101];
        Convert.FromHexString(Moedas[i]).CopyTo(r, 0);
        return r;
    }

    public static string NomeMoeda(int i) { var r = Moeda(i); return System.Text.Encoding.Latin1.GetString(r, 1, r[0]); }

    internal static readonly byte[] SufixoDivisao = { 0xAA, (byte)' ', (byte)'D', (byte)'i', (byte)'v', (byte)'i', (byte)'s', 0xE3, (byte)'o' };  // seg03:0b4c
    internal static readonly byte[] NomeDistrital = "Distrital"u8.ToArray();                                                                   // seg03:0b56
    internal static readonly byte[] SufixoJornada = { 0xAA, (byte)' ', (byte)'J', (byte)'o', (byte)'r', (byte)'n', (byte)'a', (byte)'d', (byte)'a' };  // seg03:6e3e

    // cabeca de cada equipa no save: DS:0x2AFE, "EFa\0" e zeros
    internal static byte[] CabecaEquipa() { var b = new byte[50]; b[0] = (byte)'E'; b[1] = (byte)'F'; b[2] = (byte)'a'; return b; }

    internal static readonly Ext Ext01 = Ext.Const(0x3FFB, 0xCCCC, 0xCCCC, 0xCCCC, 0xCCCD);   // 0,1 (seg03:6e34, seg09:35c7)
    internal static readonly Ext Ext09 = Ext.Const(0x3FFE, 0xE666, 0x6666, 0x6666, 0x6666);   // 0,9 (seg03:6f38)
    internal static readonly Ext Ext03 = Ext.Const(0x3FFD, 0x9999, 0x9999, 0x9999, 0x999A);   // 0,3 (seg05:39b9, seg03:b093)
    internal static readonly Ext Ext104 = Ext.Const(0x3FFF, 0x851E, 0xB851, 0xEB85, 0x1EB8);  // 1,04 (seg03:8037, seg03:b1ac)
    internal static readonly Ext Ext1000 = Ext.Const(0x4008, 0xFA00, 0, 0, 0);                // 1000 (seg03:8057)
    internal static readonly Ext Ext004 = Ext.Const(0x3FFA, 0xA3D7, 0x0A3D, 0x70A3, 0xD700);  // 0,04 (seg03:8063)
    internal static readonly byte[] Real48De09 = { 0x80, 0x66, 0x66, 0x66, 0x66, 0x66 };   // 0,9 em Real (seg03:c0f4)
    internal static readonly byte[] Real48De06 = { 0x80, 0x9A, 0x99, 0x99, 0x99, 0x19 };   // 0,6 em Real (seg03:bee6)
    internal static readonly byte[] Real48De1 = { 0x81, 0, 0, 0, 0, 0 };                    // 1,0 em Real (seg03:26ea)
    internal static readonly Ext ExtUm = Ext.F32(1);
    internal static readonly Ext ExtInflacao = Ext.F32(2);   // seg11:3a1b: inflacao inicial
    internal static readonly Ext Ext2B50 = Ext.F32(1);       // seg11:3a1f
}
