using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RetroFoot.App;

// Dicas (tooltips) com o texto inteiro onde ele foi cortado. Nada a fazer nas telas: com o
// rato parado, o controle debaixo dele e desenhado de novo num DrawingGroup (sem ir a tela)
// e procuram-se os textos cortados nesse desenho:
//  - com reticencias (Trimming): o "…" e o pedaco antes dele, que aponta para a string inteira;
//  - cortados por um PushClip na horizontal (as colunas das listas classicas);
//  - marcados a mao com Dicas.Marcar (quem encurta o texto sem reticencias).
// Nos TextBlock com TextTrimming, a dica aparece quando alguma linha ficou cortada.
// So com o rato (no toque nao ha "passar por cima"). Liga-se uma vez (App), vale para todas
// as janelas e para os dois visuais.
public static class Dicas
{
    public const int Atraso = 450;   // ms parado ate mostrar

    // a dica aberta: em que controle, onde (coordenadas dele) e o texto
    static Control? _alvo;
    static Rect _onde;
    static string? _texto;
    static TopLevel? _janela;
    static Point _rato;
    static DispatcherTimer? _relogio;
    static bool _ligado;

    public static void Ligar()
    {
        if (_ligado) return;
        _ligado = true;
        InputElement.PointerMovedEvent.AddClassHandler<TopLevel>((t, e) => Moveu(t, e), RoutingStrategies.Tunnel, true);
        InputElement.PointerExitedEvent.AddClassHandler<TopLevel>((_, _) => Fechar(true), RoutingStrategies.Direct, true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((_, _) => Fechar(true), RoutingStrategies.Tunnel, true);
        InputElement.PointerWheelChangedEvent.AddClassHandler<TopLevel>((_, _) => Fechar(true), RoutingStrategies.Tunnel, true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((_, _) => Fechar(true), RoutingStrategies.Tunnel, true);
    }

    static void Moveu(TopLevel t, PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse) return;
        _janela = t;
        _rato = e.GetPosition(t);
        if (_alvo != null)
        {
            // ainda dentro do texto cortado: a dica fica
            var p = t.TranslatePoint(_rato, _alvo);
            if (p is { } q && _onde.Contains(q) && _alvo.IsEffectivelyVisible) return;
            Fechar(false);
        }
        _relogio ??= new DispatcherTimer(TimeSpan.FromMilliseconds(Atraso), DispatcherPriority.Background, (_, _) => Parou());
        _relogio.Stop();
        _relogio.Start();
    }

    static void Parou()
    {
        _relogio?.Stop();
        var t = _janela;
        if (t == null || _alvo != null) return;
        try
        {
            var achou = Procurar(t, _rato);
            if (achou is not var (c, r, texto)) return;
            // o controle ja tem a sua propria dica: nao mexe
            if (ToolTip.GetTip(c) != null) return;
            _alvo = c; _onde = r; _texto = texto;
            ToolTip.SetPlacement(c, PlacementMode.Pointer);
            ToolTip.SetShowDelay(c, 0);
            ToolTip.SetTip(c, texto);
            ToolTip.SetIsOpen(c, true);
        }
        catch (Exception)
        {
            // uma dica nunca pode derrubar o jogo
            Fechar(true);
        }
    }

    static void Fechar(bool pararRelogio)
    {
        if (pararRelogio) _relogio?.Stop();
        var c = _alvo;
        var texto = _texto;
        _alvo = null; _texto = null;
        if (c == null) return;
        ToolTip.SetIsOpen(c, false);
        // so tira a dica se ainda for a nossa
        if (ToolTip.GetTip(c) is string s && s == texto)
        {
            c.ClearValue(ToolTip.TipProperty);
            c.ClearValue(ToolTip.PlacementProperty);
            c.ClearValue(ToolTip.ShowDelayProperty);
        }
    }

    // ------------------------------------------------------------ onde ha texto cortado

    // o controle debaixo de p (coordenadas da janela) com um texto cortado em p
    static (Control, Rect, string)? Procurar(TopLevel t, Point p)
    {
        var topo = t.GetVisualsAt(p).FirstOrDefault();
        if (topo == null) return null;
        // os filhos do de cima que contem p (o de cima primeiro), depois ele e os pais
        var candidatos = new List<Avalonia.Visual>();
        Descer(topo, t, p, candidatos);
        for (Avalonia.Visual? v = topo; v != null && v != t; v = v.GetVisualParent()) candidatos.Add(v);
        foreach (var v in candidatos)
        {
            if (v is not Control c || !c.IsEffectivelyVisible) continue;
            if (t.TranslatePoint(p, c) is not { } q) continue;
            if (c is TextBlock tb)
            {
                if (TextBlockCortado(tb) is { } s) return (c, new Rect(c.Bounds.Size), s);
                return null;
            }
            if (!Desenhado(c)) continue;
            if (Coletor.Gravar(c) is { } col)
                foreach (var (r, s, ordem) in Cortados(col))
                    if (r.Inflate(1).Contains(q) && !col.Tapado(q, ordem)) return (c, r, s);
            // o primeiro desenhado por nos tapa os de baixo
            return null;
        }
        return null;
    }

    static void Descer(Avalonia.Visual v, TopLevel t, Point p, List<Avalonia.Visual> l)
    {
        var filhos = v.GetVisualChildren().ToList();
        for (int i = filhos.Count - 1; i >= 0; i--)
        {
            var f = filhos[i];
            if (!f.IsVisible) continue;
            if (t.TranslatePoint(p, f) is not { } q || !new Rect(f.Bounds.Size).Contains(q)) continue;
            Descer(f, t, p, l);
            l.Add(f);
        }
    }

    static string? TextBlockCortado(TextBlock tb)
    {
        if (tb.TextTrimming == TextTrimming.None) return null;
        var texto = tb.Text ?? tb.Inlines?.Text;
        if (string.IsNullOrEmpty(texto)) return null;
        return tb.TextLayout.TextLines.Any(l => l.HasCollapsed) ? texto : null;
    }

    // controles com Render nosso (os desenhados a mao)
    static readonly Dictionary<Type, bool> _desenhado = new();

    static bool Desenhado(Control c)
    {
        var tipo = c.GetType();
        if (!_desenhado.TryGetValue(tipo, out var d))
        {
            var m = tipo.GetMethod("Render", new[] { typeof(DrawingContext) });
            _desenhado[tipo] = d = m?.DeclaringType?.Assembly == typeof(Dicas).Assembly;
        }
        return d;
    }

    // ------------------------------------------------------------ marcas a mao

    internal static IImmutableBrush MarcaPincel => Marca;
    static readonly IImmutableBrush Marca = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromArgb(0, 1, 2, 3));

