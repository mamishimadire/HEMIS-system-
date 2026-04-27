SELECT TOP 5 RunID, IsCurrent, Status, FailCount, TotalRecords, DeceasedColumn, LEFT(CAST(ResultsJSON AS nvarchar(max)), 500) AS ResultsPrefix
FROM [HEMISBaseSystem].[dbo].[ValidationRuns]
WHERE RuleNumber = 29
ORDER BY RunID DESC;
