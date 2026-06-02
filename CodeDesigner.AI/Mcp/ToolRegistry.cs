using System.Reflection;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Mcp;

/// <summary>A discovered tool: its metadata, implementing type, and entry method.</summary>
public sealed record ToolDescriptor(CdToolAttribute Meta, Type ImplType, MethodInfo Entry);

/// <summary>
/// Reflects over the tool assemblies for <c>[CdTool]</c> classes and keeps only the
/// ones the active <see cref="ToolProfile"/> allows (by domain + risk). This is the
/// launch-time filter that keeps the model from ever seeing every tool at once.
/// </summary>
internal static class ToolRegistry
{
    /// <summary>Every discovered <c>[CdTool]</c> across the assemblies, ignoring any profile.
    /// The runtime domain gate needs this so it can widen the live set on demand.</summary>
    public static IReadOnlyList<ToolDescriptor> DiscoverAll(IEnumerable<Assembly> assemblies)
    {
        var result = new List<ToolDescriptor>();

        foreach (var asm in assemblies)
        {
            foreach (var type in SafeGetTypes(asm))
            {
                var meta = type.GetCustomAttribute<CdToolAttribute>();
                if (meta is null) continue;

                var entry = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.GetCustomAttribute<ToolEntryPointAttribute>() is not null);
                if (entry is null) continue;

                result.Add(new ToolDescriptor(meta, type, entry));
            }
        }

        return result.OrderBy(d => d.Meta.Domain).ThenBy(d => d.Meta.Name).ToList();
    }

    /// <summary>The subset of <see cref="DiscoverAll"/> the active profile allows (by name + domain + risk).
    /// This is the launch-time filter that keeps the model from ever seeing every tool at once.</summary>
    public static IReadOnlyList<ToolDescriptor> Discover(IEnumerable<Assembly> assemblies, ToolProfile profile) =>
        DiscoverAll(assemblies)
            .Where(d => profile.Allows(d.Meta.Name, d.Meta.Domain, d.Meta.Risk))
            .ToList();

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