    // Quem encurta um texto a mao (sem reticencias) diz aqui o texto inteiro e onde o
    // desenhou; fora da procura de dicas nao faz nada.
    public static void Marcar(DrawingContext dc, Rect onde, string inteiro)
    {
        if (Coletor.Atual is not { } c) return;
        c.Marcas.Add((inteiro, c.Ordem));
        dc.DrawRectangle(Marca, null, onde);
    }

    // os textos cortados de um desenho gravado (rect nas coordenadas do controle, texto
    // inteiro, e a ordem em que foi desenhado: o que vem depois tapa)
    static List<(Rect, string, int)> Cortados(Coletor c)
    {
        var res = new List<(Rect, string, int)>();
        var pedacos = c.Pedacos;
        for (int i = 0; i < c.Marcados.Count && i < c.Marcas.Count; i++) res.Add((c.Marcados[i], c.Marcas[i].Item1, c.Marcas[i].Item2));
        for (int i = 0; i < pedacos.Count; i++)
        {
            var p = pedacos[i];
            var s = p.Txt.Span.Trim();
            // reticencias: o texto inteiro e a string de onde vem o pedaco antes
            if (s.Length == 1 && s[0] == '\u2026' && i > 0 && MemoryMarshal.TryGetString(pedacos[i - 1].Txt, out var inteiro, out _, out _))
            {
                var r = p.R;
                for (int j = i - 1; j >= 0 && MemoryMarshal.TryGetString(pedacos[j].Txt, out var o, out _, out _) && ReferenceEquals(o, inteiro); j--)
                    r = r.Union(pedacos[j].R);
                if (inteiro.Trim().Length > 0) res.Add((r, inteiro.Trim(), p.Ordem));
                continue;
            }
            // cortado por um clip na horizontal
            if (p.Corte is { } c2 && p.R.Width > 2 && s.Length > 0
                && (p.R.Right > c2.Right + 1.5 && p.R.Left < c2.Right - 1 || p.R.Left < c2.Left - 1.5 && p.R.Right > c2.Left + 1)
                && Math.Min(p.R.Bottom, c2.Bottom) - Math.Max(p.R.Top, c2.Top) > p.R.Height / 2)
            {
                var texto = MemoryMarshal.TryGetString(p.Txt, out var o, out _, out _) ? o : p.Txt.ToString();
                if (texto.Trim().Length > 0) res.Add((p.R.Intersect(c2), texto.Trim(), p.Ordem));
            }
        }
        return res;
    }
}

