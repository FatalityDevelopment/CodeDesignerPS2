using System.Reflection;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Mcp;

/// <summary>A discovered tool: its metadata, implementing type, and entry method.</summary>
internal sealed record ToolDescriptor(CdToolAttribute Meta, Type ImplType, MethodInfo Entry);

/// <summary>
/// Reflects over the tool assemblies for <c>[CdTool]</c> classes and keeps only the
/// ones the active <see cref="ToolProfile"/> allows (by domain + risk). This is the
/// launch-time filter that keeps the model from ever seeing every tool at once.
/// </summary>
internal static class ToolRegistry
{
    public static IReadOnlyList<ToolDescriptor> Discover(IEnumerable<Assembly> assemblies, ToolProfile profile)
    {
        var result = new List<ToolDescriptor>();

        foreach (var asm in assemblies)
        {
            foreach (var type in SafeGetTypes(asm))
            {
                var meta = type.GetCustomAttribute<CdToolAttribute>();
                if (meta is null) continue;
                if (!profile.Allows(meta.Name, meta.Domain, meta.Risk)) continue;

                var entry = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.GetCustomAttribute<ToolEntryPointAttribute>() is not null);
                if (entry is null) continue;

                result.Add(new ToolDescriptor(meta, type, entry));
            }
        }

        return result.OrderBy(d => d.Meta.Domain).ThenBy(d => d.Meta.Name).ToList();
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
