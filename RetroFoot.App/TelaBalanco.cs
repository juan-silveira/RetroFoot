using System;
using System.Linq;
using System.Buffers.Binary;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A TbalanceDlg (7:3b82, Execute): o balanco de tres temporadas de uma equipa. O titulo e o
// nome completo da equipa; a janela e a lista nas cores dela. Cada linha e
// TThreeBalances.Formatar (11:21d1): o rotulo (120 px) e uma coluna de 90 px (a direita, +10)
// por temporada: a atual (+0x31), a anterior (+0x89) e a de antes (+0xA5). Temporada, Receitas
// e Gastos inteiras em negrito. Os rotulos sao os do pt-BR.txt do launcher (como a aba
// Financas da TelaEquipa).
//
// No original abre pelo Elenco (TPlantelDlg.balanceBtn) de qualquer equipa, so com o add-in
// 0x11 (registado); quem chama decide.
public static class TelaBalanco
{
    static readonly string[] Rotulos = { "Temporada", "Receitas", "Bilheteria", "Jogadores vendidos", "Prêmios", "Gastos",
        "Salários", "Jogadores comprados", "Arquibancadas", "Juros", "", "Receita total", "Gasto total", "Saldo" };

    // abre a janela por cima da tela atual (thread da interface); fechou: ao fechar
    public static void Abrir(Sessao s, SEquipa e, Action? fechou = null) => Abrir(s, e, fechou, null, null);

    internal static void Abrir(Sessao s, SEquipa e, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        if (Visual.Moderno) { AbrirModerno(s, e, fechou, abrir, fechar); return; }
        var (letra, fundo) = TelaDinheiro.Cores(e);
        var j = new Janela(TelaDinheiro.Texto(e.NomeCompleto), 471, 343, fundo);
        j.Por(new Bevel(), 5, 8, 460, 249);
        var ok = j.Por(BotaoMargem.Ok(T("OK"), TelaDinheiro.Arial9b, 10), 203, 268, 64, 64);
        j.Por(new ListaBalanco(Temporadas(e), s.S.Cabecalho.Ano, letra, Moeda.DoSave(s.S)), 13, 16, 444, 225);
        var d = TelaDinheiro.Abrir(s, j, abrir, fechar);
        if (fechou != null) d.Fechou += fechou;
        ok.Clique += d.Fechar;
        j.AoEnter = d.Fechar;
    }

    // as tres temporadas: a atual no save (+0x31 = B16 + 0x1B), as duas anteriores
    static ReadOnlyMemory<byte>[] Temporadas(SEquipa e) => new ReadOnlyMemory<byte>[] { e.B16.AsMemory(0x31 - 0x16, 28), e.B89, e.BA5 };

    // os 7 numeros de uma temporada: 0 bilheteria, 1 vendidos, 2 premios, 3 comprados,
    // 4 salarios, 5 arquibancadas, 6 juros (os gastos ja vem negativos)
    static long V(ReadOnlyMemory<byte> b, int slot) => BinaryPrimitives.ReadInt32LittleEndian(b.Span.Slice(4 * slot));

