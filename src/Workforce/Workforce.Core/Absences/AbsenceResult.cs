namespace Workforce.Core.Absences;

/// <summary>Business errors of the absence use cases (mapped to HTTP statuses by the API; never carry personal data).</summary>
public enum AbsenceError
{
    None = 0,
    UnknownPerson = 1,
    NotFound = 2,
    InvalidPeriod = 3,
    AfterExit = 4,
    Overlap = 5,
    NoEntitlement = 6,
    InsufficientBalance = 7,
    NoApprover = 8,

    /// <summary>The absence is not in a state that allows this (e.g. deciding an already decided request).</summary>
    InvalidState = 9,

    /// <summary>The caller is not the person who may do this (not in the approval chain, not the requester).</summary>
    NotAllowed = 10,

    AlreadyStarted = 11,

    /// <summary>The file is empty or larger than allowed.</summary>
    FileTooLarge = 12,

    /// <summary>The content is not a PDF, JPEG or PNG.</summary>
    UnsupportedFileType = 13,

    /// <summary>The sick note has as many documents as allowed.</summary>
    TooManyDocuments = 14,

    /// <summary>The malware check failed or found something.</summary>
    FileRejected = 15,

    /// <summary>The retention period is outside the allowed range.</summary>
    InvalidRetention = 16,
    InvalidReminderSettings = 17,
    InvalidVacationRules = 18,
    WaitingPeriod = 19,
}

public sealed record AbsenceResult(AbsenceError Error, Absence? Absence = null)
{
    public static AbsenceResult Ok(Absence absence) => new(AbsenceError.None, absence);

    public static AbsenceResult Fail(AbsenceError error) => new(error);
}
