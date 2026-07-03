namespace BatHouseholdHub.Components.Bills;

/// <summary>Feedback surfaced in the Bills page toast after a tab component changes data.
/// Undo, when present, restores whatever the action removed.</summary>
public record Toast(string Message, Func<Task>? Undo = null);
