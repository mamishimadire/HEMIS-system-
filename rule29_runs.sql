SELECT TOP 5 RunID, ClientID, AuditDatabase, HemisServer, StudTable, DeceasedTable, StudColumn, DeceasedColumn, Status
FROM [HEMISBaseSystem].[dbo].[ValidationRuns]
WHERE RuleNumber = 29
ORDER BY RunID DESC;
