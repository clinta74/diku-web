#:project ../src/Muwbta.Server/Muwbta.Server.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false

// Generates a room's terrain grid, the same way every other room in the world got one.
//
//     dotnet run tools/generate-terrain.cs <kind> <room-key> [<kind> <room-key> ...]
//
// Writes one JSON object per room to stdout: { "key", "grid", "legend" }.
//
// **The seed is the room key**, which is what makes this reproducible: run it twice and the same
// room comes back byte-identical, so regenerating the world is a no-op diff rather than a rewrite
// of every room (WORLD.md §10.1). That is also why there is no --seed flag to get "a different
// one" - a room's art is a function of its name, and wanting different art means wanting a
// different name or a different kind.
//
// It refuses a room the layout service could not place anything in. `check-bundle.cs` enforces the
// same floor, but finding out here is cheaper than finding out after the room is authored.

using System.Text.Json;
using System.Text.Json.Nodes;
using Muwbta.Server.Building;

if (args.Length == 0 || args.Length % 2 != 0)
{
    Console.Error.WriteLine("usage: generate-terrain.cs <kind> <room-key> [<kind> <room-key> ...]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("kinds:");

    foreach (var k in TerrainGenerator.Kinds)
    {
        Console.Error.WriteLine($"  {k.Key,-10} {k.Summary}");
    }

    return 2;
}

var output = new JsonArray();

for (var i = 0; i < args.Length; i += 2)
{
    var kind = args[i];
    var roomKey = args[i + 1];

    if (TerrainGenerator.Find(kind) is null)
    {
        Console.Error.WriteLine($"no terrain kind '{kind}'.");
        return 1;
    }

    var terrain = TerrainGenerator.Generate(kind, roomKey);
    var open = TerrainGenerator.OpenCells(terrain);

    // Entities are placed only on open ground and are simply not drawn when there is none, so a
    // room under the floor is one whose occupants vanish.
    if (open < 40)
    {
        Console.Error.WriteLine($"{roomKey}: only {open} standable cells, needs 40.");
        return 1;
    }

    Console.Error.WriteLine($"{roomKey}: {kind}, {open} standable cells");

    output.Add(new JsonObject
    {
        ["key"] = roomKey,
        ["grid"] = new JsonArray([.. terrain.Grid.Select(row => (JsonNode)row!)]),
        ["legend"] = new JsonObject(
            terrain.Legend.Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value))),
    });
}

Console.WriteLine(output.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
return 0;
