using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;

namespace RetroFoot.Motor.Arquivos;

// Uma equipa em JSON (teams/<PAIS>/<slug>.json da pasta de dados do RetroFoot) <-> o .EFT do
// jogo classico (formato em docs internos do launcher, secao 7; o mesmo do
// TeamCodec.cs do launcher e do Eft.cs do motor). Todos os campos do .EFT vao para o JSON, em
// ordem fixa; o que um .EFT de patch tenha fora do normal (bytes reservados nao zerados, o 4o
// byte das cores, bytes depois do treinador) fica em "eft" para a volta dar o mesmo arquivo,
// byte a byte. Esquema em docs/ferramentas.md.
public static class EquipaJson
{
    public const string Formato = "retrofoot-team";
    public const int Versao = 1;
    static readonly Encoding Latin1 = Encoding.Latin1;
    static readonly string[] Posicoes = { "G", "D", "M", "A" };

    static readonly JsonSerializerOptions OpcoesTexto = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // um texto JSON com as letras acentuadas legiveis
    static string Str(string s) => JsonSerializer.Serialize(s, OpcoesTexto);

    // ---------------------------------------------------------------- .EFT -> JSON
    // nomeArquivo: o nome do .EFT (ex.: PALMEIRA.EFT); a ordem das equipas no jogo novo e a
    // ordem alfabetica desses nomes, por isso ele vai junto
    public static string DeEft(byte[] b, string nomeArquivo)
    {
        if (b.Length < 0x33 || !b.AsSpan(0, 4).SequenceEqual(Eft.Marca))
            throw new InvalidDataException("não é um arquivo de equipa válido (EFa)");
        try
        {
            int o = 0x32;
            string Texto() { var (p, n) = Cifra.TextoEFT(b.AsSpan(o)); o += n; return Latin1.GetString(p); }
            var reservado = b.AsSpan(4, 0x32 - 4).ToArray();
            var nome = Texto();
            var curto = Texto();
            uint letra = Rgb(b, o), fundo = Rgb(b, o + 4);
            byte extraLetra = b[o + 3], extraFundo = b[o + 7];
            o += 8;
            var pais = Texto();
            int nivel = b[o];
            int n = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o + 1));
            o += 3;
            var jogadores = new List<(string Nome, int Posicao, string Pais)>();
            for (int k = 0; k < n; k++)
            {
                var jp = Texto();
                var jn = Texto();
                int pos = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
                o += 2;
                jogadores.Add((jn, pos, jp));
            }
            var treinador = Texto();
            var resto = b.AsSpan(o).ToArray();

