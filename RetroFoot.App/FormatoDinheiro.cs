using System;
using System.Globalization;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;

namespace RetroFoot.App;

// O dinheiro na tela, sempre com o simbolo da moeda do jogo e os milhares separados:
// "R$ 1.095.134", "$ 200.000", "€ 1.234.567", "£ 100.843" (o original escrevia por extenso,
// "28 milhões e 768 mil reais"). Separador "." em pt-PT, pt-BR e es; "," em en.
// Os Escudos (contados em contos) sairam do jogo: o jogo novo nao os oferece mais e um save
// antigo nessa moeda continua a abrir, com o dinheiro mostrado em euros a taxa oficial
// (1 EUR = 200,482 PTE; 1 conto = 1000 PTE, entao 1 conto = 4,988 EUR). O que se digita
// (lances, preco de venda) continua na unidade do save, "milhares de contos".
// Numeros que nao sao dinheiro (lugares, publico): Numero(v), sem simbolo.
public static class FormatoDinheiro
{
    // o separador de milhares do idioma
    public static char Separador => Idioma.Atual == Idioma.EN ? ',' : '.';

    public static string Numero(long v)
    {
        var t = Math.Abs(v).ToString("#,0", CultureInfo.InvariantCulture);
        if (Separador != ',') t = t.Replace(',', Separador);
        return v < 0 ? "-" + t : t;
    }

    // o simbolo pelo nome da moeda gravado no save (Tabelas.Moedas do motor); null = sem
    public static string? Simbolo(Moeda m) => Simbolo(m.Nome);

    public static string? Simbolo(string nome)
    {
        var n = nome.Trim().ToLowerInvariant();
        if (n.StartsWith("reais") || n.StartsWith("real")) return "R$";
        if (n.StartsWith("dólar") || n.StartsWith("dolar")) return "$";
        if (n.StartsWith("euro")) return "€";
        if (n.StartsWith("libra")) return "£";
        if (n.StartsWith("escudo")) return "€";   // convertido (EscudosLegados)
        return null;
    }

    // um save antigo em Escudos (contos)
    public static bool EscudosLegados(Moeda m) => m.Nome.Trim().StartsWith("escudo", StringComparison.OrdinalIgnoreCase);

    const decimal ContoEmEuros = 1000m / 200.482m;

    public static string Valor(Moeda m, long v)
    {
        if (EscudosLegados(m)) v = (long)Math.Round(v * ContoEmEuros, MidpointRounding.AwayFromZero);
        var num = Numero(Math.Abs(v));
        var sinal = v < 0 ? "-" : "";
        // moeda desconhecida (save editado a mao): o nome dela na frente
        return sinal + (Simbolo(m) ?? m.Nome.Trim()) + " " + num;
    }

    public static string Valor(Jogo98 s, long v) => Valor(Moeda.DoSave(s), v);
}
