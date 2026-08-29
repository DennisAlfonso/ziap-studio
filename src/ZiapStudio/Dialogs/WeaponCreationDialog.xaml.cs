using Microsoft.UI.Xaml.Controls;
using ZiapStudio.Core.Fusion.Weapons;

namespace ZiapStudio.Dialogs;

public sealed partial class WeaponCreationDialog : ContentDialog
{
    private static readonly IReadOnlyList<WeaponChoice> HandednessOptions =
    [new(1, "Una mano"), new(2, "Due mani")];

    private static readonly IReadOnlyList<WeaponChoice> RarityOptions =
    [
        new(0, "Comune"), new(1, "Non comune"), new(2, "Rara"),
        new(3, "Leggendaria"), new(4, "Mitologica"), new(5, "Definitiva"),
        new(6, "Utopica"), new(7, "Progetto"),
    ];

    private readonly WeaponNotetagCatalog _catalog;

    public WeaponCreationDialog(WeaponNotetagCatalog? catalog = null)
    {
        InitializeComponent();
        _catalog = catalog ?? WeaponNotetagCatalog.Empty;
        FamilyBox.ItemsSource = WeaponAuthoringSchema.Families;
        HandednessBox.ItemsSource = HandednessOptions;
        RarityBox.ItemsSource = RarityOptions;
        WeaponTypeBox.ItemsSource = _catalog.WeaponTypes.Count > 0
            ? _catalog.WeaponTypes
            : [new WeaponDatabaseOption(1, "Arma"), new WeaponDatabaseOption(3, "Arma da fuoco")];
        AttackElementBox.ItemsSource = _catalog.Elements.Count > 0
            ? _catalog.Elements
            : [new WeaponDatabaseOption(1, "Fisico")];
        AttackSkillBox.ItemsSource = _catalog.AttackSkills.Count > 0
            ? _catalog.AttackSkills
            : [new WeaponAttackSkillOption(343, "Colpo pistola", 0.35, 10, 0.2, 8, null)];
        FamilyBox.SelectedItem = WeaponAuthoringSchema.FindFamily("firearm");
        RarityBox.SelectedIndex = 0;
        SelectById(AttackElementBox, 1);
        SelectById(AttackSkillBox, 343);
    }

    public WeaponCreationDraft? Draft { get; private set; }

    private void FamilyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FamilyBox.SelectedItem is not WeaponFamilyDefinition family)
        {
            return;
        }

        SubtypeBox.ItemsSource = family.IsFirearm
            ? WeaponAuthoringSchema.FirearmSubtypes
            : [new WeaponSubtypeDefinition(family.DefaultSubtype, family.DisplayName)];
        SubtypeBox.SelectedIndex = 0;
        SelectById(HandednessBox, family.DefaultHandedness);
        SelectById(WeaponTypeBox, family.IsFirearm ? 3 : 1);
        if (family.IsFirearm)
        {
            SelectById(AttackSkillBox, 343);
        }
    }

    private void ContentDialog_PrimaryButtonClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        try
        {
            var family = Required<WeaponFamilyDefinition>(FamilyBox, "famiglia");
            var subtype = Required<WeaponSubtypeDefinition>(SubtypeBox, "sottotipo");
            var handedness = Required<WeaponChoice>(HandednessBox, "impugnatura");
            var weaponType = Required<WeaponDatabaseOption>(WeaponTypeBox, "tipo arma");
            var element = Required<WeaponDatabaseOption>(AttackElementBox, "elemento");
            var skill = Required<WeaponAttackSkillOption>(AttackSkillBox, "abilità attacco");
            var rarity = Required<WeaponChoice>(RarityBox, "rarità");
            if (string.IsNullOrWhiteSpace(WeaponNameBox.Text))
            {
                throw new InvalidOperationException("Inserisci il nome dell'arma.");
            }

            Draft = family.IsFirearm
                ? WeaponCreationDraft.Firearm(WeaponNameBox.Text.Trim(), DescriptionBox.Text.Trim())
                : new WeaponCreationDraft
                {
                    Name = WeaponNameBox.Text.Trim(),
                    Description = DescriptionBox.Text.Trim(),
                    Family = family.Id,
                };
            Draft = Draft with
            {
                Subtype = subtype.Id,
                Handedness = handedness.Id,
                WeaponTypeId = weaponType.Id,
                IconIndex = Integer(IconBox),
                AttackSkillId = skill.Id,
                AttackElementId = element.Id,
                Rarity = rarity.Id,
                RequiredLevel = Integer(RequiredLevelBox),
            };
        }
        catch (Exception exception)
        {
            args.Cancel = true;
            ValidationInfoBar.Message = exception.Message;
            ValidationInfoBar.IsOpen = true;
        }
    }

    private static T Required<T>(ComboBox box, string label) where T : class =>
        box.SelectedItem as T ?? throw new InvalidOperationException($"Seleziona {label}.");

    private static void SelectById(ComboBox box, int id)
    {
        box.SelectedItem = box.Items.Cast<object>().FirstOrDefault(item => item switch
        {
            WeaponDatabaseOption option => option.Id == id,
            WeaponAttackSkillOption option => option.Id == id,
            WeaponChoice option => option.Id == id,
            _ => false,
        });
        if (box.SelectedItem is null && box.Items.Count > 0)
        {
            box.SelectedIndex = 0;
        }
    }

    private static int Integer(NumberBox box)
    {
        if (double.IsNaN(box.Value) || double.IsInfinity(box.Value) ||
            box.Value != Math.Truncate(box.Value) || box.Value is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidOperationException($"{box.Header}: inserisci un numero intero valido.");
        }

        return checked((int)box.Value);
    }

    private sealed record WeaponChoice(int Id, string DisplayName);
}
