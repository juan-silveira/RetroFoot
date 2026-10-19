using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Partida;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// O desempate da Taca por penaltis: TPenaltiesDlg (seg11:0d46 e os relogios), nas posicoes do
// DFM. O motor ja decidiu as cobrancas (Penaltis.DesempateTaca, na ordem do Random); a janela
// mostra uma a cada tique do Timer1 (3 s se uma das equipas tem treinador humano, senao 1 s),
// casa e fora alternadas. Cada linha (TPenaltyPair, 11:0b21; Formatar 11:15ab) tem os dois
// batedores (130 pontos, sem negrito) e os dois resultados (100, em negrito), com 30 vazios
// entre casa e fora; a ultima fica escolhida. textResult e "golos casa:golos fora". Depois da
// ultima, o Timer2 (3 s) fecha; antes disso a janela nao fecha (FormCloseQuery, 11:1127).
public sealed class TelaPenaltis : UserControl
{
    public event Action? Fechou;

    readonly Desempate _d;
    readonly List<(SJogador? Bat, int Res)[]> _pares = new();
    readonly Lista _lista = null!;
    readonly Etiqueta _placar = null!;
    readonly PenaltisModerno? _m;
    readonly DispatcherTimer _timer1 = new(), _timer2 = new() { Interval = TimeSpan.FromMilliseconds(3000) };
    readonly int[] _golos = new int[2];
    int _mostradas;
    bool _fechou;

    static readonly IBrush VerdeClaro = W95.Cor(0x80, 0xFF, 0x80);   // Color = 8454016

    public TelaPenaltis(JogoAtual jg, Desempate d)
    {
        _d = d;
        bool humano = P98.Humano(jg, Lado.Casa) || P98.Humano(jg, Lado.Visit);
        _timer1.Interval = TimeSpan.FromMilliseconds(humano ? 3000 : 1000);
        _timer1.Tick += (_, _) => Proxima();
        _timer2.Tick += (_, _) => Fechar();
        if (Visual.Moderno)
        {
            // o visual Moderno (PartidaDialogosModernos.cs): os mesmos relogios
            Content = _m = new PenaltisModerno(jg);
            if (d.Cobrancas.Count == 0) Acabou(); else _timer1.Start();
            return;
        }
        // Width 640 x Height 357 da janela (bsSizeable): a area cliente fica com 632 x 323
        var j = new Janela(Idioma.T("Penaltis"), 632, 323, VerdeClaro);
        j.Por(new Etiqueta
        {
            Texto = Idioma.T("D e s e m p a t e   p o r   P e n a l t i s"), Familia = P98.FamTimes, Tamanho = 37, Negrito = true,
            Italico = true, Alin = TextAlignment.Center, Quebra = false,
        }, 8, 12, 617, 49);
        _placar = j.Por(new Etiqueta
        {
            Familia = P98.FamArial, Tamanho = 48, Negrito = true, Cor = W95.Azul, Alin = TextAlignment.Center, Quebra = false,
        }, 200, 76, 233, 56);
        // os nomes nas cores das equipas, encostados as pontas da lista (11:0fb5-10cd)
        const double lx = 58, lw = 517;
        for (int lado = 0; lado < 2; lado++)
        {
            var e = jg.Equipa[lado];
            string nome = P98.NomeEquipa(e);
            double w = Math.Ceiling(P98.Txt(nome, P98.Arial16b, W95.Preto).Width);
            var et = new Etiqueta
            {
                Texto = nome, Familia = P98.FamArial, Tamanho = 16, Negrito = true, Cor = P98.Letra(e), Fundo = P98.Fundo(e), Quebra = false,
            };
            j.Por(et, lado == Lado.Casa ? lx : lx + lw - w, 136, w, 19);
        }
        _lista = j.Por(new Lista { AlturaItem = 22, Fundo = W95.Branco }, lx, 176, lw, 113);
        _lista.Quantos = () => _pares.Count;
        _lista.Desenhar = DesenharPar;
        _lista.Habilitado = false;
        j.AoFechar = () => { };
        Content = j;
        if (d.Cobrancas.Count == 0) _timer2.Start(); else _timer1.Start();
    }

    // Timer1Timer (11:114d)
    void Proxima()
    {
        if (_mostradas >= _d.Cobrancas.Count) return;
        var c = _d.Cobrancas[_mostradas++];
        if (c.Resultado == ResultadoPenalti.Gol) _golos[c.Lado]++;
        if (c.Lado == Lado.Casa || _pares.Count == 0)
            _pares.Add(new (SJogador?, int)[] { (null, 0), (null, 0) });
        _pares[^1][c.Lado] = (c.Batedor, c.Resultado);
        if (_m != null) _m.Atualizar(_pares, _golos);
        else
        {
            _lista.Sel = _pares.Count - 1;
            _placar.Mudar($"{_golos[Lado.Casa]}:{_golos[Lado.Visit]}");
        }
        if (_mostradas == _d.Cobrancas.Count) Acabou();
    }

    void Acabou()
    {
        _timer1.Stop();
        _timer2.Start();
        _m?.Fim(_d.PassaVisitante);
    }

    // 11:144f com o texto de 11:15ab
    void DesenharPar(DrawingContext dc, int i, Rect r, bool escolhido)
    {
        if (i >= _pares.Count) return;
        var cor = W95.Preto;
        if (escolhido)
        {
            dc.FillRectangle(W95.Destaque, r);
            cor = W95.Branco;
        }
        double x = r.X + 2;
        double y = r.Y + Math.Floor((r.Height - P98.Txt("Ag", P98.Arial16, cor).Height) / 2);
        for (int k = 0; k < 2; k++)
        {
            var (b, res) = _pares[i][k];
            using (dc.PushClip(new Rect(x, r.Y, 130, r.Height)))
                if (b != null) W95.Escreve(dc, P98.NomeJogador(b), P98.Arial16, cor, x, y);
            x += 130;
            using (dc.PushClip(new Rect(x, r.Y, 100, r.Height)))
                if (res != 0) W95.Escreve(dc, P98.Resultado(res), P98.Arial16b, cor, x, y);
            x += 100;
            if (k == 0) x += 30;
        }
    }

    // Timer2Timer (11:1431)
    void Fechar()
    {
        _timer2.Stop();
        if (_fechou) return;
        _fechou = true;
        Fechou?.Invoke();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer1.Stop();
        _timer2.Stop();
    }
}
