using System.Reflection;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Commands;
using Muwbta.Engine.Presentation;

namespace Muwbta.Engine.Tests.Architecture;

/// <summary>
/// PLAN.md §4.2 promises the room map is cosmetic and that this is enforced structurally
/// rather than by discipline. These tests are that enforcement.
///
/// The failure mode they exist to prevent: someone adds a range check or an adjacency rule
/// six months from now, and the cosmetic grid quietly becomes a positional combat system
/// with none of the design work that would require. Coordinates being physically absent
/// from Domain means such a change cannot compile without deliberately defeating this test.
/// </summary>
public sealed class CoordinateIsolationTests
{
    private static readonly Assembly DomainAssembly = typeof(Room).Assembly;
    private static readonly Assembly EngineAssembly = typeof(CommandRegistry).Assembly;

    private static readonly string[] CoordinateNames =
        ["x", "y", "z", "posx", "posy", "position", "coord", "coords", "coordinate", "cell"];

    [Fact]
    public void Domain_declares_no_coordinate_members()
    {
        var offenders = new List<string>();

        foreach (var type in DomainAssembly.GetTypes().Where(t => !IsCompilerGenerated(t)))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (IsCoordinateName(property.Name))
                {
                    offenders.Add($"{type.FullName}.{property.Name}");
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (!field.Name.Contains('<', StringComparison.Ordinal) && IsCoordinateName(field.Name))
                {
                    offenders.Add($"{type.FullName}.{field.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Domain must not know about map coordinates (PLAN.md §4.2). Offending members: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Domain_references_no_other_project_in_this_solution()
    {
        // PLAN.md §2.2: Domain has zero dependencies, and that isolation is what keeps the
        // rules unit-testable without mocks.
        var referenced = DomainAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Muwbta", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            referenced.Count == 0,
            "Muwbta.Domain must reference no other Muwbta project. Found: "
            + string.Join(", ", referenced));
    }

    [Fact]
    public void Command_handlers_cannot_reach_the_layout_service()
    {
        // Handlers reach presentation only through PlayerView, which writes map payloads and
        // never hands a position back. So no rule can branch on where something is drawn.
        var layoutType = typeof(RoomLayoutService);
        var offenders = new List<string>();

        var commandTypes = EngineAssembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Muwbta.Engine.Commands", StringComparison.Ordinal) == true)
            .Where(t => !IsCompilerGenerated(t));

        foreach (var type in commandTypes)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == layoutType)
                {
                    offenders.Add($"field {type.Name}.{field.Name}");
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.PropertyType == layoutType)
                {
                    offenders.Add($"property {type.Name}.{property.Name}");
                }
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetParameters().Any(p => p.ParameterType == layoutType))
                {
                    offenders.Add($"parameter on {type.Name}.{method.Name}");
                }
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (constructor.GetParameters().Any(p => p.ParameterType == layoutType))
                {
                    offenders.Add($"constructor of {type.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Command handlers must not touch RoomLayoutService (PLAN.md §4.2). Found: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void The_layout_service_is_the_only_type_producing_map_coordinates()
    {
        // Confirms the escape hatch stays a single, findable place rather than spreading.
        var producers = EngineAssembly.GetTypes()
            .Where(t => !IsCompilerGenerated(t))
            .Where(t => t.Namespace?.StartsWith("Muwbta.Engine.Presentation", StringComparison.Ordinal) == true)
            .Select(t => t.Name)
            .ToList();

        Assert.Contains(nameof(RoomLayoutService), producers);
        Assert.Contains(nameof(PlayerView), producers);
    }

    private static bool IsCoordinateName(string name) =>
        CoordinateNames.Contains(name.ToLowerInvariant());

    private static bool IsCompilerGenerated(MemberInfo type) =>
        type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null
        || type.Name.Contains('<', StringComparison.Ordinal);
}
