using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Online;

// O retrato de um jogo online (docs/multiplayer-online.md): tudo o que um aparelho precisa para
// continuar o jogo do ponto em que o host parou na preparacao de uma equipa humana. O save
// sozinho nao basta: ele nao guarda o Random (cada carga sorteia uma semente nova), a procura
// dos ingressos dos jogos da jornada, a forca media guardada (+0xC5) de cada equipa nem a
// ordem da lista dos jogadores (DS:26FE), que as transferencias mudam. Com o retrato, o jogo
// continua igual ao que seguiria no host (a base da troca de host e da rodada refeita).
public sealed class Retrato
{
    const uint Marca = 0x31544552;   // "RET1"

    public byte[] Save = Array.Empty<byte>();   // com a retomada na equipa (DS:271E)
    public uint Semente;
    public List<ushort> OrdemJogadores = new();
    public Dictionary<ushort, short> ForcaMedia = new();
    public List<(byte[] Procura, byte[]? Ocupacao)?> Demanda = new();
    public List<Chicotada> Chicotadas = new();

    // comprimido (vai pela rede)
    public byte[] Bytes()
    {
        using var ms = new MemoryStream();
        using (var z = new GZipStream(ms, CompressionLevel.Optimal, true))
        using (var w = new BinaryWriter(z))
        {
            w.Write(Marca);
            w.Write(Save.Length); w.Write(Save);
            w.Write(Semente);
            w.Write(OrdemJogadores.Count);
            foreach (var id in OrdemJogadores) w.Write(id);
            w.Write(ForcaMedia.Count);
            foreach (var (id, v) in ForcaMedia) { w.Write(id); w.Write(v); }
            w.Write(Demanda.Count);
            foreach (var d in Demanda)
            {
                w.Write(d != null);
                if (d is not { } x) continue;
                w.Write(x.Procura.Length); w.Write(x.Procura);
                w.Write(x.Ocupacao != null);
                if (x.Ocupacao != null) { w.Write(x.Ocupacao.Length); w.Write(x.Ocupacao); }
            }
            w.Write(Chicotadas.Count);
            foreach (var c in Chicotadas) { w.Write(c.Treinador); w.Write(c.Equipa); w.Write(c.Tipo); w.Write(c.Flag); }
        }
        return ms.ToArray();
    }

    public static Retrato Ler(byte[] b)
    {
        using var z = new GZipStream(new MemoryStream(b), CompressionMode.Decompress);
        using var r = new BinaryReader(z);
        if (r.ReadUInt32() != Marca) throw new InvalidDataException("retrato desconhecido");
        var t = new Retrato { Save = r.ReadBytes(r.ReadInt32()), Semente = r.ReadUInt32() };
        for (int i = r.ReadInt32(); i > 0; i--) t.OrdemJogadores.Add(r.ReadUInt16());
        for (int i = r.ReadInt32(); i > 0; i--) t.ForcaMedia[r.ReadUInt16()] = r.ReadInt16();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            if (!r.ReadBoolean()) { t.Demanda.Add(null); continue; }
            var p = r.ReadBytes(r.ReadInt32());
            byte[]? o = r.ReadBoolean() ? r.ReadBytes(r.ReadInt32()) : null;
            t.Demanda.Add((p, o));
        }
        for (int i = r.ReadInt32(); i > 0; i--)
            t.Chicotadas.Add(new Chicotada(r.ReadUInt16(), r.ReadUInt16(), r.ReadByte(), r.ReadByte()));
        return t;
    }

    // o jogo de volta, parado antes da preparacao da equipa do retrato: as opcoes e os
    // treinadores humanos sao os de quem o continua
    public JogoEmCurso Jogo(Opcoes op, Humano h)
    {
        var j = new JogoEmCurso(Jogo98.Ler(Save), Semente, op, h);
        var m = j.M;
        m.OrdemJogadores = new List<ushort>(OrdemJogadores);
        m.ForcasMedias.Clear();
        foreach (var (id, v) in ForcaMedia) m.ForcasMedias[id] = v;
        m.DemandaPorPosicao(Demanda);
        m.Chicotadas = new List<Chicotada>(Chicotadas);
        m.PularAntesDaJornada = true;
        return j;
    }
}
