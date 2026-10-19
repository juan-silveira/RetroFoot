using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RetroFoot.Motor.Partida;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// A janela da lesao de um jogador de equipa humana: TforcedSubstitutionDlg (seg06:3780),
// nas posicoes do DFM. So abre com substituicoes (+0x4D > 0); quem chama ve isso.
//  - titulo: o nome curto da equipa; janela, letra e lista nas cores da equipa;
//  - texto: "Nome (Posicao) se lesionou e tem de ser substituido.\rEscolha o jogador a
//    entrar." (o nome com a estrela, seg12:13fe; a posicao de DS:18A8);
//  - a lista sao os reservas, na ordem do elenco (sem reordenar); conta os goleiros (DS:286E);
//  - Update (38c1): sem escolhido o OK fica como estava (desabilitado ao abrir); com um
//    escolhido, OK so se for do mesmo tipo (goleiro/linha) de quem saiu ou se nao houver
//    goleiro no banco;
//  - OK (3a1a): com um escolhido, ele fica titular, quem saiu fica fora e a equipa gasta uma
//    substituicao (sem somar jogo no historial). Depois, a forca dos setores (3:397d).
public sealed class TelaSubstituicaoForcada : UserControl
{
    public event Action? Fechou;

    readonly SEquipa _e;
    readonly SJogador _saiu;
    readonly List<SJogador> _res = new();
    readonly Lista _lista = null!;
    readonly Botao _ok = null!;
    readonly int _goleiros;
    bool _fechou;

    public TelaSubstituicaoForcada(SEquipa e, SJogador saiu)
    {
        _e = e; _saiu = saiu;
        Focusable = true;
        if (Visual.Moderno)
        {
            // o visual Moderno (PartidaDialogosModernos.cs), com as mesmas regras
            var m = new SubstituicaoForcadaModerna(e, saiu);
            m.Fechou += () => { if (_fechou) return; _fechou = true; Fechou?.Invoke(); };
            Content = m;
            return;
        }
        var letra = P98.Letra(e); var fundo = P98.Fundo(e);
        var j = new Janela(P98.NomeEquipa(e), 384, 190, fundo);
        foreach (var p in e.Jogadores)
            if (Mem.Escalacao(p) == Mem.EscReserva)
            {
                if (p.Posicao == 0) _goleiros++;
                _res.Add(p);
            }
        string pos = P98.Posicao(saiu.Posicao);
        j.Por(new Etiqueta
        {
            Texto = Idioma.T("{0} ({1}) lesionou-se e tem de ser substituído.\rEscolha o jogador a entrar.", P98.NomeJogador(saiu), pos),
            // (o DFM pede MS Sans Serif; a do Assets nao desenha bem o "í" e o "ê")
            Familia = P98.FamArial, Negrito = true, Cor = letra,
        }, 8, 8, 361, 49);
        j.Por(new Bevel(), 8, 80, 241, 97);
        _lista = j.Por(P98.ListaJogadores(_res, e, P98.Arial11b, 15), 16, 88, 225, 81 + 4);
        _lista.Clique += _ => Atualizar();
        _lista.Duplo += _ => Ok();
        _ok = j.Por(Botao.Ok(P98.Arial9b, true), 308, 112, 64, 64);
        // (sem ninguem no banco que possa entrar o original ficaria preso, sem fechar: aqui o
        // OK so fecha. Ex.: so goleiros no banco e saiu um jogador de linha)
        _ok.Habilitado = !_res.Exists(Elegivel);
        _ok.Clique += Ok;
        j.AoFechar = () => { };
        Content = j;
    }

    // seg06:38c1
    void Atualizar()
    {
        if (_lista.Sel < 0 || _lista.Sel >= _res.Count) return;
        _ok.Habilitado = Elegivel(_res[_lista.Sel]) || !_res.Exists(Elegivel);
    }

    // pode entrar no lugar de quem saiu: do mesmo tipo (goleiro/linha), ou sem goleiro no banco
    bool Elegivel(SJogador p) => Elegivel(_saiu, p, _goleiros);

    internal static bool Elegivel(SJogador saiu, SJogador p, int goleiros) => (saiu.Posicao == 0) == (p.Posicao == 0) || goleiros == 0;

    // seg06:3a1a: o escolhido (se puder) entra no lugar de quem saiu; depois os setores
    internal static void Confirmar(SEquipa e, SJogador saiu, SJogador? entra)
    {
        if (entra != null)
        {
            Mem.MudarEscalacao(entra, Mem.EscTitular);
            Mem.MudarEscalacao(saiu, Mem.EscFora);
            Mem.MudarSubstituicoes(e, (short)(Mem.Substituicoes(e) - 1));
        }
        Sorteio.ForcaDosSetores(e);
    }

    // seg06:3a1a
    void Ok()
    {
        if (_fechou || !_ok.Habilitado) return;
        Confirmar(_e, _saiu, _lista.Sel >= 0 && _lista.Sel < _res.Count && Elegivel(_res[_lista.Sel]) ? _res[_lista.Sel] : null);
        _fechou = true;
        Fechou?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && _ok != null) { Ok(); e.Handled = true; }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // depois de a lista entrar na arvore (os filhos entram depois do pai) e de quem abriu
        // focar o dialogo: com o foco no dialogo as setas nao escolhiam ninguem e o OK ficava
        // desligado (a lesao so saia com o rato)
        if (_lista != null) Avalonia.Threading.Dispatcher.UIThread.Post(() => _lista.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
