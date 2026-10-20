using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Partida;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// A janela de detalhes da partida e de substituicao: TsubstitutionDlg (seg02:21cb), desenhada
// nas posicoes do DFM (versao em Go: assets/formularios/TsubstitutionDlg.json). Abre no
// intervalo (motivo 4), quando o goleiro e expulso (3) e quando se clica num jogo durante a
// jornada (1). O motor ja reordenou o elenco (G, D, M, A e o nome) nos motivos 3 e 4; no 1 quem
// abre a janela faz isso (LigacaoPartida).
//
// O que o codigo do original faz (seg02):
//  - titulo: "Casa, golos - Fora, golos" (nomes curtos, +0x4F de cada equipa);
//  - lances (24a1): golo "1:0 Nome 23'" (contagem dos golos e golos de penalti ate ali,
//    " (pen.)" no de penalti), expulsao "Nome expulso 34'" so com "Cartoes vermelhos"
//    marcada (DS:2B30), lesao "Nome lesionou-se 34'" so com "Lesoes" (DS:2B31), anulado
//    "Nome 34' - Anulado"; o nome sem a estrela (seg12:1462). As caixinhas leem e gravam as
//    opcoes viewRedCardEvents e viewInjuryEvents. Com as regras do launcher (lances.asm), a
//    caixinha "Cartao amarelo" (desmarcada a cada vez que abre) mostra "Nome amarelo 34'";
//  - arbitro: "Nome (Pais)" (seg3:c8c9);
//  - a parte de baixo (as listas) so aparece se a equipa e humana, ou no motivo 1 com o
//    add-in 0x14 (ver os outros); senao a janela acaba acima do quadro (2006);
//  - Substituir e as listas so funcionam se a equipa e humana, tem substituicoes (+0x4D) e
//    o motivo e 3 ou 4, ou o add-in 0x12 (substituicoes durante o jogo) esta ligado;
//  - Update (2905): Substituir habilitado com um titular e um reserva escolhidos, e goleiro
//    so por goleiro, a nao ser no motivo 3 com o goleiro reserva entrando;
//  - substituteBtnClick (2a4f): quem sai fica fora (+0x10 = 0), quem entra titular e soma 1
//    jogo no historial (+0x17); a lista dos titulares e reordenada; forca dos setores
//    (3:397d) e uma substituicao a menos. A janela continua aberta (Continuar fecha).
public sealed class TelaSubstituicao : UserControl
{
    public event Action? Fechou;

    readonly JogoAtual _jg;
    readonly SEquipa _e;
    readonly int _motivo;
    readonly Opcoes _op;
    readonly List<SJogador> _tit = new(), _res = new();
    readonly ListaTexto _lances = new();
    readonly Caixinha? _amarelos;
    readonly Caixinha _vermelhos = null!, _lesoes = null!;
    readonly Lista? _listaTit, _listaRes;
    readonly Botao? _substituir;
    readonly Botao _continuar = null!;
    bool _fechou;

    // ClientWidth do DFM; a altura (2006): Top + Height do quadro + a altura do titulo + 5 (a
    // altura da janela), ou Top do quadro + titulo - 2 sem as listas
    const double Cw = 466, ChCompleta = 176 + 201 + 5 - 6, ChCurta = 176 - 2 - 6;

