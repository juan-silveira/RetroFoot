using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Online;

// A rodada ao vivo nos outros aparelhos (docs/multiplayer-online.md, fase 3). O motor roda so
// no host; os outros mostram a mesma tela da jornada com o estado que o host transmite:
//  - no comeco (rodada/aoVivo/inicio): o titulo, se e da Taca e o jogo inteiro naquele momento
//    (o save comprimido: as escalacoes depois da preparacao de todos e das transferencias);
//  - a cada minuto (rodada/aoVivo/tique): o relogio da jornada e, de cada jogo que mudou desde o
//    tique anterior, o estado do jogo das duas equipas (+0x4D: golos, substituicoes...), os
//    lances e os extras, e o estado de cada jogador que mudou (+0x0C: escalacao, cartoes...);
//    de 15 em 15 tiques, todos os jogos (quem ligou no meio fica certo).
// O cliente monta uma Jornada de verdade com isso (JogoAtual sobre a copia do jogo) e entrega a
// mesma tela do computador e do celular.
public static class AoVivo
{
    public const int Chave = 15;   // de quantos em quantos tiques vao todos os jogos

    public static string Comprimir(byte[] b)
    {
        using var ms = new MemoryStream();
        using (var z = new GZipStream(ms, CompressionLevel.Optimal, true)) z.Write(b);
        return Convert.ToBase64String(ms.ToArray());
    }

