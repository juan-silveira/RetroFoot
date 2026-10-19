using System;

namespace RetroFoot.App;

// O visual (Configuracoes.VisualTela): Original (padrao, o aspecto do jogo original) ou
// Moderno. Tudo o que na interface muda o aspecto do original fica atras desta escolha:
//  - os logos das equipas e das ligas (Escudos: no Original nao ha logos em tela nenhuma);
//  - a jornada (TelaJornada: cartoes por divisao, placar no centro, no estilo dos
//    aplicativos de resultados);
//  - os detalhes do jogo (clique na jornada: a linha do tempo em vez da TsubstitutionDlg);
//  - a Classificacao (a tabela escura por divisao em vez do TclassificationDlg).
// As adicoes de jogo (cartoes amarelos, patrocinios, equipas por divisao) nao sao do visual:
// tem as suas proprias configuracoes. Lido no comeco; Salvar nas Configuracoes muda na hora
// (as telas abertas redesenham; as que montam controles, da proxima vez que abrirem).
public static class Visual
{
    // RETROFOOT_VISUAL=moderno|classico (ou original) passa por cima das Configuracoes (testes, demos)
    public static bool Moderno { get; private set; } = Environment.GetEnvironmentVariable("RETROFOOT_VISUAL") switch
    {
        "moderno" => true, "classico" or "original" => false, _ => Configuracoes.Ler().Moderno,
    };
    public static event Action? Mudou;

    // as telas abertas redesenham sem trocar o visual (o tamanho do texto mudou: Acessibilidade)
    public static void Redesenhar() => Mudou?.Invoke();

    public static void Aplicar(bool moderno)
    {
        if (moderno == Moderno) return;
        Moderno = moderno;
        Mudou?.Invoke();
    }
}