            // escrito a mao para cada jogador ficar numa linha (facil de editar e de comparar)
            var sb = new StringBuilder();
            sb.Append("{\n");
            void Campo(string k, string valor, bool virgula = true) =>
                sb.Append("  ").Append(Str(k)).Append(": ").Append(valor).Append(virgula ? ",\n" : "\n");
            Campo("format", Str(Formato));
            Campo("version", Versao.ToString(CultureInfo.InvariantCulture));
            Campo("file", Str(nomeArquivo));
            Campo("name", Str(nome));
            Campo("shortName", Str(curto));
            Campo("country", Str(pais));
            Campo("level", nivel.ToString(CultureInfo.InvariantCulture));
            Campo("textColor", Str(Cor(letra)));
            Campo("backgroundColor", Str(Cor(fundo)));
            Campo("coach", Str(treinador));
            bool temReservado = reservado.Any(x => x != 0);
            bool temExtra = temReservado || extraLetra != 0 || extraFundo != 0 || resto.Length > 0;
            sb.Append("  \"players\": [");
            for (int i = 0; i < jogadores.Count; i++)
            {
                var j = jogadores[i];
                var pos = j.Posicao is >= 0 and < 4 ? Str(Posicoes[j.Posicao]) : j.Posicao.ToString(CultureInfo.InvariantCulture);
                sb.Append(i == 0 ? "\n" : ",\n").Append("    { \"name\": ").Append(Str(j.Nome))
                  .Append(", \"position\": ").Append(pos).Append(", \"country\": ").Append(Str(j.Pais)).Append(" }");
            }
            sb.Append(jogadores.Count > 0 ? "\n  ]" : "]").Append(temExtra ? ",\n" : "\n");
            if (temExtra)
            {
                // so para a volta ao .EFT ser identica; o jogo nao usa
                var e = new List<string>();
                if (temReservado) e.Add($"    \"reserved\": {Str(Convert.ToHexString(reservado))}");
                if (extraLetra != 0) e.Add($"    \"textColorByte4\": {extraLetra}");
                if (extraFundo != 0) e.Add($"    \"backgroundColorByte4\": {extraFundo}");
                if (resto.Length > 0) e.Add($"    \"trailing\": {Str(Convert.ToHexString(resto))}");
                sb.Append("  \"eft\": {\n").Append(string.Join(",\n", e)).Append("\n  }\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }
        catch (Exception x) when (x is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("arquivo de equipa truncado", x);
        }
    }

    static uint Rgb(byte[] b, int o) => (uint)(b[o] << 16 | b[o + 1] << 8 | b[o + 2]);
    static string Cor(uint c) => "#" + c.ToString("X6");

    // ---------------------------------------------------------------- JSON -> .EFT
    public sealed record Lida(string Arquivo, byte[] Bytes);

    static readonly JsonDocumentOptions OpcoesLeitura = new()
    {
        AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip,
    };

    // o .EFT do JSON e o nome dele ("file"; sem ele, o nome do JSON em maiusculas + .EFT)
    public static Lida ParaEft(string json, string? caminhoJson = null)
    {
        if (JsonNode.Parse(json, documentOptions: OpcoesLeitura) is not JsonObject o)
            throw new InvalidDataException("o JSON da equipa não é um objeto");
        var arquivo = Texto(o, "file");
        if (string.IsNullOrWhiteSpace(arquivo))
        {
            var baseNome = caminhoJson == null ? "EQUIPA" : Path.GetFileNameWithoutExtension(caminhoJson);
            arquivo = baseNome.ToUpperInvariant() + ".EFT";
        }
        var extra = o["eft"] as JsonObject;
        var s = new MemoryStream();
        s.Write(Eft.Marca);
        var reservado = extra != null && Texto(extra, "reserved") is { Length: > 0 } hr ? Convert.FromHexString(hr) : new byte[0x32 - 4];
        if (reservado.Length != 0x32 - 4) throw new InvalidDataException("eft.reserved tem de ter 46 bytes");
        s.Write(reservado);
        s.Write(Cifra.PorTextoEFT(Bytes(Texto(o, "name"))));
        s.Write(Cifra.PorTextoEFT(Bytes(Texto(o, "shortName"))));
        PorCor(s, LerCor(Texto(o, "textColor")), extra == null ? 0 : Inteiro(extra, "textColorByte4") ?? 0);
        PorCor(s, LerCor(Texto(o, "backgroundColor")), extra == null ? 0 : Inteiro(extra, "backgroundColorByte4") ?? 0);
        s.Write(Cifra.PorTextoEFT(Bytes(Texto(o, "country"))));
        s.WriteByte((byte)Math.Clamp(Inteiro(o, "level") ?? 1, 0, 255));
        var jogadores = o["players"] as JsonArray ?? new JsonArray();
        Span<byte> w2 = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(w2, (ushort)jogadores.Count);
        s.Write(w2);
        foreach (var no in jogadores)
        {
            if (no is not JsonObject j) throw new InvalidDataException("jogador inválido");
            s.Write(Cifra.PorTextoEFT(Bytes(Texto(j, "country"))));
            s.Write(Cifra.PorTextoEFT(Bytes(Texto(j, "name"))));
            BinaryPrimitives.WriteUInt16LittleEndian(w2, (ushort)Posicao(j["position"]));
            s.Write(w2);
        }
        s.Write(Cifra.PorTextoEFT(Bytes(Texto(o, "coach"))));
        if (extra != null && Texto(extra, "trailing") is { Length: > 0 } ht) s.Write(Convert.FromHexString(ht));
        return new Lida(arquivo.Trim(), s.ToArray());
    }

    public static Lida LerArquivo(string caminho) => ParaEft(File.ReadAllText(caminho, Encoding.UTF8), caminho);

    static int Posicao(JsonNode? n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<string>(out var t))
            {
                t = t.Trim().ToUpperInvariant();
                int k = Array.IndexOf(Posicoes, t);
                if (k >= 0) return k;
                // GK/DF/MF/FW e os nomes por extenso tambem servem
                if (t is "GK" or "GOL" or "GR" || t.StartsWith("GOAL") || t.StartsWith("GUARDA")) return 0;
                if (t is "DF" or "DEF" || t.StartsWith("DEF")) return 1;
                if (t is "MF" or "MC" or "MEI" || t.StartsWith("MID") || t.StartsWith("MED") || t.StartsWith("MEI")) return 2;
                if (t is "FW" or "AT" or "ATA" or "AV" || t.StartsWith("FOR") || t.StartsWith("ATA") || t.StartsWith("AVA") || t.StartsWith("DEL")) return 3;
                if (int.TryParse(t, out i)) return i;
            }
        }
        throw new InvalidDataException("posição inválida (use G, D, M ou A)");
    }

    static uint LerCor(string? s)
    {
        s = (s ?? "").Trim().TrimStart('#');
        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c)) return c;
        throw new InvalidDataException("cor inválida (use #RRGGBB): " + s);
    }

    static void PorCor(Stream s, uint c, int quarto) =>
        s.Write(new[] { (byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)quarto });

    static string? Texto(JsonObject o, string k) =>
        o[k] is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString()) : null;

    static int? Inteiro(JsonObject o, string k)
    {
        if (o[k] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, out i)) return i;
        return null;
    }

    // texto do JSON (UTF-8) -> Latin-1 do jogo: letras fora do Latin-1 perdem o acento; no
    // maximo 255 (o tamanho do texto Pascal)
    public static byte[] Bytes(string? s)
    {
        var b = Latin1.GetBytes(ListaArbitros.ParaLatin1(s ?? ""));
        return b.Length > 255 ? b[..255] : b;
    }

    // ---------------------------------------------------------------- nomes
    // o nome do arquivo: minusculas sem acentos, so a-z 0-9 e hifens (ex.: "sao-paulo-fc")
    public static string Slug(string nome)
    {
        var sb = new StringBuilder();
        foreach (var c in nome.Normalize(NormalizationForm.FormD))
        {
            // acentos, pontos e apostrofos somem ("F.C." -> "fc")
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark || c is '.' or '\'' or '’') continue;
            char l = char.ToLowerInvariant(c);
            if (l is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(l);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var r = sb.ToString().Trim('-');
        if (r.Length > 60) r = r[..60].Trim('-');
        return r.Length > 0 ? r : "equipa";
    }

    // a pasta do pais: o codigo em maiusculas (3 letras; "XXX" se nao houver)
    public static string PastaPais(string pais)
    {
        var p = new string(pais.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (p.Length > 3) p = p[..3];
        return p.Length > 0 ? p : "XXX";
    }

    // o que a pasta e o nome do JSON precisam: pais e nome completo do .EFT
    public static (string Pais, string Nome) Identidade(byte[] eft)
    {
        var e = Eft.Decodificar(eft);
        return (Latin1.GetString(Latin1.GetBytes(e.Pais)), Latin1.GetString(e.NomeCompleto));
    }
}