    // ------------------------------------------------------------ Moderno
    // O balanco no visual Moderno: o logo e o nome no cabecalho; a tabela com as tres
    // temporadas (a atual destacada), receitas em verde, gastos em vermelho e o saldo colorido
    // pelo sinal. No 16:9, ao lado, as barras de receita e gasto de cada temporada.
    static void AbrirModerno(Sessao s, SEquipa e, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        var t = Temporadas(e);
        int ano = s.S.Cabecalho.Ano;
        var tabela = new Grid { ColumnDefinitions = new ColumnDefinitions("*,128,128,128") };
        int linha = 0;
        void Linha(string rotulo, Func<int, string> valor, IBrush? cor = null, bool negrito = false, bool secao = false, Func<int, IBrush>? corDe = null)
        {
            tabela.RowDefinitions.Add(new RowDefinition(secao ? 34 : 25, GridUnitType.Pixel));
            var peso = negrito || secao ? FontWeight.SemiBold : FontWeight.Normal;
            var r = ClubeModerno.T(rotulo, secao ? 13 : 13.5, cor ?? (secao ? ClubeModerno.Texto : ClubeModerno.Fraco), peso);
            r.VerticalAlignment = secao ? VerticalAlignment.Bottom : VerticalAlignment.Center;
            r.Margin = new Thickness(secao ? 0 : 12, 0, 0, secao ? 4 : 0);
            Grid.SetRow(r, linha);
            tabela.Children.Add(r);
            for (int k = 0; k < 3; k++)
            {
                var v = ClubeModerno.T(valor(k), 13.5, corDe?.Invoke(k) ?? (k == 0 ? ClubeModerno.Texto : ClubeModerno.Fraco), peso, TextAlignment.Right);
                v.VerticalAlignment = r.VerticalAlignment;
                v.TextWrapping = TextWrapping.NoWrap;   // "-R$ 21.014.000" numa linha so
                v.Margin = new Thickness(0, 0, 14, secao ? 4 : 0);
                Grid.SetRow(v, linha);
                Grid.SetColumn(v, k + 1);
                tabela.Children.Add(v);
            }
            linha++;
        }
        void Separador()
        {
            tabela.RowDefinitions.Add(new RowDefinition(9, GridUnitType.Pixel));
            var b = new Border { Height = 1, Background = ClubeModerno.Linha, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(b, linha++);
            Grid.SetColumnSpan(b, 4);
            tabela.Children.Add(b);
        }
        string M(int k, params int[] slots) { long soma = 0; foreach (var x in slots) soma += V(t[k], x); return FormatoDinheiro.Valor(s.S, soma); }
        IBrush Sinal(int k, params int[] slots)
        {
            long soma = 0; foreach (var x in slots) soma += V(t[k], x);
            return soma > 0 ? ClubeModerno.Verde : soma < 0 ? ClubeModerno.Vermelho : ClubeModerno.Fraco;
        }
        // a coluna da temporada atual num fundo proprio
        var destaque = new Border { Background = ClubeModerno.Cartao, CornerRadius = new CornerRadius(8) };
        Grid.SetColumn(destaque, 1);
        Grid.SetRowSpan(destaque, 99);
        tabela.Children.Add(destaque);
        Linha(T("Temporada"), k => (ano - k).ToString(), ClubeModerno.Fraco, true, corDe: k => k == 0 ? ClubeModerno.Azul : ClubeModerno.Fraco);
        Linha(T("Receitas"), _ => "", ClubeModerno.Verde, secao: true);
        Linha(T("Bilheteria"), k => M(k, 0));
        Linha(T("Jogadores vendidos"), k => M(k, 1));
        Linha(T("Prêmios"), k => M(k, 2));
        Linha(T("Gastos"), _ => "", ClubeModerno.Vermelho, secao: true);
        Linha(T("Salários"), k => M(k, 4));
        Linha(T("Jogadores comprados"), k => M(k, 3));
        Linha(T("Arquibancadas"), k => M(k, 5));
        Linha(T("Juros"), k => M(k, 6));
        Separador();
        Linha(T("Receita total"), k => M(k, 0, 1, 2), ClubeModerno.Texto, true);
        Linha(T("Gasto total"), k => M(k, 3, 4, 5, 6), ClubeModerno.Texto, true);
        Linha(T("Saldo"), k => M(k, 0, 1, 2, 3, 4, 5, 6), ClubeModerno.Texto, true, corDe: k => Sinal(k, 0, 1, 2, 3, 4, 5, 6));

        Control corpo = tabela;
        double largura = 600;
        if (Formato.Largo)
        {
            // as barras: receita (verde) e gasto (vermelho) de cada temporada, na mesma escala
            long[] rec = new long[3], gas = new long[3];
            for (int k = 0; k < 3; k++)
            {
                rec[k] = V(t[k], 0) + V(t[k], 1) + V(t[k], 2);
                gas[k] = -(V(t[k], 3) + V(t[k], 4) + V(t[k], 5) + V(t[k], 6));
            }
            double max = Math.Max(1, Math.Max(rec.Max(), gas.Max()));
            var barras = new StackPanel { Spacing = 14 };
            barras.Children.Add(ClubeModerno.Rotulo(T("Receita × gasto")));
            for (int k = 0; k < 3; k++)
            {
                var p = new StackPanel { Spacing = 5 };
                p.Children.Add(ClubeModerno.T((ano - k).ToString(), 13, k == 0 ? ClubeModerno.Azul : ClubeModerno.Texto, FontWeight.SemiBold));
                p.Children.Add(ClubeModerno.Progresso(rec[k] / max, ClubeModerno.Verde));
                p.Children.Add(ClubeModerno.Progresso(Math.Max(0, gas[k]) / max, ClubeModerno.Vermelho));
                p.Children.Add(ClubeModerno.T(FormatoDinheiro.Valor(s.S, rec[k] - gas[k]), 12, Sinal(k, 0, 1, 2, 3, 4, 5, 6)));
                barras.Children.Add(p);
            }
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20,190") };
            g.Children.Add(tabela);
            var sec = ClubeModerno.Secao(barras, 16);
            sec.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(sec, 2);
            g.Children.Add(sec);
            corpo = g;
            largura = 820;
        }
        var j = new JanelaModerna(TelaDinheiro.Texto(e.NomeCompleto), T("Balanço financeiro"), new EscudoClube(e, 44), corpo, largura, 512);
        j.Botao(T("OK"), true, j.Fechar);
        j.AoEnter = j.Fechar;
        if (fechou != null) j.Fechou += fechou;
        j.Abrir(abrir ?? (c => s.AbrirDialogo(c)), fechar ?? (c => s.FecharDialogo(c)));
    }

    // ListBoxBalance: sem borda, sem selecao, 14 linhas de 16, Arial 11 na cor da letra
    sealed class ListaBalanco(ReadOnlyMemory<byte>[] temporadas, int ano, IBrush letra, Moeda moeda) : Control
    {
        public override void Render(DrawingContext dc)
        {
            using var _ = dc.PushClip(new Rect(Bounds.Size));
            for (int i = 0; i < Rotulos.Length; i++)
            {
                double y = 16 * i, x = 2;
                bool negrito = i is 0 or 1 or 5;
                var f = negrito ? TelaDinheiro.Arial11b : TelaDinheiro.Arial11;
                using (dc.PushClip(new Rect(x, y, 120, 16))) W95.Escreve(dc, T(Rotulos[i]), f, letra, x, y);
                x += 120;
                for (int k = 0; k < 3; k++)
                {
                    var t = Valor(i, k);
                    if (t == null) { x += 90; continue; }   // 'Z' sem 6: so a largura
                    var ft = W95.Txt(t, f, letra);
                    dc.DrawText(ft, new Point(x + 90 - ft.WidthIncludingTrailingWhitespace, y));
                    x += 100;
                }
            }
        }

        string? Valor(int i, int k)
        {
            var b = temporadas[k];
            long V(int slot) => BinaryPrimitives.ReadInt32LittleEndian(b.Span.Slice(4 * slot));
            return i switch
            {
                0 => (ano - k).ToString(),
                2 or 3 or 4 => FormatoDinheiro.Valor(moeda, V(i - 2)),
                6 => FormatoDinheiro.Valor(moeda, V(4)),
                7 => FormatoDinheiro.Valor(moeda, V(3)),
                8 or 9 => FormatoDinheiro.Valor(moeda, V(i - 3)),
                11 => FormatoDinheiro.Valor(moeda, V(0) + V(1) + V(2)),
                12 => FormatoDinheiro.Valor(moeda, V(3) + V(4) + V(5) + V(6)),
                13 => FormatoDinheiro.Valor(moeda, V(0) + V(1) + V(2) + V(3) + V(4) + V(5) + V(6)),
                _ => null,
            };
        }
    }
}
