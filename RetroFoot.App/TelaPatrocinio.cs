using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Patrocinio;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A proposta de patrocinio (patrocinio.asm, rotina perguntar): a janela de Sim/Nao do aumento
// de salario (TaskMoreSalaryDlg, DS:0x2880) no estilo de patrocinio do patch. ClientWidth 369,
// ClientHeight 320 (era 260); o fundo e SPONSORS\FUNDO.BMP esticado por cima do vermelho (sem o
// arquivo, fica o vermelho); o rotulo de cima "Proposta de patrocínio" em azul-marinho (Arial
// -16 negrito, em (16, 24)); o texto em preto (Arial -13 negrito, (20, 56), 329x113); o logo da
// marca (TImage 120x60 em (124, 176), Center) e os botoes Sim e Nao (64x64, Arial -9 negrito,
// glifo em cima) descidos para o Top 248. O titulo da janela e "Patrocínio".
public sealed class TelaPatrocinio : UserControl
{
    const double L = 369, A = 320;

    readonly Action<bool> fim;
    bool respondeu;

    public TelaPatrocinio(Proposta p, Marcas? marcas, Moeda moeda, Action<bool> fim)
    {
        this.fim = fim;
        Focusable = true;
        // o celular (PartidaCelularJanelas.cs): a folha com a mesma resposta
        if (Celular.Ativo) { Content = PatrocinioCelular.Folha(p, marcas, moeda, Responder); return; }
        if (Visual.Moderno) { Moderna(p, marcas, moeda); return; }
        var fundoArq = marcas?.CaminhoFundo();
        var fundo = Logo(fundoArq);
        var j = new Janela(T("Patrocínio"), L, A, fundo != null ? Brushes.Transparent : W95.Vermelho);
        if (fundo != null)
            j.Por(new Image { Source = fundo, Stretch = Stretch.Fill }, 0, 0, L, A);
        j.Por(new Rotulo { Texto = T("Proposta de patrocínio"), Fonte = W95.Arial16b, Cor = W95.Marinho, Auto = true }, 16, 24, 200, 19);
        // o TLabel sem WordWrap: so o CR quebra a linha
        var texto = Patrocinios.Texto(p, v => FormatoDinheiro.Valor(moeda, v)).Replace('\r', '\n');
        j.Por(new Rotulo { Texto = texto, Fonte = W95.Arial13b, Cor = W95.Preto }, 20, 56, 329, 113);
        if (Logo(p.Registro != null ? marcas?.CaminhoLogo(p.Registro) : null) is { } img)
            j.Por(new Image { Source = img, Stretch = Stretch.None, ClipToBounds = true }, 124, 176, 120, 60);
        var f9 = new Fonte(new FontFamily("avares://RetroFoot.App/Assets/Fontes#Liberation Sans"), 9, true);
        var sim = j.Por(new Botao { Texto = T("Sim"), Fonte = f9, Glifo = W95.Imagem("Botoes/BBYES.png"), NumGlifos = 2, GlifoEmCima = true }, 98, 248, 64, 64);
        var nao = j.Por(new Botao { Texto = T("Não"), Fonte = f9, Glifo = W95.Imagem("Botoes/BBNO.png"), NumGlifos = 2, GlifoEmCima = true }, 206, 248, 64, 64);
        sim.Clique += () => Responder(true);
        nao.Clique += () => Responder(false);
        // ActiveControl = YesBtn: Enter responde Sim; o fechar da janela e o Esc, Nao
        j.AoEnter = () => Responder(true);
        j.AoFechar = () => Responder(false);
        Content = j;
    }

