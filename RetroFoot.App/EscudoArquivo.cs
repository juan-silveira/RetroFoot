using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using RetroFoot.Motor.Arquivos;
using SkiaSharp;
using static RetroFoot.App.Idioma;
using Rgba = RetroFoot.Motor.Arquivos.EscudoProcessador.Rgba;

namespace RetroFoot.App;

// Le e grava as imagens dos escudos do Editor de Equipas (o processamento, sem arquivos, fica
// em Motor/Arquivos/EscudoProcessador.cs): PNG, JPG, WEBP, BMP, GIF pela SkiaSharp e SVG pelo
// Svg.Skia (desenhado com 512 pontos no lado maior). Grava PNG.
public static class EscudoArquivo
{
    public static readonly string[] Extensoes = { "png", "jpg", "jpeg", "webp", "svg", "bmp", "gif" };
    const int LadoSvg = 512, LadoMaximo = 4096;

    // a imagem em pixels RGBA, ou o motivo de nao abrir
    public static (Rgba? Imagem, string? Erro) Abrir(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo)) return (null, T("O arquivo não existe."));
            if (Path.GetExtension(arquivo).Equals(".svg", StringComparison.OrdinalIgnoreCase)) return Svg(arquivo);
            using var s = File.OpenRead(arquivo);
            using var codec = SKCodec.Create(s);
            if (codec == null) return (null, T("Não é uma imagem que o RetroFoot consiga abrir. Use PNG, JPG, WEBP, SVG ou BMP."));
            var info = codec.Info;
            if (info.Width > LadoMaximo * 2 || info.Height > LadoMaximo * 2)
                return (null, T("A imagem é grande demais ({0}x{1} px).", info.Width, info.Height));
            using var bmp = SKBitmap.Decode(codec, new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (bmp == null) return (null, T("Não é uma imagem que o RetroFoot consiga abrir. Use PNG, JPG, WEBP, SVG ou BMP."));
            return (Pixels(bmp), null);
        }
        catch (Exception e) { return (null, T("Não consegui abrir a imagem: {0}", e.Message)); }
    }

    static (Rgba?, string?) Svg(string arquivo)
    {
        using var svg = new Svg.Skia.SKSvg();
        var pic = svg.Load(arquivo);
        if (pic == null) return (null, T("O SVG não tem nada para desenhar."));
        var r = pic.CullRect;
        if (r.Width <= 0 || r.Height <= 0) return (null, T("O SVG não tem nada para desenhar."));
        float esc = LadoSvg / Math.Max(r.Width, r.Height);
        int w = Math.Max(1, (int)Math.Round(r.Width * esc)), h = Math.Max(1, (int)Math.Round(r.Height * esc));
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Transparent);
            c.Scale(esc);
            c.Translate(-r.Left, -r.Top);
            c.DrawPicture(pic);
        }
        return (Pixels(bmp), null);
    }

    static Rgba Pixels(SKBitmap bmp)
    {
        var r = new Rgba(bmp.Width, bmp.Height);
        // a copia na ordem e no alfa certos (RGBA, sem pre-multiplicar)
        using var c = bmp.ColorType == SKColorType.Rgba8888 && bmp.AlphaType == SKAlphaType.Unpremul ? null
            : new SKBitmap(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var fonte = bmp;
        if (c != null) { bmp.CopyTo(c, SKColorType.Rgba8888); fonte = c; }
        var p = fonte.GetPixels();
        int linha = bmp.Width * 4;
        for (int y = 0; y < bmp.Height; y++) Marshal.Copy(p + y * fonte.RowBytes, r.Px, y * linha, linha);
        return r;
    }

    public static byte[] Png(Rgba r)
    {
        using var bmp = new SKBitmap(new SKImageInfo(r.Largura, r.Altura, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var p = bmp.GetPixels();
        int linha = r.Largura * 4;
        for (int y = 0; y < r.Altura; y++) Marshal.Copy(r.Px, y * linha, p + y * bmp.RowBytes, linha);
        using var img = SKImage.FromBitmap(bmp);
        using var dados = img.Encode(SKEncodedImageFormat.Png, 100);
        return dados.ToArray();
    }

    public static void GravarPng(Rgba r, string arquivo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        File.WriteAllBytes(arquivo, Png(r));
    }

    public static Bitmap Bitmap(Rgba r) => new(new MemoryStream(Png(r)));

    // um PNG do jogo ou do jogador em pixels (para as cores sugeridas)
    public static Rgba? DeBitmap(Bitmap b)
    {
        try
        {
            using var m = new MemoryStream();
            b.Save(m);
            m.Position = 0;
            using var s = SKBitmap.Decode(m);
            return s == null ? null : Pixels(s);
        }
        catch { return null; }
    }
}
