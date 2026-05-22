using System.Reflection;
using MassTransit;

namespace Hdos.Common.Messaging;

internal static class NameFormatterExtensions
{
    public static string ToKebabCaseString(this MemberInfo member) =>
        KebabCaseEndpointNameFormatter.Instance.SanitizeName(member.Name);
}

internal class KebabCaseEntityNameFormatter : IEntityNameFormatter
{
    private const string Suffix = "IntegrationEvent";

    public string FormatEntityName<T>()
    {
        var name = typeof(T).Name;
        if (name.EndsWith(Suffix, StringComparison.Ordinal) && name.Length > Suffix.Length)
            name = name[..^Suffix.Length];
        return KebabCaseEndpointNameFormatter.Instance.SanitizeName(name);
    }
}