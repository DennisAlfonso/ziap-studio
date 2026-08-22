using System.Globalization;
using System.Text.Json;
using ZiapStudio.Core.Fusion.Bosses;

namespace ZiapStudio.Services.Fusion.Bosses;

internal sealed class FusionBossActionCatalog
{
    private readonly IReadOnlyDictionary<string, ActionDescriptor> _descriptors;

    private FusionBossActionCatalog(IReadOnlyDictionary<string, ActionDescriptor> descriptors)
    {
        _descriptors = descriptors;
    }

    public static async Task<FusionBossActionCatalog> LoadAsync(
        FileSystemService fileSystem,
        string projectPath,
        CancellationToken cancellationToken)
    {
        var descriptors = BuiltInDescriptors().ToDictionary(
            descriptor => descriptor.Id,
            StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(projectPath, "data", "FusionActionCatalog.json");
        if (!fileSystem.FileExists(path))
        {
            return new FusionBossActionCatalog(descriptors);
        }

        try
        {
            using var document = JsonDocument.Parse(
                await fileSystem.ReadAllTextAsync(path, cancellationToken));
            if (document.RootElement.TryGetProperty("actions", out var actions) &&
                actions.ValueKind == JsonValueKind.Object)
            {
                foreach (var action in actions.EnumerateObject())
                {
                    if (action.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    descriptors.TryGetValue(action.Name, out var fallback);
                    descriptors[action.Name] = new ActionDescriptor(
                        action.Name,
                        ReadString(action.Value, "displayName") ?? fallback?.DisplayName ?? action.Name,
                        ReadString(action.Value, "category") ?? fallback?.Category ?? "Azione",
                        ReadString(action.Value, "icon") ?? fallback?.IconGlyph ?? "◆",
                        ReadString(action.Value, "descriptionTemplate") ??
                            fallback?.DescriptionTemplate ?? "Esegue {action}.");
                }
            }
        }
        catch (JsonException)
        {
            // Il catalogo e' un livello editoriale opzionale: i fallback integrati
            // mantengono leggibile l'encounter anche se il file e' incompleto.
        }

        return new FusionBossActionCatalog(descriptors);
    }

    public ActionSemantics Describe(
        string actionId,
        IReadOnlyList<JsonElement> elements,
        FusionBossAttackGeometry? attackGeometry)
    {
        var descriptor = _descriptors.TryGetValue(actionId, out var configured)
            ? configured
            : new ActionDescriptor(actionId, HumanizeIdentifier(actionId), "Azione", "◆", "Esegue {action}.");
        var config = elements.Count > 1 && elements[1].ValueKind == JsonValueKind.Object
            ? elements[1]
            : default;
        var values = BuildTemplateValues(actionId, elements, config, attackGeometry);
        var description = descriptor.DescriptionTemplate;
        foreach (var pair in values)
        {
            description = description.Replace(
                $"{{{pair.Key}}}",
                pair.Value,
                StringComparison.OrdinalIgnoreCase);
        }

        var (reads, writes) = BuildDataFlow(actionId, config, elements);
        return new ActionSemantics(
            descriptor.DisplayName,
            description,
            descriptor.Category,
            descriptor.IconGlyph,
            reads,
            writes);
    }

    public string DescribeCondition(JsonElement condition)
    {
        if (condition.ValueKind != JsonValueKind.Array || condition.GetArrayLength() == 0)
        {
            return $"Attende che si verifichi {HumanizeIdentifier(Summarize(condition))}.";
        }

        var parts = condition.EnumerateArray().ToArray();
        var conditionId = parts[0].ValueKind == JsonValueKind.String
            ? parts[0].GetString() ?? "condition"
            : "condition";
        return conditionId switch
        {
            "bossDefeated" => "Attende che il boss venga sconfitto.",
            "roleMovementComplete" => $"Attende che {RoleLabel(StringAt(parts, 1))} completi il movimento.",
            "signal" => $"Attende il segnale “{StringAt(parts, 1)}”.",
            "mechanicResolved" => $"Attende la risoluzione della meccanica “{StringAt(parts, 1)}”.",
            "mechanicActive" => $"Attende che la meccanica “{StringAt(parts, 1)}” diventi attiva.",
            "resourceAtLeast" => $"Attende che la risorsa “{StringAt(parts, 1)}” raggiunga almeno {ValueAt(parts, 2)}.",
            "resourceAtMost" => $"Attende che la risorsa “{StringAt(parts, 1)}” scenda a {ValueAt(parts, 2)} o meno.",
            _ => $"Attende la condizione “{HumanizeIdentifier(conditionId)}”.",
        };
    }

    private static Dictionary<string, string> BuildTemplateValues(
        string actionId,
        IReadOnlyList<JsonElement> elements,
        JsonElement config,
        FusionBossAttackGeometry? attackGeometry)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["action"] = HumanizeIdentifier(actionId),
            ["arg1"] = StringAt(elements, 1),
            ["arg2"] = StringAt(elements, 2),
            ["role"] = RoleLabel(ReadString(config, "role")),
            ["caster"] = RoleLabel(ReadString(config, "casterRole")),
            ["key"] = ReadString(config, "key") ?? "senza nome",
            ["attack.id"] = ReadString(config, "attackId") ??
                ReadString(config, "preparedAttackId") ?? "senza nome",
            ["cue"] = StringAt(elements, 1),
            ["sequence"] = ReadString(config, "sequence") ?? string.Empty,
            ["amount"] = config.ValueKind == JsonValueKind.Object &&
                config.TryGetProperty("amount", out var amount)
                    ? Summarize(amount)
                    : ValueAt(elements, 1),
        };
        var skillId = ReadInt(config, "skillId");
        result["skill"] = attackGeometry is not null
            ? $"Skill #{attackGeometry.SkillId} “{attackGeometry.SkillName}”"
            : skillId is { } id ? $"Skill #{id}" : "l'abilità configurata";
        result["skill.id"] = skillId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        result["target"] = config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("target", out var target)
                ? DescribeTarget(target)
                : "il bersaglio configurato";
        result["origin"] = config.ValueKind == JsonValueKind.Object &&
            config.TryGetProperty("origin", out var origin)
                ? DescribeTarget(origin)
                : "la posizione dell'utilizzatore";
        result["targets"] = DescribeTargets(config);
        return result;
    }