    // podeDuranteJogo = add-in 0x12; verOutros = add-in 0x14 (ambos do registro)
    public TelaSubstituicao(JogoAtual jg, int lado, int motivo, Opcoes op, bool cartoesAmarelos,
        bool podeDuranteJogo = false, bool verOutros = false)
    {
        _jg = jg; _e = jg.Equipa[lado]!; _motivo = motivo; _op = op;
        bool humano = P98.Humano(jg, lado);
        bool completa = humano || (motivo == 1 && verOutros);
        bool pode = humano && Mem.Substituicoes(_e) > 0 && (motivo == 3 || motivo == 4 || podeDuranteJogo);
        Focusable = true;
        if (Celular.Ativo)
        {
            // o celular (PartidaCelularJanelas.cs), com as mesmas regras
            var c = new SubstituicaoCelular(jg, lado, motivo, op, cartoesAmarelos, completa, pode);
            c.Fechou += Fechar;
            Content = c;
            return;
        }
        if (Visual.Moderno)
        {
            // o visual Moderno (SubstituicaoModerna.cs), com as mesmas regras
            var m = new SubstituicaoModerna(jg, lado, motivo, op, cartoesAmarelos, completa, pode);
            m.Fechou += Fechar;
            Content = m;
            return;
        }

        var casa = jg.Equipa[Lado.Casa]; var fora = jg.Equipa[Lado.Visit];
        string titulo = $"{P98.NomeEquipa(casa)}, {(casa == null ? 0 : Mem.Golos(casa))} - {P98.NomeEquipa(fora)}, {(fora == null ? 0 : Mem.Golos(fora))}";
        var j = new Janela(titulo, Cw, completa ? ChCompleta : ChCurta, W95.Face);

        // os lances e as caixinhas (no launcher, "Cartao amarelo" a 16 e as outras a 136 e 264)
        j.Por(_lances, 16, 16, 185, 121);
        if (cartoesAmarelos)
        {
            _amarelos = j.Por(new Caixinha { Texto = Idioma.T("Cartão amarelo") }, 16, 144, 113, 17);
            _vermelhos = j.Por(new Caixinha { Texto = Idioma.T("Cartão vermelho"), Marcada = op.VerExpulsoes }, 136, 144, 121, 17);
            _lesoes = j.Por(new Caixinha { Texto = Idioma.T("Lesão"), Marcada = op.VerLesoes }, 264, 144, 65, 17);
        }
        else
        {
            _vermelhos = j.Por(new Caixinha { Texto = Idioma.T("Cartões vermelhos"), Marcada = op.VerExpulsoes }, 16, 144, 121, 17);
            _lesoes = j.Por(new Caixinha { Texto = Idioma.T("Lesões"), Marcada = op.VerLesoes }, 144, 144, 65, 17);
        }
        _vermelhos.Mudou += () => { _op.VerExpulsoes = _vermelhos.Marcada; Lances(); };
        _lesoes.Mudou += () => { _op.VerLesoes = _lesoes.Marcada; Lances(); };
        if (_amarelos != null) _amarelos.Mudou += Lances;

        // o arbitro
        j.Por(new GrupoPartida { Texto = Idioma.T("Árbitro") }, 216, 8, 241, 49);
        string arb = jg.Arbitro is { } a ? P98.Texto(a.Nome) + " (" + P98.NomePais(P98.Texto(a.Pais)) + ")" : "";
        j.Por(new Etiqueta { Texto = arb, Familia = new FontFamily("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif"), Negrito = true, Quebra = false }, 224, 32, 225, 16);

        _continuar = j.Por(new Botao
        {
            Texto = Idioma.T("Continuar"), Fonte = P98.Arial9, Glifo = W95.Imagem("Botoes/BBOK.png"), NumGlifos = 2, GlifoEmCima = true,
        }, 372, 96, 64, 64);
        _continuar.Clique += Fechar;
        j.AoFechar = Fechar;

        if (completa)
        {
            // o quadro, as listas e o Substituir nas cores da equipa (2006)
            var letra = P98.Letra(_e); var fundo = P98.Fundo(_e);
            j.Por(new GrupoPartida { Texto = P98.NomeEquipa(_e), Fonte = P98.Arial11b, Cor = letra, Fundo = fundo }, 8, 176, 449, 201);
            foreach (var p in _e.Jogadores)
                switch (Mem.Escalacao(p))
                {
                    case Mem.EscTitular: _tit.Add(p); break;
                    case Mem.EscReserva: _res.Add(p); break;
                }
            // as listas com os 4 pontos que os EXEs do launcher somam (a TListBox do Delphi 1
            // fica 4 mais baixa que o DFM; aqui a Lista mostra Height - 4)
            _listaTit = j.Por(P98.ListaJogadores(_tit, _e, P98.MsSans11, 16), 24, 192, 185, 178 + 4);
            _listaRes = j.Por(P98.ListaJogadores(_res, _e, P98.MsSans11, 16), 256, 192, 185, 82 + 4);
            _substituir = j.Por(new Botao
            {
                Texto = Idioma.T("Substituir"), Fonte = P98.Arial9, Glifo = W95.Imagem("Botoes/SUBSTITUIR.png"), NumGlifos = 2, GlifoEmCima = true,
            }, 8 + 360, 176 + 128, 64, 64);
            _substituir.Habilitado = false;
            _substituir.IsVisible = pode;
            _listaTit.Habilitado = _listaRes.Habilitado = pode;
            _listaTit.Clique += _ => Atualizar();
            _listaRes.Clique += _ => Atualizar();
            _substituir.Clique += Substituir;
        }

        Lances();
        Content = j;
    }

