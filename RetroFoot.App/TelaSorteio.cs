using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Save;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.App;

// O sorteio dos jogos da Taca: TsorteioDlg (seg05:30f5 e o timerNextTeamTimer, 5:3232), nas
// posicoes do DFM. Mostra-se com o motor parado, depois de criados os jogos (S.Jogos, DS:270A)
// e de a lista das equipas da Taca (S.OutrasEquipas, DS:26F2) ser ordenada pelo nome
// (Epoca.OrdenarTacaPorNome, o que a janela faz ao abrir).
//  - listBoxOfCupTeams (3 colunas, Sorted): os nomes curtos de todas as equipas da Taca;
//  - o relogio (1,5 s se ha treinador humano na Taca, senao 0,4 s) percorre os jogos: num
//    tique a casa (o nome na sua cor, sai da lista das equipas), no outro o visitante, e o
//    jogo entra em listBoxOfGames, escolhido. Quando a ultima equipa humana sai, o relogio
//    passa a 0,4 s. Depois do ultimo jogo, mais 5 tiques e a janela fecha.
// O texto de cada jogo e do TGame (vmt[0], nao decifrado): aqui "Casa - Fora".
public sealed class TelaSorteio : UserControl
{
    public event Action? Fechou;

    readonly List<string> _equipas;
    readonly List<(SEquipa? Casa, SEquipa? Fora)> _jogos;
    readonly Func<SEquipa?, bool> _humano;
    readonly List<string> _feitos = new();
    readonly ColunasTexto _listaEquipas = null!;
    readonly Lista _listaJogos = null!;
    readonly Etiqueta _casa = null!, _fora = null!;
    readonly SorteioModerno? _m;
    readonly DispatcherTimer _relogio = new();
    int _indice, _fase, _humanos;
    bool _fechou;

    static readonly IBrush AmareloClaro = W95.Cor(0xFF, 0xFF, 0x80);   // Color = 8454143

    public TelaSorteio(Jogo98 s, Func<ushort, SEquipa?> equipa, Func<SEquipa?, bool> humano)
    {
        _humano = humano;
        var taca = s.OutrasEquipas.Select(equipa).Where(e => e != null).ToList();
        _equipas = taca.Select(P98.NomeEquipa).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        _jogos = s.Jogos.Select(g => (equipa(g.Casa), equipa(g.Fora))).ToList();
        // seg3:0b1e: as equipas da Taca com treinador humano
        _humanos = taca.Count(humano);
        _relogio.Interval = TimeSpan.FromMilliseconds(_humanos > 0 ? 1500 : 400);
        _relogio.Tick += (_, _) => Tique();
        if (Visual.Moderno)
        {
            // o visual Moderno (PartidaDialogosModernos.cs): o mesmo relogio
            var pote = taca.OrderBy(P98.NomeEquipa, StringComparer.OrdinalIgnoreCase).ToList();
            Content = _m = new SorteioModerno(s, pote!, _jogos.Count);
            _relogio.Start();
            return;
        }

        var j = new Janela(Idioma.T("Sorteio dos jogos da taça"), 631, 453, AmareloClaro);
        _listaEquipas = j.Por(new ColunasTexto { Itens = _equipas }, 8, 24, 377, 345);
        _listaJogos = j.Por(new Lista { AlturaItem = 14 }, 392, 24, 233, 345);
        _listaJogos.Quantos = () => _feitos.Count;
        _listaJogos.Desenhar = Lista.Textos(i => _feitos[i], P98.Arial11, W95.Preto);
        _listaJogos.Habilitado = false;
        _casa = j.Por(NovaEtiqueta(), 40, 400, 250, 22);
        _fora = j.Por(NovaEtiqueta(), 344, 400, 250, 22);
        j.AoFechar = () => { };
        Content = j;
        _relogio.Start();
    }

    static Etiqueta NovaEtiqueta() => new()
    {
        Familia = P98.FamArial, Tamanho = 16, Alin = TextAlignment.Center, Quebra = false,
    };

