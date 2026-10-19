using System;
using System.Collections.Generic;
using RetroFoot.Motor.Base;
using R = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.NovoJogo;

// Objetos do jogo em memoria durante a criacao de um jogo novo, com os deslocamentos dos
// campos no jogo classico. (Traduzido de motor/novojogo/estado.go.)
public sealed class Estado
{
    public ushort ProximoID;                 // DS:0x2722 (seg03:d3e0 da o proximo e soma 1)
    public int Ano;                          // DS:0x2B42
    public Ext Inflacao;                     // DS:0x2B46
    public Ext Real2B50;                     // DS:0x2B50 (comeca em 1,0; uso nao lido)
    public byte[] Moeda = new byte[101];     // DS:0x2D40 (registro da moeda)
    public int Jornada;                      // DS:0x2B40
    public int Inicio;                       // DS:0x271C: jornada em que a temporada recomeca
    public ushort Vez;                       // DS:0x271E: equipa do humano da vez
    public byte Taca271A;                    // DS:0x271A

    public List<Treinador> Treinadores = new();  // DS:0x2702
    public List<Arbitro> Arbitros = new();       // DS:0x2706
    public List<Equipa> Equipas = new();         // DS:0x26F6
    public List<Equipa> NaTaca = new();          // DS:0x26F2
    public List<Divisao> Divisoes = new();       // DS:0x26FA (5: 1a a 4a e Distrital)
    public List<Partida> Partidas = new();       // DS:0x270A
    public List<Jogador> Jogadores = new();      // DS:0x26FE (todos, na ordem de criacao)

    public R Regras;

    // DS:0x2D87 (registro da moeda + 0x47)
    internal int FatorMoeda() => BitConverter.ToInt32(Moeda, 0x47);

    // DS:0x2D9B (registro da moeda + 0x5B, Extended)
    internal Ext PassoBilhete() => Ext.Bytes(Moeda.AsSpan(0x5B, 10));

    // seg03:d3e0
    internal ushort NovoID() => ProximoID++;
}

// TCoach (seg03:827c)
public sealed class Treinador
{
    public ushort ID;                        // +0x04
    public byte[] Nome = Array.Empty<byte>();
    public byte B06;                         // +0x06
    public byte Humano;                      // +0x07
    public ushort W0C;
    public uint D0E, D16;
    public byte[] B1A = new byte[5];
    public List<EventoTreinador> Eventos = new();  // +0x12 (ate 100)
}

// TCoachEvent (seg03:b4cf): tipo, ano, equipa
public sealed class EventoTreinador
{
    public byte Tipo;                        // +0x04 (5 = entrou na equipa)
    public uint Ano;                         // +0x05
    public ushort Equipa;                    // +0x09
}

// TReferee (seg03:c6ca)
public sealed class Arbitro
{
    public ushort ID;
    public byte Disponivel;                  // +0x06: 1 livre; 0 ja escolhido nesta jornada
    public byte[] Nome = Array.Empty<byte>();
    public string Pais = "";
}

// TTeam (seg03:25e5 cria, seg03:27fb le o .EFT)
public sealed class Equipa
{
    public ushort ID;                        // +0x08
    public byte[] NomeCompleto = Array.Empty<byte>(), NomeCurto = Array.Empty<byte>();
    public byte[] CorLetra = new byte[4], CorFundo = new byte[4];
    public string Pais = "";
    public byte Nivel;
    public List<Jogador> Jogadores = new();  // +0x0A
    public Treinador Treinador = null!;      // +0x0E
    // +0x16..+0x4C: estatisticas da temporada, o lugar em cada jornada (+0x22, 14 bytes),
    // +0x30 e +0x31 (entradas e saidas de dinheiro, 28 bytes, ver seg03:58bf)
    public byte[] B16 = new byte[55];
    public byte[] B4D = new byte[14];        // +0x4D (zerado antes de cada jogo)
    public byte[] Moral = new byte[6];       // +0x5B (Real)
    public byte[] B89 = new byte[28];        // +0x89: copia de +0x31 da temporada anterior
    public byte[] BA5 = new byte[28];        // +0xA5: copia de +0x89
    public uint D6D;
    public int Dinheiro;                     // +0x71
    public int D75;                          // +0x75 (= dinheiro inicial)
    public Ext ValorClube;                   // +0x79 (comeca em 1,0)
    public byte[] PorPosicao = new byte[4];  // +0x83..+0x86 (G, D, M, A)
    public byte Estrangeiros;                // +0x87
    public byte NJogadores;                  // +0x88
    public uint DC1;
    public ushort WC7, WC9;
    public List<EntradaCalendario> Calendario = new();  // +0x69
    public byte Estadio;
    public byte[] EstadioReal = new byte[6];
}

// TCalendarEntry (seg03:1e9f)
public sealed class EntradaCalendario
{
    public ushort Jornada;                   // +0x04
    public byte Taca;                        // +0x07
    public byte B06;
    public ushort Adversario;                // +0x08 (ID)
    public ushort W0A = 0xFFFF, W0C = 0xFFFF;
    public ushort Fora;                      // +0x0E (1 = fora)
    public uint D10 = 0xFFFFFFFF;
}

// TPlayer (seg03:70d9)
public sealed class Jogador
{
    public ushort ID;
    public string Pais = "";
    public byte[] NomeCifrado = Array.Empty<byte>();
    public byte Posicao;                     // +0x19
    public byte Estrela;                     // +0x1A
    public short Forca;                      // +0x0A
    public byte[] B0C = new byte[8];
    public byte[] B14 = new byte[3];
    public short Suspensao;                  // +0x27
    public short Comportamento;              // +0x2D
    public short JogosLesao;                 // +0x29
    public short Lesao;                      // +0x2F
    public short Nota;                       // +0x2B
    public int Salario;                      // +0x31
    public byte Estrangeiro;                 // +0x35 (seg12:0a2f)
    public short W36;
    public byte[] Historial = new byte[16];
}

// TDivision (seg03:6abe)
public sealed class Divisao
{
    public int DinheiroTotal;                // +0x1F (seg03:7067)
    public short W04;                        // +0x04 (52 - 14 x divisao)
    public byte[] Nome = new byte[21];       // string[20]
    public List<Equipa> Equipas = new();
}

// TGame (seg03:8db2)
public sealed class Partida
{
    public byte Taca;                        // +0x2D: 0 campeonato, 1 Taca, 2 linha de titulo
    public byte[] Nome = new byte[41];       // string[40]
    public Equipa? Casa, Fora;               // +0x2E, +0x32
    public Ext Bilhete;                      // +0x36
    public int Publico;                      // +0x40
    public Arbitro? Arbitro;                 // +0x48
    public ushort W4C;                       // influencia do arbitro no mandante + 2
    public ushort W4E;                       // influencia no visitante
    public byte B50;
}