    // ------------------------------------------------------------ Moderno
    // A proposta no visual Moderno: o logo da marca numa placa clara, o nome e a proposta em
    // linhas (valor, duracao, tipo e a multa), "Aceita?" e os botoes Recusar e Aceitar (Enter
    // aceita, Esc recusa, como no original). O mesmo tamanho nos dois formatos.
    void Moderna(Proposta p, Marcas? marcas, Moeda moeda)
    {
        string Ext(long v) => FormatoDinheiro.Valor(moeda, v);
        var corpo = new StackPanel { Spacing = 14 };
        var topo = new DockPanel();
        var logo = Logo(p.Registro != null ? marcas?.CaminhoLogo(p.Registro) : null);
        var placa = new Border
        {
            Width = 140, Height = 76, CornerRadius = new CornerRadius(10), Background = TemaVerde.DiscoLogo,
            Margin = new Thickness(0, 0, 16, 0),
            Child = logo != null
                ? new Image { Source = logo, Stretch = Stretch.Uniform, Width = 120, Height = 60 }
                : ClubeModerno.T(p.Nome.Length > 0 ? p.Nome[..1] : "?", 34, ClubeModerno.Escuro, FontWeight.Bold, TextAlignment.Center),
        };
        if (placa.Child is TextBlock tb) tb.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        DockPanel.SetDock(placa, Dock.Left);
        topo.Children.Add(placa);
        var nome = new StackPanel { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Spacing = 4 };
        nome.Children.Add(ClubeModerno.Rotulo(p.Tipo switch
        {
            Proposta.Renovacao => T("Renovação de patrocinador master"),
            Proposta.Pontual => T("Patrocínio no uniforme"),
            _ => T("Patrocinador master"),
        }));
        nome.Children.Add(ClubeModerno.T(p.Nome, 22, ClubeModerno.Texto, FontWeight.Bold));
        topo.Children.Add(nome);
        corpo.Children.Add(topo);

        var linhas = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,*") };
        int r = 0;
        void Linha(string rot, string val, IBrush cor)
        {
            linhas.RowDefinitions.Add(new RowDefinition(28, GridUnitType.Pixel));
            var a = ClubeModerno.T(rot, 13, ClubeModerno.Fraco);
            var b = ClubeModerno.T(val, 14, cor, FontWeight.SemiBold);
            a.VerticalAlignment = b.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Grid.SetRow(a, r); Grid.SetRow(b, r); Grid.SetColumn(b, 2);
            linhas.Children.Add(a); linhas.Children.Add(b);
            r++;
        }
        bool pontual = p.Tipo == Proposta.Pontual;
        string n = p.Quantidade.ToString();
        Linha(T("Valor"), Ext(p.Valor) + (pontual ? T(" por jogo") : T(" por temporada")), ClubeModerno.Verde);
        Linha(T("Duração"), n + (pontual ? (p.Quantidade == 1 ? T(" jogo") : T(" jogos")) : (p.Quantidade == 1 ? T(" temporada") : T(" temporadas"))), ClubeModerno.Texto);
        if (!pontual && p.Multa != 0) Linha(T("Multa pela troca"), Ext(p.Multa), ClubeModerno.Vermelho);
        corpo.Children.Add(ClubeModerno.Secao(linhas));
        corpo.Children.Add(ClubeModerno.T(pontual ? T("Para estampar a marca no uniforme. Aceita?")
            : p.Tipo == Proposta.Renovacao ? T("Para renovar o contrato de patrocinador master. Aceita?")
            : T("Para ser o patrocinador master do seu time. Aceita?"), 14, ClubeModerno.Texto));

        var j = new JanelaModerna(T("Patrocínio"), T("Proposta de patrocínio"), null, corpo, 500, 400);
        j.Botao(T("Recusar"), false, () => Responder(false));
        j.Botao(T("Aceitar"), true, () => Responder(true));
        j.AoEnter = () => Responder(true);
        j.AoFechar = () => Responder(false);
        Width = j.Width;
        Height = j.Height;
        Content = j;
    }

    void Responder(bool sim)
    {
        if (respondeu) return;
        respondeu = true;
        fim(sim);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Return or Key.Space) { Responder(true); e.Handled = true; }
        else if (e.Key == Key.Escape) { Responder(false); e.Handled = true; }
    }

    // os BMP da pasta SPONSORS (logos 120x60 e o fundo), lidos uma vez
    static readonly Dictionary<string, Bitmap?> imagens = new();

    public static Bitmap? Logo(string? arquivo)
    {
        if (arquivo == null) return null;
        lock (imagens)
        {
            if (imagens.TryGetValue(arquivo, out var b)) return b;
            try { b = new Bitmap(arquivo); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException) { b = null; }
            return imagens[arquivo] = b;
        }
    }
}