    public static byte[] Descomprimir(string s)
    {
        using var z = new GZipStream(new MemoryStream(Convert.FromBase64String(s)), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }

    static JsonArray Lances(JogoAtual jg) => new(jg.Lances.Select(l => (JsonNode)new JsonArray(
        l.Lado, l.Minuto, l.Tipo, (int)l.JogadorID, (int)l.Posicao, l.SegundoAmarelo ? 1 : 0, l.Acrescimo)).ToArray());

    static JsonArray Extras(JogoAtual jg) => new(jg.Extras.Select(x => (JsonNode)new JsonArray(
        x.Lado, x.Minuto, x.Tipo, x.Acrescimo, x.Ordem, x.Intervalo ? 1 : 0, (int)(x.Saiu?.ID ?? 0), (int)(x.Entrou?.ID ?? 0),
        (int)(x.Batedor?.ID ?? 0), (int)(x.Goleiro?.ID ?? 0), x.Resultado)).ToArray());

    // ---------------------------------------------------------------- o host
    public sealed class Emissor
    {
        // o que ja foi mandado: o estado de cada equipa (+0x4D), a escalacao/suspensao/lesao
        // de cada jogador e quantos lances e extras cada jogo tinha
        readonly Dictionary<ushort, string> _b4d = new();
        readonly Dictionary<ushort, (ushort Esc, ushort Susp, ushort Les)> _jog = new();
        readonly Dictionary<int, (int L, int X, string Ultimo)> _lances = new();
        int _tique;

        public JsonObject Inicio(JogoEmCurso j, byte[] titulo, Jornada jn)
        {
            _b4d.Clear(); _jog.Clear(); _lances.Clear();
            _tique = 0;
            return new JsonObject
            {
                ["titulo"] = Convert.ToBase64String(titulo),
                ["taca"] = jn.Taca,
                ["save"] = Comprimir(j.M.S.Gravar()),
            };
        }

        public JsonObject Tique(Jornada jn)
        {
            _tique++;
            bool todos = _tique % Chave == 1;
            var jogos = new JsonArray();
            foreach (var jg in jn.Jogos)
            {
                var o = new JsonObject();
                var eqs = new JsonArray();
                bool mudou = false;
                var ps = new JsonArray();
                for (int l = 0; l < 2; l++)
                {
                    var e = jg.Equipa[l];
                    string? b = e == null ? null : Convert.ToBase64String(e.B4D);
                    if (e != null && (todos || !_b4d.TryGetValue(e.ID, out var ant) || ant != b)) { _b4d[e.ID] = b!; mudou = true; eqs.Add(b); }
                    else eqs.Add(null);
                    if (e == null) continue;
                    foreach (var p in e.Jogadores)
                    {
                        var v = (Memoria.Escalacao(p), p.Suspensao, p.JogosLesao);
                        if (!todos && _jog.TryGetValue(p.ID, out var a) && a == v) continue;
                        _jog[p.ID] = v;
                        ps.Add(new JsonArray((int)p.ID, (int)v.Item1, (int)v.Suspensao, (int)v.JogosLesao));
                    }
                }
                if (ps.Count > 0) { o["p"] = ps; mudou = true; }
                o["e"] = eqs;
                // os lances e extras inteiros do jogo quando mudaram (poucos por jogo)
                int k = jg.Dados.Casa * 65536 + jg.Dados.Fora;
                string ult = jg.Lances.Count == 0 ? "" : $"{jg.Lances[^1].Tipo}/{jg.Lances[^1].JogadorID}/{jg.Lances[^1].Minuto}";
                var lx = (jg.Lances.Count, jg.Extras.Count, ult);
                if (todos || !_lances.TryGetValue(k, out var la) || la != lx)
                {
                    _lances[k] = lx;
                    o["l"] = Lances(jg);
                    o["x"] = Extras(jg);
                    o["ac"] = new JsonArray(jg.Acrescimos.Select(a => (JsonNode)a).ToArray());
                    mudou = true;
                }
                if (!mudou) continue;
                o["c"] = (int)jg.Dados.Casa;
                o["v"] = (int)jg.Dados.Fora;
                jogos.Add(o);
            }
            return new JsonObject
            {
                ["n"] = _tique, ["m"] = jn.Minuto, ["a"] = jn.Acrescimo, ["am"] = jn.AcrescimoMax, ["f"] = jn.Fim,
                ["ea"] = jn.EmAcrescimos ? 1 : 0, ["todos"] = todos, ["jogos"] = jogos,
                // os jogos que seguem (na Taca, os decididos no 90 saem da jornada)
                ["ativos"] = new JsonArray(jn.Jogos.Select(x => (JsonNode)(x.Dados.Casa * 65536 + x.Dados.Fora)).ToArray()),
            };
        }
    }

    // ---------------------------------------------------------------- o cliente
    // A jornada montada sobre a copia do jogo (o save do inicio), atualizada a cada tique
    public sealed class Receptor
    {
        public readonly Jogo98 S;
        public readonly Jornada Jornada;
        public readonly byte[] Titulo;
        readonly Dictionary<ushort, Jogador> _jogadores = new();
        readonly Dictionary<ushort, Equipa> _equipas = new();
        int _n;

        public Receptor(JsonObject inicio) : this(inicio, JogoDaPartida) { }

        // como JogoEmCurso.JogoDaPartida, sobre a copia
        public static JogoAtual JogoDaPartida(Jogo98 s, Jogo d)
        {
            var jg = new JogoAtual { Dados = d };
            jg.Equipa[0] = s.Equipas.FirstOrDefault(e => e.ID == d.Casa && d.Casa != 0);
            jg.Equipa[1] = s.Equipas.FirstOrDefault(e => e.ID == d.Fora && d.Fora != 0);
            for (int l = 0; l < 2; l++)
                if (jg.Equipa[l] is { } e) jg.Treinador[l] = s.Treinadores.FirstOrDefault(t => t.ID == e.Treinador);
            jg.Arbitro = s.Arbitros.FindLast(a => a.ID == d.Arbitro);
            return jg;
        }

        public Receptor(JsonObject inicio, Func<Jogo98, Jogo, JogoAtual> jogoDaPartida)
        {
            S = Jogo98.Ler(Descomprimir((string)inicio["save"]!));
            Titulo = Convert.FromBase64String((string)inicio["titulo"]!);
            Jornada = new Jornada { R = new Aleatorio(0), Taca = (bool?)inicio["taca"] ?? false };
            Jornada.Iniciar();
            foreach (var e in S.Equipas)
            {
                _equipas[e.ID] = e;
                foreach (var p in e.Jogadores) _jogadores[p.ID] = p;
            }
            foreach (var d in S.Jogos) _todos.Add(jogoDaPartida(S, d));
            Jornada.Jogos.AddRange(_todos);
        }

        // aplica um tique (fora de ordem ou repetido: ignorado); true se mudou
        public bool Aplicar(JsonObject t)
        {
            int n = (int?)t["n"] ?? 0;
            if (n <= _n && !((bool?)t["todos"] ?? false)) return false;
            _n = n;
            Jornada.Minuto = (int?)t["m"] ?? Jornada.Minuto;
            Jornada.Acrescimo = (int?)t["a"] ?? 0;
            Jornada.AcrescimoMax = (int?)t["am"] ?? 0;
            Jornada.Fim = (int?)t["f"] ?? 90;
            Jornada.EmAcrescimos = (int?)t["ea"] == 1;
            if (t["jogos"] is JsonArray jogos)
                foreach (var o in jogos.OfType<JsonObject>())
                {
                    int c = (int?)o["c"] ?? -1, v = (int?)o["v"] ?? -1;
                    if (_todos.FirstOrDefault(x => x.Dados.Casa == c && x.Dados.Fora == v) is { } jg) Jogo(jg, o);
                }
            if (t["ativos"] is JsonArray ativos)
            {
                var k = ativos.Select(x => (int?)x ?? 0).ToHashSet();
                Jornada.Jogos.Clear();
                Jornada.Jogos.AddRange(_todos.Where(x => k.Contains(x.Dados.Casa * 65536 + x.Dados.Fora)));
            }
            return true;
        }

        readonly List<JogoAtual> _todos = new();

        void Jogo(JogoAtual jg, JsonObject o)
        {
            if (o["e"] is JsonArray eqs)
                for (int l = 0; l < 2 && l < eqs.Count; l++)
                    if ((string?)eqs[l] is { } b && jg.Equipa[l] is { } e) Convert.FromBase64String(b).CopyTo(e.B4D, 0);
            if (o["p"] is JsonArray ps)
                foreach (var p in ps.OfType<JsonArray>())
                    if (_jogadores.TryGetValue((ushort)(int)p[0]!, out var j))
                    {
                        Memoria.MudarEscalacao(j, (ushort)(int)p[1]!);
                        j.Suspensao = (ushort)(int)p[2]!;
                        j.JogosLesao = (ushort)(int)p[3]!;
                    }
            if (o["l"] is JsonArray ls)
            {
                jg.Lances.Clear();
                foreach (var a in ls.OfType<JsonArray>())
                {
                    var id = (ushort)(int)a[3]!;
                    jg.Lances.Add(new Lance
                    {
                        Lado = (int)a[0]!, Minuto = (int)a[1]!, Tipo = (int)a[2]!, JogadorID = id, Posicao = (byte)(int)a[4]!,
                        SegundoAmarelo = (int)a[5]! != 0, Acrescimo = (int)a[6]!,
                        Jogador = _jogadores.GetValueOrDefault(id) ?? new Jogador { ID = id },
                    });
                }
            }
            if (o["x"] is JsonArray xs)
            {
                jg.Extras.Clear();
                foreach (var a in xs.OfType<JsonArray>())
                    jg.Extras.Add(new LanceExtra
                    {
                        Lado = (int)a[0]!, Minuto = (int)a[1]!, Tipo = (int)a[2]!, Acrescimo = (int)a[3]!, Ordem = (int)a[4]!, Intervalo = (int)a[5]! != 0,
                        Saiu = J((int)a[6]!), Entrou = J((int)a[7]!), Batedor = J((int)a[8]!), Goleiro = J((int)a[9]!), Resultado = (int)a[10]!,
                    });
            }
            if (o["ac"] is JsonArray ac)
                for (int i = 0; i < ac.Count && i < jg.Acrescimos.Length; i++) jg.Acrescimos[i] = (int?)ac[i] ?? 0;
        }

        Jogador? J(int id) => id == 0 ? null : _jogadores.GetValueOrDefault((ushort)id);

        // a disputa de penaltis que o host mostrou (HostOnline.TransmitirPenaltis): o jogo e as cobrancas
        public (JogoAtual Jogo, Desempate D)? Penaltis(JsonObject o)
        {
            int c = (int?)o["c"] ?? -1, v = (int?)o["v"] ?? -1;
            if (_todos.FirstOrDefault(x => x.Dados.Casa == c && x.Dados.Fora == v) is not { } jg) return null;
            var d = new Desempate { PassaVisitante = (bool?)o["pv"] ?? false, Rapido = (bool?)o["rapido"] ?? false };
            if (o["somas"] is JsonArray sm) for (int i = 0; i < 2 && i < sm.Count; i++) d.Somas[i] = (short)((int?)sm[i] ?? 0);
            if (o["golos"] is JsonArray gl) for (int i = 0; i < 2 && i < gl.Count; i++) d.Golos[i] = (short)((int?)gl[i] ?? 0);
            if (o["cobrancas"] is JsonArray cs)
                foreach (var a in cs.OfType<JsonArray>())
                    if (J((int?)a[1] ?? 0) is { } b) d.Cobrancas.Add(new Cobranca((int?)a[0] ?? 0, b, (int?)a[2] ?? 0));
            return (jg, d);
        }
    }
}
