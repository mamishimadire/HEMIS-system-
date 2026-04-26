namespace HemisAudit.ViewModels
{
    public class Rule34HolidayLoadRequest
    {
        public int StartYear { get; set; }
        public int EndYear { get; set; }
    }

    public class Rule34HolidayItemViewModel
    {
        public string Date { get; set; } = "";
        public string Name { get; set; } = "";
        public string Source { get; set; } = "";
    }

    public class Rule34HolidayLoadResult
    {
        public bool Success { get; set; }
        public int StartYear { get; set; }
        public int EndYear { get; set; }
        public int TotalCount { get; set; }
        public List<Rule34HolidayItemViewModel> Holidays { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule34GetColumnsRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
    }

    public class Rule34ColumnSelectionResult
    {
        public bool Success { get; set; }
        public List<string> Columns { get; set; } = new();
        public string? AutoFirstDayColumn { get; set; }
        public string? AutoLastDayColumn { get; set; }
        public string? AutoCensusDateColumn { get; set; }
        public string? Error { get; set; }
    }

    public class Rule34VerifyRequest
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "";
        public string TableName { get; set; } = "";
        public string FirstDayColumn { get; set; } = "";
        public string LastDayColumn { get; set; } = "";
        public string CensusDateColumn { get; set; } = "";
    }

    public class Rule34SampleRowViewModel
    {
        public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class Rule34VerifyResult
    {
        public bool Success { get; set; }
        public int TotalRecords { get; set; }
        public List<Rule34SampleRowViewModel> SampleRows { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule34ValidationRequest
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string FirstDayColumn { get; set; } = "";
        public string LastDayColumn { get; set; } = "";
        public string CensusDateColumn { get; set; } = "";
        public int StartYear { get; set; }
        public int EndYear { get; set; }
    }

    public class Rule34ValidationRowRecord
    {
        public int ValidationNumber { get; set; }
        public string FirstDayValue { get; set; } = "";
        public string LastDayValue { get; set; } = "";
        public int? CurrentDays { get; set; }
        public decimal? CurrentDaysHalf { get; set; }
        public string ComputedCensusDate { get; set; } = "";
        public string ActualCensusDate { get; set; } = "";
        public string CensusDateValue { get; set; } = "";
        public string DayStatus { get; set; } = "";
        public bool ComparisonResult { get; set; }
        public bool DateMatch { get; set; }
        public string ValidationStatus { get; set; } = "";
    }

    public class Rule34ValidationSummary
    {
        public bool Success { get; set; }
        public int TotalValidated { get; set; }
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public decimal ExceptionRate { get; set; }
        public string Status { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public string Database { get; set; } = "";
        public string TableName { get; set; } = "";
        public string FirstDayColumn { get; set; } = "";
        public string LastDayColumn { get; set; } = "";
        public string CensusDateColumn { get; set; } = "";
        public int StartYear { get; set; }
        public int EndYear { get; set; }
        public string HolidayYearRange { get; set; } = "";
        public int HolidayCount { get; set; }
        public int WeekendCount { get; set; }
        public int ClientId { get; set; }
        public int? SavedRunId { get; set; }
        public List<Rule34HolidayItemViewModel> Holidays { get; set; } = new();
        public List<Rule34ValidationRowRecord> ValidationRows { get; set; } = new();
        public List<Rule34ValidationRowRecord> Exceptions { get; set; } = new();
        public string? Error { get; set; }
    }

    public class Rule34RunReviewViewModel
    {
        public int RunId { get; set; }
        public int ClientId { get; set; }
        public string EngagementName { get; set; } = "";
        public string MaconomyNumber { get; set; } = "";
        public Rule34ValidationSummary Summary { get; set; } = new();
        public List<RunSignoffViewModel> Signoffs { get; set; } = new();
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff =>
            !string.IsNullOrWhiteSpace(CurrentUserEngagementRole) &&
            Signoffs.Any(s =>
                s.IsCurrentUser &&
                string.Equals(s.SignoffRole, CurrentUserEngagementRole, StringComparison.OrdinalIgnoreCase));
        public bool CanCurrentUserSignOff =>
            string.Equals(CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CurrentUserEngagementRole, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CurrentUserEngagementRole, "Director", StringComparison.OrdinalIgnoreCase);
        public bool CanCurrentUserDownload =>
            string.Equals(CurrentUserEngagementRole, "DataAnalyst", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CurrentUserEngagementRole, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CurrentUserEngagementRole, "Director", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CurrentUserEngagementRole, "Trainee", StringComparison.OrdinalIgnoreCase);
    }

    public class Rule34WorkspaceStateViewModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public bool ResultsVisible { get; set; }
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Driver { get; set; } = "ODBC Driver 17 for SQL Server";
        public string TableName { get; set; } = "";
        public string FirstDayColumn { get; set; } = "";
        public string LastDayColumn { get; set; } = "";
        public string CensusDateColumn { get; set; } = "";
        public int StartYear { get; set; }
        public int EndYear { get; set; }
        public string CurrentUserEngagementRole { get; set; } = "";
        public bool HasDataAnalystSignoff { get; set; }
        public bool CurrentUserHasSignedOff { get; set; }
        public string CurrentUserSignoffComment { get; set; } = "";
        public string CurrentStatus { get; set; } = "";
        public string? LastEditedByUserName { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public Rule34ValidationSummary? Summary { get; set; }
    }

    public class Rule34WorkspaceSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool SignoffsCleared { get; set; }
        public int? ClearedSignoffCount { get; set; }
        public Rule34WorkspaceStateViewModel? Workspace { get; set; }
        public string? Error { get; set; }
    }

    public class Rule34RunSignoffInputModel
    {
        public int RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule34WorkspaceSignoffInputModel
    {
        public int ClientId { get; set; }
        public int? RunId { get; set; }
        public string Comment { get; set; } = "";
    }

    public class Rule34SqlResult
    {
        public bool Success { get; set; }
        public string Sql { get; set; } = "";
        public string? Error { get; set; }
    }
}