    private static (IReadOnlyList<string> Reads, IReadOnlyList<string> Writes) BuildDataFlow(
        string actionId,
        JsonElement config,
        IReadOnlyList<JsonElement> elements)
    {
        var reads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var writes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config.ValueKind == JsonValueKind.Object)
        {
            CollectTargetReads(config, "target", reads);
            CollectTargetReads(config, "origin", reads);
            if (config.TryGetProperty("targets", out var targets) && targets.ValueKind == JsonValueKind.Array)
            {
                foreach (var target in targets.EnumerateArray())
                {
                    CollectCapturedTarget(target, reads);
                }
            }
        }

        if (actionId.Equals("combat.captureTarget", StringComparison.OrdinalIgnoreCase) &&
            ReadString(config, "key") is { } targetKey)
        {
            writes.Add($"bersaglio:{targetKey}");
        }
        else if (actionId.Equals("combat.prepareAttack", StringComparison.OrdinalIgnoreCase) &&
            (ReadString(config, "attackId") ?? ReadString(config, "preparedAttackId")) is { } preparedId)
        {
            writes.Add($"attacco:{preparedId}");
        }
        else if ((actionId.Equals("combat.commitAttack", StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals("combat.cancelAttack", StringComparison.OrdinalIgnoreCase)) &&
            (ReadString(config, "attackId") ?? ReadString(config, "preparedAttackId")) is { } consumedId)
        {
            reads.Add($"attacco:{consumedId}");
        }
        else if (actionId.Equals("setResource", StringComparison.OrdinalIgnoreCase))
        {
            var key = ReadString(config, "key") ?? StringAt(elements, 1);
            if (!string.IsNullOrWhiteSpace(key))
            {
                writes.Add($"risorsa:{key}");
            }
        }
        else if (actionId.Equals("emit", StringComparison.OrdinalIgnoreCase))
        {
            var signal = StringAt(elements, 1);
            if (!string.IsNullOrWhiteSpace(signal))
            {
                writes.Add($"segnale:{signal}");
            }
        }
        else if (actionId.Equals("addAltarCharge", StringComparison.OrdinalIgnoreCase))
        {
            writes.Add("meccanica:altarCharge");
        }
        else if (actionId.Equals("cleanupDamageWindow", StringComparison.OrdinalIgnoreCase))
        {
            writes.Add("meccanica:damageWindow");
        }
        else if (actionId.Equals("completeWipeCycle", StringComparison.OrdinalIgnoreCase))
        {
            writes.Add("meccanica:wipe");
        }
        else if ((actionId.Equals("alphaAbsChangeRoleHp", StringComparison.OrdinalIgnoreCase) ||
            actionId.Equals("combat.changeRoleHp", StringComparison.OrdinalIgnoreCase)) &&
            ReadString(config, "role") is { } role)
        {
            writes.Add($"hp:{role}");
        }

        return (reads.ToArray(), writes.ToArray());
    }

