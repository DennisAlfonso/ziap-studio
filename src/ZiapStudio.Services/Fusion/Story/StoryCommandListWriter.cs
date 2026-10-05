using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZiapStudio.Core.Fusion.Story;
using ZiapStudio.Services.Editing;
using ZiapStudio.Services.ProjectSafety;

namespace ZiapStudio.Services.Fusion.Story;

/// <summary>
/// Applies one prevalidated insert/removal to a single RPG Maker command list.
/// It verifies the live source fingerprint, target identity, anchor signature and
/// terminal code before atomically replacing the JSON file.
/// </summary>
public sealed class StoryCommandListWriter
{
    private readonly FileSystemService _fileSystem;
    private readonly AtomicJsonFileWriter _atomicWriter;
    private readonly ProjectWriteCoordinator? _writeCoordinator;

    public StoryCommandListWriter(
        FileSystemService fileSystem,
        AtomicJsonFileWriter atomicWriter,
        ProjectWriteCoordinator? writeCoordinator = null)
    {
        _fileSystem = fileSystem;
        _atomicWriter = atomicWriter;
        _writeCoordinator = writeCoordinator;
    }

    public Task InsertAsync(
        string projectPath,
        StoryCompositionPlan plan,
        IReadOnlyList<StoryGeneratedCommand> commands,
        CancellationToken cancellationToken = default) => MutateAsync(
        projectPath, plan.Target, plan.ExpectedSourceSnapshot.ContentHash,
        plan.AnchorStartCommandIndex, plan.AnchorEndCommandIndex, plan.AnchorCommands,
        insertionIndex: plan.InsertionCommandIndex, commands, remove: false, cancellationToken);

