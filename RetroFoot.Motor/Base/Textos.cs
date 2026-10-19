using System;

namespace RetroFoot.Motor.Base;

// A traducao dos textos que o motor mostra ao jogador (mensagens, nao dados): o motor nao
// conhece a App, que poe aqui a sua (RetroFoot.App.Idioma); sem ela, o texto como esta.
// NUNCA usar no que vai para o save (nomes, noticias gravadas): o save fica igual byte a byte.
public static class Textos
{
    public static Func<string, string> Traduzir { get; set; } = s => s;

    public static string T(string chave) => Traduzir(chave);

    public static string T(string formato, params object?[] args) => string.Format(Traduzir(formato), args);
}
