using System;

namespace RetroFoot.App;

// O celular (Android; no computador, RETROFOOT_CELULAR=1 para ver como fica, com
// RETROFOOT_RETRATO=1 numa janela do tamanho de um celular em pe). No celular o jogo usa as
// telas proprias dele (em pe, ao toque, no verde e amarelo do Moderno) onde ja existem; as que
// ainda nao tem versao propria abrem as do Moderno, ajustadas a largura.
public static class Celular
{
    public static bool Ativo { get; } = OperatingSystem.IsAndroid() || Environment.GetEnvironmentVariable("RETROFOOT_CELULAR") == "1";
}
