using System;

namespace RetroFoot.Motor.Base;

// As regras do jogador que nao dependem do jogo em curso.
public static class Posicao
{
    public const int Guarda = 0, Defesa = 1, Medio = 2, Avancado = 3;
}

// Atributos que o jogo tira do nome ao criar um jogo novo (seg03:7516-766d).
public readonly record struct Atributos(int Nota, int Lesao, int Comportamento, bool Estrela)
{
    public static readonly string[] Comportamentos = { "Fair Play", "Cordeirinho", "Cavalheiro", "Caneleiro", "Caceteiro", "Sarrafeiro" };

    // soma dos codigos Latin-1 do nome, com a 1a letra maiuscula (seg03:7516). Espacos e
    // acentos contam.
    public static int Soma(ReadOnlySpan<byte> nomeLatin1)
    {
        int s = 0;
        for (int i = 0; i < nomeLatin1.Length; i++)
        {
            byte c = nomeLatin1[i];
            if (i == 0 && c >= 'a' && c <= 'z') c -= 0x20;
            s += c;
        }
        return s;
    }

    public static Atributos DoNome(int soma, int posicao)
    {
        int nota = soma % 10 + 1;
        int lesao = soma % 11;
        int comp = 5 - (int)Math.Sqrt(soma % 36);
        if (posicao == Posicao.Defesa) comp = (comp + 2) % 6;
        return new Atributos(nota, lesao, comp, nota >= 8 && (posicao == Posicao.Medio || posicao == Posicao.Avancado));
    }
}
