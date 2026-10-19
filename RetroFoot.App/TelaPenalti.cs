using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Partida;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// A janela do penalti num jogo com treinador humano: TpenaltyDlg (seg08:328b), nas posicoes
// do DFM, nas cores da equipa que cobra (janela, letra, lista).
//  - titulo: o nome curto de quem cobra;
//  - quem cobra e humano: "Treinador, escolha o jogador para bater o penalti!", a lista dos
//    titulares (na ordem do elenco), o quadro e o Chutar (desabilitado ate escolher). Chutar
//    (3751): esconde a escolha, "Goleador: Nome" e liga o relogio (contador ja em 1);
//  - quem cobra e o computador (o humano defende): o batedor e o titular de maior P
//    (14:0ee6 com 11:201d), "Goleador: Nome" escondido ate o 1o tique, relogio ligado;
//  - relogio (382d, 1 s): no tique 3 o goleiro (3:67c1) e a cobranca (3:81ed), e o texto do
//    resultado (DS:177A) aparece grande; no tique 5 fecha. Antes disso nao fecha (372a).
// A cobranca vem de fora (cobrar): o motor e quem sorteia (Jornada.Penalti); a janela so a
// mostra no tempo do original. Terminou devolve o batedor.
public sealed class TelaPenalti : UserControl
{
    public event Action<SJogador>? Terminou;

    readonly List<SJogador> _titulares;
    readonly Func<SJogador, int> _cobrar;
    readonly Etiqueta _escolha = null!, _resultado = null!;
    readonly PenaltiModerno? _m;
    readonly Lista? _lista;
    readonly Bevel? _quadro;
    readonly Botao? _chutar;
    readonly DispatcherTimer _relogio = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    SJogador? _batedor;
    int _tiques;
    bool _fim;

    // humano: titulares para escolher (batedorCpu = null); computador: batedorCpu
    public TelaPenalti(SEquipa atacante, string treinador, List<SJogador> titulares, SJogador? batedorCpu, Func<SJogador, int> cobrar)
    {
        _titulares = titulares; _cobrar = cobrar;
        Focusable = true;
        _relogio.Tick += (_, _) => Tique();
        if (Visual.Moderno)
        {
            // o visual Moderno (PartidaDialogosModernos.cs): o mesmo relogio
            _m = new PenaltiModerno(atacante, treinador, titulares, batedorCpu == null);
            _m.Chutou += b =>
            {
                if (_relogio.IsEnabled || _batedor != null) return;
                _batedor = b;
                _m.MostrarBatedor(b);
                _relogio.Start();
            };
            if (batedorCpu == null) _tiques = 1;
            else { _batedor = batedorCpu; _relogio.Start(); }
            Content = _m;
            return;
        }
        var letra = P98.Letra(atacante); var fundo = P98.Fundo(atacante);
        var j = new Janela(P98.NomeEquipa(atacante), 388, 316, fundo);
        j.Por(new Etiqueta
        {
            Texto = Idioma.T("P E N A L T I"), Familia = P98.FamTimes, Tamanho = 48, Negrito = true, Cor = letra,
            Alin = TextAlignment.Center, Quebra = false,
        }, 8, 16, 369, 55);
        _escolha = j.Por(new Etiqueta { Familia = P98.FamArial, Tamanho = 11, Negrito = true, Cor = letra, Quebra = false }, 8, 80, 369, 14);
        _resultado = j.Por(new Etiqueta
        {
            Familia = P98.FamTimes, Tamanho = 53, Negrito = true, Italico = true, Cor = letra, Alin = TextAlignment.Center, Quebra = false,
        }, 8, 136, 369, 73);
        _resultado.IsVisible = false;
        if (batedorCpu == null)
        {
            _escolha.Texto = Idioma.T("{0}, escolha o jogador para marcar o penalti!", treinador);
            _quadro = j.Por(new Bevel(), 8, 112, 185, 193);
            _lista = j.Por(P98.ListaJogadores(titulares, atacante, P98.MsSans11, 16), 16, 120, 169, 178 + 4);
            _lista.Clique += _ => { if (_chutar != null) _chutar.Habilitado = true; };
            _chutar = j.Por(new Botao
            {
                Texto = Idioma.T("Chutar"), Fonte = P98.MsSans11b, Glifo = W95.Imagem("Botoes/BBOK.png"), NumGlifos = 2, GlifoEmCima = true,
            }, 256, 228, 64, 64);
            _chutar.Habilitado = false;
            _chutar.Clique += Chutar;
            _tiques = 1;
        }
        else
        {
            _batedor = batedorCpu;
            _escolha.Texto = Idioma.T("Marcador: {0}", P98.NomeJogador(batedorCpu));
            _escolha.IsVisible = false;
            _relogio.Start();
        }
        j.AoFechar = () => { };   // FormCloseQuery: so fecha no fim
        Content = j;
    }

    // seg08:3751
    void Chutar()
    {
        if (_lista == null || _chutar == null || !_chutar.Habilitado || _relogio.IsEnabled) return;
        if (_lista.Sel < 0 || _lista.Sel >= _titulares.Count) return;
        _batedor = _titulares[_lista.Sel];
        _chutar.IsVisible = false;
        _lista.IsVisible = false;
        _quadro!.IsVisible = false;
        _escolha.Mudar(Idioma.T("Marcador: {0}", P98.NomeJogador(_batedor)));
        _relogio.Start();
    }

    // seg08:382d
    void Tique()
    {
        _tiques++;
        if (_tiques == 1)
        {
            if (_m != null) _m.MostrarBatedor(_batedor!); else _escolha.IsVisible = true;
            return;
        }
        if (_tiques == 3)
        {
            int r = _cobrar(_batedor!);
            if (_m != null) { _m.MostrarResultado(r); return; }
            _resultado.Mudar(P98.Resultado(r));
            _resultado.IsVisible = true;
            return;
        }
        if (_tiques == 5 && !_fim)
        {
            _fim = true;
            _relogio.Stop();
            Terminou?.Invoke(_batedor!);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && _chutar is { IsVisible: true }) { Chutar(); e.Handled = true; }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // (a lista so entra na arvore depois do pai: o foco vai depois, como na substituicao forcada)
        if (_m == null) Dispatcher.UIThread.Post(() => { if (_lista is { IsVisible: true }) _lista.Focus(); else Focus(); }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _relogio.Stop();
    }
}
