using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;

namespace RetroFoot.Motor.Arquivos;

// Uma equipa aberta no Editor de Equipas do RetroFoot (teams/<PAIS>/<slug>.json). Portado do
// TeamCodec.cs do launcher do jogo classico (EftTeam/EftPlayer, regras do seg12:275e); o JSON e o
// .EFT saem de EquipaJson, entao a volta continua byte a byte (os bytes fora do normal de um
// .EFT de patch ficam guardados e voltam iguais).
public sealed class JogadorEditavel
{
    public string Nome = "";
    public int Posicao;                 // 0 G, 1 D, 2 M, 3 A
    public string Pais = "";

    public Atributos Atributos => Atributos.DoNome(Atributos.Soma(EquipaJson.Bytes(Nome)), Posicao);
    public JogadorEditavel Copia() => new() { Nome = Nome, Posicao = Posicao, Pais = Pais };
}

public sealed class EquipaEditavel
{
    public string Arquivo = "";          // o nome do .EFT ("file" no JSON); "" = equipa nova
    public string? Caminho;              // o JSON na pasta de dados (null = ainda nao gravada)
    public string NomeCompleto = "", NomeAbreviado = "", Pais = "", Treinador = "";
    public int Nivel = 10;
    public uint CorLetra, CorFundo = 0xFFFFFF;
    public List<JogadorEditavel> Jogadores = new();

    // o que um .EFT de patch traz fora do normal (para a volta ser identica)
    byte[] _reservado = new byte[0x32 - 4];
    byte _byteLetra, _byteFundo;
    byte[] _resto = Array.Empty<byte>();

    static readonly Encoding Latin1 = Encoding.Latin1;

    public static EquipaEditavel Ler(string caminhoJson)
    {
        var l = EquipaJson.LerArquivo(caminhoJson);
        var e = DoEft(l.Bytes, l.Arquivo);
        e.Caminho = caminhoJson;
        return e;
    }

