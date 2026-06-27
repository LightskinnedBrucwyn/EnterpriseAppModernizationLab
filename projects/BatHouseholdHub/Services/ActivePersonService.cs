namespace BatHouseholdHub.Services;

/// <summary>Tracks which household member's view is active (the M/J avatar chips in the top nav).
/// Scoped per circuit so each open tab/device can pick its own person.</summary>
public class ActivePersonService
{
    public string Current { get; private set; } = "Trey";
    public event Action? Changed;

    public void SetActive(string person)
    {
        if (Current == person) return;
        Current = person;
        Changed?.Invoke();
    }
}
