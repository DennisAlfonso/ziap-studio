using System.Text.Json.Nodes;
using ZiapStudio.Core.Editing;
using ZiapStudio.Core.Fusion.Weapons;

namespace ZiapStudio.Services.Fusion.Weapons;

public sealed class WeaponCreationService
{
    private readonly WeaponAdvancedNoteEditor _noteEditor;

    public WeaponCreationService(WeaponAdvancedNoteEditor? noteEditor = null)
    {
        _noteEditor = noteEditor ?? new WeaponAdvancedNoteEditor();
    }

    public int? FindAvailableSlot(DocumentEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.WorkingState is not JsonArray records)
        {
            return null;
        }

        return records
            .OfType<JsonObject>()
            .Where(IsAvailableSlot)
            .Select(record => ReadInteger(record, "id"))
            .Where(id => id > 0)
            .Order()
            .Cast<int?>()
            .FirstOrDefault();
    }

    public int Create(DocumentEditSession session, WeaponCreationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(draft);
        if (!session.Definition.ResourceName.Equals("weapons", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La creazione armi richiede il documento Weapons.json.");
        }

        var family = Validate(draft);
        var recordId = FindAvailableSlot(session) ??
            throw new InvalidOperationException(
                "Weapons.json non contiene slot liberi. Aumenta il massimo nel database RPG Maker e riapri il progetto.");
        var note = BuildNote(draft, family);
        var traits = new JsonArray
        {
            new JsonObject
            {
                ["code"] = 31,
                ["dataId"] = draft.AttackElementId,
                ["value"] = 0,
            },
            new JsonObject
            {
                ["code"] = 35,
                ["dataId"] = draft.AttackSkillId,
                ["value"] = 1,
            },
        };

        session.SetValue(recordId, "name", JsonValue.Create(draft.Name.Trim()));
        session.SetValue(recordId, "description", JsonValue.Create(draft.Description.Trim()));
        session.SetValue(recordId, "wtypeId", JsonValue.Create(draft.WeaponTypeId));
        session.SetValue(recordId, "etypeId", JsonValue.Create(draft.EquipTypeId));
        session.SetValue(recordId, "iconIndex", JsonValue.Create(draft.IconIndex));
        session.SetValue(recordId, "animationId", JsonValue.Create(draft.AnimationId));
        session.SetValue(recordId, "price", JsonValue.Create(draft.Price));
        session.SetValue(recordId, "params[2]", JsonValue.Create(draft.Power));
        session.SetValue(recordId, "traits", traits);
        session.SetValue(recordId, "note", JsonValue.Create(note));
        return recordId;
    }

    public void SetAttackSkill(DocumentEditSession session, int recordId, int skillId) =>
        SetTrait(session, recordId, 35, skillId, 1);

    public void SetAttackElement(DocumentEditSession session, int recordId, int elementId) =>
        SetTrait(session, recordId, 31, elementId, 0);

    private string BuildNote(WeaponCreationDraft draft, WeaponFamilyDefinition family)
    {
        var note = _noteEditor.ApplyFamilyClassification(string.Empty, family.Id);
        note = _noteEditor.SetWeaponSubtype(note, draft.Subtype);
        note = _noteEditor.SetHandedness(note, draft.Handedness);
        note = _noteEditor.SetPerks(note, draft.Perks);
        note = _noteEditor.SetRarity(note, draft.Rarity);
        note = _noteEditor.SetRequiredLevel(note, draft.RequiredLevel);
        if (draft.OverrideDamageFormula)
        {
            note = _noteEditor.SetDamageRate(note, draft.DamageRate);
            note = _noteEditor.SetFlatDamage(note, draft.FlatDamage);
            note = _noteEditor.SetDefenseRate(note, draft.DefenseRate);
        }
        if (draft.OverrideAttackBehavior)
        {
            note = _noteEditor.SetAttackInterval(note, draft.AttackInterval);
            note = _noteEditor.SetAttackRange(note, draft.AttackRange);
            note = _noteEditor.SetAttackRadius(note, draft.AttackRadius);
            note = _noteEditor.SetProjectileSpeed(note, draft.ProjectileSpeed);
            note = _noteEditor.SetProjectileColliderRadius(note, draft.ProjectileColliderRadius);
        }
        if (family.IsFirearm)
        {
            note = _noteEditor.SetMagazineSize(note, draft.MagazineSize);
            note = _noteEditor.SetReloadDuration(note, draft.ReloadDuration);
            note = _noteEditor.SetFirearmAccuracy(note, draft.Accuracy);
            note = _noteEditor.SetFirearmStability(note, draft.Stability);
            note = _noteEditor.SetFirearmHandling(note, draft.Handling);
            note = _noteEditor.SetFirearmAimMinimumDistance(note, draft.AimMinimumDistance);
            note = _noteEditor.SetFirearmAimMaximumDistance(note, draft.AimMaximumDistance);
            note = _noteEditor.SetFirearmAimMovementMultiplier(note, draft.AimMovementMultiplier);
        }

        return _noteEditor.AddDisassemblyResult(
            note,
            draft.DisassemblyResource,
            draft.DisassemblyMinimum,
            draft.DisassemblyMaximum,
            100);
    }

    private static WeaponFamilyDefinition Validate(WeaponCreationDraft draft)
    {
        var family = WeaponAuthoringSchema.FindFamily(draft.Family) ??
            throw new ArgumentException($"Famiglia arma sconosciuta: {draft.Family}.", nameof(draft));
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            throw new ArgumentException("Il nome dell'arma è obbligatorio.", nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.Subtype))
        {
            throw new ArgumentException("Il sottotipo dell'arma è obbligatorio.", nameof(draft));
        }

        if (family.IsFirearm && !WeaponAuthoringSchema.IsKnownFirearmSubtype(draft.Subtype))
        {
            throw new ArgumentException($"Sottotipo firearm sconosciuto: {draft.Subtype}.", nameof(draft));
        }

        if (draft.Handedness is < 1 or > 2 || draft.WeaponTypeId <= 0 || draft.EquipTypeId <= 0 ||
            draft.IconIndex < 0 || draft.AnimationId < 0 || draft.Price < 0 || draft.AttackSkillId <= 0 ||
            draft.AttackElementId < 0 || draft.Rarity is < 0 or > 7 || draft.RequiredLevel <= 0 ||
            draft.Perks.Count != 3 || draft.DamageRate < 0 || draft.DefenseRate < 0 ||
            draft.AttackInterval <= 0 || draft.AttackRange <= 0 || draft.AttackRadius < 0 ||
            draft.ProjectileSpeed < 0 || draft.ProjectileColliderRadius <= 0)
        {
            throw new ArgumentException("Lo schema arma contiene valori comuni non validi.", nameof(draft));
        }

        if (family.IsFirearm &&
            (draft.MagazineSize <= 0 || draft.ReloadDuration < 0 ||
             draft.Accuracy is < 0 or > 100 || draft.Stability is < 0 or > 100 ||
             draft.Handling is < 0 or > 100 || draft.AimMinimumDistance < 1 ||
             draft.AimMaximumDistance < draft.AimMinimumDistance ||
             draft.AimMovementMultiplier is <= 0 or > 1))
        {
            throw new ArgumentException("Lo schema firearm contiene valori non validi.", nameof(draft));
        }

        return family;
    }

    private static void SetTrait(
        DocumentEditSession session,
        int recordId,
        int code,
        int dataId,
        double value)
    {
        var traits = session.TryGetValue(recordId, "traits", out var node) && node is JsonArray array
            ? array.DeepClone().AsArray()
            : [];
        var trait = traits.OfType<JsonObject>().FirstOrDefault(candidate =>
            ReadInteger(candidate, "code") == code);
        if (trait is null)
        {
            traits.Add(new JsonObject
            {
                ["code"] = code,
                ["dataId"] = dataId,
                ["value"] = value,
            });
        }
        else
        {
            trait["dataId"] = dataId;
            trait["value"] = value;
        }

        session.SetValue(recordId, "traits", traits);
    }

    private static bool IsAvailableSlot(JsonObject record) =>
        ReadInteger(record, "id") > 0 &&
        ReadInteger(record, "wtypeId") == 0 &&
        string.IsNullOrWhiteSpace(record["name"]?.GetValue<string>());

    private static int ReadInteger(JsonObject record, string key) =>
        record[key] is JsonValue value && value.TryGetValue<int>(out var result)
            ? result
            : 0;
}
