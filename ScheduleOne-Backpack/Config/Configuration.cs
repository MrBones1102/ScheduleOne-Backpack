using MelonLoader;
using UnityEngine;

#if IL2CPP
using Il2CppScheduleOne.Levelling;
#elif MONO
using ScheduleOne.Levelling;
#endif

namespace Backpack.Config;

public class Configuration
{
    private static Configuration _instance;
    public static Configuration Instance => _instance ??= new Configuration();

    private readonly string _configFile = Path.Combine("UserData", "Backpack.cfg");

    private readonly MelonPreferences_Category _generalCategory;
    private readonly MelonPreferences_Entry<KeyCode> _toggleKeyEntry;
    private readonly MelonPreferences_Entry<bool> _enableSearchEntry;
    private readonly MelonPreferences_Entry<bool> _enableSellingEntry;

    private readonly MelonPreferences_Category _backpackCategory;
    // FullRank (a struct) can't be shown by in-game mod menus, so rank and tier are separate entries.
    private readonly MelonPreferences_Entry<ERank> _unlockRankEntry;
    private readonly MelonPreferences_Entry<int> _unlockTierEntry;
    private readonly MelonPreferences_Entry<int> _storageSlotsEntry;

    public Configuration()
    {
        _generalCategory = MelonPreferences.CreateCategory("General");
        _generalCategory.SetFilePath(_configFile, false);
        _toggleKeyEntry = _generalCategory.CreateEntry("ToggleKey", KeyCode.B, "Key to toggle backpack");
        _enableSearchEntry = _generalCategory.CreateEntry("EnableSearch", false, "Enable police search for backpack items");
        _enableSellingEntry = _generalCategory.CreateEntry("EnableSelling", true, "Allow handing over items from the backpack in deals (in multiplayer, the host can disable this for everyone)");

        _backpackCategory = MelonPreferences.CreateCategory("Backpack");
        _backpackCategory.SetFilePath(_configFile, false);
        _unlockRankEntry = _backpackCategory.CreateEntry("UnlockRank", ERank.Hoodlum, "Required rank to unlock (Street_Rat = available from the start)");
        _unlockTierEntry = _backpackCategory.CreateEntry("UnlockTier", 1, "Required tier within the rank to unlock (1-5)");
        _storageSlotsEntry = _backpackCategory.CreateEntry("StorageSlots", 12, "Number of total storage slots");
    }

    public KeyCode ToggleKey { get; set; }
    public bool EnableSearch { get; set; }

    /// <summary>
    /// Local preference, read live so toggling it in a mod menu applies to the next deal without a reload.
    /// </summary>
    public bool EnableSelling => _enableSellingEntry.Value;

    /// <summary>
    /// Set from the host's config in multiplayer; always true in single-player and on the host.
    /// </summary>
    public bool HostAllowsSelling { get; internal set; } = true;

    public bool CanSellFromBackpack => EnableSelling && HostAllowsSelling;
    public FullRank UnlockLevel { get; internal set; }
    public int StorageSlots { get; internal set; }

    public void Load()
    {
        MelonPreferences.Load();
        Reset();
    }

    public void Reset()
    {
        ToggleKey = _toggleKeyEntry.Value;
        EnableSearch = _enableSearchEntry.Value;
        HostAllowsSelling = true;
        UnlockLevel = new FullRank(_unlockRankEntry.Value, Math.Clamp(_unlockTierEntry.Value, 1, 5));
        StorageSlots = Math.Clamp(_storageSlotsEntry.Value, 1, PlayerBackpack.MaxStorageSlots);
    }

    public void Save()
    {
        _toggleKeyEntry.Value = ToggleKey;
        _enableSearchEntry.Value = EnableSearch;
        _unlockRankEntry.Value = UnlockLevel.Rank;
        _unlockTierEntry.Value = Math.Clamp(UnlockLevel.Tier, 1, 5);
        _storageSlotsEntry.Value = Math.Clamp(StorageSlots, 1, PlayerBackpack.MaxStorageSlots);
        MelonPreferences.Save();
    }
}