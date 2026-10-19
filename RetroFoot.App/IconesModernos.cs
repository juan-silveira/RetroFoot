using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RetroFoot.App;

// Os icones do visual Moderno (Visual.cs): no lugar dos glifos de bitmap do Windows 95
// (Assets/Botoes, Imagens, Mercado, Dialogos), icones vetoriais desenhados com traco, nas
// cores do tema escuro (MM / ClubeModerno). O Classico continua com os bitmaps; aqui so se
// diz qual icone corresponde a cada bitmap.
//
// Os caminhos sao dos icones Lucide (https://lucide.dev), grade de 24x24, traco 2, pontas
// redondas. Licenca dos icones:
//
//   ISC License
//   Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of
//   Feather (MIT). All other copyright (c) for Lucide are held by Lucide Contributors 2022.
//   Permission to use, copy, modify, and/or distribute this software for any purpose with
//   or without fee is hereby granted, provided that the above copyright notice and this
//   permission notice appear in all copies.
//   THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO
//   THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO
//   EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL
//   DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER
//   IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
//   CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
static class IconesModernos
{
    public sealed class Icone(string nome, string caminho, IBrush cor)
    {
        public readonly string Nome = nome;
        public readonly Geometry Geometria = Geometry.Parse(caminho);
        public readonly IBrush Cor = cor;
    }

    // circulo e retangulo arredondado do SVG como caminho
    static string C(double cx, double cy, double r) =>
        FormattableString.Invariant($"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0");

    static string R(double x, double y, double w, double h, double rx) =>
        FormattableString.Invariant($"M{x + rx} {y}h{w - 2 * rx}a{rx} {rx} 0 0 1 {rx} {rx}v{h - 2 * rx}a{rx} {rx} 0 0 1 {-rx} {rx}h{-(w - 2 * rx)}a{rx} {rx} 0 0 1 {-rx} {-rx}v{-(h - 2 * rx)}a{rx} {rx} 0 0 1 {rx} {-rx}z");

    static readonly IBrush Sim = MM.Vivo, Nao = MM.Vermelho, Acao = MM.Azul;

    public static readonly Icone
        Ok = new("check", "M20 6 9 17l-5-5", Sim),
        Cancelar = new("x", "M18 6 6 18M6 6l12 12", Nao),
        Proibido = new("ban", C(12, 12, 10) + "M4.9 4.9l14.2 14.2", Nao),
        Todas = new("check-check", "M18 6 7 17l-5-5M22 10l-7.5 7.5L13 16", Sim),
        Configurar = new("settings",
            "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"
            + C(12, 12, 3), Acao),
        Construir = new("construction",
            R(2, 6, 20, 8, 1) + "M17 14v7M7 14v7M17 3v3M7 3v3M10 14 2.3 6.3M14 6l7.7 7.7M8 6l8 8", MM.Amarelo),
        Dinheiro = new("banknote", R(2, 6, 20, 12, 2) + C(12, 12, 2) + "M6 12h.01M18 12h.01", Acao),
        Pedir = new("circle-plus", C(12, 12, 10) + "M8 12h8M12 8v8", Acao),
        Pagar = new("circle-minus", C(12, 12, 10) + "M8 12h8", Acao),
        Substituir = new("arrow-left-right", "M8 3 4 7l4 4M4 7h16M16 21l4-4-4-4M20 17H4", Acao),
        Procurar = new("search", C(11, 11, 8) + "M21 21l-4.3-4.3", Acao),
        Bandeira = new("flag", "M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1zM4 22v-7", Acao),
        Equipa = new("shield",
            "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z", Acao),
        Calendario = new("calendar", "M8 2v4M16 2v4" + R(3, 4, 18, 18, 2) + "M3 10h18", Acao),
        Plantel = new("users", "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" + C(9, 7, 4) + "M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75", Acao),
        Balanco = new("chart-column", "M3 3v16a2 2 0 0 0 2 2h16M18 17V9M13 17V5M8 17v-3", Acao),
        Comprar = new("shopping-cart", C(8, 21, 1) + C(19, 21, 1) + "M2.05 2.05h2l2.66 12.42a2 2 0 0 0 2 1.58h9.78a2 2 0 0 0 1.95-1.57l1.65-7.43H5.12", Acao),
        Observado = new("eye", "M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0" + C(12, 12, 3), Acao),
        Lista = new("list", "M3 6h.01M3 12h.01M3 18h.01M8 6h13M8 12h13M8 18h13", Acao),
        Remover = new("trash-2", "M3 6h18M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2M10 11v6M14 11v6", Nao),
        Limpar = new("eraser", "M7 21l-4.3-4.3c-1-1-1-2.5 0-3.4l9.6-9.6c1-1 2.5-1 3.4 0l5.6 5.6c1 1 1 2.5 0 3.4L13 21M22 21H7M5 11l9 9", Acao),
        Leilao = new("gavel", "M14.5 12.5l-8 8a2.119 2.119 0 1 1-3-3l8-8M16 16l6-6M8 8l6-6M9 7l8 8M21 11l-8-8", Sim),
        Vender = new("circle-dollar-sign", C(12, 12, 10) + "M16 8h-6a2 2 0 1 0 0 4h4a2 2 0 1 1 0 4H8M12 18V6", Sim),
        Renovar = new("refresh-cw", "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8M21 3v5h-5M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16M8 16H3v5", Sim),
        Jogar = new("play", "M6 3l14 9-14 9z", Sim),
        Adicionar = new("user-plus", "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" + C(9, 7, 4) + "M19 8v6M22 11h-6", Acao),
        Tirar = new("user-minus", "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" + C(9, 7, 4) + "M22 11h-6", Nao);

