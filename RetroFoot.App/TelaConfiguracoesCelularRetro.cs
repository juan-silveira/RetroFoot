using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As Configuracoes do celular no Retro: a janela do computador (MontarClassico em
// TelaConfiguracoes.cs) adaptada ao toque, em pe: a barra de titulo, as cinco abas do
// TTabbedNotebook (Jogo, Regras, Aparencia, Idioma, Dados) com as mesmas molduras de grupo, as
// bolinhas, as caixas de marcar e as listas, os mesmos textos, e OK / Cancelar com os glifos
// fixos embaixo. O formato da tela (4:3 / 16:9) nao existe no celular: fica o que estava gravado.
public sealed partial class TelaConfiguracoes
{
    CR95.Caderno? _cadernoR;

    void RetroCelular(int aba)
    {
        Background = W95.Face;
        FontFamily = CUi.Fonte;
        var nomes = Array.ConvertAll(Secoes, s => T(s));
        _cadernoR = new CR95.Caderno(nomes, AbaRetro, Math.Clamp(aba, 0, Secoes.Length - 1));
        var erro = CR95.Txt("", 13.5, W95.Vermelho, true);
        erro.IsVisible = false;
        _erro = m => { erro.Text = m; erro.IsVisible = m.Length > 0; };
        var rodape = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                erro,
                CUi.Botoes(CR95.Botao(T("OK"), "Botoes/BBOK.png", true, () => Salvar(_erro), false),
                    CR95.Botao(T("Cancelar"), "Botoes/BBCANCEL.png", false, () => Fechar(false), false)),
            },
        };
        var corpo = new Border { Background = W95.Face, Child = _cadernoR };
        Content = new CUi.Folha(T("Configurações"), corpo, rodape, inteira: true, rolar: false, fechar: () => Fechar(false));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    // a pagina de uma aba, feita com o estado de agora (a versao liga e desliga o resto)
    Control AbaRetro(int k)
    {
        var p = new StackPanel { Spacing = 10 };
        bool mod = _cfg.Modificada;
        TextBlock Texto(string s, double tam = 13.5, IBrush? cor = null, bool negrito = false) => CR95.Txt(s, tam, cor ?? W95.Preto, negrito);
        void Refazer() => _cadernoR?.Refazer();
        switch (k)
        {
            case 0:
            {
                var (versao, _, _) = CR95.Radios(new[] { T("Retrô"), T("Atual") }, mod ? 1 : 0,
                    i => { if ((i == 1) == _cfg.Modificada) return; _cfg.Modificada = i == 1; Refazer(); }, 16.5);
                p.Children.Add(CUi.GrupoRetro(T("Versão do jogo"), versao));
                p.Children.Add(Texto(mod ? DescModificada : DescOriginal));
                var equipas = new CR95.Combo(Array.ConvertAll(Configuracoes.OpcoesEquipas, n => n.ToString()),
                    mod ? Math.Max(0, Array.IndexOf(Configuracoes.OpcoesEquipas, _cfg.EquipasPorDivisao)) : 0, 16) { IsEnabled = mod };
                equipas.Mudou += i => { if (!_cfg.Modificada) return; _cfg.EquipasPorDivisao = Configuracoes.OpcoesEquipas[i]; Refazer(); };
                var rot = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
                rot.Children.Add(Texto(T("Equipas por divisão:"), 15.5));
                rot.Children.Add(equipas);
                if (!mod) rot.Children.Add(Texto(T("(só na versão Atual)"), 13, W95.Cinzento));
                p.Children.Add(rot);
                p.Children.Add(Texto(AjudaEquipas()));
                if (_emCurso != null)
                {
                    p.Children.Add(new Border { Height = 2, BorderBrush = W95.Sombra, BorderThickness = new Thickness(0, 1, 0, 0), BoxShadow = BoxShadows.Parse("inset 0 -1 0 0 #FFFFFF"), Margin = new Thickness(0, 4) });
                    p.Children.Add(Texto(T("Jogo em curso: ") + RegrasDoJogo(_emCurso), 13.5, W95.Marinho, true));
                }
                p.Children.Add(Texto(Nota(), 13, W95.Cinzento));
                break;
            }
            case 1:
            {
                if (!mod) p.Children.Add(Texto(T("Na versão Retrô as regras são as de 1998: só cartões vermelhos, sem patrocínios nem acréscimos."), 13.5, W95.Vermelho, true));
                // na Retro aparecem como no jogo original (desligadas) e travadas; o _cfg guarda a escolha
                Control Marca(string nome, bool valor, Action<bool> mudou) =>
                    CR95.Marca(nome, mod && valor, v => { if (_cfg.Modificada) mudou(v); }, mod);
                p.Children.Add(new StackPanel
                {
                    Children =
                    {
                        Marca(T("Cartões amarelos"), _cfg.CartoesAmarelos, v => _cfg.CartoesAmarelos = v),
                        Marca(T("Patrocínios"), _cfg.Patrocinios, v => _cfg.Patrocinios = v),
                        Marca(T("Acréscimos"), _cfg.Acrescimos, v => _cfg.Acrescimos = v),
                        Marca(T("5 substituições (FIFA)"), _cfg.CincoSubstituicoes, v => _cfg.CincoSubstituicoes = v),
                        Marca(T("9 reservas no banco"), _cfg.NoveReservas, v => _cfg.NoveReservas = v),
                    },
                });
                var sobem = new CR95.Combo(OpcoesSobem, mod ? _cfg.SobemDescem : 0, 16) { IsEnabled = mod };
                sobem.Mudou += i => { if (!_cfg.Modificada) return; _cfg.SobemDescem = i; Refazer(); };
                var rot = new StackPanel { Spacing = 6 };
                rot.Children.Add(Texto(T("Sobem e descem por divisão:"), 15.5));
                rot.Children.Add(sobem);
                p.Children.Add(rot);
                p.Children.Add(Texto(AjudaSobem()));
                var (estr, _, _) = CR95.Radios(new[]
                {
                    T("Realístico: até 5 por equipa (Bosman e lusófonos não contam)"), T("Liberado: sem limite de estrangeiros"),
                }, _cfg.EstrangeirosLiberados ? 1 : 0, i => _cfg.EstrangeirosLiberados = i == 1, 15);
                p.Children.Add(CUi.GrupoRetro(T("Jogadores estrangeiros"), estr));
                p.Children.Add(CUi.GrupoRetro(T("Desempate na classificação"), Texto(Desempate(), 15)));
                break;
            }
            case 2:
            {
                // (o formato 4:3 / 16:9 e do computador: no celular a tela e a do telefone)
                var (visual, _, _) = CR95.Radios(new[] { T("Retrô: o aspecto do Windows 95, sem logos"), T("Moderno: tema escuro, cartões e logos") },
                    _cfg.Moderno ? 1 : 0, i => _cfg.VisualTela = i == 1 ? Configuracoes.VisualModerno : Configuracoes.VisualClassico, 15);
                p.Children.Add(CUi.GrupoRetro(T("Visual"), visual));
                p.Children.Add(Texto(T("O visual e o formato mudam na hora, mesmo com um jogo aberto."), 13, W95.Cinzento));
                break;
            }
            case 3:
            {
                // tocar troca na hora: a tela se refaz no idioma escolhido, nesta aba (TrocarIdioma)
                var (idioma, _, _) = CR95.Radios(Nomes, IndiceIdioma, TrocarIdioma, 16);
                p.Children.Add(CUi.GrupoRetro(T("Idioma do jogo"), idioma));
                break;
            }
            default:
            {
                var dados = new StackPanel { Spacing = 10 };
                dados.Children.Add(new SelectableTextBlock
                {
                    Text = DadosJogo.Raiz, FontFamily = W95.Arial13.Familia, FontSize = 14.5 * Acessibilidade.Texto, FontWeight = FontWeight.Bold,
                    Foreground = W95.Marinho, TextWrapping = TextWrapping.Wrap,
                });
                dados.Children.Add(Texto(TextoPastaDados, 13));
                // (o gestor de arquivos so existe no computador)
                if (DadosJogo.PodeAbrirPasta)
                    dados.Children.Add(new CUi.Botao(T("Abrir pasta de dados"), false, () => DadosJogo.AbrirPasta()) { HorizontalAlignment = HorizontalAlignment.Stretch });
                p.Children.Add(CUi.GrupoRetro(T("Pasta de dados"), dados));
                break;
            }
        }
        return p;
    }
}
