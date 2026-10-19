using System;

namespace RetroFoot.Motor.Base;

// As cifras simples que o jogo classico usa nos arquivos (docs internos do
// launcher, secoes 1, 3 e 7).
public static class Cifra
{
    // Texto Pascal dos .EFT e dos nomes no save: o tamanho vem em claro e cada letra foi
    // somada ao byte cifrado anterior (a primeira, ao tamanho). Devolve o texto e quantos
    // bytes ele ocupou.
    public static (byte[] Texto, int Lidos) TextoEFT(ReadOnlySpan<byte> b)
    {
        int n = b[0];
        var p = new byte[n];
        byte anterior = (byte)n;
        for (int i = 0; i < n; i++)
        {
            p[i] = (byte)(b[1 + i] - anterior);
            anterior = b[1 + i];
        }
        return (p, 1 + n);
    }

    public static byte[] PorTextoEFT(ReadOnlySpan<byte> p)
    {
        var o = new byte[1 + p.Length];
        o[0] = (byte)p.Length;
        byte anterior = (byte)p.Length;
        for (int i = 0; i < p.Length; i++)
        {
            anterior += p[i];
            o[1 + i] = anterior;
        }
        return o;
    }

    // corpo de uma equipe no save: plano[i] = cifrado[i] - delta; delta += plano[i] - 0x20
    public static byte[] Rolante(ReadOnlySpan<byte> cifrado)
    {
        var plano = new byte[cifrado.Length];
        byte delta = 0;
        for (int i = 0; i < cifrado.Length; i++)
        {
            byte p = (byte)(cifrado[i] - delta);
            plano[i] = p;
            delta += (byte)(p - 0x20);
        }
        return plano;
    }

    public static byte[] PorRolante(ReadOnlySpan<byte> plano)
    {
        var cifrado = new byte[plano.Length];
        byte delta = 0;
        for (int i = 0; i < plano.Length; i++)
        {
            cifrado[i] = (byte)(plano[i] + delta);
            delta += (byte)(plano[i] - 0x20);
        }
        return cifrado;
    }

    // texto das listas .TXE (REFEREE, COUNTRY, Bosman, PLOP): o byte i recebe
    // (i+2)n + soma de (i+1-j) plano[j] para j < i
    public static (byte[] Texto, int Lidos) TextoTXE(ReadOnlySpan<byte> b)
    {
        int n = b[0];
        var p = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int k = (i + 2) * n;
            for (int j = 0; j < i; j++) k += (i + 1 - j) * p[j];
            p[i] = (byte)(b[1 + i] - (byte)k);
        }
        return (p, 1 + n);
    }

    public static byte[] PorTextoTXE(ReadOnlySpan<byte> p)
    {
        int n = p.Length;
        var o = new byte[1 + n];
        o[0] = (byte)n;
        for (int i = 0; i < n; i++)
        {
            int k = (i + 2) * n;
            for (int j = 0; j < i; j++) k += (i + 1 - j) * p[j];
            o[1 + i] = (byte)(p[i] + (byte)k);
        }
        return o;
    }
}
