using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using TM = RetroFoot.App.TextosMercado;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// Os jogadores sob observacao no celular: a tela inteira com a lista (o preco e a equipa de
// cada um), o Limpar e o Fechar embaixo; o toque num jogador abre a ficha com o Remover e o
// Equipa... Com mais de 15, o aviso vermelho e o Fechar (e o Voltar) desligados ate um sair,
// como o FormCloseQuery do original. A logica e a do Form (TelaObservados.cs).
public static partial class TelaObservados
{
    sealed partial class Form
    {
        protected override Control? VersaoCelular() => new ObservadosCelular(this);

        sealed class ObservadosCelular : UserControl
        {
            readonly Form _f;
            readonly ContentControl _corpo = new();
            readonly CUi.Folha _folha;
            Action? _fecharFicha;

            public ObservadosCelular(Form f)
            {
                _f = f;
                _folha = new CUi.Folha(T("Jogadores sob observação"), _corpo, new Border(), null, inteira: true, fechar: Fechar);
                Content = _folha;
                Refazer();
            }

            void Fechar()
            {
                if (_f.Cheia()) return;
                _f.Fechar();
            }

            void Refazer()
            {
                var s = _f.S;
                var ids = _f.Ids();
                var p = new StackPanel { Spacing = 10 };
                if (_f.Cheia())
                    p.Children.Add(CUi.Cartao_(null, CUi.Txt(T("Só pode ter {0} jogadores sob observação", MercadoJogo.LimiteObservados), 15, CUi.Erro, FontWeight.Bold, true)));
                var t = CUi.TituloSecao_(T("{0} jogadores", ids.Count) + "  ·  " + T("Dinheiro em caixa") + " " + FormatoDinheiro.Valor(s.S, (int)_f._e.Dinheiro));
                t.Margin = new Thickness(4, 0, 0, 0);
                p.Children.Add(t);
                if (ids.Count == 0)
                    p.Children.Add(CUi.Cartao_(null, CUi.Txt(T("Nenhum jogador sob observação."), 15, CUi.Fraco, FontWeight.Normal, true)));
                else
                {
                    var linhas = new List<Control>();
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var (eq, k) = MercadoJogo.EquipaDoJogador(s.S, ids[i]);
                        if (eq == null) continue;
                        var j = eq.Jogadores[k];
                        int v = s.Mercado.PrecoDeVenda(eq, _f._e, j, MercadoJogo.ForcaMedia(eq));
                        int n = i;
                        linhas.Add(CUi.Toque(MercadoCelular.Jogador(j, MercadoCelular.Selos(j), MercadoCelular.Extra(v > 0 ? _f.Dinheiro(v) : "", Texto(eq.NomeCurto))), () => Tocar(n), 48));
                    }
                    p.Children.Add(CUi.Grupo(linhas));
                }
                _corpo.Content = p;
                var limpar = new CUi.Botao(T("Limpar"), false, () =>
                {
                    MercadoJogo.LimparObservados(s.S, _f._tr);
                    _f._lista.Sel = -1;
                    Refazer();
                }) { Habilitado = ids.Count > 0 };
                var fechar = new CUi.Botao(T("Fechar"), true, Fechar) { Habilitado = !_f.Cheia() };
                _folha.Rodape(CUi.Botoes(limpar, fechar));
            }

            void Tocar(int i)
            {
                _f._lista.Sel = i;
                var id = _f.Escolhido();
                if (id == 0) return;
                var s = _f.S;
                var (eq, k) = MercadoJogo.EquipaDoJogador(s.S, id);
                if (eq == null) return;
                var j = eq.Jogadores[k];
                int v = s.Mercado.PrecoDeVenda(eq, _f._e, j, MercadoJogo.ForcaMedia(eq));
                var corpo = new StackPanel { Spacing = 12 };
                corpo.Children.Add(CUi.Cartao_(null, MercadoCelular.Cabecalho(j, eq, Texto(eq.NomeCurto))));
                corpo.Children.Add(MercadoCelular.Ficha(j, s.J.M.Regras.CartoesAmarelos, new[] { (T("Preço"), v > 0 ? _f.Dinheiro(v) : "–") }));
                var remover = new CUi.Botao(T("Remover"), false, () =>
                {
                    FecharFicha();
                    _f._lista.Sel = i;
                    _f.Remover();
                    Refazer();
                });
                var equipa = new CUi.Botao(T("Time..."), true, () =>
                {
                    FecharFicha();
                    _f._lista.Sel = i;
                    _f.Equipa();
                }) { Habilitado = !_f.Cheia() };
                var folha = new CUi.Folha(TM.NomeJogador(j), corpo, CUi.Botoes(remover, equipa), Texto(eq.NomeCompleto), fechar: FecharFicha);
                FecharFicha();
                _fecharFicha = MercadoCelular.Abrir(s, folha);
            }

            void FecharFicha()
            {
                var f = _fecharFicha;
                _fecharFicha = null;
                f?.Invoke();
            }
        }
    }
}
