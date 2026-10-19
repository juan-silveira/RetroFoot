using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;

namespace RetroFoot.Motor.NovoJogo;

// Cria um jogo novo (jogo classico, 98.003) a partir dos arquivos do jogo, como o
// executavel faz, ate o estado gravado no 1o save (inicio da 1a jornada). Ordem do
// original (seg05:24d6): selecao de paises (seg06:1604), moeda (seg02:3dc7), seg05:3c2a
// (Randomize, listas, arbitros, equipas por nivel, ano, divisoes, dinheiro/forca/salario/
// estadio), TgetTeamsDlg (humanos), seg05:0148 (historial dos treinadores), seg05:0002
// (inicio da temporada) e seg05:0c41 (jornada 1). (Traduzido de motor/novojogo.)

// um .EFT da pasta EQUIPAS
public sealed record Arquivo(string Nome, Eft Eft);

// registro TCountry (seg12:00a3): +0x04 nome, +0x19 codigo, +0x1D escolhido, +0x1E vagas
public sealed class Pais
{
    public string Codigo = "";
    public byte[] Nome = Array.Empty<byte>();
    public bool Selecionado;
    public int Vagas;
}

public sealed class Dados
{
    // equipas em ordem alfabetica do nome do arquivo (no Windows/Wine e a ordem do
    // diretorio; so importa entre equipas do mesmo nivel e para os IDs)
    public List<Arquivo> Equipas = new();
    public List<Pais> Paises = new();        // COUNTRY.TXE (DS:0x29BC), com POR se faltar
    public List<Pais> Bosman = new();        // CTRGROUP/BOSMAN.TXE (DS:0x29C4)
    public List<Pais> PLOP = new();          // CTRGROUP/PLOP.TXE (DS:0x29C0)
    public List<byte[]> Arbitro = new();     // REFEREE.TXE sem o cabecalho: "CCC Nome"
    public Dictionary<string, string> Mapa = new();
    // RetroFoot: a lista de arbitros ja e a final (referees.tsv da pasta de dados); o jogo
    // novo nao junta os da FIFA de novo
    public bool ArbitrosProntos;

    static readonly Encoding Latin1 = Encoding.Latin1;