    /// <summary>Read-only preflight used before a remote append is attempted.</summary>
    public async Task ValidateInsertionAsync(
        string projectPath,
        StoryCompositionPlan plan,
        CancellationToken cancellationToken = default)
    {
        _writeCoordinator?.TrackSnapshot(plan.ExpectedSourceSnapshot);
        var path = GetSafeSourcePath(projectPath, plan.Target.SourceFile);
        var bytes = await _fileSystem.ReadAllBytesAsync(path, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, plan.ExpectedSourceSnapshot.ContentHash, StringComparison.Ordinal))
        {
            throw new StoryLocalSourceConflictException("La sorgente RPG Maker è cambiata dopo la creazione del plan.");
        }
        var root = JsonNode.Parse(bytes) ?? throw new StoryCommandListWriteException("La sorgente RPG Maker non contiene JSON valido.");
        var list = ResolveCommandList(root, plan.Target);
        ValidateTerminal(list);
        ValidateAnchor(list, plan.AnchorStartCommandIndex, plan.AnchorEndCommandIndex, plan.AnchorCommands);
        EnsureSafeInsertion(list, plan.AnchorEndCommandIndex, plan.InsertionCommandIndex, plan.Indent);
    }

    /// <summary>
    /// Crash-window probe: recognizes exactly the commands this plan would have
    /// written, without using it as a fuzzy rebase mechanism.
    /// </summary>
    public async Task<bool> HasInsertionAsync(
        string projectPath,
        StoryCompositionPlan plan,
        IReadOnlyList<StoryGeneratedCommand> commands,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var path = GetSafeSourcePath(projectPath, plan.Target.SourceFile);
            var root = JsonNode.Parse(await _fileSystem.ReadAllTextAsync(path, cancellationToken));
            if (root is null) return false;
            var list = ResolveCommandList(root, plan.Target);
            ValidateTerminal(list);
            if (plan.InsertionCommandIndex < 0 || plan.InsertionCommandIndex + commands.Count > list.Count - 1)
            {
                return false;
            }
            return commands.Select((command, offset) =>
            {
                var node = list[plan.InsertionCommandIndex + offset] as JsonObject;
                return node is not null && ReadCode(node) == command.Code &&
                    (node["indent"]?.GetValue<int?>() ?? 0) == command.Indent &&
                    (node["parameters"]?.ToJsonString() ?? "[]") == JsonSerializer.Serialize(command.Parameters);
            }).All(matches => matches);
        }
        catch (Exception exception) when (exception is IOException or JsonException or StoryCommandListWriteException)
        {
            return false;
        }
    }

    public Task RemoveAsync(
        string projectPath,
        StoryCommandListTarget target,
        ZiapStudio.Core.Editing.DocumentSourceSnapshot expectedSnapshot,
        StoryBlock block,
        CancellationToken cancellationToken = default) => MutateAsync(
        projectPath, target, expectedSnapshot.ContentHash, block.CommandStartIndex, block.CommandEndIndex,
        block.SourceCommands.Select(command => new StoryCommandSignature
        {
            Code = command.Code,
            Indent = command.Indent,
            ParametersJson = command.Parameters,
        }).ToArray(), block.CommandStartIndex, [], remove: true, cancellationToken);

    private async Task MutateAsync(
        string projectPath,
        StoryCommandListTarget target,
        string expectedHash,
        int anchorStart,
        int anchorEnd,
        IReadOnlyList<StoryCommandSignature> expectedCommands,
        int insertionIndex,
        IReadOnlyList<StoryGeneratedCommand> generated,
        bool remove,
        CancellationToken cancellationToken)
    {
        _writeCoordinator?.TrackSnapshot(new ZiapStudio.Core.Editing.DocumentSourceSnapshot
        {
            SourcePath = GetSafeSourcePath(projectPath, target.SourceFile),
            ContentHash = expectedHash,
        });
        var path = GetSafeSourcePath(projectPath, target.SourceFile);
        var bytes = await _fileSystem.ReadAllBytesAsync(path, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(hash, expectedHash, StringComparison.Ordinal))
        {
            throw new StoryLocalSourceConflictException("La sorgente RPG Maker è cambiata dopo la creazione del plan.");
        }
        JsonNode root;
        try
        {
            root = JsonNode.Parse(bytes) ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new StoryCommandListWriteException("La sorgente RPG Maker non contiene JSON valido.", exception);
        }
        var list = ResolveCommandList(root, target);
        ValidateTerminal(list);
        ValidateAnchor(list, anchorStart, anchorEnd, expectedCommands);
        if (!remove)
        {
            if (insertionIndex != anchorEnd + 1 || insertionIndex < 0 || insertionIndex >= list.Count)
            {
                throw new StoryCommandListWriteException("L'indice di inserimento non è sicuro.");
            }
            EnsureSafeInsertion(list, anchorEnd, insertionIndex, expectedCommands[0].Indent);
            for (var offset = 0; offset < generated.Count; offset++)
            {
                list.Insert(insertionIndex + offset, ToJsonCommand(generated[offset]));
            }
        }
        else
        {
            if (anchorStart < 0 || anchorEnd >= list.Count - 1)
            {
                throw new StoryCommandListWriteException("Il range da rimuovere tocca il terminal code 0.");
            }
            for (var index = anchorEnd; index >= anchorStart; index--)
            {
                list.RemoveAt(index);
            }
        }
        ValidateTerminal(list);
        var serialized = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        try
        {
            await _atomicWriter.WriteAsync(path, Encoding.UTF8.GetBytes(serialized), expectedHash, cancellationToken);
        }
        catch (ProjectWriteException exception)
        {
            throw new StoryCommandListWriteException(exception.Message, exception);
        }
        // The atomic writer validates JSON and the expected hash. Read back the targeted list too.
        var reread = JsonNode.Parse(await _fileSystem.ReadAllTextAsync(path, cancellationToken)) ?? throw new JsonException();
        ValidateTerminal(ResolveCommandList(reread, target));
    }

    private static JsonArray ResolveCommandList(JsonNode root, StoryCommandListTarget target)
    {
        if (target.Kind == StoryCommandListKind.MapPage)
        {
            if (root is not JsonObject map || map["events"] is not JsonArray events)
            {
                throw new StoryCommandListWriteException("Map JSON senza events array.");
            }
            var @event = events.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.GetValue<int?>() == target.EventId)
                ?? throw new StoryCommandListWriteException("L'evento target non esiste più.");
            if (target.PageNumber is not int pageNumber || @event["pages"] is not JsonArray pages ||
                pageNumber < 1 || pageNumber > pages.Count || pages[pageNumber - 1] is not JsonObject page ||
                page["list"] is not JsonArray list)
            {
                throw new StoryCommandListWriteException("La page target non esiste più.");
            }
            return list;
        }
        if (root is not JsonArray commonEvents)
        {
            throw new StoryCommandListWriteException("CommonEvents.json deve essere un array.");
        }
        var common = commonEvents.OfType<JsonObject>().SingleOrDefault(item => item["id"]?.GetValue<int?>() == target.EventId)
            ?? throw new StoryCommandListWriteException("Il Common Event target non esiste più.");
        return common["list"] as JsonArray ??
            throw new StoryCommandListWriteException("Il Common Event non ha un command list.");
    }

    private static void ValidateTerminal(JsonArray list)
    {
        // Some legacy FHD lists contain code 0 as an in-flow raw command. The
        // only terminal this writer owns is the final item: it must remain one
        // final code 0 and insertion always occurs before it.
        if (list.Count == 0 || ReadCode(list[^1]) != 0)
        {
            throw new StoryCommandListWriteException("Il command list RPG Maker non ha un terminal code 0 finale.");
        }
    }

    private static void ValidateAnchor(
        JsonArray list,
        int start,
        int end,
        IReadOnlyList<StoryCommandSignature> expected)
    {
        if (start < 0 || end < start || end >= list.Count - 1 || expected.Count != end - start + 1)
        {
            throw new StoryLocalSourceConflictException("Il command range dell'anchor non è più valido.");
        }
        for (var offset = 0; offset < expected.Count; offset++)
        {
            var item = list[start + offset] as JsonObject;
            var current = item is null ? null : new StoryCommandSignature
            {
                Code = ReadCode(item),
                Indent = item["indent"]?.GetValue<int?>() ?? 0,
                ParametersJson = item["parameters"]?.ToJsonString() ?? "[]",
            };
            if (current is null || current != expected[offset])
            {
                throw new StoryLocalSourceConflictException("I command dell'anchor sono cambiati.");
            }
        }
    }

    private static void EnsureSafeInsertion(
        JsonArray list,
        int anchorEnd,
        int insertionIndex,
        int indent)
    {
        if (anchorEnd + 1 != insertionIndex || insertionIndex >= list.Count)
        {
            throw new StoryCommandListWriteException("L'inserimento non segue l'anchor completo.");
        }
        var following = list[insertionIndex] as JsonObject;
        var followingCode = ReadCode(following);
        var followingIndent = following?["indent"]?.GetValue<int?>() ?? 0;
        // Closing branch markers are safe only at the current indent; crossing to a
        // lower indent would silently move content outside the original branch.
        if ((followingCode != 0 && followingIndent < indent) ||
            ((followingCode is 402 or 403 or 404 or 411 or 412 or 413) && followingIndent < indent))
        {
            throw new StoryCommandListWriteException("Questa posizione richiede l'Event Editor avanzato.");
        }
    }

    private static JsonObject ToJsonCommand(StoryGeneratedCommand command) => new()
    {
        ["code"] = command.Code,
        ["indent"] = command.Indent,
        ["parameters"] = JsonSerializer.SerializeToNode(command.Parameters),
    };

    private static int ReadCode(JsonNode? node) => node is JsonObject @object
        ? @object["code"]?.GetValue<int?>() ?? int.MinValue
        : int.MinValue;

    private static string GetSafeSourcePath(string projectPath, string sourceFile)
    {
        var dataRoot = Path.GetFullPath(Path.Combine(projectPath, "data"));
        var path = Path.GetFullPath(Path.Combine(projectPath, sourceFile));
        var prefix = dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new StoryCommandListWriteException("Sorgente RPG Maker fuori dalla cartella data.");
        }
        return path;
    }
}

public class StoryCommandListWriteException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class StoryLocalSourceConflictException(string message) : StoryCommandListWriteException(message);