    public static EquipaEditavel DoEft(byte[] b, string arquivo)
    {
        if (b.Length < 0x33 || !b.AsSpan(0, 4).SequenceEqual(Eft.Marca))
            throw new InvalidDataException("não é um arquivo de equipa válido (EFa)");
        try
        {
            var e = new EquipaEditavel { Arquivo = arquivo };
            int o = 0x32;
            string Texto() { var (p, n) = Cifra.TextoEFT(b.AsSpan(o)); o += n; return Latin1.GetString(p); }
            e._reservado = b.AsSpan(4, 0x32 - 4).ToArray();
            e.NomeCompleto = Texto();
            e.NomeAbreviado = Texto();
            e.CorLetra = (uint)(b[o] << 16 | b[o + 1] << 8 | b[o + 2]);
            e._byteLetra = b[o + 3];
            e.CorFundo = (uint)(b[o + 4] << 16 | b[o + 5] << 8 | b[o + 6]);
            e._byteFundo = b[o + 7];
            o += 8;
            e.Pais = Texto();
            e.Nivel = b[o];
            int n = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o + 1));
            o += 3;
            for (int k = 0; k < n; k++)
            {
                var j = new JogadorEditavel { Pais = Texto(), Nome = Texto() };
                j.Posicao = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
                o += 2;
                e.Jogadores.Add(j);
            }
            e.Treinador = Texto();
            e._resto = b.AsSpan(o).ToArray();
            return e;
        }
        catch (Exception x) when (x is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("arquivo de equipa truncado", x);
        }
    }

    // o .EFT do jogo classico (o mesmo formato do TeamCodec.Write do launcher)
    public byte[] ParaEft()
    {
        var s = new MemoryStream();
        s.Write(Eft.Marca);
        s.Write(_reservado);
        s.Write(Cifra.PorTextoEFT(EquipaJson.Bytes(NomeCompleto)));
        s.Write(Cifra.PorTextoEFT(EquipaJson.Bytes(NomeAbreviado)));
        s.Write(new[] { (byte)(CorLetra >> 16), (byte)(CorLetra >> 8), (byte)CorLetra, _byteLetra });
        s.Write(new[] { (byte)(CorFundo >> 16), (byte)(CorFundo >> 8), (byte)CorFundo, _byteFundo });
        s.Write(Cifra.PorTextoEFT(EquipaJson.Bytes(Pais)));
        s.WriteByte((byte)Math.Clamp(Nivel, 0, 255));
        Span<byte> w = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)Jogadores.Count);
        s.Write(w);
        foreach (var j in Jogadores)
        {
            s.Write(Cifra.PorTextoEFT(EquipaJson.Bytes(j.Pais)));
            s.Write(Cifra.PorTextoEFT(EquipaJson.Bytes(j.Nome)));
            BinaryPrimitives.WriteUInt16LittleEndian(w, (ushort)j.Posicao);
            s.Write(w);
        }
        s.Write(Cifra.PorTextoEFT(EquipaJson.Bytes(Treinador)));
        s.Write(_resto);
        return s.ToArray();
    }

    public string ParaJson() => EquipaJson.DeEft(ParaEft(), Arquivo.Length > 0 ? Arquivo : "EQUIPA.EFT");

    public EquipaEditavel Copia()
    {
        var c = (EquipaEditavel)MemberwiseClone();
        c.Jogadores = Jogadores.Select(j => j.Copia()).ToList();
        return c;
    }

    // igual em tudo o que vai para o arquivo
    public bool MesmoConteudo(EquipaEditavel o) => Arquivo == o.Arquivo && ParaEft().AsSpan().SequenceEqual(o.ParaEft());

    // no arquivo os jogadores vem agrupados G, D, M, A: o novo entra no fim do grupo da
    // posicao dele (TeamCodec.InserirNaPosicao). Devolve o indice.
    public int InserirNaPosicao(JogadorEditavel j)
    {
        int i = 0;
        for (int k = 0; k < Jogadores.Count; k++) if (Jogadores[k].Posicao <= j.Posicao) i = k + 1;
        Jogadores.Insert(i, j);
        return i;
    }

    // ---------------------------------------------------------------- pasta de dados
    // grava o JSON na pasta (teams/<PAIS>/<slug>.json; a mesma equipa muda de pasta se o pais
    // mudou). Uma equipa nova ganha um nome de .EFT livre (ate 8 letras, como o Editor de
    // Equipas do original). Devolve o caminho.
    public string Gravar(string raiz)
    {
        var indice = PastaDados.IndiceEquipas(raiz);
        if (Arquivo.Length == 0) Arquivo = SugerirArquivo(NomeAbreviado.Length > 0 ? NomeAbreviado : NomeCompleto, indice.Keys);
        // o indice aponta para o JSON desta equipa (se outro tem o mesmo "file", fica o nosso)
        if (Caminho != null) indice[Arquivo.ToUpperInvariant()] = Caminho;
        var destino = PastaDados.GravarEquipa(raiz, Arquivo, ParaEft(), indice);
        if (Caminho != null && Caminho != destino && File.Exists(Caminho)) File.Delete(Caminho);
        Caminho = destino;
        return destino;
    }

    // nome de arquivo de ate 8 letras (A-Z 0-9) + .EFT, sem repetir os que ja existem
    public static string SugerirArquivo(string nome, IEnumerable<string> existentes)
    {
        var usados = new HashSet<string>(existentes.Select(x => x.ToUpperInvariant()), StringComparer.Ordinal);
        var b = new string(nome.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(char.ToUpperInvariant).Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9').ToArray());
        if (b.Length == 0) b = "EQUIPA";
        if (b.Length > 8) b = b[..8];
        var s = b;
        for (int n = 2; usados.Contains(s + ".EFT"); n++)
            s = b[..Math.Min(b.Length, 8 - n.ToString(CultureInfo.InvariantCulture).Length)] + n.ToString(CultureInfo.InvariantCulture);
        return s + ".EFT";
    }
}

// As regras do jogo para gravar uma equipa (seg12:275e no jogo classico; docs internos do launcher
// do launcher, "Regras para gravar uma equipe") e o estrangeiro pela Lei Bosman e PLOP
// (seg12:0a2f). Portado de RegrasEquipe (TeamCodec.cs do launcher). As mensagens sao as do
// original, traduzidas por Textos.T.
public sealed class RegrasEquipa
{
    public const int MinJogadores = 14, MaxJogadores = 20, MinCampo = 10, MaxEstrangeiros = 5,
        NivelMin = 1, NivelMax = 20, MaxNomeCompleto = 40, MaxNome = 20;

    public enum Situacao { Nacional, Bosman, Plop, Estrangeiro }

    readonly HashSet<string> _paises, _bosman, _plop;