    public static Dados Ler(string pasta)
    {
        // a pasta de dados do RetroFoot (countries.tsv, teams/*.json): Arquivos.PastaDados
        if (Arquivos.PastaDados.EhPastaNova(pasta)) return Arquivos.PastaDados.Ler(pasta);
        var d = new Dados();
        d.Paises = LerPaises(Achar(pasta, "COUNTRY.TXE"));
        var grupos = Achar(pasta, "CTRGROUP");
        d.Bosman = LerGrupo(d.Paises, Achar(grupos, "BOSMAN.TXE"));
        d.PLOP = LerGrupo(d.Paises, Achar(grupos, "PLOP.TXE"));
        var arb = Eft.LerTxe(Achar(pasta, "REFEREE.TXE"));
        // seg03:15d6: o 1o texto tem de ser "Referee"; senao nenhum arbitro e lido
        if (arb.Count > 0 && Latin1.GetString(arb[0]) == "Referee") d.Arbitro = arb.Skip(1).ToList();
        var equipas = Achar(pasta, "EQUIPAS");
        // em ordem alfabetica do nome em maiusculas (como o Go: bytes invalidos viram U+FFFD)
        var nomes = ArquivosBrutos.Listar(equipas)
            .Where(e => string.Equals(Path.GetExtension(e.Nome), ".eft", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Nome.ToUpperInvariant(), StringComparer.Ordinal)
            .ToList();
        foreach (var n in nomes)
        {
            try { d.Equipas.Add(new Arquivo(n.Nome, Eft.Decodificar(ArquivosBrutos.Ler(equipas, n)))); }
            catch (Exception e) { throw new InvalidDataException($"{n.Nome}: {e.Message}", e); }
        }
        return d;
    }

    // acha um arquivo ou pasta sem diferenciar maiusculas (como no Windows)
    internal static string Achar(string pasta, string nome)
    {
        if (Directory.Exists(pasta))
            foreach (var e in Directory.EnumerateFileSystemEntries(pasta))
                if (string.Equals(Path.GetFileName(e), nome, StringComparison.OrdinalIgnoreCase)) return e;
        return Path.Combine(pasta, nome);
    }

    // TListOfCountries.Create (seg12:016d): o 1o texto e "Country"; cada outro e "CCC Nome"
    static List<Pais> LerPaises(string arquivo)
    {
        var l = Eft.LerTxe(arquivo);
        var r = new List<Pais>();
        if (l.Count > 0 && Latin1.GetString(l[0]) == "Country")
            foreach (var t in l.Skip(1))
            {
                var (cod, nome) = PartirTxe(t, 20);
                if (cod.Length == 0) continue;
                r.Add(new Pais { Codigo = cod, Nome = nome });
            }
        if (AcharPais(r, "POR") == null) r.Add(new Pais { Codigo = "POR", Nome = "PORTUGAL"u8.ToArray() });
        return r;
    }

    // "CCC Nome" como seg12:016d e seg03:15d6: codigo = letras 1 a 3, nome = 5 em diante
    internal static (string Codigo, byte[] Nome) PartirTxe(byte[] t, int max)
    {
        var cod = new List<byte>();
        var nome = new List<byte>();
        for (int i = 0; i < t.Length; i++)
        {
            if (i < 3) cod.Add(t[i]);
            else if (i >= 4 && nome.Count < max) nome.Add(t[i]);
        }
        return (Latin1.GetString(cod.ToArray()), nome.ToArray());
    }

    // os codigos da Lei Bosman e do PLOP, como o jogo os le ao abrir (DS:29C4 e DS:29C0)
    public static (List<string> Bosman, List<string> Plop) ListasDoJogo(string pasta)
    {
        if (Arquivos.PastaDados.EhPastaNova(pasta)) return Arquivos.PastaDados.ListasDoJogo(pasta);
        var paises = LerPaises(Achar(pasta, "COUNTRY.TXE"));
        var grupos = Achar(pasta, "CTRGROUP");
        return (LerGrupo(paises, Achar(grupos, "BOSMAN.TXE")).Select(p => p.Codigo).ToList(),
                LerGrupo(paises, Achar(grupos, "PLOP.TXE")).Select(p => p.Codigo).ToList());
    }

    // TListOfBosmanCountries (seg12:0545): de cada texto (menos o 1o) valem as 3 primeiras letras
    static List<Pais> LerGrupo(List<Pais> paises, string arquivo)
    {
        var l = Eft.LerTxe(arquivo);
        var r = new List<Pais>();
        for (int i = 1; i < l.Count; i++)
        {
            var t = l[i];
            var cod = Latin1.GetString(t, 0, Math.Min(3, t.Length));
            var p = AcharPais(paises, cod);
            if (p != null) r.Add(p);
        }
        return r;
    }

    // seg12:044f com modo 2 (pelo codigo)
    internal static Pais? AcharPais(List<Pais> l, string codigo)
    {
        if (codigo.Length > 3) codigo = codigo[..3];
        return l.FirstOrDefault(p => p.Codigo == codigo);
    }

    // UpCase (25:2487) letra a letra: so a..z
    internal static string Maiusculas(string s)
    {
        var b = s.ToCharArray();
        for (int i = 0; i < b.Length; i++) if (b[i] >= 'a' && b[i] <= 'z') b[i] = (char)(b[i] - 0x20);
        return new string(b);
    }

    // seg12:0919: o codigo em maiusculas; se nao estiver em COUNTRY.TXE, o substituto de
    // Mapa (o jogo procuraria no elif98.ini e depois perguntaria ao jogador)
    internal string PaisDoArquivo(string cod)
    {
        var c = Maiusculas(cod);
        if (c.Length > 3) c = c[..3];
        if (AcharPais(Paises, c) != null) return c;
        if (Mapa.TryGetValue(c, out var m)) return m;
        throw new InvalidDataException($"país \"{c}\" não está em COUNTRY.TXE (o jogo perguntaria qual usar)");
    }

    // seg12:0a2f: 0 mesmo pais; 1 os dois na Lei Bosman ou os dois na PLOP; 2 estrangeiro
    internal byte Estrangeiro(string paisEquipa, string paisJogador)
    {
        if (paisEquipa == paisJogador) return 0;
        var a = AcharPais(Paises, paisEquipa);
        var b = AcharPais(Paises, paisJogador);
        if (a != null && b != null && Bosman.Contains(a) && Bosman.Contains(b)) return 1;
        if (a != null && b != null && PLOP.Contains(a) && PLOP.Contains(b)) return 1;
        return 2;
    }

    // ---------------------------------------------------------------- selecao de paises
    // o comeco de TselectCountriesDlg.Run (seg06:1604): soma 1 nas vagas do pais de cada
    // equipa e poe o pais na lista quando aparece a 1a equipa dele
    public SelecaoPaises ContarEquipas()
    {
        var s = new SelecaoPaises();
        foreach (var p in Paises) { p.Selecionado = false; p.Vagas = 0; }
        foreach (var a in Equipas)
        {
            s.Total++;
            var p = AcharPais(Paises, PaisDoArquivo(a.Eft.Pais));
            if (p == null) continue;
            p.Vagas++;
            if (p.Vagas == 1) s.Lista.Add(p);
        }
        // a lista e ordenada pelo Windows (LBS_SORT)
        var ordenada = s.Lista.OrderBy(p => p, Comparer<Pais>.Create((x, y) => SelecaoPaises.OrdemListBox(x.Nome, y.Nome))).ToList();
        s.Lista.Clear();
        s.Lista.AddRange(ordenada);
        return s;
    }
}

// o estado de TselectCountriesDlg
public sealed class SelecaoPaises
{
    public const int MinimoEquipas = 34;   // 0x22: 4 divisoes de 8 + 2
    public const int MaximoEquipas = 128;  // 0x80: acima disso o OK tira vagas por sorteio