    private static void CollectTargetReads(JsonElement config, string property, ISet<string> reads)
    {
        if (config.TryGetProperty(property, out var target))
        {
            CollectCapturedTarget(target, reads);
        }
    }

    private static void CollectCapturedTarget(JsonElement target, ISet<string> reads)
    {
        if (target.ValueKind == JsonValueKind.Object &&
            ReadString(target, "type")?.Equals("captured", StringComparison.OrdinalIgnoreCase) == true &&
            ReadString(target, "key") is { } key)
        {
            reads.Add($"bersaglio:{key}");
        }
    }

    private static string DescribeTargets(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("targets", out var targets) ||
            targets.ValueKind != JsonValueKind.Array)
        {
            return config.ValueKind == JsonValueKind.Object && config.TryGetProperty("target", out var target)
                ? DescribeTarget(target)
                : "i bersagli configurati";
        }

        var labels = targets.EnumerateArray().Select(DescribeTarget).ToArray();
        return labels.Length switch
        {
            0 => "nessun bersaglio",
            1 => labels[0],
            <= 3 => string.Join(", ", labels),
            _ => $"{labels.Length} bersagli ({string.Join(", ", labels.Take(2))}, …)",
        };
    }

    private static string DescribeTarget(JsonElement target)
    {
        if (target.ValueKind != JsonValueKind.Object)
        {
            return Summarize(target);
        }

        var type = ReadString(target, "type") ?? "target";
        return type switch
        {
            "playerPosition" => "la posizione attuale del giocatore",
            "captured" => $"la posizione memorizzata “{ReadString(target, "key") ?? "?"}”",
            "anchor" => ReadInt(target, "index") is { } index
                ? $"l'anchor “{ReadString(target, "anchor") ?? "?"}” #{index + 1}"
                : $"l'anchor “{ReadString(target, "anchor") ?? "?"}”",
            "role" => $"la posizione di {RoleLabel(ReadString(target, "role"))}",
            "point" => $"il punto ({Number(target, "x")}, {Number(target, "y")})",
            _ => $"il bersaglio {HumanizeIdentifier(type)}",
        };
    }

    private static IEnumerable<ActionDescriptor> BuiltInDescriptors()
    {
        yield return new("combat.captureTarget", "Memorizza un bersaglio", "Movimento", "⌖", "Memorizza {target} come “{key}” per riutilizzarla negli step successivi.");
        yield return new("combat.moveTo", "Sposta un ruolo", "Movimento", "➜", "Sposta {role} verso {target}.");
        yield return new("combat.face", "Orienta un ruolo", "Movimento", "◉", "Orienta {role} verso {target}.");
        yield return new("combat.stopMovement", "Ferma il movimento", "Movimento", "■", "Ferma il movimento di {role}.");
        yield return new("combat.prepareAttack", "Prepara un attacco", "Combattimento", "◌", "Mostra il telegraph di {skill} contro {target} e conserva l'istanza “{attack.id}” fino alla risoluzione.");
        yield return new("combat.commitAttack", "Risolvi un attacco", "Combattimento", "⚡", "Esegue immediatamente l'attacco preparato “{attack.id}”.");
        yield return new("combat.cancelAttack", "Annulla un attacco", "Combattimento", "×", "Annulla l'attacco preparato “{attack.id}” e rimuove il telegraph.");
        yield return new("combat.cast", "Esegue un attacco", "Combattimento", "⚔", "{caster} usa {skill} contro {target}, con origine {origin}.");
        yield return new("combat.castVolley", "Lancia una raffica", "Combattimento", "✦", "{caster} usa {skill} in raffica contro {targets}.");
        yield return new("alphaAbsMapSkill", "Esegue un attacco mappa", "Combattimento", "⚔", "{caster} usa {skill} contro {target}.");
        yield return new("alphaAbsSkillBurst", "Esegue una raffica ABS", "Combattimento", "✦", "Esegue una raffica dell'abilità configurata.");
        yield return new("presentation.cue", "Riproduce un segnale visivo", "Presentazione", "◈", "Riproduce il cue “{cue}”.");
        yield return new("presentation.loopCue", "Mantiene un segnale visivo", "Presentazione", "◌", "Mantiene attivo il cue “{cue}”.");
        yield return new("showBossUI", "Mostra l'interfaccia del boss", "Interfaccia", "▣", "Rende visibile l'interfaccia della boss battle.");
        yield return new("hideBossUI", "Nasconde l'interfaccia del boss", "Interfaccia", "▢", "Nasconde l'interfaccia della boss battle.");
        yield return new("emit", "Invia un segnale", "Flusso", "◆", "Invia il segnale “{arg1}” alle altre parti dell'encounter.");
        yield return new("setResource", "Imposta una risorsa", "Stato", "◇", "Aggiorna la risorsa “{arg1}”.");
        yield return new("startWipe", "Avvia la meccanica wipe", "Meccanica", "⚠", "Avvia il profilo di wipe “{arg1}”.");
        yield return new("wipe.beginResolution", "Avvia la risoluzione del wipe", "Meccanica", "⚠", "Inizia la fase di risoluzione della meccanica wipe.");
        yield return new("wipe.resolve", "Conclude il wipe", "Meccanica", "✓", "Conclude e registra la risoluzione della meccanica wipe.");
        yield return new("prepareWave", "Prepara un'ondata", "Meccanica", "≈", "Prepara la prossima ondata della meccanica.");
        yield return new("startWave", "Avvia un'ondata", "Meccanica", "≈", "Avvia l'ondata preparata.");
        yield return new("expireWave", "Termina un'ondata", "Meccanica", "✓", "Termina l'ondata attiva.");
        yield return new("shield.damagePercent", "Danneggia lo scudo", "Meccanica", "⬡", "Riduce lo scudo della percentuale configurata.");
        yield return new("damageWindow.activate", "Apre una finestra di danno", "Meccanica", "◎", "Rende attiva una finestra in cui il boss può ricevere danno.");
        yield return new("damageWindow.applyEntry", "Applica danno della finestra", "Meccanica", "◎", "Applica una voce di danno accumulata nella finestra.");
        yield return new("damageWindow.complete", "Chiude la finestra di danno", "Meccanica", "✓", "Conclude la finestra di danno attiva.");
        yield return new("party.recoverAll", "Cura il gruppo", "Combattimento", "+", "Ripristina le risorse del gruppo.");
        yield return new("encounter.complete", "Completa l'encounter", "Flusso", "✓", "Segna la boss battle come completata.");
        yield return new("completeIf", "Verifica una condizione di uscita", "Flusso", "?", "Completa lo step solo se la condizione configurata e' soddisfatta.");
        yield return new("addAltarCharge", "Aumenta la carica dell'altare", "Meccanica", "+", "Aggiunge {amount} alla carica dell'altare.");
        yield return new("alphaAbsChangeRoleHp", "Modifica gli HP di un ruolo", "Combattimento", "♥", "Modifica di {amount} gli HP di {role} tramite Alpha ABS.");
        yield return new("combat.changeRoleHp", "Modifica gli HP di un ruolo", "Combattimento", "♥", "Modifica di {amount} gli HP di {role}.");
        yield return new("cleanupDamageWindow", "Ripulisce la finestra di danno", "Meccanica", "×", "Chiude e ripulisce la finestra di danno della fase precedente.");
        yield return new("completeWipeCycle", "Completa il ciclo wipe", "Meccanica", "✓", "Conclude il ciclo corrente della meccanica wipe.");
    }

    private static string HumanizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "azione";
        }

        var leaf = value.Split('.').Last();
        var words = System.Text.RegularExpressions.Regex.Replace(leaf, "([a-z0-9])([A-Z])", "$1 $2")
            .Replace('_', ' ')
            .Replace('-', ' ');
        return char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }

    private static string RoleLabel(string? role) => role?.ToLowerInvariant() switch
    {
        "boss" => "il boss",
        null or "" => "l'utilizzatore",
        _ => $"il ruolo “{role}”",
    };

    private static string StringAt(IReadOnlyList<JsonElement> values, int index) =>
        index < values.Count
            ? values[index].ValueKind == JsonValueKind.String
                ? values[index].GetString() ?? string.Empty
                : Summarize(values[index])
            : string.Empty;

    private static string ValueAt(IReadOnlyList<JsonElement> values, int index) =>
        index < values.Count ? Summarize(values[index]) : "?";

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.TryGetInt32(out var result)
            ? result
            : null;

    private static string Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : "?";

    private static string Summarize(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => element.GetRawText(),
    };

    private sealed record ActionDescriptor(
        string Id,
        string DisplayName,
        string Category,
        string IconGlyph,
        string DescriptionTemplate);
}

internal sealed record ActionSemantics(
    string DisplayName,
    string Description,
    string Category,
    string IconGlyph,
    IReadOnlyList<string> Reads,
    IReadOnlyList<string> Writes);
