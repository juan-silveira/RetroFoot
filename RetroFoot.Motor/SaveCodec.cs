using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RetroFoot.Motor.Codec
{
    public class SavePlayer
    {
        public string Nome { get; set; } = "";
        public string Posicao { get; set; } = "G";
        public bool Estrela { get; set; }
        public int Comportamento { get; set; }
        public int RecordStartInEft { get; set; }
        public int RecordSizeInEft { get; set; }
        public int ForcaOffsetInFile { get; set; }
        public int SalarioOffsetInFile { get; set; }
        public int PosicaoOffsetInFile { get; set; }
        public int EstrelaOffsetInFile { get; set; }
        public int Forca { get; set; }
        public int Salario { get; set; }
        // Atributos que o jogo deriva do nome ao criar o jogo, mas depois le do
        // save (docs internos do launcher): editar aqui muda o jogo
        public int Nota { get; set; }
        public int Lesao { get; set; }
        // Historial do jogador (so leitura): t32 jogos, t36 golos na epoca, t40 lesoes, t44 vermelhos
        public int Jogos { get; set; }
        public int Gols { get; set; }
        public int Lesoes { get; set; }
        public int Expulsoes { get; set; }
        // Cartoes amarelos (patch do launcher): os 2 bytes de cima das lesoes (t42-t43)
        // e das expulsoes (t46 acumulados; t47 e a marca do jogo do ultimo amarelo)
        public int AmarelosCarreira { get; set; }
        public int AmarelosAcumulados { get; set; }
        // So o byte t46 (acumulados); t47 (marca do jogo) nunca e escrito
        public int AmarelosAcumuladosOffsetInFile { get; set; } = -1;
        public int NotaOffsetInFile { get; set; } = -1;
        public int LesaoOffsetInFile { get; set; } = -1;
        public int ComportamentoOffsetInFile { get; set; } = -1;
        public int Suspensao { get; set; }       // jogos ("S")
        public int JogosLesionado { get; set; }  // jogos ("L")
        public int SuspensaoOffsetInFile { get; set; } = -1;
        // Nacionalidade (codigo de 3 letras, ex.: "BRA"): inicio do registro
        public string Pais { get; set; } = "";
        public int JogosLesionadoOffsetInFile { get; set; } = -1;
    }

    public class SaveTeam
    {
        public string Nome { get; set; } = "";
        // Nome curto (o "abreviado" da equipe), o que o jogo mostra nas tabelas
        public string NomeCurto { get; set; } = "";
        public string NomeExibido => NomeCurto.Length > 0 ? NomeCurto : Nome;
        public int EftStartOffset { get; set; }
        public long Verba { get; set; }
        public int VerbaOffset { get; set; } = -1;
        // Cores RGB (0xRRGGBB) da letra e do fundo; -1 no offset = nao achadas
        public int CorLetra { get; set; }
        public int CorFundo { get; set; }
        public int CoresOffset { get; set; } = -1;
        public double Moral { get; set; }         // 0..2 (barra do jogo: moral x 10 / 20)
        public int MoralOffset { get; set; } = -1;
        public int Estadio { get; set; }          // N: capacidade = 5000 x N
        public int EstadioOffset { get; set; } = -1;
        public List<SavePlayer> Players { get; } = new List<SavePlayer>();
        public string Pais { get; set; } = "";     // pais da equipe (3 letras)
        // Identificador da equipe (2 bytes antes do "EFa") e do seu treinador
        // (2 bytes logo depois do moral; docs internos do launcher, Treinadores)
        public int Id { get; set; } = -1;
        public int TecnicoId { get; set; } = -1;
        public int TecnicoOffset { get; set; } = -1;
        // Divisao em que joga ("1ª Divisão" ... ou "Distrital"); vazio se nao achou
        public string Divisao { get; set; } = "";
        // So equipes das divisoes podem ter treinador humano: no Distrital o jogo
        // da "List index out of bounds" (testado no Android)
        public bool PodeTerHumano => Divisao.Length > 0 && !Divisao.StartsWith("Distrital");
    }

    public class SaveTecnico
    {
        public int Id { get; set; }
        public string Nome { get; set; } = "";
        public bool Humano { get; set; }
    }

    public class SaveFile
    {
        public byte[] RawBytes { get; set; } = Array.Empty<byte>();
        public int Ano { get; set; }
        public double Inflacao { get; set; }      // o jogo mostra Inflacao x 10
        public int InflacaoOffset { get; set; } = -1;
        public List<SaveTeam> Teams { get; } = new List<SaveTeam>();
        public List<SaveTecnico> Tecnicos { get; } = new List<SaveTecnico>();
        // Equipe do humano "da vez" (cabecalho, 1 + tamanho + 2): se apontar para
        // uma equipe do computador, o jogo carrega e ja disputa a rodada
        public int HumanoDaVez { get; set; } = -1;
        public int HumanoDaVezOffset { get; set; } = -1;

        public SaveTeam? TimeDoTecnico(SaveTecnico tec)
        {
            foreach (var t in Teams) if (t.TecnicoId == tec.Id) return t;
            return null;
        }

        public SaveTecnico? Tecnico(int id)
        {
            foreach (var t in Tecnicos) if (t.Id == id) return t;
            return null;
        }
    }

    // Codec de save .e98 do jogo classico.
    //
    // Estrutura DETERMINISTICA (v0.4.19, verificado em 200+ records e 9 saves):
    //
    // Cada EFT body decodificado com Caesar rolante:
    //   plain[i] = (cipher[i] - delta) mod 256
    //   delta = (delta + plain[i] - 0x20) mod 256
    //
    // Records dentro do EFT identificados por: raw[+0] == 0x03 (Pascal length
    // prefix da nacionalidade) + decoded[+1..+3] eh 3 letras minusculas.
    //
    // Player record:
    //   raw[+0] = 0x03 (marker Pascal)
    //   decoded[+1..+3] = nacionalidade (3 lowercase: bra, por, esp, etc.)
    //   raw[+4] = NL (name length in bytes)
    //   decoded[+5..+5+NL-1] = nome (1a letra unshifted lowercase, resto
    //                          shifted +0x20, ultima char shifted +0x40 as vezes)
    //   record_size = NL + 55
    //   raw[+sz-50] = posicao (0=G, 1=D, 2=M, 3=A)
    //   raw[+sz-49] = estrela (0/1)
    //   raw[+sz-48] = forca (0-99)
    //   raw[+sz-33] = comportamento (0-5)
    //   raw[+sz-25..sz-22] = salario int32 LE
    //
    // Primeiro record em cada EFT eh TEAM HEADER (178 bytes fixo). Players
    // comecam em records[1] onwards.
    //
    // VERBA do time: uint32 LE em first_record_offset_in_file + 0x8E.
    //
    // Team name: byte em decoded[0x32] = 0x20 + name_length. Nome comeca em
    // decoded[0x33] e tem esse tamanho. Chars: lowercase, digits shifted
    // (0x50..0x59 = 0..9), acentos ISO-8859-1 diretos.
    public static class SaveCodec
    {
        private static readonly byte[] EFT_MAGIC = { (byte)'E', (byte)'F', (byte)'a', 0 };
        // Estadio: o jogo so deixa construir ate N = 24 (120.000 lugares; botao "Construir"
        // desativado em N >= 0x18, seg07:29dd). Na leitura aceita ate 40 (saves ja editados).
        public const int ESTADIO_MAX = 24, ESTADIO_LEITURA_MAX = 40;
        public const long DINHEIRO_MAX = 999_999_999;
        // Suspensao sorteada de 1 a 4 jogos, lesao de 1 a 20 (seg03:48be, seg03:4940)
        public const int SUSPENSAO_MAX = 4, JOGOS_LESIONADO_MAX = 20;
        // Amarelos acumulados: o 3o amarelo vira 1 jogo de suspensao, entao 0 a 2
        public const int AMARELOS_ACUMULADOS_MAX = 2;
        public const double INFLACAO_MIN = 0.5, INFLACAO_MAX = 10.0;
        public const double MORAL_MAX = 2.0;
        public const int FORCA_MIN = 1;
        // A forca e uma word que o jogo le com sinal; o limite de verdade de cada jogador e o
        // ForcaMaximaNoElenco (contas do time)
        public const int FORCA_MAX = 32767;

        // Maior forca que o jogo ainda usa direito: o peso do sorteio do autor do gol
        // (seg03:0f69) e posicao^2 x forca x Nota em 16 bits com sinal. Acima de 32767 o
        // peso fica negativo e o jogador nunca marca (e anula gols do time). Goleiro
        // (posicao 0) tem peso 0: vale o FORCA_MAX. So o jogador; o time e o ForcaMaximaNoElenco.
        public static int ForcaMaxima(string posicao, int nota)
        {
            int p = "GDMA".IndexOf(posicao ?? "G", StringComparison.Ordinal);
            if (p <= 0) return FORCA_MAX;
            return Math.Min(FORCA_MAX, 32767 / (p * p * Math.Max(1, Math.Min(10, nota))));
        }
        // Limites do elenco: contas que o jogo faz em 16 bits (docs internos do launcher,
        // "Limites de 16 bits"). Passando deles o resultado "da a volta" e a partida sai errada.
        public const int SOMA_PESOS_MAX = 65535;   // soma dos pesos de gol (seg03:106c, Random da word baixa)
        public const int SOMA_FORCA_MAX = 32767;   // soma da forca do elenco (seg03:4f20, fild word)
        public const int ESCALACAO_MAX = 32766;    // 3G + D + M + A do time; o do adversario soma junto (seg03:4686)

        public static long PesoGol(string posicao, int forca, int nota)
        {
            int p = Math.Max(0, "GDMA".IndexOf(posicao ?? "G", StringComparison.Ordinal));
            return (long)p * p * forca * nota;
        }

        // O que o jogador soma no setor quando esta em campo (seg03:397d): forca + 2 se tem
        // estrela + 3 se e do pais do clube (contado sempre, pior caso); o goleiro vale 3 vezes
        public static long PesoEscalacao(string posicao, int forca, bool estrela) =>
            (forca + (estrela ? 2 : 0) + 3L) * (posicao == "G" ? 3 : 1);

        /// Maior forca que o jogador pode ter com a Nota dada sem estourar nenhum limite
        /// (o dele e os do elenco, com os outros jogadores como estao). 0 = nenhuma serve.
        public static int ForcaMaximaNoElenco(IList<SavePlayer> elenco, SavePlayer editado, int nota, bool estrela)
        {
            var outros = elenco.Where(x => x != editado).ToList();
            long max = ForcaMaxima(editado.Posicao, nota);
            long peso1 = PesoGol(editado.Posicao, 1, nota);
            if (peso1 > 0)
                max = Math.Min(max, (SOMA_PESOS_MAX - outros.Sum(x => PesoGol(x.Posicao, x.Forca, x.Nota))) / peso1);
            max = Math.Min(max, SOMA_FORCA_MAX - outros.Sum(x => (long)x.Forca));
            // na escalacao mais forte ele entra com os 10 maiores dos outros
            long top10 = outros.Select(x => PesoEscalacao(x.Posicao, x.Forca, x.Estrela)).OrderByDescending(v => v).Take(10).Sum();
            max = Math.Min(max, (ESCALACAO_MAX - top10) / (editado.Posicao == "G" ? 3 : 1) - (estrela ? 2 : 0) - 3);
            return (int)Math.Max(0, max);
        }

        /// Confere o elenco com o jogador editado nos valores novos; devolve a mensagem do
        /// primeiro limite estourado, ou null.
        public static string? LimitesDoElenco(IList<SavePlayer> elenco, SavePlayer editado, int forca, int nota, bool estrela)
        {
            var erro = Estouro(elenco, editado, forca, nota, estrela);
            if (erro == null) return null;
            int max = ForcaMaximaNoElenco(elenco, editado, nota, estrela);
            return erro + (max >= FORCA_MIN
                ? $"\n\nCom Nota {nota}, a força máxima dele neste elenco é {max:N0}."
                : "\n\nO resto do elenco já passa do limite: diminua a força de outros jogadores.");
        }

        private static string? Estouro(IList<SavePlayer> elenco, SavePlayer editado, int forca, int nota, bool estrela)
        {
            int F(SavePlayer x) => x == editado ? forca : x.Forca;
            int N(SavePlayer x) => x == editado ? nota : x.Nota;
            bool E(SavePlayer x) => x == editado ? estrela : x.Estrela;

            long pesos = elenco.Sum(x => PesoGol(x.Posicao, F(x), N(x)));
            if (pesos > SOMA_PESOS_MAX)
                return $"A soma dos pesos de gol do elenco ficaria em {pesos:N0} (máximo {SOMA_PESOS_MAX:N0}).\n" +
                    "Peso de cada jogador = posição² × força × Nota (G = 0, D = 1, M = 4, A = 9).\n" +
                    "Acima disso o autor dos gols é sempre o mesmo jogador, ou o time deixa de marcar.";
            long soma = elenco.Sum(x => (long)F(x));
            if (soma > SOMA_FORCA_MAX)
                return $"A soma da força do elenco ficaria em {soma:N0} (máximo {SOMA_FORCA_MAX:N0}).\n" +
                    "Acima disso o público dos jogos sai errado.";
            long escal = elenco.Select(x => PesoEscalacao(x.Posicao, F(x), E(x)))
                .OrderByDescending(v => v).Take(11).Sum();
            if (escal > ESCALACAO_MAX)
                return $"A escalação mais forte possível somaria {escal:N0} (máximo {ESCALACAO_MAX:N0}).\n" +
                    "Conta: 11 jogadores com maior (força + 2 se estrela + 3), o goleiro valendo 3 vezes.\n" +
                    "Acima disso a força dos times na partida sai errada.";
            return null;
        }

        public const int FORCA_WARN_ABOVE = 50;
        public const int SALARIO_MIN = 50;
        // O salario e um inteiro de 4 bytes (raw[sz-25..sz-22]); o jogo limita os
        // que ele calcula a 50000 x moeda, mas guarda valores maiores sem problema
        public const int SALARIO_MAX = 9999999;

        public static readonly string[] ComportamentoLabels = {
            "Fair Play", "Cordeirinho", "Cavalheiro",
            "Caneleiro", "Caceteiro", "Sarrafeiro"
        };

        // Regra do jogo (seg03:7516): estrela = Nota >= 8 e posicao Meio ou Avancado
        public static bool TemEstrela(string posicao, int nota) => nota >= 8 && (posicao == "M" || posicao == "A");

        // Ordem da tela do time no jogo: G, D, M, A e, dentro de cada posicao, nome em ordem
        // alfabetica (o save guarda outra ordem, que muda a cada rodada)
        public static List<SavePlayer> OrdemDoJogo(IEnumerable<SavePlayer> jogadores) =>
            jogadores.OrderBy(j => "GDMA".IndexOf(j.Posicao, StringComparison.Ordinal) is var p && p >= 0 ? p : 9)
                .ThenBy(j => SemAcento(j.Nome), StringComparer.Ordinal).ToList();

        // Sem depender de cultura (o build do Linux roda em globalizacao invariante)
        private static string SemAcento(string s)
        {
            const string com = "ÁÀÂÃÄÉÈÊËÍÌÎÏÓÒÔÕÖÚÙÛÜÇÑáàâãäéèêëíìîïóòôõöúùûüçñ", sem = "AAAAAEEEEIIIIOOOOOUUUUCNaaaaaeeeeiiiiooooouuuucn";
            var c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++) { int k = com.IndexOf(c[i]); if (k >= 0) c[i] = sem[k]; }
            return new string(c).ToUpperInvariant();
        }

        public static SaveFile Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var sf = new SaveFile { RawBytes = bytes };
            LerTecnicos(bytes, sf);

            // O save comeca com um texto cifrado de tamanho variavel (1 byte de
            // tamanho); 7 bytes depois dele vem o ano (int32) e logo a inflacao
            // (real de 10 bytes)
            int anoOff = bytes.Length > 0 ? 1 + bytes[0] + 7 : 0;
            if (anoOff + 14 <= bytes.Length)
            {
                int ano = BitConverter.ToInt32(bytes, anoOff);
                double inf = Ext80(bytes, anoOff + 4);
                if (ano >= 1900 && ano <= 3000 && inf >= 0.3 && inf <= 12)
                {
                    sf.Ano = ano;
                    sf.Inflacao = inf;
                    sf.InflacaoOffset = anoOff + 4;
                }
            }

            var eftPositions = FindAllEftStarts(bytes);
            for (int e = 0; e < eftPositions.Count; e++)
            {
                int eftStart = eftPositions[e];
                int eftEnd = e + 1 < eftPositions.Count ? eftPositions[e + 1] : bytes.Length;
                var team = new SaveTeam { EftStartOffset = eftStart };
                if (eftStart >= 2) team.Id = BitConverter.ToUInt16(bytes, eftStart - 2);

                int bodyStart = eftStart + 4;
                var decoded = DecodeCaesar(bytes, bodyStart, eftEnd);

                team.Nome = ExtractTeamName(decoded);
                team.NomeCurto = ExtractNomeCurto(bytes, decoded, eftStart, eftEnd);

                // Cores: depois dos dois nomes (textos Pascal) em 0x32
                int o = eftStart + 0x32;
                if (o < eftEnd && bytes[o] < 60)
                {
                    o += 1 + bytes[o];
                    if (o < eftEnd && bytes[o] < 60)
                    {
                        o += 1 + bytes[o];
                        if (o + 8 <= eftEnd && bytes[o + 3] == 0 && bytes[o + 7] == 0)
                        {
                            team.CoresOffset = o;
                            team.CorLetra = bytes[o] << 16 | bytes[o + 1] << 8 | bytes[o + 2];
                            team.CorFundo = bytes[o + 4] << 16 | bytes[o + 5] << 8 | bytes[o + 6];
                        }
                    }
                }

                // Estadio: N 9 bytes antes do EFa seguinte (depois dele: real de 6
                // bytes e o identificador da proxima equipe); na ultima equipe, 7
                // bytes antes da lista de identificadores
                int nOff = e + 1 < eftPositions.Count ? eftPositions[e + 1] - 9 : UltimoEstadio(bytes, eftPositions);
                if (nOff > eftStart && bytes[nOff] >= 1 && bytes[nOff] <= ESTADIO_LEITURA_MAX)
                {
                    team.EstadioOffset = nOff;
                    team.Estadio = bytes[nOff];
                }

                // Detecta player records: raw byte 0x03 + decoded 3 lowercase
                var records = new List<(int Offset, int NL)>();
                int bodyLen = eftEnd - bodyStart;
                for (int i = 0; i < bodyLen - 5; i++)
                {
                    if (bytes[bodyStart + i] != 0x03) continue;
                    if (i + 4 >= decoded.Length) continue;
                    byte a = decoded[i + 1], b = decoded[i + 2], c = decoded[i + 3];
                    if (!(a >= 'a' && a <= 'z' && b >= 'a' && b <= 'z' && c >= 'a' && c <= 'z'))
                        continue;
                    int NL = bytes[bodyStart + i + 4];
                    if (NL <= 0 || NL >= 40) continue;
                    records.Add((i, NL));
                }

                if (records.Count == 0) { sf.Teams.Add(team); continue; }

                // Verba: uint32 LE em first_record + 0x8E do arquivo
                int firstRecFileOff = bodyStart + records[0].Offset;
                team.Pais = PaisDoRegistro(decoded, records[0].Offset);
                team.VerbaOffset = firstRecFileOff + 0x8E;
                if (team.VerbaOffset + 4 <= bytes.Length)
                    team.Verba = (uint)BitConverter.ToInt32(bytes, team.VerbaOffset);

                // Moral: real de 6 bytes 74 bytes depois do inicio do pais
                int moralOff = firstRecFileOff + 74;
                if (moralOff + 6 <= bytes.Length)
                {
                    double m = Real48(bytes, moralOff);
                    if (m >= 0 && m <= 10) { team.Moral = m; team.MoralOffset = moralOff; }
                }
                int tecOff = moralOff + 6;
                if (sf.Tecnicos.Count > 0 && tecOff + 2 <= bytes.Length && sf.Tecnico(BitConverter.ToUInt16(bytes, tecOff)) != null)
                {
                    team.TecnicoOffset = tecOff;
                    team.TecnicoId = BitConverter.ToUInt16(bytes, tecOff);
                }

                // Skip records[0] (team header). Players from records[1..]
                for (int idx = 1; idx < records.Count; idx++)
                {
                    var (recOff, NL) = records[idx];
                    int recSize = NL + 55;
                    if (recOff + recSize > decoded.Length) continue;

                    int posOff = bodyStart + recOff + recSize - 50;
                    int starOff = bodyStart + recOff + recSize - 49;
                    int forcaOff = bodyStart + recOff + recSize - 48;
                    int compOff = bodyStart + recOff + recSize - 33;
                    int salarioOff = bodyStart + recOff + recSize - 25;
                    int notaOff = bodyStart + recOff + recSize - 27;
                    int lesaoOff = bodyStart + recOff + recSize - 29;
                    int suspOff = bodyStart + recOff + recSize - 35;
                    int lesJogosOff = bodyStart + recOff + recSize - 31;

                    if (salarioOff + 4 > bytes.Length) break;

                    // Forca eh uint16 LE em [sz-48..sz-47]. Normal 0-99 usa
                    // so o low byte; user pode setar ate 9999 (uint16 max).
                    int forcaVal = BitConverter.ToUInt16(bytes, forcaOff);
                    var p = new SavePlayer
                    {
                        Nome = ExtractPlayerName(decoded, recOff, NL),
                        Pais = PaisDoRegistro(decoded, recOff),
                        RecordStartInEft = recOff,
                        RecordSizeInEft = recSize,
                        Posicao = PosicaoLabel(bytes[posOff]),
                        Estrela = bytes[starOff] != 0,
                        Forca = forcaVal,
                        Comportamento = bytes[compOff],
                        PosicaoOffsetInFile = posOff,
                        EstrelaOffsetInFile = starOff,
                        ForcaOffsetInFile = forcaOff,
                        SalarioOffsetInFile = salarioOff,
                        Salario = BitConverter.ToInt32(bytes, salarioOff),
                        Nota = BitConverter.ToInt16(bytes, notaOff),
                        Lesao = BitConverter.ToInt16(bytes, lesaoOff),
                        NotaOffsetInFile = notaOff,
                        LesaoOffsetInFile = lesaoOff,
                        ComportamentoOffsetInFile = compOff,
                        Suspensao = BitConverter.ToInt16(bytes, suspOff),
                        JogosLesionado = BitConverter.ToInt16(bytes, lesJogosOff),
                        SuspensaoOffsetInFile = suspOff,
                        JogosLesionadoOffsetInFile = lesJogosOff,
                        Jogos = BitConverter.ToInt32(bytes, bodyStart + recOff + recSize - 18),
                        Gols = BitConverter.ToInt32(bytes, bodyStart + recOff + recSize - 14),
                        Lesoes = BitConverter.ToUInt16(bytes, bodyStart + recOff + recSize - 10),
                        AmarelosCarreira = BitConverter.ToUInt16(bytes, bodyStart + recOff + recSize - 8),
                        Expulsoes = BitConverter.ToUInt16(bytes, bodyStart + recOff + recSize - 6),
                        AmarelosAcumulados = bytes[bodyStart + recOff + recSize - 4],
                        AmarelosAcumuladosOffsetInFile = bodyStart + recOff + recSize - 4,
                    };
                    team.Players.Add(p);
                }

                sf.Teams.Add(team);
            }
            LerDivisoes(bytes, eftPositions, sf);
            return sf;
        }

        // Lista de treinadores: contador em 1 + tamanho do cabecalho + 132; cada um tem
        // id, nome (cada byte = letra + byte anterior), 1 byte humano, 14 bytes,
        // 2 bytes, quantidade do historico e 7 bytes por entrada. Se algo nao fechar,
        // fica sem treinadores (o editor esconde a troca de equipe).
        private static void LerTecnicos(byte[] b, SaveFile sf)
        {
            if (b.Length < 2) return;
            int o = 1 + b[0] + 132;
            int primeiraEquipe = IndexOf(b, EFT_MAGIC, 0);
            if (o + 2 > b.Length || primeiraEquipe < 0) return;
            int n = BitConverter.ToUInt16(b, o);
            o += 2;
            var lidos = new List<SaveTecnico>();
            for (int k = 0; k < n; k++)
            {
                if (o + 3 > primeiraEquipe) return;
                var t = new SaveTecnico { Id = BitConverter.ToUInt16(b, o) };
                int nl = b[o + 2], p = o + 3 + nl;
                if (nl == 0 || nl > 40 || p + 19 > primeiraEquipe) return;
                var nome = new StringBuilder();
                int anterior = nl;
                for (int i = o + 3; i < p; i++)
                {
                    int c = (b[i] - anterior) & 0xFF;
                    if (c < 0x20) return;
                    nome.Append((char)c);
                    anterior = b[i];
                }
                t.Nome = nome.ToString();
                if (b[p] > 1) return;
                t.Humano = b[p] == 1;
                o = p + 19 + 7 * BitConverter.ToUInt16(b, p + 17);
                lidos.Add(t);
            }
            if (o > primeiraEquipe) return;
            sf.Tecnicos.AddRange(lidos);
            sf.HumanoDaVezOffset = 1 + b[0] + 2;
            sf.HumanoDaVez = BitConverter.ToUInt16(b, sf.HumanoDaVezOffset);
        }

        // Depois da lista ordenada de equipes: quantidade de divisoes e, para cada uma,
        // 4 bytes, D (2 bytes), nome (string de 20), quantidade e identificadores. Em
        // alguns saves a lista nao bate (o jogo embaralha os ids depois de algumas
        // temporadas, ou ela so tem as equipes das divisoes): ai a tabela e procurada
        // depois da ultima equipe
        private static void LerDivisoes(byte[] b, List<int> efts, SaveFile sf)
        {
            var conhecidos = new HashSet<int>();
            foreach (int st in efts) if (st >= 2) conhecidos.Add(BitConverter.ToUInt16(b, st - 2));
            Dictionary<int, string> div = null;
            int p = ListaOrdenada(b, efts);
            if (p >= 0) div = TabelaDivisoes(b, p + 2 + 2 * BitConverter.ToUInt16(b, p), conhecidos);
            for (int o = efts.Count == 0 ? b.Length : efts[efts.Count - 1]; div == null && o + 2 < b.Length; o++)
                div = TabelaDivisoes(b, o, conhecidos);
            if (div == null) return;
            foreach (var t in sf.Teams)
                if (div.TryGetValue(t.Id, out var nome)) t.Divisao = nome;
        }

        // A tabela so vale se todos os ids forem de equipes do save, sem repetir, e
        // cobrirem quase todas (null se nao for a tabela)
        private static Dictionary<int, string> TabelaDivisoes(byte[] b, int o, HashSet<int> conhecidos)
        {
            if (o + 2 > b.Length) return null;
            int nd = BitConverter.ToUInt16(b, o);
            o += 2;
            if (nd <= 0 || nd > 50) return null;
            var div = new Dictionary<int, string>();
            for (int d = 0; d < nd; d++)
            {
                if (o + 4 + 2 + 21 + 2 > b.Length) return null;
                int no = o + 6, nl = b[no];
                if (nl == 0 || nl > 20) return null;
                string nome = Encoding.GetEncoding("ISO-8859-1").GetString(b, no + 1, nl);
                int n = BitConverter.ToUInt16(b, no + 21);
                o = no + 23;
                if (n > conhecidos.Count || o + 2 * n > b.Length) return null;
                for (int k = 0; k < n; k++)
                {
                    int id = BitConverter.ToUInt16(b, o + 2 * k);
                    if (!conhecidos.Contains(id) || div.ContainsKey(id)) return null;
                    div[id] = nome;
                }
                o += 2 * n;
            }
            return div.Count >= conhecidos.Count - 4 ? div : null;
        }

        /// <summary>
        /// Poe o treinador na equipe destino como a "chicotada psicologica" do jogo
        /// (seg03:4f40): as duas equipes trocam de treinador e ficam com moral 1,0.
        /// O humano "da vez" acompanha a troca.
        /// </summary>
        public static void TrocarEquipe(SaveFile sf, SaveTecnico tec, SaveTeam destino)
        {
            var origem = sf.TimeDoTecnico(tec) ?? throw new InvalidOperationException($"{tec.Nome} não treina nenhuma equipe.");
            if (origem == destino) return;
            if (!destino.PodeTerHumano)
                throw new InvalidOperationException($"{destino.Nome} não está numa divisão (o jogo quebra com treinador humano no Distrital).");
            if (destino.TecnicoOffset < 0) throw new InvalidOperationException($"Não achei o treinador de {destino.Nome}.");
            int outro = destino.TecnicoId;
            destino.TecnicoId = tec.Id;
            origem.TecnicoId = outro;
            if (origem.MoralOffset > 0) origem.Moral = 1.0;
            if (destino.MoralOffset > 0) destino.Moral = 1.0;
            if (sf.HumanoDaVez == origem.Id) sf.HumanoDaVez = destino.Id;
            else if (sf.HumanoDaVez == destino.Id) sf.HumanoDaVez = origem.Id;
        }

        public static void Write(string path, SaveFile sf)
        {
            var bytes = (byte[])sf.RawBytes.Clone();
            if (sf.HumanoDaVezOffset > 0)
                Array.Copy(BitConverter.GetBytes((ushort)sf.HumanoDaVez), 0, bytes, sf.HumanoDaVezOffset, 2);
            // So regrava se mudou: o real de 10 bytes tem mais precisao que double
            if (sf.InflacaoOffset > 0 && Math.Abs(sf.Inflacao - Ext80(bytes, sf.InflacaoOffset)) > 1e-9)
                PutExt80(bytes, sf.InflacaoOffset, Math.Max(INFLACAO_MIN, Math.Min(INFLACAO_MAX, sf.Inflacao)));
            foreach (var team in sf.Teams)
            {
                if (team.CoresOffset > 0)
                {
                    PutRgb(bytes, team.CoresOffset, team.CorLetra);
                    PutRgb(bytes, team.CoresOffset + 4, team.CorFundo);
                }
                if (team.TecnicoOffset > 0)
                    Array.Copy(BitConverter.GetBytes((ushort)team.TecnicoId), 0, bytes, team.TecnicoOffset, 2);
                if (team.MoralOffset > 0 && Math.Abs(team.Moral - Real48(bytes, team.MoralOffset)) > 1e-9)
                    PutReal48(bytes, team.MoralOffset, Math.Max(0, Math.Min(MORAL_MAX, team.Moral)));
                if (team.EstadioOffset > 0)
                    bytes[team.EstadioOffset] = (byte)Math.Max(1, Math.Min(ESTADIO_MAX, team.Estadio));
                if (team.VerbaOffset > 0 && team.VerbaOffset + 4 <= bytes.Length)
                {
                    var v = (uint)Math.Max(0, Math.Min(DINHEIRO_MAX, team.Verba));
                    var vb = BitConverter.GetBytes(v);
                    Array.Copy(vb, 0, bytes, team.VerbaOffset, 4);
                }
                foreach (var p in team.Players)
                {
                    if (p.ForcaOffsetInFile > 0 && p.ForcaOffsetInFile + 2 <= bytes.Length)
                    {
                        int f = Math.Max(FORCA_MIN, Math.Min(FORCA_MAX, p.Forca));
                        var fb = BitConverter.GetBytes((ushort)f);
                        bytes[p.ForcaOffsetInFile] = fb[0];
                        bytes[p.ForcaOffsetInFile + 1] = fb[1];
                    }
                    if (p.SalarioOffsetInFile > 0 && p.SalarioOffsetInFile + 4 <= bytes.Length)
                    {
                        int s = Math.Max(SALARIO_MIN, Math.Min(SALARIO_MAX, p.Salario));
                        Array.Copy(BitConverter.GetBytes(s), 0, bytes, p.SalarioOffsetInFile, 4);
                    }
                    if (p.NotaOffsetInFile > 0)
                        Array.Copy(BitConverter.GetBytes((short)Math.Max(1, Math.Min(10, p.Nota))), 0, bytes, p.NotaOffsetInFile, 2);
                    if (p.EstrelaOffsetInFile > 0)
                        bytes[p.EstrelaOffsetInFile] = (byte)(p.Estrela ? 1 : 0);
                    if (p.SuspensaoOffsetInFile > 0)
                        Array.Copy(BitConverter.GetBytes((short)Math.Max(0, Math.Min(SUSPENSAO_MAX, p.Suspensao))), 0, bytes, p.SuspensaoOffsetInFile, 2);
                    if (p.JogosLesionadoOffsetInFile > 0)
                        Array.Copy(BitConverter.GetBytes((short)Math.Max(0, Math.Min(JOGOS_LESIONADO_MAX, p.JogosLesionado))), 0, bytes, p.JogosLesionadoOffsetInFile, 2);
                    // So grava se mudou: um valor fora de 0..2 ja no save fica como esta
                    if (p.AmarelosAcumuladosOffsetInFile > 0 && p.AmarelosAcumuladosOffsetInFile < bytes.Length
                        && p.AmarelosAcumulados != bytes[p.AmarelosAcumuladosOffsetInFile])
                        bytes[p.AmarelosAcumuladosOffsetInFile] = (byte)Math.Max(0, Math.Min(AMARELOS_ACUMULADOS_MAX, p.AmarelosAcumulados));
                    if (p.LesaoOffsetInFile > 0)
                        Array.Copy(BitConverter.GetBytes((short)Math.Max(0, Math.Min(10, p.Lesao))), 0, bytes, p.LesaoOffsetInFile, 2);
                    if (p.ComportamentoOffsetInFile > 0)
                        Array.Copy(BitConverter.GetBytes((short)Math.Max(0, Math.Min(5, p.Comportamento))), 0, bytes, p.ComportamentoOffsetInFile, 2);
                }
            }
            File.WriteAllBytes(path, bytes);
        }

        // ---- helpers ----

        // Estadio da ultima equipe: 7 bytes antes da lista "quantidade de
        // equipes + identificadores em ordem" que vem depois dela
        private static int UltimoEstadio(byte[] bytes, List<int> starts)
        {
            int p = ListaOrdenada(bytes, starts);
            return p > 7 ? p - 7 : -1;
        }

        // Depois da ultima equipe: quantidade + identificadores em ordem crescente
        private static int ListaOrdenada(byte[] bytes, List<int> starts)
        {
            if (starts.Count == 0) return -1;
            var ids = new List<int>();
            foreach (int s in starts) if (s >= 2) ids.Add(BitConverter.ToUInt16(bytes, s - 2));
            ids.Sort();
            var pat = new List<byte>(BitConverter.GetBytes((ushort)starts.Count));
            foreach (int id in ids) pat.AddRange(BitConverter.GetBytes((ushort)id));
            return IndexOf(bytes, pat.ToArray(), starts[starts.Count - 1]);
        }

        private static void PutRgb(byte[] b, int o, int rgb)
        {
            b[o] = (byte)(rgb >> 16); b[o + 1] = (byte)(rgb >> 8); b[o + 2] = (byte)rgb; b[o + 3] = 0;
        }

        // Real de 6 bytes do Turbo Pascal/Delphi 1: expoente (vies 129) e 39 bits
        // de mantissa com o 1 implicito; bit 47 = sinal
        public static double Real48(byte[] b, int o)
        {
            if (b[o] == 0) return 0;
            long m = 0;
            for (int i = 5; i >= 1; i--) m = (m << 8) | b[o + i];
            double sinal = (m & (1L << 39)) != 0 ? -1 : 1;
            m &= (1L << 39) - 1;
            return sinal * (1 + m / (double)(1L << 39)) * Math.Pow(2, b[o] - 129);
        }

        public static void PutReal48(byte[] b, int o, double v)
        {
            if (v == 0) { for (int i = 0; i < 6; i++) b[o + i] = 0; return; }
            int e = (int)Math.Floor(Math.Log(Math.Abs(v), 2));
            double mant = Math.Abs(v) / Math.Pow(2, e);
            if (mant >= 2) { mant /= 2; e++; }
            long m = (long)Math.Round((mant - 1) * (1L << 39));
            if (m >= 1L << 39) { m = 0; e++; }
            if (v < 0) m |= 1L << 39;
            b[o] = (byte)(e + 129);
            for (int i = 1; i <= 5; i++) { b[o + i] = (byte)m; m >>= 8; }
        }

        // Real de 10 bytes (extended do 8087): 64 bits de mantissa com o 1
        // explicito, 15 bits de expoente (vies 16383) e sinal
        public static double Ext80(byte[] b, int o)
        {
            ulong m = BitConverter.ToUInt64(b, o);
            int ex = BitConverter.ToUInt16(b, o + 8);
            if (m == 0) return 0;
            double v = m / Math.Pow(2, 63) * Math.Pow(2, (ex & 0x7FFF) - 16383);
            return (ex & 0x8000) != 0 ? -v : v;
        }

        public static void PutExt80(byte[] b, int o, double v)
        {
            if (v == 0) { for (int i = 0; i < 10; i++) b[o + i] = 0; return; }
            int e = (int)Math.Floor(Math.Log(Math.Abs(v), 2));
            double mant = Math.Abs(v) / Math.Pow(2, e);
            if (mant >= 2) { mant /= 2; e++; }
            ulong m = (ulong)Math.Round(mant * Math.Pow(2, 63));
            Array.Copy(BitConverter.GetBytes(m), 0, b, o, 8);
            Array.Copy(BitConverter.GetBytes((ushort)((e + 16383) | (v < 0 ? 0x8000 : 0))), 0, b, o + 8, 2);
        }

        private static List<int> FindAllEftStarts(byte[] bytes)
        {
            var list = new List<int>();
            int i = 0;
            while (i <= bytes.Length - EFT_MAGIC.Length)
            {
                int p = IndexOf(bytes, EFT_MAGIC, i);
                if (p < 0) break;
                list.Add(p);
                i = p + 1;
            }
            return list;
        }

        private static byte[] DecodeCaesar(byte[] bytes, int start, int end)
        {
            end = Math.Min(end, bytes.Length);
            if (end <= start) return Array.Empty<byte>();
            var plain = new byte[end - start];
            int delta = 0;
            for (int i = 0; i < plain.Length; i++)
            {
                int p = (bytes[start + i] - delta) & 0xFF;
                plain[i] = (byte)p;
                delta = (delta + p - 0x20) & 0xFF;
            }
            return plain;
        }

        // Team name: byte em decoded[0x2E] = 0x20 + name_len. Nome comeca em
        // 0x2F. Padding de 0x20 (space) precede o length byte.
        private static string ExtractTeamName(byte[] decoded)
        {
            // Length byte tipicamente em 0x2E mas pode variar. Escaneia
            // 0x28..0x32 procurando o primeiro byte >= 0x22 e <= 0x50 seguido
            // de chars validos.
            for (int lenPos = 0x28; lenPos <= 0x32 && lenPos + 1 < decoded.Length; lenPos++)
            {
                byte lenByte = decoded[lenPos];
                if (lenByte < 0x22 || lenByte > 0x50) continue;
                int nameLen = lenByte - 0x20;
                if (lenPos + 1 + nameLen > decoded.Length) continue;

                var sb = new StringBuilder(nameLen);
                int validChars = 0;
                for (int i = lenPos + 1; i < lenPos + 1 + nameLen; i++)
                {
                    byte b = decoded[i];
                    char? c = MapTeamChar(b);
                    if (c != null) { sb.Append(c.Value); validChars++; }
                    else sb.Append('?');
                }
                if (validChars >= nameLen * 3 / 4)
                    return sb.ToString().Trim();
            }
            return "?";
        }

        // Nomes no save: cada caractere (Latin-1) somado de 0x20 ("." = 0x4E, espaco = 0x40,
        // "B" = 0x62, "e" = 0x85, "e" agudo = 0x09). Fora do Latin-1 imprimivel: null.
        private static char? MapTeamChar(byte b)
        {
            int c = (b - 0x20) & 0xFF;
            if (c < 0x20 || (c >= 0x7F && c < 0xA0)) return null;
            return (char)c;
        }

        // Nome do jogador: decoded[recStart+5..+5+NL-1], com maiusculas e acentos como no jogo
        private static string ExtractPlayerName(byte[] decoded, int recStart, int NL)
        {
            if (decoded.Length < recStart + 5 + NL) return "?";
            var sb = new StringBuilder(NL);
            for (int i = 0; i < NL; i++)
            {
                char? c = MapTeamChar(decoded[recStart + 5 + i]);
                if (c != null) sb.Append(c.Value);
            }
            return sb.ToString().Trim();
        }

        // Os dois nomes sao textos Pascal em 0x32 (o tamanho fica sem cifra no arquivo):
        // o completo e logo depois o curto
        private static string ExtractNomeCurto(byte[] b, byte[] decoded, int eft, int fim)
        {
            int o = eft + 0x32;
            if (o >= fim || b[o] > 40) return "";
            o += 1 + b[o];
            if (o >= fim) return "";
            int n = b[o], ini = o + 1 - (eft + 4);
            if (n == 0 || n > 20 || ini + n > decoded.Length) return "";
            var sb = new StringBuilder(n);
            for (int i = 0; i < n; i++)
            {
                char? c = MapTeamChar(decoded[ini + i]);
                if (c == null) return "";
                sb.Append(c.Value);
            }
            return sb.ToString().Trim();
        }

        // Cada registro comeca pelo pais: 0x03 + 3 letras minusculas
        private static string PaisDoRegistro(byte[] decoded, int rec) =>
            rec + 4 <= decoded.Length ? Encoding.ASCII.GetString(decoded, rec + 1, 3).ToUpperInvariant() : "";

        private static string PosicaoLabel(byte b) => b switch
        {
            0 => "G",
            1 => "D",
            2 => "M",
            3 => "A",
            _ => "?"
        };

        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            for (int i = start; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }
    }
}