record struct Pedaco(Rect R, ReadOnlyMemory<char> Txt, Rect? Corte, int Ordem);

// Grava o que um Render desenha, sem desenhar: os pedacos de texto (com a transformacao e o
// recorte de cada um) e as marcas. O DrawingContext do Avalonia nao deixa herdar (construtor
// interno) e o do DrawingGroup nao aceita bitmaps; entao uma subclasse e gerada em tempo de
// execucao (IgnoresAccessChecksTo, como o DispatchProxy) e cada metodo abstrato chama aqui o
// estatico do mesmo nome e argumentos, se houver (senao nao faz nada).
public sealed class Coletor
{
    [ThreadStatic] internal static Coletor? Atual;
    internal readonly List<Pedaco> Pedacos = new();
    internal readonly List<(string, int)> Marcas = new();
    internal readonly List<Rect> Marcados = new();
    // o que foi pintado por cima (fundos opacos, imagens), com a ordem
    readonly List<(Rect, int)> _tampas = new();
    internal int Ordem;

    internal bool Tapado(Point q, int ordem)
    {
        foreach (var (r, o) in _tampas)
            if (o > ordem && r.Contains(q)) return true;
        return false;
    }

    void Tampa(IBrush? b, Rect r)
    {
        Ordem++;
        if (b == null || b.Opacity < 0.9 || b is ISolidColorBrush { Color.A: < 200 }) return;
        _tampas.Add((r.TransformToAABB(_m), Ordem));
    }
    readonly Stack<(Matrix, Rect?)> _pilha = new();
    Matrix _m = Matrix.Identity;
    Rect? _corte;

    static Type? _tipo;
    static bool _falhou;

    internal static Coletor? Gravar(Avalonia.Visual v)
    {
        if (_falhou) return null;
        DrawingContext dc;
        try { dc = (DrawingContext)Activator.CreateInstance(_tipo ??= Gerar())!; }
        catch (Exception) { _falhou = true; return null; }
        var c = new Coletor();
        var antes = Atual;
        Atual = c;
        try { v.Render(dc); }
        catch (Exception) { }   // desenhar fora da tela nao pode derrubar o jogo; fica o que deu
        finally { Atual = antes; }
        return c;
    }

    void Empilhar(Matrix? m, Rect? clip)
    {
        _pilha.Push((_m, _corte));
        if (m is { } mm) _m = mm * _m;
        if (clip is { } r)
        {
            var a = r.TransformToAABB(_m);
            _corte = _corte is { } k ? k.Intersect(a) : a;
        }
    }

    // chamados pela subclasse gerada (pelo nome e argumentos dos metodos do DrawingContext)
    public static void PushTransformCore(Matrix m) => Atual?.Empilhar(m, null);
    public static void PushClipCore(Rect r) => Atual?.Empilhar(null, r);
    public static void PushClipCore(RoundedRect r) => Atual?.Empilhar(null, r.Rect);
    public static void PushGeometryClipCore(Geometry g) => Atual?.Empilhar(null, g?.Bounds);
    public static void PopTransformCore() => Atual?.Desempilhar();
    public static void PopClipCore() => Atual?.Desempilhar();
    public static void PopGeometryClipCore() => Atual?.Desempilhar();

