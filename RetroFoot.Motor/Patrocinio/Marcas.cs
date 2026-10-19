using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RetroFoot.Motor.Patrocinio;

// Uma marca de SPONSORS\MARCAS.DAT: o registro de 80 bytes que o launcher gera do MARCAS.TXT
// (launcher/tools/patrocinios/gerar_dat.py):
//    0 string[24] nome      25 string[12] logo     38 byte tier      39 byte divisao maxima
//   40 longint verba anual  44 longint cota minima 48 byte reserva pontual (%)
//   49 byte modo (0 qualquer pais, 1 exclusivo, 2 preferido, 3 exceto)
//   50 byte quantos paises  51 8 x 3 bytes os codigos
public sealed class Marca
{
    public const int Tamanho = 80;
    public const byte Qualquer = 0, Exclusivo = 1, Preferido = 2, Exceto = 3;

    public readonly byte[] Registro;

    public Marca(ReadOnlySpan<byte> r) => Registro = r[..Tamanho].ToArray();

    // o string[24] como esta no registro (1 byte de tamanho e 24): vai inteiro para a proposta
    public ReadOnlySpan<byte> NomeBruto => Registro.AsSpan(0, 25);
    public string Nome => Str(0, 24);
    public string Logo => Str(25, 12);
    public byte Tier => Registro[38];
    public byte DivisaoMaxima => Registro[39];
    public int Verba => BinaryPrimitives.ReadInt32LittleEndian(Registro.AsSpan(40));
    public int Cota => BinaryPrimitives.ReadInt32LittleEndian(Registro.AsSpan(44));
    public byte Reserva => Registro[48];
    public byte Modo => Registro[49];
    public byte NumPaises => Registro[50];
    // o k-esimo codigo (3 bytes), lido mesmo alem dos 8 (como o patch, que nao limita)
    public ReadOnlySpan<byte> Pais(int k) => Registro.AsSpan(51 + 3 * k, 3);

    string Str(int o, int max) => Encoding.Latin1.GetString(Registro, o + 1, Math.Min((int)Registro[o], max));

    public override string ToString() => Nome;
}

// O SPONSORS\MARCAS.DAT da pasta do jogo: cabecalho de 8 bytes ("PAT1", versao, quantidade) e
// um registro de 80 bytes por marca. O patch le o arquivo a cada vez, com BlockRead (que falha
// se faltar algum byte): aqui o arquivo inteiro fica em memoria e cada leitura faz a mesma
// verificacao. Marcas.Ler devolve null quando o arquivo nao abre (o Reset falha).
public sealed class Marcas
{
    public const int Cabecalho = 8;

    public readonly byte[] Dados;
    // a pasta SPONSORS (para os logos); null em testes
    public readonly string? Pasta;

    public Marcas(byte[] dados, string? pasta = null)
    {
        Dados = dados;
        Pasta = pasta;
    }

    // <pastaJogo>\SPONSORS\MARCAS.DAT (sem diferenciar maiusculas, como no Windows)
    public static Marcas? Ler(string? pastaJogo)
    {
        if (pastaJogo == null) return null;
        var pasta = Achar(pastaJogo, "SPONSORS");
        var arq = pasta == null ? null : Achar(pasta, "MARCAS.DAT");
        if (arq == null) return null;
        try { return new Marcas(File.ReadAllBytes(arq), pasta); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static string? Achar(string pasta, string nome)
    {
        if (!Directory.Exists(pasta)) return null;
        var direto = Path.Combine(pasta, nome);
        if (File.Exists(direto) || Directory.Exists(direto)) return direto;
        foreach (var f in Directory.EnumerateFileSystemEntries(pasta))
            if (string.Equals(Path.GetFileName(f), nome, StringComparison.OrdinalIgnoreCase)) return f;
        return null;
    }

    // o cabecalho le inteiro?
    public bool CabecalhoLe => Dados.Length >= Cabecalho;
    // a distribuicao so segue com "PA" no comeco (cmp word [REG], 'PA')
    public bool Assinatura => CabecalhoLe && Dados[0] == (byte)'P' && Dados[1] == (byte)'A';
    // a palavra +6 do cabecalho (o sorteio usa so ela, sem conferir a assinatura)
    public int Quantidade => CabecalhoLe ? BinaryPrimitives.ReadUInt16LittleEndian(Dados.AsSpan(6)) : 0;

    // ler_marca: o registro da marca b (1..N), lendo b registros depois do cabecalho; null se o
    // arquivo acaba antes. Com b = 0 o laco do patch da a volta na palavra (65536 leituras):
    // tambem falha.
    public Marca? Registro(int b)
    {
        b &= 0xFFFF;
        if (b == 0) b = 0x10000;
        long fim = Cabecalho + (long)Marca.Tamanho * b;
        if (!CabecalhoLe || fim > Dados.Length) return null;
        return new Marca(Dados.AsSpan((int)(fim - Marca.Tamanho), Marca.Tamanho));
    }

    // as marcas na ordem do arquivo (as que leem inteiras)
    public IEnumerable<Marca> Todas()
    {
        for (int b = 1; b <= Quantidade; b++)
            if (Registro(b) is { } m) yield return m;
            else yield break;
    }

    // o caminho do logo da marca (SPONSORS\<logo>), se o arquivo existe
    public string? CaminhoLogo(Marca m) => Pasta == null || m.Logo.Length == 0 ? null : Achar(Pasta, m.Logo);

    // SPONSORS\FUNDO.BMP: o fundo da janela de proposta
    public string? CaminhoFundo() => Pasta == null ? null : Achar(Pasta, "FUNDO.BMP");
}
