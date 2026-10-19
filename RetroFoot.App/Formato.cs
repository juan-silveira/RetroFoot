using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RetroFoot.App;

// O formato da tela (Configuracoes.FormatoTela): 4:3, o visual do original (a jornada numa
// coluna, com paginas), ou 16:9 (a jornada em colunas, a tela da equipa larga). Lido no
// comeco; Salvar nas Configuracoes muda na hora (a janela, a tela da equipa e a jornada).
public static class Formato
{
    public static bool Largo { get; private set; } = Configuracoes.Ler().Largo;
    public static double Proporcao => Largo ? 16.0 / 9 : 4.0 / 3;
    public static event Action? Mudou;

    public static void Aplicar(bool largo)
    {
        if (largo == Largo) return;
        Largo = largo;
        Mudou?.Invoke();
    }

    // o tamanho da janela no formato: 16:9 com 85% da largura util da tela; 4:3 com 85% da
    // altura util (ate 90% da outra medida)
    public static Size Janela(Size area)
    {
        double w, h;
        if (Largo)
        {
            w = Math.Floor(area.Width * 0.85); h = Math.Floor(w * 9 / 16);
            if (h > area.Height * 0.9) { h = Math.Floor(area.Height * 0.9); w = Math.Floor(h * 16 / 9); }
        }
        else
        {
            h = Math.Floor(area.Height * 0.85); w = Math.Floor(h * 4 / 3);
            if (w > area.Width * 0.9) { w = Math.Floor(area.Width * 0.9); h = Math.Floor(w * 3 / 4); }
        }
        return new Size(w, h);
    }
}

// A caixa do formato: o filho no maior retangulo com a proporcao do formato, no centro; o
// resto preto (nunca as faixas verdes quando a janela tem outra proporcao).
public sealed class CaixaFormato : Decorator
{
    public CaixaFormato()
    {
        ClipToBounds = true;
        AttachedToVisualTree += (_, _) => { Formato.Mudou -= Mudou; Formato.Mudou += Mudou; };
        DetachedFromVisualTree += (_, _) => Formato.Mudou -= Mudou;
    }

    void Mudou() => InvalidateMeasure();

    public override void Render(DrawingContext dc) => dc.FillRectangle(Brushes.Black, new Rect(Bounds.Size));

    static Rect Caixa(Size s)
    {
        double w = s.Width, h = s.Height;
        if (double.IsInfinity(w) || double.IsInfinity(h)) return new Rect(0, 0, 800, 600);
        double p = Formato.Proporcao;
        if (w / h > p) w = Math.Round(h * p); else h = Math.Round(w / p);
        return new Rect(Math.Floor((s.Width - w) / 2), Math.Floor((s.Height - h) / 2), w, h);
    }

    protected override Size MeasureOverride(Size disponivel)
    {
        Child?.Measure(Caixa(disponivel).Size);
        return double.IsInfinity(disponivel.Width) || double.IsInfinity(disponivel.Height) ? new Size(800, 600) : disponivel;
    }

    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(Caixa(final));
        return final;
    }
}