    // seg02:24a1
    void Lances()
    {
        _lances.Itens.Clear();
        int gc = 0, gf = 0;
        bool amarelos = _amarelos?.Marcada == true;
        foreach (var l in _jg.Lances)
        {
            string nome = P98.NomeSimples(l.Jogador);
            string min = MinutoTexto.SemAspa(l.Minuto, l.Acrescimo);
            switch (l.Tipo)
            {
                case TipoLance.Gol:
                case TipoLance.Penalti:
                    if (l.Lado == Lado.Casa) gc++; else gf++;
                    _lances.Itens.Add($"{gc}:{gf} {nome} {min}'" + (l.Tipo == TipoLance.Penalti ? Idioma.T(" (pen.)") : ""));
                    break;
                case TipoLance.Amarelo:
                    if (amarelos) _lances.Itens.Add(Idioma.T("{0} amarelo {1}'", nome, min));
                    break;
                case TipoLance.Expulsao:
                    if (_vermelhos.Marcada) _lances.Itens.Add(Idioma.T("{0} expulso {1}'", nome, min));
                    break;
                case TipoLance.Lesao:
                    if (_lesoes.Marcada) _lances.Itens.Add(Idioma.T("{0} lesionou-se {1}'", nome, min));
                    break;
                case TipoLance.GolAnulado:
                    _lances.Itens.Add(Idioma.T("{0} {1}' - Anulado", nome, min));
                    break;
            }
        }
        _lances.Mudou();
    }

    // seg02:2905
    void Atualizar()
    {
        if (_listaTit == null || _listaRes == null || _substituir == null) return;
        bool ok = _listaTit.Sel >= 0 && _listaTit.Sel < _tit.Count && _listaRes.Sel >= 0 && _listaRes.Sel < _res.Count;
        if (ok) ok = PodeTrocar(_tit[_listaTit.Sel], _res[_listaRes.Sel], _motivo);
        _substituir.Habilitado = ok;
        _substituir.IsVisible = Mem.Substituicoes(_e) > 0;
        _listaTit.Habilitado = _listaRes.Habilitado = _substituir.IsVisible;
    }

    // seg02:2a4f
    void Substituir()
    {
        if (_listaTit == null || _listaRes == null || _substituir == null || !_substituir.Habilitado) return;
        if (_listaTit.Sel < 0 || _listaTit.Sel >= _tit.Count || _listaRes.Sel < 0 || _listaRes.Sel >= _res.Count) return;
        Trocar(_e, _tit, _res, _tit[_listaTit.Sel], _res[_listaRes.Sel]);
        _listaTit.Sel = -1;
        _listaRes.Sel = -1;
        _listaTit.InvalidateVisual();
        _listaRes.InvalidateVisual();
        Atualizar();
        Focus();
    }

    // seg02:2905: goleiro so por goleiro, a nao ser no motivo 3 com o goleiro reserva entrando
    internal static bool PodeTrocar(SJogador sai, SJogador entra, int motivo)
    {
        bool gTit = sai.Posicao == 0, gRes = entra.Posicao == 0;
        return gTit == gRes || (motivo == 3 && gRes);
    }

    // seg02:2a4f: quem sai fica fora, quem entra titular (+1 jogo); listas, setores e +0x4D
    internal static void Trocar(SEquipa e, List<SJogador> tit, List<SJogador> res, SJogador sai, SJogador entra)
    {
        Mem.MudarEscalacao(sai, Mem.EscFora);
        Mem.MudarEscalacao(entra, Mem.EscTitular);
        P98.SomaJogo(entra);
        tit.Remove(sai);
        tit.Add(entra);
        P98.OrdenarPorPosicaoNome(tit);
        res.Remove(entra);
        Sorteio.ForcaDosSetores(e);
        Mem.MudarSubstituicoes(e, (short)(Mem.Substituicoes(e) - 1));
    }

    void Fechar()
    {
        if (_fechou) return;
        _fechou = true;
        Fechou?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // o foco comeca no Continuar (ActiveControl = OKBtn)
        if (e.Key is Key.Enter or Key.Escape) { Fechar(); e.Handled = true; }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Content is not SubstituicaoModerna and not SubstituicaoCelular) Focus();
    }
}
