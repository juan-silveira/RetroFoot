// O atributo que o runtime reconhece (o mesmo do DispatchProxy): o assembly gerado pelo
// Coletor (Dicas.cs) pode herdar do DrawingContext, cujo construtor e interno.
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute
{
    public string AssemblyName { get; } = assemblyName;
}
