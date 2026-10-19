using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// Os add-ins (TAddInsList, DS:29B4) e o nivel (DS:29DE). O RetroFoot nao tem registo: o
// nivel e sempre 0x64, o da copia registada, e todos os add-ins da lista ficam ativos.
public static class Registo
{
    public const int Nivel = 0x64;

    // os add-ins que a lista tem (seg11:3227 e seguintes), na ordem, com os nomes (seg11:30e5)
    public static readonly (int Id, string Nome)[] AddIns =
    {
        (0x0F, "Ler times de versões anteriores"), (0x5A, "Adicionar países"), (0x50, "Memória livre"),
        (0x5C, "Página de controle"), (0x0A, "Ver tendência do árbitro"), (0x23, "Ver influência do árbitro"),
        (0x1E, "Jogos super-rápidos"), (0x3D, "Jogos ultrassônicos"), (0x14, "Jogadores selecionados dos outros times"),
        (0x0E, "Gestão de treinadores"), (0x28, "Férias dos treinadores"), (0x3C, "Todos os treinadores de férias"),
        (0x11, "Balanço dos times"), (0x10, "Compra direta de jogadores"), (0x13, "Pedir dinheiro emprestado"),
        (0x0B, "Ver o elenco quando for convidado"), (0x51, "Ver pontuação dos treinadores"), (0x46, "Sair sem salvar"),
        (0x0C, "Jogadores sob observação"), (0x0D, "Escalação individual de jogadores"),
        (0x12, "Substituições durante o jogo"), (0x5E, "Pesquisar todos os jogadores"),
    };

    public const int AddInPaginaControle = 0x5C, AddInSuperRapidos = 0x1E, AddInUltrassonicos = 0x3D,
        AddInGestaoTreinadores = 0x0E, AddInFerias = 0x28, AddInTodosDeFerias = 0x3C, AddInCompraDirecta = 0x10,
        AddInPlantelNoConvite = 0x0B, AddInPontuacaoTreinador = 0x51;

    // 11:30bc: ativo se esta na lista e id <= nivel
    public static bool AddIn(int id) => id <= Nivel && AddIns.Any(a => a.Id == id);
}

// TaboutDlg ("Sobre", 11:344a) e TAddInsDlg ("Extras", DFM
// orfao: a classe nao esta no EXE; aqui, a lista dos add-ins ativos). No visual Moderno, as
// mesmas caixas no tema escuro (AcercaModerna, em ModernoConfigDialogos.cs).
public static class TelaAcerca
{
    // o nome do jogo nas janelas e a unica linha de credito (o modo Classico)
    public const string Nome = "RetroFoot";
    public static string Slogan => T("Gestor de futebol");

    // a versao do RetroFoot.App.csproj (<Version>), sem o "+commit" que o SDK junta
    public static string Versao
    {
        get
        {
            var a = typeof(TelaAcerca).Assembly;
            var v = (Attribute.GetCustomAttribute(a, typeof(System.Reflection.AssemblyInformationalVersionAttribute))
                as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion
                ?? a.GetName().Version?.ToString(3) ?? "0.1.0";
            int mais = v.IndexOf('+');
            return mais >= 0 ? v[..mais] : v;
        }
    }

    public static void Abrir(Sessao s, Action? fechou = null) => Abrir((Dialogos)s, fechou);

    // O Sobre do RetroFoot no Classico (o jeito da TaboutDlg: verde, botoes do Windows 95):
    // o logo, o slogan, a versao e OK
    public static void Abrir(Dialogos d, Action? fechou = null)
    {
        if (Visual.Moderno) { AcercaModerna.Abrir(d, fechou); return; }
        var verde = W95.Verde;
        var j = new Janela(T("Sobre"), 513, 148, verde);
        // o logo (a bola e o nome, Assets/Marca) no lugar do titulo em italico
        var logo = Logotipo.Logo(56);
        logo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        j.Por(logo, 16, 9, 410, 58);
        j.Por(new Rotulo { Texto = Slogan, Fonte = new Fonte(Dlg.FArial, 16, true), Cor = W95.Amarelo, Alin = TextAlignment.Center }, 16, 70, 410, 19);
        j.Por(new Rotulo { Texto = T("Versão {0}", Versao), Fonte = W95.Arial11, Cor = Dlg.Branco, Alin = TextAlignment.Center }, 16, 92, 410, 14);
        var ok = j.Por(Dlg.BotaoGrande(T("OK"), "Botoes/BBOK.png", Dlg.Arial9), 440, 26, 64, 64);
        CaixaDialogo? c = null;
        void Fechar()
        {
            d.Fechar(c!);
            fechou?.Invoke();
        }
        ok.Clique += Fechar;
        j.AoEnter = Fechar;
        j.AoFechar = Fechar;
        c = d.Abrir(j);
    }

    // TAddInsDlg ("Extras"): os add-ins ativos, por nome (Sorted)
    public static void Extras(Dialogos d)
    {
        if (Visual.Moderno) { AcercaModerna.Extras(d); return; }
        var nomes = Registo.AddIns.Where(a => Registo.AddIn(a.Id)).Select(a => T(a.Nome))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        var j = new Janela(T("Extras"), 317, 248, W95.Face);
        j.Por(new Bevel(), 8, 8, 297, 161);
        var ok = j.Por(Dlg.BotaoGrande(T("OK"), "Botoes/BBOK.png"), 126, 180, 64, 64);
        var l = j.Por(new Lista { AlturaItem = 13 }, 16, 16, 281, 145);
        l.Quantos = () => nomes.Count;
        l.Desenhar = Lista.Textos(i => nomes[i], W95.MsSans11b, W95.Preto);
        CaixaDialogo? c = null;
        void Fechar() => d.Fechar(c!);
        ok.Clique += Fechar;
        j.AoEnter = Fechar;
        j.AoFechar = Fechar;
        c = d.Abrir(j);
    }
}

// TLabel com italico/sublinhado (o textGameName do Sobre, os vencedores do fim de temporada)
sealed class RotuloEstilo : Control
{
    public string Texto = "";
    public FontFamily Familia = Dlg.FArial;
    public double Tamanho = 13;
    public bool Negrito, Italico, Sublinhado, Centro;
    public IBrush Cor = W95.Preto;
    public IBrush? Fundo;

    public RotuloEstilo() { IsHitTestVisible = false; }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size);
        if (Fundo != null) dc.FillRectangle(Fundo, r);
        if (Texto.Length == 0) return;
        var t = Dlg.Txt(Texto, Familia, Tamanho, Cor, Negrito, Italico);
        if (Sublinhado) t.SetTextDecorations(TextDecorations.Underline);
        double x = Centro ? Math.Floor((r.Width - t.WidthIncludingTrailingWhitespace) / 2) : 0;
        using (dc.PushClip(r)) dc.DrawText(t, new Point(x, 0));
    }
}
