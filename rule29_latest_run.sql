SELECT TOP 1 RunID, IsCurrent, Status, FailCount, TotalRecords, DeceasedColumn
FROM [HEMISBaseSystem].[dbo].[ValidationRuns]
WHERE RuleNumber = 29
ORDER BY RunID DESC;
