using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RetroFoot.Motor.Base;

// Pastas com nomes de arquivo que nao sao UTF-8 valido (ex.: "ARA\xC7A_BR.EFT" em Latin-1,
// comum nas pastas EQUIPAS dos jogadores). No Linux e no macOS o .NET troca esses bytes por
// U+FFFD e nao consegue mais abrir o arquivo; aqui a pasta e lida e os arquivos abertos pelos
// bytes do nome (libc). No Windows os nomes sao UTF-16 e as APIs normais bastam.
public static class ArquivosBrutos
{
    public sealed record Entrada(string Nome, byte[]? Bruto);

    public static List<Entrada> Listar(string pasta)
    {
        if (OperatingSystem.IsWindows())
            return Directory.GetFiles(pasta).Select(f => new Entrada(Path.GetFileName(f), null)).ToList();
        var r = new List<Entrada>();
        IntPtr d = opendir(Encoding.UTF8.GetBytes(pasta + "\0"));
        if (d == IntPtr.Zero) throw new DirectoryNotFoundException(pasta);
        try
        {
            while (true)
            {
                IntPtr ent = readdir(d);
                if (ent == IntPtr.Zero) break;
                // struct dirent: no Linux d_name em +19; no macOS (64 bits) em +21
                int off = OperatingSystem.IsMacOS() ? 21 : 19;
                var nome = new List<byte>();
                for (int i = 0; ; i++)
                {
                    byte c = Marshal.ReadByte(ent, off + i);
                    if (c == 0) break;
                    nome.Add(c);
                }
                var b = nome.ToArray();
                if (b.Length == 0 || (b.Length == 1 && b[0] == '.') || (b.Length == 2 && b[0] == '.' && b[1] == '.')) continue;
                r.Add(new Entrada(Encoding.UTF8.GetString(b), b));
            }
        }
        finally { closedir(d); }
        return r;
    }

    public static byte[] Ler(string pasta, Entrada e)
    {
        if (e.Bruto == null || OperatingSystem.IsWindows()) return File.ReadAllBytes(Path.Combine(pasta, e.Nome));
        var caminho = Encoding.UTF8.GetBytes(pasta.TrimEnd('/') + "/").Concat(e.Bruto).Append((byte)0).ToArray();
        int fd = open(caminho, 0);
        if (fd < 0) throw new FileNotFoundException(Path.Combine(pasta, e.Nome));
        using var h = new SafeFileHandle((IntPtr)fd, true);
        using var s = new FileStream(h, FileAccess.Read);
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    [DllImport("libc", SetLastError = true)] static extern IntPtr opendir(byte[] nome);
    [DllImport("libc", SetLastError = true)] static extern IntPtr readdir(IntPtr d);
    [DllImport("libc")] static extern int closedir(IntPtr d);
    [DllImport("libc", SetLastError = true)] static extern int open(byte[] caminho, int flags);
}