    void Desempilhar() { if (_pilha.Count > 0) (_m, _corte) = _pilha.Pop(); }

    public static void DrawGlyphRun(IBrush b, GlyphRun g)
    {
        if (Atual is not { } c || g == null) return;
        var r = g.Bounds;   // relativo a origem da linha (BaselineOrigin ja conta)
        c.Pedacos.Add(new Pedaco(r.TransformToAABB(c._m), g.Characters, c._corte, ++c.Ordem));
    }

    public static void DrawRectangleCore(IBrush b, IPen p, RoundedRect r, BoxShadows s)
    {
        if (Atual is not { } c) return;
        if (ReferenceEquals(b, Dicas.MarcaPincel)) c.Marcados.Add(r.Rect.TransformToAABB(c._m));
        else c.Tampa(b, r.Rect);
    }

    // (as formas nao tapam: so contam na ordem)
    public static void DrawGeometryCore(IBrush b, IPen p, object g) { if (Atual is { } c) c.Ordem++; }

    public static void DrawBitmap(object s, double opacidade, Rect origem, Rect destino)
    {
        if (Atual is { } c && opacidade >= 0.9) c.Tampa(Brushes.Black, destino);
    }

    static Type Gerar()
    {
        var baseT = typeof(DrawingContext);
        var nome = new System.Reflection.AssemblyName("RetroFoot.Dicas.Gerado");
        var ab = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(nome, System.Reflection.Emit.AssemblyBuilderAccess.Run);
        ab.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(
            typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor(new[] { typeof(string) })!,
            new object[] { baseT.Assembly.GetName().Name! }));
        var mb = ab.DefineDynamicModule(nome.Name!);
        var tb = mb.DefineType("ContextoDicas", System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Sealed, baseT);
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var ctorBase = baseT.GetConstructor(F, Type.EmptyTypes)!;
        var ctor = tb.DefineConstructor(System.Reflection.MethodAttributes.Public, System.Reflection.CallingConventions.Standard, Type.EmptyTypes);
        var il = ctor.GetILGenerator();
        il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
        il.Emit(System.Reflection.Emit.OpCodes.Call, ctorBase);
        il.Emit(System.Reflection.Emit.OpCodes.Ret);
        for (var t = baseT; t != null && t != typeof(object); t = t.BaseType)
            foreach (var m in t.GetMethods(F | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (!m.IsAbstract) continue;
                var ps = m.GetParameters().Select(p => p.ParameterType).ToArray();
                var attrs = (m.Attributes & System.Reflection.MethodAttributes.MemberAccessMask)
                    | System.Reflection.MethodAttributes.Virtual | System.Reflection.MethodAttributes.HideBySig;
                var nm = tb.DefineMethod(m.Name, attrs, m.ReturnType, ps);
                var g = nm.GetILGenerator();
                // o estatico com o mesmo nome e argumentos (object serve para os tipos internos)
                var alvo = typeof(Coletor).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .FirstOrDefault(a => a.Name == m.Name && a.GetParameters().Select(p => p.ParameterType)
                        .SequenceEqual(ps, new Aceita()));
                if (alvo != null && m.ReturnType == typeof(void))
                {
                    for (int i = 0; i < ps.Length; i++) g.Emit(System.Reflection.Emit.OpCodes.Ldarg, i + 1);
                    g.Emit(System.Reflection.Emit.OpCodes.Call, alvo);
                }
                else if (m.ReturnType != typeof(void))
                {
                    var l = g.DeclareLocal(m.ReturnType);
                    g.Emit(System.Reflection.Emit.OpCodes.Ldloca, l);
                    g.Emit(System.Reflection.Emit.OpCodes.Initobj, m.ReturnType);
                    g.Emit(System.Reflection.Emit.OpCodes.Ldloc, l);
                }
                g.Emit(System.Reflection.Emit.OpCodes.Ret);
                tb.DefineMethodOverride(nm, m);
            }
        return tb.CreateType();
    }

    sealed class Aceita : IEqualityComparer<Type>
    {
        public bool Equals(Type? nosso, Type? deles) => nosso == deles || nosso == typeof(object) && deles is { IsValueType: false };
        public int GetHashCode(Type t) => 0;
    }
}
