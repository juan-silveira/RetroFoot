# Lacunas: jogo clássico x RetroFoot.App (desktop Linux)

Levantamento feito pelos formulários (DFM, blocos `TPF0`) e textos do executável do jogo clássico, pelos
docs internos do launcher e pelo código do app. Status:
**ok** = existe, alcançável e ligado ao motor; **feito** = faltava e foi feito agora;
**falta** = ainda não existe; **fora** = fora do escopo do jogo no desktop.

## Formulários

| Item (original) | Status | Onde / notas |
|---|---|---|
| TmainWindow (fundo verde) | ok | `Sessao.FundoVerde`, `TelaInicio` |
| TselectGameDlg (jogo novo / continuar) | ok | `TelaInicio.EscolherJogo` (+ botão Configurar) |
| TselectCountriesDlg | ok | `TelaInicio.SelecionarPaises` |
| TcurrencyDlg | ok | `TelaInicio.EscolherMoeda` |
| TgetTeamsDlg (até 6 humanos, sortear equipas) | ok | `TelaInicio.EscolherTreinadores` |
| TprepareGameDlg (tela da equipa, 7 páginas) | ok | `TelaEquipa` |
| TjourneyDlg (jornada, setas de página) | ok | `TelaJornada`; clique num jogo abre os detalhes |
| TclassificationDlg | ok (visual moderno) | `TelaClassificacao`; relógio de 10 tiques |
| Clique numa equipa da Classificação → plantel (seg06:07f4) | **feito** | `TelaClassificacao.AbrirEquipa`; relógio parado enquanto o plantel está aberto |
| TEndOfSeasonDlg (Fim de Temporada) | ok | `TelaFimDeTemporada` (`Campeonato.AoFim`) |
| TsorteioDlg (sorteio da Taça) | ok | `TelaSorteio` (opção Ver sorteio) |
| TsubstitutionDlg | ok | `TelaSubstituicao` (`LigacaoPartida`) |
| TforcedSubstitutionDlg (Lesão) | ok | `TelaSubstituicaoForcada` |
| TpenaltyDlg / TPenaltiesDlg | ok | `TelaPenalti`, `TelaPenaltis` |
| TsellPlayerDlg (leilão) | ok | `TelaLeilao` (também nos leilões da CPU) |
| TaskMoreSalaryDlg | ok | `TelaPedidoSalario`, `TelaNovoSalario`, `TelaPatrocinio` |
| TinviteCoachDlg (convite) + Plantel... | ok | `TelaTreinadores.Convite` (`Humano.Convite`) |
| TcoachManagerDlg (adicionar/remover/férias) | ok | `TelaTreinadores.Gestao` (add-in 0x0E) |
| TcoachOptionsDlg (Perfil) | ok | `TelaTreinadores.Perfil` |
| TOptionsDlg (Geral/Jogo/Controle, Árbitros, empréstimos, endividadas) | ok | `TelaOpcoes` |
| TRefereeAidDlg (Ajuda do Árbitro) | ok | clique no árbitro da `TelaEquipa` |
| TObservedPlayersDlg | ok | `TelaObservados` (add-in 0x0C) |
| TcheckPlayerDlg (Lista de Jogadores / Procurar) | ok | `TelaProcura` |
| TPlantelDlg (Observado, Balanço, Empréstimo, Lista, Calendário, Comprar, Procurar) | ok | `TelaPlantel` |
| TstadiumDlg | ok | `TelaEstadio` |
| TloanDlg | ok | `TelaEmprestimo` (add-in 0x13) |
| TbalanceDlg | ok | `TelaBalanco` (add-in 0x11) |
| TaboutDlg + TCodeDlg (Registar) + TAddInsDlg | ok | `TelaAcerca`; registo também nas Configurações |
| TPrintListDlg (listas) | ok | `TelaListas.Lista` |
| TInputDlg (mensagens) | ok | `Dlg.PedirTexto`, `TelaInicio.Mensagem` |
| TclockDlg, TwaitDlg, TprogressDlg, TForm1 | fora | internos (relógio/espera), sem tela própria no jogo |
| TEditor_Main, TSaveTeamDlg, TOpenTeamDlg, TEditPlayerDlg, TtransferPlayerDlg, TCoachNameDlg, TTeamSkillDlg, TSetTeamNameDlg, TTeamColorDlg, TJanelaCor, TCantSaveTeamDlg, TSetCountryDlg, TassignNacDlg | fora | Editor de Equipas (EDITEQ); sem entrada nos menus do jogo |

## Menus da tela da equipa

| Item | Status | Notas |
|---|---|---|
| RetroFoot > Opções / Sair sem gravar / Sair / Acerca | ok | + Configurações e Gravar (acréscimos) |
| Seleccionar > 12 formações, 5-0-5, Automático, Melhores | ok | teclas F1–F12, T, A, M |
| Equipa > Imprimir | fora | imprimir o plantel nao faz sentido hoje; retirado a pedido do usuario |
| Equipa > Empréstimo / Estádio / Historial | ok | |
| Jogador > Vender / Observados / Procurar / Últimas transferências / Aquisições | ok | |
| Campeonato > Classificação / Melhores marcadores / Calendário / Últimos vencedores / Marcadores de sempre | ok | |
| Treinador > História / Gestão / Ranking / Perfil | ok | |
| Botões Renovar contrato, Calendário e Plantel do adversário, Vender, Propor | ok | |

## Fluxo de jogo e mensagens

| Item | Status | Notas |
|---|---|---|
| Janela "Chicotadas Psicológicas" / "Demissões de Técnicos" (seg05:025a, TLash.Formatar 3:b2c8) | **feito** | gancho `Ganchos.Chicotadas` no motor (antes de esvaziar a lista: depois da jornada, da Taça e no fim da temporada); `TelaListas.Chicotadas`; opção "Mostrar demissões" (Nunca/Dos humanos/Todas) agora tem efeito |
| Gravação automática (Opções > Salvar o jogo, DS:2B3C, 4:21dd) | **feito** | `Sessao.GravarAutomatico` ao abrir a tela da equipa: todas, de 3 em 3 ou só na 1ª jornada; não na jornada retomada (DS:2720) |
| Gravar / carregar jogo | ok | RetroFoot > Gravar, Sair grava; continuar na TselectGameDlg |
| Técnico demitido / convites de outros clubes | ok | motor + `TelaTreinadores.Convite`; agora a demissão aparece na janela das chicotadas |
| Vários humanos | ok | até 6 no jogo novo; Gestão de treinadores no meio do jogo |
| Fim de temporada, subida/descida, prémios | ok | `TelaFimDeTemporada` |
| Taça (sorteio, jornada, prolongamento, penáltis) | ok | |
| Patrocínios (patch do launcher) | ok | `TelaPatrocinio` |