    public RegrasEquipa(IEnumerable<string> paises, IEnumerable<string> bosman, IEnumerable<string> plop)
    {
        static HashSet<string> H(IEnumerable<string> x) => new(x.Select(c => c.Trim().ToUpperInvariant()), StringComparer.Ordinal);
        _paises = H(paises);
        _bosman = H(bosman);
        _plop = H(plop);
    }

    public static RegrasEquipa DaPasta(string raiz)
    {
        var (b, p) = PastaDados.ListasDoJogo(raiz);
        return new RegrasEquipa(PastaDados.CodigosDosPaises(raiz), b, p);
    }

    // "Liberado": todos os paises estao na Lei Bosman, ninguem conta como estrangeiro
    public bool Liberado => _paises.Count > 0 && _paises.All(_bosman.Contains);

    public bool PaisExiste(string pais) => _paises.Contains(pais.Trim().ToUpperInvariant());

    public Situacao Classificar(string paisJogador, string paisEquipa)
    {
        var j = paisJogador.Trim().ToUpperInvariant();
        var e = paisEquipa.Trim().ToUpperInvariant();
        if (j == e) return Situacao.Nacional;
        if (_bosman.Contains(j) && _bosman.Contains(e)) return Situacao.Bosman;
        if (_plop.Contains(j) && _plop.Contains(e)) return Situacao.Plop;
        return Situacao.Estrangeiro;
    }

    public int Estrangeiros(EquipaEditavel t) => t.Jogadores.Count(j => Classificar(j.Pais, t.Pais) == Situacao.Estrangeiro);

    // os motivos para o jogo nao aceitar a equipa; vazio = pronta
    public List<string> Validar(EquipaEditavel t)
    {
        var erros = new List<string>();
        if (t.NomeCompleto.Trim().Length == 0 || t.NomeAbreviado.Trim().Length == 0) erros.Add(Textos.T("Não está definido o nome da equipa"));
        else if (t.NomeCompleto.Length > MaxNomeCompleto || t.NomeAbreviado.Length > MaxNome)
            erros.Add(Textos.T("O nome é comprido demais (completo até {0} letras, abreviado até {1})", MaxNomeCompleto, MaxNome));
        if (!PaisExiste(t.Pais)) erros.Add(Textos.T("Não está definido o país da equipa"));
        if (t.Nivel is < NivelMin or > NivelMax) erros.Add(Textos.T("Não está definido o nível da equipa"));
        if (t.Jogadores.Count < MinJogadores) erros.Add(Textos.T("Não tem jogadores suficientes (mínimo {0})", MinJogadores));
        if (t.Jogadores.Count > MaxJogadores) erros.Add(Textos.T("Tem demasiados jogadores (máximo {0})", MaxJogadores));
        if (!t.Jogadores.Any(j => j.Posicao == Posicao.Guarda)) erros.Add(Textos.T("Não tem guarda-redes"));
        if (t.Jogadores.Count(j => j.Posicao is >= 1 and <= 3) < MinCampo) erros.Add(Textos.T("Não tem jogadores de campo suficientes (mínimo {0})", MinCampo));
        if (Estrangeiros(t) > MaxEstrangeiros) erros.Add(Textos.T("Tem demasiados jogadores estrangeiros (máximo {0})", MaxEstrangeiros));
        if (t.Treinador.Trim().Length == 0) erros.Add(Textos.T("Não está definido o treinador"));
        var ruins = t.Jogadores.Where(j => ErroNome(j.Nome) != null).Select(j => j.Nome.Length > 0 ? j.Nome : "?").ToList();
        if (ruins.Count > 0) erros.Add(Textos.T("Nomes de jogadores inválidos: {0}", string.Join(", ", ruins.Take(4))));
        return erros;
    }

    // o nome de um jogador ou treinador: ate 20 letras (com ou sem acento), espaco, ponto,
    // hifen ou apostrofo; o Editor de Equipas do original nao aceita numeros
    public static string? ErroNome(string nome, int max = MaxNome)
    {
        var n = nome.Trim();
        if (n.Length == 0) return Textos.T("Escreva o nome.");
        if (n.Length > max) return Textos.T("O nome tem mais de {0} letras.", max);
        if (n.Any(c => char.IsDigit(c) || c > 0xFF || char.IsControl(c)))
            return Textos.T("Use só letras (com ou sem acento), espaço, ponto, hífen ou apóstrofo.");
        return null;
    }
}
