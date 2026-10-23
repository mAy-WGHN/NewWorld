namespace BcIntegration.Core.MasterData;

public static class StoreChangeDetector
{
    /// <summary>Real change only: compares all fields, opening hours by content.</summary>
    public static bool HasChanged(StoreState? previous, StoreState current) =>
        previous is null
        || previous.StoreNumber != current.StoreNumber
        || previous.Name != current.Name
        || previous.Address != current.Address
        || previous.IsActive != current.IsActive
        || !previous.OpeningHours.SequenceEqual(current.OpeningHours);
}
