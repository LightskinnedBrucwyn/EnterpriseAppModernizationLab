namespace BatHouseholdHub.Services;

/// <summary>Tracks which household member's view is active (the M/J avatar chips in the top nav)
/// and which profiles have been unlocked with their PIN. Scoped per circuit, so each open
/// tab/device picks its own person and unlocking on one phone never unlocks another.</summary>
public class ActivePersonService(HouseholdStore store)
{
    private readonly HashSet<string> _unlocked = new(StringComparer.OrdinalIgnoreCase);

    public string Current { get; private set; } = "Trey";
    public event Action? Changed;

    /// <summary>A profile with no PIN is always unlocked; one with a PIN needs Unlock() first.</summary>
    public bool IsUnlocked(string person) => !store.HasPin(person) || _unlocked.Contains(person);

    public bool Unlock(string person, string pin)
    {
        if (!store.VerifyPin(person, pin)) return false;
        _unlocked.Add(person);
        return true;
    }

    public void SetActive(string person)
    {
        if (Current == person) return;
        Current = person;
        Changed?.Invoke();
    }
}