    // seg05:3232
    void Tique()
    {
        if (_indice >= _jogos.Count)
        {
            if (_indice >= _jogos.Count + 5) Fechar();
            _indice++;
            return;
        }
        var (casa, fora) = _jogos[_indice];
        if (_fase == 0)
        {
            if (_humanos == 0) _relogio.Interval = TimeSpan.FromMilliseconds(400);
            if (_humano(casa)) _humanos--;
            if (_m != null) _m.Casa(casa);
            else
            {
                Mostrar(_fora, null);
                Mostrar(_casa, casa);
            }
            _fase = 1;
            return;
        }
        if (_humano(fora)) _humanos--;
        _feitos.Add(P98.NomeEquipa(casa) + " - " + P98.NomeEquipa(fora));
        if (_m != null) { _m.Fora(fora); _m.Jogo(casa, fora); }
        else
        {
            Mostrar(_fora, fora);
            _listaJogos.Sel = _feitos.Count - 1;
        }
        _fase = 0;
        _indice++;
    }

    // o nome na cor da equipa, e sai da lista das equipas da Taca
    void Mostrar(Etiqueta et, SEquipa? e)
    {
        if (e == null) { et.Fundo = null; et.Mudar(""); return; }
        string nome = P98.NomeEquipa(e);
        et.Cor = P98.Letra(e);
        et.Fundo = P98.Fundo(e);
        et.Mudar(nome);
        int k = _equipas.IndexOf(nome);
        if (k >= 0) { _equipas.RemoveAt(k); _listaEquipas.InvalidateVisual(); }
    }

    void Fechar()
    {
        _relogio.Stop();
        if (_fechou) return;
        _fechou = true;
        Fechou?.Invoke();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _relogio.Stop();
    }

    // RETROFOOT_TELA=sorteio: um sorteio de verdade com o save do oraculo (como o JogarTaca: a
    // lista da Taca embaralhada, os jogos criados e a lista ordenada pelo nome)
    public static Control Demo(Jogo98 s, RetroFoot.Motor.Rodada.Mundo m)
    {
        m.S.Jogos.Clear();   // so a demo: sem fechar os jogos do save
        RetroFoot.Motor.Rodada.Listas.Embaralhar(m.R, s.OutrasEquipas);
        short n = (short)s.OutrasEquipas.Count;
        short p = 1;
        while (n / 2.0 > p) p *= 2;
        m.CriarJogos(s.OutrasEquipas.Select(id => m.Equipa(id)!).ToList(), (short)(n - p), Array.Empty<byte>(), null, (byte)RetroFoot.Motor.Partida.TipoJogo.Taca);
        RetroFoot.Motor.Temporada.Epoca.OrdenarTacaPorNome(s);
        return new TelaSorteio(s, m.Equipa, e => e != null && Mem.Humano(m.TreinadorDa(e)));
    }
}

// TListBox com Columns = 3 (sem selecao): os itens descem em colunas de largura/3; o que nao
// cabe vai para colunas a direita (rolagem horizontal pela roda)
sealed class ColunasTexto : Control
{
    public List<string> Itens = new();
    public Fonte Fonte = P98.Arial11;
    public double AlturaItem = 14;
    public int Colunas = 3;
    int primeira;

    public override void Render(DrawingContext dc)
    {
        var d = W95.Caixa(dc, new Rect(Bounds.Size), W95.Branco);
        int porColuna = Math.Max(1, (int)(d.Height / AlturaItem));
        double lc = Math.Floor(d.Width / Colunas);
        int total = (Itens.Count + porColuna - 1) / porColuna;
        primeira = Math.Clamp(primeira, 0, Math.Max(0, total - Colunas));
        using (dc.PushClip(d))
            for (int i = primeira * porColuna; i < Itens.Count; i++)
            {
                int c = i / porColuna - primeira, l = i % porColuna;
                if (c >= Colunas) break;
                using (dc.PushClip(new Rect(d.X + c * lc, d.Y, lc, d.Height)))
                    W95.Escreve(dc, Itens[i], Fonte, W95.Preto, d.X + c * lc + 2, d.Y + l * AlturaItem);
            }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        primeira -= Math.Sign(e.Delta.Y);
        InvalidateVisual();
        e.Handled = true;
    }
}
