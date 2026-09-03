#:project ../src/Muwbta.Domain/Muwbta.Domain.csproj

// Prints the derived one-line description of every starter ability, by Path. A quick eyeball of
// what `abilities` will say in play.
//
// NOTE: `dotnet run` on a file-based app caches its build against *this file's* content, so a
// change in Muwbta.Domain alone may not be picked up. Touch this file if the output looks stale.

using Muwbta.Domain.Abilities;
using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;

var effects = new EffectRegistry();

foreach (var path in new[]
    { CharacterPath.Warden, CharacterPath.Shade, CharacterPath.Adept, CharacterPath.Hallow })
{
    Console.WriteLine();
    Console.WriteLine($"=== {path} ===");

    foreach (var ability in AbilityCatalogue.AsAbilities
        .Where(a => a.Path == path)
        .OrderBy(a => a.UnlockLevel))
    {
        Console.WriteLine(
            $"  {ability.UnlockLevel,2}  {ability.Name}  ({ability.CostValue} " +
            $"{ability.CostType.ToString().ToLowerInvariant()}) — " +
            AbilityDescriber.Describe(ability, effects));
    }
}