    public List<Pais> Lista = new();       // a ListBoxOfCountries (Sorted = True)
    public int Total;                      // DS:0x2858: arquivos de equipa lidos
    public int Escolhidas;                 // DS:0x285A: soma das vagas dos escolhidos
    public int Escolhidos;                 // DS:0x2854: paises escolhidos
    public int Minimo;                     // 0 = MinimoEquipas; com N, Regras.MinimoEquipas

    public int MinimoDeEquipas => Minimo > 0 ? Minimo : MinimoEquipas;

    // o clique num pais (seg06:1975)
    public void Alternar(Pais p)
    {
        p.Selecionado = !p.Selecionado;
        if (p.Selecionado) { Escolhidas += p.Vagas; Escolhidos++; }
        else { Escolhidas -= p.Vagas; Escolhidos--; }
    }

    public void Escolher(params string[] codigos)
    {
        foreach (var c in codigos)
        {
            bool ok = false;
            foreach (var p in Lista.Where(p => p.Codigo == c))
            {
                if (!p.Selecionado) Alternar(p);
                ok = true;
            }
            if (!ok) throw new ArgumentException("país sem equipas: " + c);
        }
    }

    // o botao "Todas" (seg06:1c68)
    public void Todos()
    {
        Escolhidas = Total;
        Escolhidos = Lista.Count;
        foreach (var p in Lista) p.Selecionado = true;
    }

    // "Nenhum" (nao existe no original)
    public void Nenhum()
    {
        Escolhidas = 0; Escolhidos = 0;
        foreach (var p in Lista) p.Selecionado = false;
    }

    public bool TodosEscolhidos => Lista.Count > 0 && Escolhidos == Lista.Count;

    // OKBtnClick (seg06:1b95) e o teste do fim de Run (seg06:18ce). O original chama
    // Randomize aqui (o relogio); g faz esse papel. Enquanto houver mais de 128 equipas,
    // sorteia uma posicao da lista; se o pais estiver escolhido e tiver mais de 1 vaga (ou
    // mais de 0, quando cada escolhido tem uma so), sorteia Random(128) e, se sair menor
    // que as vagas, tira uma.
    public void Confirmar(Aleatorio g)
    {
        while (Escolhidas > MaximoEquipas)
        {
            var p = Lista[g.Random(Lista.Count)];
            if (!p.Selecionado) continue;
            int minimo = Escolhidos == Escolhidas ? 0 : 1;
            if (p.Vagas <= minimo) continue;
            if (g.Random(MaximoEquipas) < p.Vagas)
            {
                p.Vagas--;
                Escolhidas--;
                if (p.Vagas == 0) Escolhidos--;
            }
        }
        if (Escolhidas < MinimoDeEquipas) throw new InvalidOperationException($"são precisas pelo menos {MinimoDeEquipas} equipas");
    }

    // a ordem de uma ListBox ordenada do Windows 3.1 (lstrcmpi): sem diferenciar
    // maiusculas e com as acentuadas junto da letra sem acento. INCERTO (como no Go).
    internal static int OrdemListBox(byte[] a, byte[] b)
    {
        var ca = Chave(a); var cb = Chave(b);
        for (int i = 0; i < ca.Length && i < cb.Length; i++)
            if (ca[i] != cb[i]) return ca[i] < cb[i] ? -1 : 1;
        return ca.Length.CompareTo(cb.Length);
    }

    static readonly Dictionary<byte, byte> MapaAcentos = MontarMapa();

    static Dictionary<byte, byte> MontarMapa()
    {
        const string de = "ÀÁÂÃÄÅàáâãäåÇçÈÉÊËèéêëÌÍÎÏìíîïÑñÒÓÔÕÖòóôõöÙÚÛÜùúûüÝýÿ";
        const string para = "AAAAAAAAAAAACCEEEEEEEEIIIIIIIINNOOOOOOOOOOUUUUUUUUYYY";
        var m = new Dictionary<byte, byte>();
        for (int i = 0; i < de.Length; i++) m[(byte)de[i]] = (byte)para[i];
        return m;
    }

    static byte[] Chave(byte[] s)
    {
        var r = new byte[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            byte c = s[i];
            if (MapaAcentos.TryGetValue(c, out var m)) c = m;
            if (c >= 'a' && c <= 'z') c -= 0x20;
            r[i] = c;
        }
        return r;
    }
}
