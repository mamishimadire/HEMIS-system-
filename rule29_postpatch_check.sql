SELECT TOP 3 RunID, IsCurrent, Status, DeceasedColumn
FROM [HEMISBaseSystem].[dbo].[ValidationRuns]
WHERE RuleNumber = 29
ORDER BY RunID DESC;