    // o nome do arquivo do glifo (sem pasta, sem ".png", sem "-desab") -> o icone
    static readonly Dictionary<string, Icone> PorArquivo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BBOK"] = Ok, ["BBYES"] = Ok, ["BBCANCEL"] = Cancelar, ["BBNO"] = Proibido, ["BBHELP"] = Plantel,
        ["CONFIGURAR"] = Configurar, ["CONSTRUIR"] = Construir, ["EMPRESTIMO"] = Dinheiro,
        ["PAGAR"] = Pagar, ["PEDIR"] = Pedir, ["SUBSTITUIR"] = Substituir, ["TODAS"] = Todas,
        ["calendario"] = Calendario, ["jogar"] = Jogar, ["plantel"] = Plantel, ["ok"] = Ok, ["leilao-ok"] = Leilao,
        ["salario-sim"] = Ok, ["salario-nao"] = Proibido, ["salario-propor"] = Renovar, ["salario-cancelar"] = Cancelar,
        ["vender"] = Vender, ["vender-cancelar"] = Cancelar,
        ["convite-sim"] = Ok, ["convite-nao"] = Proibido, ["treinador-adicionar"] = Adicionar, ["treinador-remover"] = Tirar,
        ["TcheckPlayerDlg.findBtn"] = Procurar, ["TcheckPlayerDlg.myCountryBtn"] = Bandeira, ["TcheckPlayerDlg.OKBtn"] = Ok,
        ["TcheckPlayerDlg.teamBtn"] = Equipa,
        ["TcheckPlayerDlg.clearCountryBtn"] = Cancelar, ["TcheckPlayerDlg.clearNameBtn"] = Cancelar,
        ["TcheckPlayerDlg.clearPositionBtn"] = Cancelar, ["TcheckPlayerDlg.clearPriceBtn"] = Cancelar,
        ["TcheckPlayerDlg.clearSkillBtn"] = Cancelar,
        ["TObservedPlayersDlg.clearBtn"] = Limpar, ["TObservedPlayersDlg.removeBtn"] = Remover, ["TObservedPlayersDlg.teamBtn"] = Equipa,
        ["TPlantelDlg.balanceBtn"] = Balanco, ["TPlantelDlg.buyBtn"] = Comprar, ["TPlantelDlg.calendarBtn"] = Calendario,
        ["TPlantelDlg.findBtn"] = Procurar, ["TPlantelDlg.loanBtn"] = Dinheiro, ["TPlantelDlg.observedPlayersBtn"] = Lista,
        ["TPlantelDlg.OKBtn"] = Ok, ["TPlantelDlg.SpeedButtonObserved"] = Observado,
    };

    public static Icone? DoArquivo(string? caminho)
    {
        if (string.IsNullOrEmpty(caminho)) return null;
        var n = Path.GetFileName(caminho);
        if (n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        if (n.EndsWith("-desab", StringComparison.Ordinal)) n = n[..^6];
        return PorArquivo.TryGetValue(n, out var i) ? i : null;
    }

    // os glifos chegam aos botoes como Bitmap (W95.Imagem guarda um por caminho): o mesmo
    // objeto do cache leva ao icone
    static Dictionary<Bitmap, Icone>? porBitmap;

    public static Icone? DoBitmap(Bitmap? b)
    {
        if (b == null) return null;
        if (porBitmap == null)
        {
            porBitmap = new(ReferenceEqualityComparer.Instance);
            foreach (var pasta in new[] { "Botoes", "Imagens", "Mercado", "Dialogos" })
                foreach (var (nome, icone) in PorArquivo)
                    foreach (var suf in new[] { "", "-desab" })
                    {
                        var c = $"{pasta}/{nome}{suf}.png";
                        if (AssetLoader.Exists(new Uri("avares://RetroFoot.App/Assets/" + c)) && W95.Imagem(c) is { } bm) porBitmap[bm] = icone;
                    }
        }
        return porBitmap.TryGetValue(b, out var i) ? i : null;
    }

    // o icone no quadrado r; desligado fica apagado. O traco nunca fica mais fino que 1,6
    public static void Desenhar(DrawingContext dc, Icone i, Rect r, bool hab, IBrush? cor = null)
    {
        double k = r.Width / 24;
        var pen = new Pen(hab ? cor ?? i.Cor : MM.Apagado, Math.Max(2, 1.6 / k), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (dc.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(r.X, r.Y)))
            dc.DrawGeometry(null, pen, i.Geometria);
    }
}
