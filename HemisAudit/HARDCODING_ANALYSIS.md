# HemisAudit Codebase: Hardcoded Filter Values Analysis

**Date**: June 1, 2026  
**Scope**: Services, Controllers, Views, Models folders  
**Focus**: Hardcoded filter values, configurable filtering, PQM column mappings

---

## Executive Summary

### Critical Issues Found
1. **Rule 55**: Approval status value `'A'` is **hardcoded** in SQL generation
2. **Rule 16**: Student fulfilled status `'N'` is **hardcoded** (6+ occurrences)
3. **Rules 14/15**: Approval status `'A'` is **hardcoded** for CRSE and QUAL
4. **Rules 24/25/38**: Column name `_004` is hardcoded as `ApprovalStatusColumn`

### Best Practice Pattern Found
**Rules 21, 27, 17** implement proper configurable filtering with dynamic SQL predicates

### Rule 54 Status
Rule 54 is **NOT problematic** - it uses in-memory validation, not WHERE clause filtering

---

## Detailed Findings

### 1. RULES WITH HARDCODED FILTER VALUES

#### Rule 55 - Graduate W-Code Validation ⚠️ CRITICAL

**Files**:
- [Rule55Service.cs](HemisAudit/Services/Rule55Service.cs#L650-L770) - SQL generation
- [Rule55ViewModels.cs](HemisAudit/ViewModels/Rule55ViewModels.cs#L24)
- [Rule55/Index.cshtml](HemisAudit/Views/Rule55/Index.cshtml#L80) - Configuration UI

**The Problem**:
The filter value for QUAL approval status is configurable (`StudFulfilledFilterValue` = "W"), BUT the approval status comparison value `'A'` is **hardcoded in SQL**.

**Hardcoded Locations**:
```csharp
// Line 744: Hardcoded 'A' in summary calculation
SUM(CASE WHEN q.[{qc}] IS NOT NULL AND 
    UPPER(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(10))))) = 'A' 
    THEN 1 ELSE 0 END) AS Pass_Count,

// Line 727: Hardcoded 'A' in validation logic
WHEN UPPER(LTRIM(RTRIM(CAST(q.[{qa}] AS NVARCHAR(10))))) <> 'A'
    THEN 'FAIL – Qualification not approved ({qa} <> A)'
```

**Impact**:
- User cannot change the approval requirement from 'A' to another value (e.g., 'P', 'C')
- If DHET changes approval status convention, code must be modified

**What IS Configurable**:
- `StudFulfilledFilterValue` (default "W") - what status values to filter on
- `QualApprovalCol` - which column contains approval status
- All table and column mappings

**What IS NOT Configurable**:
- The VALUE to compare approval status against (hardcoded as 'A')

**Required Fix**:
Add `QualApprovalValueFilter` to Rule55ValidationRequest and use it in GenerateSql()

---

#### Rule 16 - Active/Discontinued Students ⚠️ HARDCODED

**Files**:
- [Rule16Service.cs](HemisAudit/Services/Rule16Service.cs#L612-L926) - Multiple hardcoded WHERE clauses

**Hardcoded Pattern**:
```sql
WHERE ISNULL(S._025, '') = 'N'  -- Hard-coded in 6+ places
```

**Occurrences**:
- Line 612: CASE WHEN condition
- Line 618: WHERE clause
- Line 627, 635, 646, 654: Additional WHERE clauses
- Lines 855-926: Multiple internal validation checks

**Problem**:
- No dashboard UI to change the filter value
- Cannot filter on 'W', 'F', or other values without code changes

**Missing Implementation**:
- No `GetFilterValuesAsync()` method
- No filter column selection in Views
- No dynamic WHERE clause generation

---

#### Rule 14/15 - Approved Courses/Credentials ⚠️ HARDCODED

**Files**:
- [Rule14Service.cs](HemisAudit/Services/Rule14Service.cs#L635-L926)
- [Rule15Service.cs](HemisAudit/Services/Rule15Service.cs#L610-L899)

**Hardcoded Values**:
```sql
WHERE ISNULL(CAST(CRSE.[_031] AS nvarchar(255)), '') = 'A'  -- Line 644
WHERE ISNULL(CONVERT(nvarchar(255), QUAL.[_004]), '') = 'A'  -- Line 850, 862
```

**Impact**:
- Tests 100% of approved credentials/qualifications only
- Cannot test other approval statuses without code modifications
- No configuration UI

---

#### Rules 24/25/38 - Column Hardcoding ⚠️ PARTIAL

**Files**:
- [Rule24ViewModels.cs](HemisAudit/ViewModels/Rule24ViewModels.cs#L40)
- [Rule38ViewModels.cs](HemisAudit/ViewModels/Rule38ViewModels.cs#L21)

**Hardcoded Default**:
```csharp
public string ApprovalStatusColumn { get; set; } = "_004";
```

**Problem**:
- Default assumes approval status is in `_004`
- No UI to change this to `CESM_Code` or other columns
- Only default, can be changed in code

**Current State**:
- Model allows change but no dashboard UI exposed

---

### 2. RULES WITH PROPER CONFIGURABLE FILTERING ✓

#### Rule 21 - First Time Entering Students (BEST PRACTICE)

**Files**:
- [Rule21Service.cs](HemisAudit/Services/Rule21Service.cs#L108-L700)
- [Rule21Controller.cs](HemisAudit/Controllers/Rule21Controller.cs#L187-L200)
- [Rule21/Index.cshtml](HemisAudit/Views/Rule21/Index.cshtml#L150-L200)

**Implementation Pattern**:

1. **GetFilterValuesAsync()** (Line 108-155):
   ```csharp
   // Queries distinct values from database for user selection
   SELECT TOP 20
       LTRIM(RTRIM(CAST([{safeColumn}] AS nvarchar(4000)))) AS FilterValue,
       COUNT(*) AS RecordCount
   FROM [{safeTable}]
   WHERE [{safeColumn}] IS NOT NULL
   ```

2. **GenerateSqlAsync()** (Line 629-700):
   ```csharp
   // Builds dynamic WHERE clause with parameterized values
   var sqlFilterPredicate = $"({trimmedExpression} IN ({string.Join(", ", rawParameterNames)}) 
                            OR {normalizedExpression} IN ({string.Join(", ", normalizedParameterNames)}))";
   
   // Uses in SQL:
   WHERE {sqlFilterPredicate}
   ```

3. **BuildFilterPredicate()** (Line 887):
   ```csharp
   // Creates SQL parameter names dynamically
   var rawParameterNames = new List<string>();
   for (int i = 0; i < rawValues.Count; i++)
   {
       var paramName = $"@rawValue{i}";
       command.Parameters.AddWithValue(paramName, rawValues[i]);
       rawParameterNames.Add(paramName);
   }
   ```

**Dashboard Flow**:
1. User selects Server → `GetDatabases()`
2. User selects Database → `GetTables()`
3. User selects Table → `GetColumns()`
4. User selects FilterColumn → `LoadFilterValues()` [gets distinct values]
5. User selects FilterValue → `BuildFilterPredicate()` 
6. User runs → `GenerateSqlAsync()` creates WHERE with dynamic values

**Key Files**:
- Controller: [LoadFilterValues endpoint](HemisAudit/Controllers/Rule21Controller.cs#L196-199)
- View: [Filter dropdown binding](HemisAudit/Views/Rule21/Index.cshtml#L530-L560)
- Service: [Dynamic SQL generation](HemisAudit/Services/Rule21Service.cs#L661-L680)

---

#### Rule 27 - Identical to Rule 21 ✓
- Same configurable filtering pattern
- [Rule27Service.cs](HemisAudit/Services/Rule27Service.cs#L137-L200)

#### Rule 17 - Similar to Rule 21 ✓
- Configurable filter values
- [Rule17Service.cs](HemisAudit/Services/Rule17Service.cs#L108-L200)

---

### 3. COLUMN MAPPING PATTERNS

#### Smart Auto-Detection (Rule 11) ✓

**Location**: [Rule11Service.cs](HemisAudit/Services/Rule11Service.cs#L195-L210)

**Pattern for PQM Code Column**:
```csharp
"pqm_code" => columns.FirstOrDefault(c => c.Equals("_004", StringComparison.OrdinalIgnoreCase)) 
             ?? columns.FirstOrDefault(),
```

**Advantages**:
- Auto-detects if `_004` exists
- Falls back to first column if `_004` not found
- Fully user-configurable

**Implementation**:
- Looks for specific column names first (intelligent defaults)
- Falls back gracefully
- User can override with dropdown selection

**Similar Patterns in**:
- [Rule11Service.cs](HemisAudit/Services/Rule11Service.cs#L205) - CESM code detection `_006`
- [Rule11Service.cs](HemisAudit/Services/Rule11Service.cs#L206-L207) - PQM HEQF type and name detection

---

#### PQM Column References in Codebase

**_004 References**:
- [ExportService.cs](HemisAudit/Services/ExportService.cs#L5181): `(10, "_004 (CESM Code)", pqmSub)`
- [Rule24ViewModels.cs](HemisAudit/ViewModels/Rule24ViewModels.cs#L40): Default `ApprovalStatusColumn = "_004"`
- [Rule38ViewModels.cs](HemisAudit/ViewModels/Rule38ViewModels.cs#L21): Default `QualApprovalCol = "_004"`
- [Rule10Service.cs](HemisAudit/Services/Rule10Service.cs#L2074): `2 => "_004"`

**CESM_Code References**:
- [Rule11Service.cs](HemisAudit/Services/Rule11Service.cs#L811): `c.[{cc}] AS CESM_Code`
- [ExportService.cs](HemisAudit/Services/ExportService.cs#L2733): CSV header `HEMIS_CESM_Code,HEMIS_Qual_Name,PQM_Code`

**Issue**: Inconsistent naming - some use `_004`, some use `CESM_Code`, some use `_006`

---

### 4. RULE 54 - NOT ACTUALLY PROBLEMATIC ✓

**Files**:
- [Rule54Service.cs](HemisAudit/Services/Rule54Service.cs)
- [Rule54Controller.cs](HemisAudit/Controllers/Rule54Controller.cs)
- [Rule54/Index.cshtml](HemisAudit/Views/Rule54/Index.cshtml)

**Rule 54 Purpose**:
Validates that CRED courses match their corresponding QUAL qualifications and both match PQM records by:
- Qualification Name matching (QUAL._003 vs PQM.Authorised_Qualification_Name)
- Research_1 value matching (CRED._050 vs PQM.Research_1)

**Why No WHERE Clause Hardcoding**:
- Rule 54 uses **in-memory validation** pattern
- Loads all CRED/QUAL/PQM records
- Compares them in C# using LINQ (not SQL WHERE)
- No database filtering - validates 100% of joined records

**Column Mappings** (All Configurable):
- CRED: CredIdCol (_001), CredCourseCol (_030), CredCreditCol (_036), CredResearch1Col (_050)
- QUAL: QualIdCol (_001), QualNameCol (_003)
- PQM: PqmNameCol (Authorised_Qualification_Name), PqmResearch1Col (Research_1)

**Configuration UI**:
- [Rule54/Index.cshtml](HemisAudit/Views/Rule54/Index.cshtml#L126-L171): Full column mapping dropdowns
- All columns are user-selectable
- Auto-detection with smart fallbacks

**Conclusion**: Rule 54 is **well-implemented** - no hardcoding issues

---

## Architecture Summary

### Filtering Architecture Pattern

```
┌─────────────────────────────────────────────────────────────┐
│ DASHBOARD VIEW (Rule21/Index.cshtml)                         │
│ - Database Connection Section                                │
│ - Table Selection (with auto-detection)                      │
│ - Filter Column Selection → LoadFilterValues API call        │
│ - Filter Value Selection → getFilterValuesAsync()            │
│ - Run Validation Button                                      │
└──────────────┬──────────────────────────────────────────────┘
               │
               ├─→ Rule21Controller.GetColumns()
               │     └─→ Rule21Service.GetColumnsAsync()
               │
               ├─→ Rule21Controller.LoadFilterValues()
               │     └─→ Rule21Service.GetFilterValuesAsync()
               │         └─→ SQL: SELECT DISTINCT [{column}] 
               │                 FROM [{table}]
               │
               └─→ Rule21Controller.RunValidationAsync()
                     └─→ Rule21Service.RunValidationAsync()
                         └─→ Rule21Service.GenerateSqlAsync()
                             └─→ SQL: WHERE {dynamicFilterPredicate}
                                 (uses parameterized values)
```

### Required Components for Configurable Filtering

1. **Controller Endpoints**:
   - `GetDatabases()`
   - `GetTables()`
   - `GetColumns()`
   - `LoadFilterValues()` - queries distinct values
   - `RunValidationAsync()`

2. **Service Methods**:
   - `GetDatabasesAsync()`
   - `GetTablesAsync()`
   - `GetColumnsAsync()` - with auto-selection logic
   - `GetFilterValuesAsync()` - queries database for distinct values
   - `GenerateSqlAsync()` - creates dynamic WHERE clauses
   - `BuildFilterPredicate()` - constructs parameterized WHERE condition

3. **ViewModels**:
   - `{Rule}ValidationRequest` - contains filter column and filter value fields
   - `{Rule}FilterValueResult` - contains list of available values with counts

4. **Views**:
   - Dropdowns for FilterColumn selection
   - Dropdowns for FilterValue selection
   - Event handlers to load values when column changes

---

## Recommendations

### Immediate Fixes (High Priority)

1. **Rule 55**: Add configurable approval status value
   - Add `QualApprovalValueFilter` property to Rule55ValidationRequest
   - Update GenerateSql() to use parameter instead of hardcoded 'A'
   - Update View to show approval value configuration

2. **Rule 16**: Implement filter configuration
   - Add `StudFulfilledFilterColumn` and `StudFulfilledFilterValue` to viewmodel
   - Copy Rule 21/27 pattern for dynamic WHERE generation
   - Add UI for filter column and value selection

3. **Rule 14/15**: Make approval status configurable
   - Add approval value configuration to viewmodels
   - Implement dynamic SQL generation similar to Rule 21

### Medium Priority Fixes

4. **Rules 24/25/38**: Expose column mapping UI
   - Add dropdowns for ApprovalStatusColumn selection
   - Allow users to choose `_004`, `CESM_Code`, or other columns

5. **Standardize Column Naming**:
   - Use consistent naming: `_004` for QUAL approval, clarify PQM column references
   - Document whether `_004` exists in PQM or if it's `CESM_Code`

### Architecture Improvement

6. **Create Reusable Filter Helper**:
   - Extract `BuildFilterPredicate()` pattern into shared utility
   - Reuse in Rules 16, 14, 15, 55 to avoid code duplication

---

## File Location Reference

### Services with Hardcoding Issues
- [Rule16Service.cs](HemisAudit/Services/Rule16Service.cs) - Multiple hardcoded 'N' values
- [Rule14Service.cs](HemisAudit/Services/Rule14Service.cs) - Hardcoded 'A' approval
- [Rule15Service.cs](HemisAudit/Services/Rule15Service.cs) - Hardcoded 'A' approval
- [Rule55Service.cs](HemisAudit/Services/Rule55Service.cs) - Hardcoded 'A' approval

### Services with Best Practice Implementation
- [Rule21Service.cs](HemisAudit/Services/Rule21Service.cs) - Dynamic filters ✓
- [Rule27Service.cs](HemisAudit/Services/Rule27Service.cs) - Dynamic filters ✓
- [Rule17Service.cs](HemisAudit/Services/Rule17Service.cs) - Dynamic filters ✓
- [Rule11Service.cs](HemisAudit/Services/Rule11Service.cs) - Smart column mapping ✓

### Controllers
- [Rule21Controller.cs](HemisAudit/Controllers/Rule21Controller.cs#L187-L200) - Reference implementation
- [Rule27Controller.cs](HemisAudit/Controllers/Rule27Controller.cs) - Similar pattern
- [Rule54Controller.cs](HemisAudit/Controllers/Rule54Controller.cs) - Column mapping approach

### Views
- [Rule21/Index.cshtml](HemisAudit/Views/Rule21/Index.cshtml#L150-L200) - Filter UI pattern
- [Rule54/Index.cshtml](HemisAudit/Views/Rule54/Index.cshtml#L126-L171) - Column mapping UI
- [Rule55/Index.cshtml](HemisAudit/Views/Rule55/Index.cshtml#L80) - Partial filter config

### ViewModels
- [Rule21ViewModels.cs](HemisAudit/ViewModels/Rule21ViewModels.cs) - Filter fields example
- [Rule55ViewModels.cs](HemisAudit/ViewModels/Rule55ViewModels.cs) - Partial implementation
- [Rule11ViewModels.cs](HemisAudit/ViewModels/Rule11ViewModels.cs) - Column mapping example

---

## Summary Table

| Rule | Hardcoded Values | Filter Config | Column Mapping | Fix Priority |
|------|------------------|---------------|----------------|--------------|
| 11 | None | ✓ N/A | ✓ Smart | None |
| 14 | 'A' approval | ✗ None | ✓ Yes | High |
| 15 | 'A' approval | ✗ None | ✓ Yes | High |
| 16 | 'N' fulfilled | ✗ None | ✗ No | High |
| 17 | None | ✓ Dynamic | ✓ Yes | None |
| 21 | None | ✓ Dynamic | ✓ Yes | None |
| 24 | None (default) | ✗ None | ✓ Default | Medium |
| 25 | None (default) | ✗ None | ✓ Default | Medium |
| 27 | None | ✓ Dynamic | ✓ Yes | None |
| 38 | None (default) | ✗ None | ✓ Default | Medium |
| 54 | None | N/A | ✓ Full | None |
| 55 | 'A' approval | ⚠️ Partial | ✓ Yes | Critical |

