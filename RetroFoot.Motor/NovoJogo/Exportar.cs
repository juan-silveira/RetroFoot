using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using S = RetroFoot.Motor.Save;

namespace RetroFoot.Motor.NovoJogo;

// O jogo gravado (.e98) do estado atual, como seg05:1796 o grava. (Traduzido de
// motor/novojogo/exportar.go.)
public static class Exportar
{
    // dataHora e o texto de DateTimeToStr(Now) no momento da gravacao (ver DataHora)
    public static S.Jogo98 Save(this Estado e, string dataHora)
    {
        var s = new S.Jogo98();
        var c = s.Cabecalho;
        c.Texto = TextoCabecalho(e.Treinadores, dataHora);
        c.G271C = (ushort)e.Jornada;   // seg05:1433 grava DS:0x2B40
        c.G271E = e.Vez;
        c.G271A = e.Taca271A;
        c.G2722 = e.ProximoID;
        c.Ano = e.Ano;
        c.Real2B46 = e.Inflacao.Bytes();
        c.Real2B50 = e.Real2B50.Bytes();
        c.G2D40 = (byte[])e.Moeda.Clone();

        foreach (var t in e.Treinadores)
        {
            var st = new S.Treinador { ID = t.ID, Nome = Cifra.PorTextoEFT(t.Nome), Humano = t.Humano, B06 = t.B06, W0C = t.W0C, D0E = t.D0E, D16 = t.D16, B1A = (byte[])t.B1A.Clone() };
            foreach (var v in t.Eventos) st.Eventos.Add(new S.EventoTreinador { B04 = v.Tipo, D05 = v.Ano, W09 = v.Equipa });
            s.Treinadores.Add(st);
        }
        foreach (var a in e.Arbitros)
            s.Arbitros.Add(new S.Arbitro { ID = a.ID, B06 = a.Disponivel, Nome = Cifra.PorTextoEFT(a.Nome), Pais = Cifra.PorTextoEFT(Encoding.Latin1.GetBytes(a.Pais)) });
        foreach (var t in e.Equipas) s.Equipas.Add(ExportarEquipa(t));
        foreach (var t in e.NaTaca) s.OutrasEquipas.Add(t.ID);
        foreach (var d in e.Divisoes)
        {
            var sd = new S.Divisao { D1F = (uint)d.DinheiroTotal, W04 = (ushort)d.W04, Nome = (byte[])d.Nome.Clone() };
            foreach (var t in d.Equipas) sd.Equipas.Add(t.ID);
            s.Divisoes.Add(sd);
        }
        // o K escolhido nas Configuracoes fica no save (Regras.MarcarSobemDescem); o automatico
        // nao marca nada (o save fica igual ao do original)
        if (e.Regras.SobemDescemEscolhido > 0) Regras.Regras.MarcarSobemDescem(s, e.Regras.SobemDescem);
        foreach (var p in e.Partidas)
        {
            s.Jogos.Add(new S.Jogo
            {
                Taca = p.Taca, B04 = (byte[])p.Nome.Clone(), B36 = p.Bilhete.Bytes(), D40 = (uint)p.Publico,
                W4C = p.W4C, W4E = p.W4E, B50 = p.B50,
                Casa = p.Casa?.ID ?? 0, Fora = p.Fora?.ID ?? 0, Arbitro = p.Arbitro?.ID ?? 0,
            });
        }
        return s;
    }

    static S.Equipa ExportarEquipa(Equipa t)
    {
        var se = new S.Equipa
        {
            ID = t.ID,
            Cabeca = Tabelas.CabecaEquipa(),
            NomeCompleto = Cifra.PorTextoEFT(t.NomeCompleto),
            NomeCurto = Cifra.PorTextoEFT(t.NomeCurto),
            CorLetra = (byte[])t.CorLetra.Clone(),
            CorFundo = (byte[])t.CorFundo.Clone(),
            Pais = Cifra.PorTextoEFT(Encoding.Latin1.GetBytes(t.Pais)),
            Nivel = t.Nivel,
            B16 = (byte[])t.B16.Clone(),
            B4D = (byte[])t.B4D.Clone(),
            Moral = (byte[])t.Moral.Clone(),
            Treinador = t.Treinador.ID,
            B89 = (byte[])t.B89.Clone(),
            BA5 = (byte[])t.BA5.Clone(),
            D6D = t.D6D,
            Dinheiro = (uint)t.Dinheiro,
            D75 = (uint)t.D75,
            B79 = t.ValorClube.Bytes(),
            B83 = new[] { t.PorPosicao[0], t.PorPosicao[1], t.PorPosicao[2], t.PorPosicao[3], t.Estrangeiros, t.NJogadores },
            DC1 = t.DC1, WC7 = t.WC7, WC9 = t.WC9,
            Estadio = t.Estadio,
            EstadioReal = (byte[])t.EstadioReal.Clone(),
        };
        foreach (var j in t.Jogadores)
            se.Jogadores.Add(new S.Jogador
            {
                ID = j.ID, Pais = Cifra.PorTextoEFT(Encoding.Latin1.GetBytes(j.Pais)), NomeCifrado = (byte[])j.NomeCifrado.Clone(),
                Posicao = j.Posicao, Estrela = j.Estrela, Forca = (ushort)j.Forca, B0C = (byte[])j.B0C.Clone(), B14 = (byte[])j.B14.Clone(),
                Suspensao = (ushort)j.Suspensao, Comportamento = (ushort)j.Comportamento, JogosLesao = (ushort)j.JogosLesao,
                Lesao = (ushort)j.Lesao, Nota = (ushort)j.Nota, Salario = (uint)j.Salario, B35 = j.Estrangeiro, W36 = (ushort)j.W36,
                Historial = (byte[])j.Historial.Clone(),
            });
        foreach (var c in t.Calendario)
            se.Calendario.Add(new S.EntradaCalendario { W08 = c.Adversario, W0E = c.Fora, W0A = c.W0A, W0C = c.W0C, D10 = c.D10, B07 = c.Taca, B06 = c.B06, W04 = c.Jornada });
        return se;
    }

    // seg05:124d: os nomes dos treinadores humanos, separados por ", " e " e " antes do
    // ultimo; depois "\r" e DateTimeToStr(Now); cifrado duas vezes (seg14:0734 e 0814)
    static byte[] TextoCabecalho(List<Treinador> treinadores, string dataHora)
    {
        var r = new List<byte>();
        byte[]? pendente = null;
        foreach (var t in treinadores.Where(t => t.Humano != 0))
        {
            if (pendente != null && pendente.Length > 0)
            {
                if (r.Count > 0) r.AddRange(", "u8.ToArray());
                r.AddRange(pendente);
            }
            pendente = t.Nome;
        }
        if (pendente != null && pendente.Length > 0)
        {
            if (r.Count > 0) r.AddRange(" e "u8.ToArray());
            r.AddRange(pendente);
        }
        r.Add((byte)'\r');
        r.AddRange(Encoding.Latin1.GetBytes(dataHora));
        var arr = r.Take(255).ToArray();
        var uma = Cifra.PorTextoEFT(arr);
        return Cifra.PorTextoEFT(uma.AsSpan(1));
    }

    // DateTimeToStr do Delphi 1 com ShortTimeFormat = "HH:MM" (seg05:13a3) e data M/d/yyyy
    // (Wine); a meia-noite exata a hora e omitida
    public static string DataHora(DateTime t)
    {
        var d = $"{t.Month}/{t.Day}/{t.Year}";
        if (t.TimeOfDay == TimeSpan.Zero) return d;
        return $"{d} {t.Hour:00}:{t.Minute:00}";
    }
}
