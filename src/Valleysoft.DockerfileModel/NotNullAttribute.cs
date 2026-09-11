#if NETSTANDARD2_0
namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class NotNullAttribute : Attribute
{
}
#endif
