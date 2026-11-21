using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Api;

internal sealed class ReferenceDataStore(string connectionString)
{
    private readonly string connectionString = string.IsNullOrWhiteSpace(connectionString)
        ? throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString))
        : connectionString;

    private static readonly IReadOnlyDictionary<string, ResourceDefinition> Resources =
        new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal)
        {
            ["countries"] = new("ref.Country", "CountryId", "CountryId", "CountryName",
                [S("CountryName", true, 100), S("CountryCode", false, 3, isAnsi: true, nullable: true), B("IsActive")]),
            ["states"] = new("ref.State", "StateId", "StateId", "StateName",
                [I("CountryId", true, "ref.Country", "CountryId"), S("StateName", true, 100), B("IsActive")], "CountryId"),
            ["districts"] = new("ref.District", "DistrictId", "DistrictId", "DistrictName",
                [I("StateId", true, "ref.State", "StateId"), S("DistrictName", true, 100), B("IsActive")], "StateId"),
            ["cities"] = new("ref.City", "CityId", "CityId", "CityName",
                [I("DistrictId", false, "ref.District", "DistrictId", nullable: true),
                    I("StateId", false, "ref.State", "StateId", nullable: true),
                    S("CityName", true, 100), B("IsActive")], "DistrictId"),
            ["tehsils"] = new("ref.Tehsil", "TehsilId", "TehsilId", "TehsilName",
                [I("DistrictId", true, "ref.District", "DistrictId"), S("TehsilName", true, 100), B("IsActive")], "DistrictId"),
            ["villages"] = new("ref.Village", "VillageId", "VillageId", "VillageName",
                [I("TehsilId", true, "ref.Tehsil", "TehsilId"), S("VillageName", true, 100), B("IsActive")], "TehsilId"),
            ["khasra"] = new("ref.Khasra", "KhasraId", "KhasraId", "KhasraNo",
                [I("VillageId", true, "ref.Village", "VillageId"), S("KhasraNo", true, 40),
                    D("TotalArea", false, 0, nullable: true),
                    S("AreaUnit", false, 20, isAnsi: true, nullable: true), B("IsActive")], "VillageId"),
            ["interest-rate-plans"] = new("lon.InterestRatePlan", "InterestRatePlanId", "InterestRatePlanId", "PlanCode",
                [S("PlanCode", true, 30, isAnsi: true), D("LoanAmount", true, 0), D("MaleRatePct", true, 0),
                    D("FemaleRatePct", true, 0), Dt("EffectiveFrom", true),
                    Dt("EffectiveTo", false, nullable: true), B("IsActive")]),
            ["loan-products"] = new("lon.LoanProduct", "ProductId", "ProductId", "ProductName",
                [S("ProductCode", true, 20, isAnsi: true), S("ProductName", true, 100),
                    L("InterestRatePlanId", false, "lon.InterestRatePlan", "InterestRatePlanId", nullable: true),
                    E("InterestMethod", true, "FLAT", "REDUCING"), E("Frequency", true, "WEEKLY", "BIWEEKLY", "MONTHLY"),
                    D("MinAmount", true, 0), D("MaxAmount", true, 0), I("MinInstallments", true, min: 1),
                    I("MaxInstallments", true, min: 1), D("DefaultRatePct", true, 0), D("MinRatePct", true, 0),
                    D("MaxRatePct", true, 0), D("ProcessingFeePct", true, 0), D("ProcessingFeeFlat", true, 0),
                    D("InsurancePct", true, 0), D("PenaltyRatePct", true, 0), I("PenaltyGraceDays", true, min: 0),
                    D("ForeclosureChargePct", true, 0), I("MinAge", true, min: 0, max: 255),
                    I("MaxAge", true, min: 0, max: 255), B("RequiresGuarantor"), B("RequiresCollateral"),
                    B("IsGroupLoan"), B("IsActive")]),
            ["account-groups"] = new("gl.AccountGroup", "AccountGroupId", "AccountGroupId", "GroupName",
                [S("GroupCode", true, 30, isAnsi: true), S("GroupName", true, 120),
                    I("ParentAccountGroupId", false, "gl.AccountGroup", "AccountGroupId", nullable: true),
                    E("AccountClass", true, "ASSET", "LIABILITY", "EQUITY", "INCOME", "EXPENSE"), B("IsActive")],
                ParentFilterColumn: "ParentAccountGroupId",
                ParentColumn: "ParentAccountGroupId"),
            ["ledgers"] = new("gl.Ledger", "LedgerId", "LedgerId", "LedgerName",
                [S("LedgerCode", true, 30, isAnsi: true), S("LedgerName", true, 150),
                    S("LedgerType", false, 40, nullable: true),
                    I("AccountGroupId", false, "gl.AccountGroup", "AccountGroupId", nullable: true),
                    L("ParentLedgerId", false, "gl.Ledger", "LedgerId", nullable: true),
                    E("AccountClass", true, "ASSET", "LIABILITY", "EQUITY", "INCOME", "EXPENSE"),
                    B("IsGroupLedger"), B("IsCash"), B("IsBank"), S("BankName", false, 100, nullable: true),
                    S("BankAccountNo", false, 40, isAnsi: true, nullable: true),
                    S("IfscCode", false, 20, isAnsi: true, nullable: true),
                    B("Editable"), B("VisibleInBranch"), B("IsDefaultGroup"), B("LoadToBranch"),
                    B("ApplySanction"), B("IsActive")],
                ParentFilterColumn: "ParentLedgerId", ParentColumn: "ParentLedgerId"),
            ["employees"] = EmployeeResource(),
            ["employee-relationships"] = new("sec.EmployeeIntroducer", null, null, null,
                [I("EmployeeId", true, "sec.Employee", "EmployeeId"),
                    I("IntroducerId", true, "sec.Employee", "EmployeeId"),
                    Dt("EffectiveFrom", true), Dt("EffectiveTo", false, nullable: true)],
                ParentFilterColumn: "EmployeeId",
                CompositeKey: ["EmployeeId", "EffectiveFrom"])
        };

    private static readonly string[] LookupCategories = ["SALUTATION", "OCCUPATION", "RELIGION", "DESIGNATION"];
    private static readonly ResourceDefinition LookupResource = new("ref.Lookup", "LookupId", "LookupId", "Name",
        [S("Code", true, 40, isAnsi: true), S("Name", true, 100), I("SortOrder", false, min: int.MinValue),
            B("IsActive")]);

    public static bool IsLookupCategory(string? category) =>
        category is not null && LookupCategories.Contains(category, StringComparer.Ordinal);

    public async Task<IReadOnlyList<object>> ListAsync(
        string resourceName, string? lookupCategory, int? parentId, bool relationshipManagersOnly,
        CancellationToken cancellationToken)
    {
        var (definition, category) = Resolve(resourceName, lookupCategory);
        ValidateParentFilter(definition, parentId);

        var sql = SelectSql(definition) + " WHERE 1 = 1";
        var parameters = new DynamicParameters();
        if (category is not null)
        {
            sql += " AND [Category] = @Category";
            parameters.Add("Category", category);
        }
        if (parentId is not null)
        {
            sql += $" AND [{definition.ParentFilterColumn}] = @ParentId";
            parameters.Add("ParentId", parentId.Value);
        }
        if (relationshipManagersOnly)
        {
            sql += " AND [Status] = 'ACTIVE' AND [IsDeleted] = 0";
        }
        sql += $" ORDER BY [{definition.OrderColumn ?? definition.KeyColumn ?? definition.CompositeKey![0]}]";

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return rows.Select(ToApiRow).ToArray();
    }

    public async Task<object?> GetAsync(
        string resourceName, string? lookupCategory, long id, CancellationToken cancellationToken)
    {
        var (definition, category) = Resolve(resourceName, lookupCategory);
        if (definition.KeyColumn is null)
        {
            throw new ReferenceDataValidationException("This resource uses a composite key; use its collection endpoint.");
        }

        var sql = SelectSql(definition) + $" WHERE [{definition.KeyColumn}] = @Id";
        var parameters = new DynamicParameters(new { Id = id });
        if (category is not null)
        {
            sql += " AND [Category] = @Category";
            parameters.Add("Category", category);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return row is null ? null : ToApiRow(row);
    }

    private static async Task<Dictionary<string, object?>> LoadCurrentValuesAsync(
        SqlConnection connection, ResourceDefinition definition, long id, string? category,
        CancellationToken cancellationToken)
    {
        var columns = string.Join(", ", definition.Fields.Select(field => $"[{field.Name}] AS [{field.Name}]"));
        var sql = $"SELECT {columns} FROM {definition.Table} WHERE [{definition.KeyColumn}] = @Id";
        var parameters = new DynamicParameters(new { Id = id });
        if (category is not null)
        {
            sql += " AND [Category] = @Category";
            parameters.Add("Category", category);
        }
        var row = await connection.QuerySingleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return ((IDictionary<string, object>)row)
            .ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
    }

    private static async Task ValidateCityHierarchyAsync(
        SqlConnection connection, ResourceDefinition definition, IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        if (definition.Table != "ref.City" ||
            !values.TryGetValue("DistrictId", out var district) || district is null ||
            !values.TryGetValue("StateId", out var state) || state is null)
        {
            return;
        }
        var districtState = await connection.ExecuteScalarAsync<int?>(
            new CommandDefinition("SELECT [StateId] FROM ref.District WHERE [DistrictId] = @DistrictId",
                new { DistrictId = district }, cancellationToken: cancellationToken));
        if (districtState != Convert.ToInt32(state, CultureInfo.InvariantCulture))
        {
            throw new ReferenceDataValidationException("stateId must match the selected district's state.");
        }
    }

    public async Task<object> CreateAsync(
        string resourceName, string? lookupCategory, JsonElement body, CancellationToken cancellationToken)
    {
        var (definition, category) = Resolve(resourceName, lookupCategory);
        var values = ParseBody(definition, body, isCreate: true);
        if (category is not null)
        {
            values.Add("Category", category);
        }
        ValidateCrossFieldRules(definition, values, null);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await ValidateForeignKeysAsync(connection, definition, values, cancellationToken);
        await ValidateCityHierarchyAsync(connection, definition, values, cancellationToken);
        if (definition.CompositeKey is not null)
        {
            var inserted = values.Keys.ToArray();
            var sql = $"INSERT INTO {definition.Table} ({string.Join(", ", inserted.Select(Quoted))}) " +
                      $"OUTPUT {string.Join(", ", definition.CompositeKey.Select(name => $"INSERTED.{Quoted(name)}"))} VALUES " +
                      $"({string.Join(", ", inserted.Select(name => "@" + name))})";
            var keyValues = await connection.QuerySingleAsync(
                new CommandDefinition(sql, ToParameters(values), cancellationToken: cancellationToken));
            var key = ToApiRow(keyValues);
            return await GetByCompositeKeyAsync(connection, definition, values, cancellationToken)
                   ?? key;
        }

        var columns = values.Keys.ToArray();
        var insertSql = $"INSERT INTO {definition.Table} ({string.Join(", ", columns.Select(Quoted))}) " +
                        $"OUTPUT INSERTED.{Quoted(definition.KeyColumn!)} VALUES " +
                        $"({string.Join(", ", columns.Select(name => "@" + name))})";
        var id = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(insertSql, ToParameters(values), cancellationToken: cancellationToken));
        return await GetByIdAsync(connection, definition, id, category, cancellationToken)
               ?? throw new InvalidOperationException("The inserted reference row could not be read back.");
    }

    public async Task<object?> UpdateAsync(
        string resourceName, string? lookupCategory, long id, JsonElement body, CancellationToken cancellationToken)
    {
        var (definition, category) = Resolve(resourceName, lookupCategory);
        if (definition.KeyColumn is null)
        {
            throw new ReferenceDataValidationException("This resource uses a composite key; use its collection endpoint.");
        }

        var values = ParseBody(definition, body, isCreate: false);
        if (values.Count == 0)
        {
            throw new ReferenceDataValidationException("Provide at least one field to update.");
        }
        ValidateCrossFieldRules(definition, values, id);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        if (await GetByIdAsync(connection, definition, id, category, cancellationToken) is null)
        {
            return null;
        }
        var mergedValues = await LoadCurrentValuesAsync(
            connection, definition, id, category, cancellationToken);
        foreach (var (name, value) in values)
        {
            mergedValues[name] = value;
        }
        ValidateCrossFieldRules(definition, mergedValues, id);
        await ValidateForeignKeysAsync(connection, definition, values, cancellationToken);
        await ValidateCityHierarchyAsync(connection, definition, mergedValues, cancellationToken);

        var assignments = values.Keys.Select(name => $"{Quoted(name)} = @{name}");
        var sql = $"UPDATE {definition.Table} SET {string.Join(", ", assignments)} WHERE [{definition.KeyColumn}] = @Id";
        if (category is not null)
        {
            sql += " AND [Category] = @Category";
            values.Add("Category", category);
        }
        var parameters = ToParameters(values);
        parameters.Add("Id", id);
        var updated = await connection.ExecuteAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        if (updated == 0)
        {
            return null;
        }
        return await GetByIdAsync(connection, definition, id, category, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        string resourceName, string? lookupCategory, long id, CancellationToken cancellationToken)
    {
        var (definition, category) = Resolve(resourceName, lookupCategory);
        if (definition.KeyColumn is null)
        {
            throw new ReferenceDataValidationException("This resource uses a composite key; use its collection endpoint.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var sql = $"DELETE FROM {definition.Table} WHERE [{definition.KeyColumn}] = @Id";
        var parameters = new DynamicParameters(new { Id = id });
        if (category is not null)
        {
            sql += " AND [Category] = @Category";
            parameters.Add("Category", category);
        }
        return await connection.ExecuteAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<object?> GetEmployeeRelationshipAsync(
        int employeeId, DateOnly effectiveFrom, CancellationToken cancellationToken)
    {
        var definition = Resources["employee-relationships"];
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync(
            new CommandDefinition(SelectSql(definition) +
                " WHERE [EmployeeId] = @EmployeeId AND [EffectiveFrom] = @EffectiveFrom",
                new { EmployeeId = employeeId, EffectiveFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue) },
                cancellationToken: cancellationToken));
        var row = rows.SingleOrDefault();
        return row is null ? null : ToApiRow(row);
    }

    public async Task<object?> UpdateEmployeeRelationshipAsync(
        int employeeId, DateOnly effectiveFrom, JsonElement body, CancellationToken cancellationToken)
    {
        var definition = Resources["employee-relationships"];
        var values = ParseBody(definition, body, isCreate: false);
        if (values.Count == 0)
        {
            throw new ReferenceDataValidationException("Provide at least one field to update.");
        }
        ValidateCrossFieldRules(definition, values, null);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var currentRows = await connection.QueryAsync(
            new CommandDefinition(
                "SELECT [EmployeeId], [IntroducerId], [EffectiveFrom], [EffectiveTo] " +
                "FROM sec.EmployeeIntroducer " +
                " WHERE [EmployeeId] = @EmployeeId AND [EffectiveFrom] = @EffectiveFrom",
                new { EmployeeId = employeeId, EffectiveFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue) },
                cancellationToken: cancellationToken));
        var current = currentRows.SingleOrDefault();
        if (current is null)
        {
            return null;
        }
        var mergedValues = ((IDictionary<string, object>)current)
            .ToDictionary(pair => char.ToUpperInvariant(pair.Key[0]) + pair.Key[1..],
                pair => (object?)pair.Value, StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            mergedValues[name] = value;
        }
        ValidateCrossFieldRules(definition, mergedValues, null);
        await ValidateForeignKeysAsync(connection, definition, values, cancellationToken);

        var parameters = ToParameters(values);
        parameters.Add("EmployeeId", employeeId);
        parameters.Add("OriginalEffectiveFrom", effectiveFrom.ToDateTime(TimeOnly.MinValue));
        var assignments = values.Keys.Select(name => $"{Quoted(name)} = @{name}");
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {definition.Table} SET {string.Join(", ", assignments)} " +
            "WHERE [EmployeeId] = @EmployeeId AND [EffectiveFrom] = @OriginalEffectiveFrom",
            parameters, cancellationToken: cancellationToken));
        if (changed == 0)
        {
            return null;
        }
        var resultingDate = values.TryGetValue("EffectiveFrom", out var newDate) && newDate is DateTime date
            ? DateOnly.FromDateTime(date)
            : effectiveFrom;
        var updatedRows = await connection.QueryAsync(
            new CommandDefinition(SelectSql(definition) +
                " WHERE [EmployeeId] = @EmployeeId AND [EffectiveFrom] = @EffectiveFrom",
                new { EmployeeId = employeeId, EffectiveFrom = resultingDate.ToDateTime(TimeOnly.MinValue) },
                cancellationToken: cancellationToken));
        var updated = updatedRows.SingleOrDefault();
        return updated is null ? null : ToApiRow(updated);
    }

    public async Task<bool> DeleteEmployeeRelationshipAsync(
        int employeeId, DateOnly effectiveFrom, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sec.EmployeeIntroducer WHERE [EmployeeId] = @EmployeeId AND [EffectiveFrom] = @EffectiveFrom",
            new { EmployeeId = employeeId, EffectiveFrom = effectiveFrom.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: cancellationToken)) > 0;
    }

    private async Task<object?> GetByIdAsync(
        SqlConnection connection, ResourceDefinition definition, long id, string? category,
        CancellationToken cancellationToken)
    {
        var sql = SelectSql(definition) + $" WHERE [{definition.KeyColumn}] = @Id";
        var parameters = new DynamicParameters(new { Id = id });
        if (category is not null)
        {
            sql += " AND [Category] = @Category";
            parameters.Add("Category", category);
        }
        var row = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return row is null ? null : ToApiRow(row);
    }

    private static async Task<object?> GetByCompositeKeyAsync(
        SqlConnection connection, ResourceDefinition definition, IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        var where = string.Join(" AND ", definition.CompositeKey!.Select(name => $"{Quoted(name)} = @{name}"));
        var rows = await connection.QueryAsync(
            new CommandDefinition(SelectSql(definition) + " WHERE " + where, ToParameters(values),
                cancellationToken: cancellationToken));
        var row = rows.SingleOrDefault();
        return row is null ? null : ToApiRow(row);
    }

    private static async Task ValidateForeignKeysAsync(
        SqlConnection connection, ResourceDefinition definition, IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        foreach (var field in definition.Fields)
        {
            if (field.ForeignTable is null || !values.TryGetValue(field.Name, out var value) || value is null)
            {
                continue;
            }

            var sql = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {field.ForeignTable} WHERE [{field.ForeignColumn}] = @Value";
            if (field.Name == "DesignationId")
            {
                sql += " AND [Category] = 'DESIGNATION'";
            }
            sql += ") THEN 1 ELSE 0 END";
            var exists = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, new { Value = value }, cancellationToken: cancellationToken));
            if (exists == 0)
            {
                throw new ReferenceDataValidationException($"'{Camel(field.Name)}' references a record that does not exist.");
            }
        }
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private SqlConnection CreateConnection() => new(connectionString);

    private static void ValidateParentFilter(ResourceDefinition definition, int? parentId)
    {
        if (parentId is not null && (parentId.Value <= 0 || definition.ParentFilterColumn is null))
        {
            throw new ReferenceDataValidationException("This resource does not support a positive parentId filter.");
        }
    }

    private static (ResourceDefinition Definition, string? Category) Resolve(string name, string? category)
    {
        if (name == "lookups")
        {
            if (!IsLookupCategory(category))
            {
                throw new ReferenceDataValidationException(
                    $"category must be one of: {string.Join(", ", LookupCategories)}.");
            }
            return (LookupResource, category);
        }
        if (!Resources.TryGetValue(name, out var definition))
        {
            throw new ReferenceDataValidationException("Unknown reference-data resource.");
        }
        return (definition, null);
    }

    private static Dictionary<string, object?> ParseBody(ResourceDefinition definition, JsonElement body, bool isCreate)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new ReferenceDataValidationException("The request body must be a JSON object.");
        }

        var fields = definition.Fields.ToDictionary(field => Camel(field.Name), StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in body.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new ReferenceDataValidationException($"'{property.Name}' is supplied more than once.");
            }
            if (!fields.TryGetValue(property.Name, out var field))
            {
                throw new ReferenceDataValidationException($"'{property.Name}' is not writable for this resource.");
            }
            values.Add(field.Name, ParseValue(field, property.Value));
        }

        if (isCreate)
        {
            foreach (var field in definition.Fields.Where(field => field.Required && !values.ContainsKey(field.Name)))
            {
                throw new ReferenceDataValidationException($"'{Camel(field.Name)}' is required.");
            }
        }

        return values;
    }

    private static object? ParseValue(FieldDefinition field, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!field.Nullable)
            {
                throw new ReferenceDataValidationException($"'{Camel(field.Name)}' cannot be null.");
            }
            return null;
        }

        try
        {
            switch (field.Kind)
            {
                case ValueKind.String:
                    if (value.ValueKind != JsonValueKind.String)
                    {
                        break;
                    }
                    var text = value.GetString()!;
                    if (field.Required && string.IsNullOrWhiteSpace(text))
                    {
                        throw new ReferenceDataValidationException($"'{Camel(field.Name)}' cannot be empty.");
                    }
                    if (text.Length > field.MaxLength)
                    {
                        throw new ReferenceDataValidationException($"'{Camel(field.Name)}' exceeds {field.MaxLength} characters.");
                    }
                    if (field.IsAnsi && text.Any(character => character > 127))
                    {
                        throw new ReferenceDataValidationException($"'{Camel(field.Name)}' must contain ASCII characters only.");
                    }
                    if (field.AllowedValues is not null &&
                        !field.AllowedValues.Contains(text, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new ReferenceDataValidationException(
                            $"'{Camel(field.Name)}' must be one of: {string.Join(", ", field.AllowedValues)}.");
                    }
                    return field.AllowedValues is null ? text.Trim() : text.ToUpperInvariant();
                case ValueKind.Int:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var intValue))
                    {
                        break;
                    }
                    CheckRange(field, intValue);
                    return intValue;
                case ValueKind.Long:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var longValue))
                    {
                        break;
                    }
                    CheckRange(field, longValue);
                    return longValue;
                case ValueKind.Decimal:
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var decimalValue))
                    {
                        break;
                    }
                    CheckRange(field, decimalValue);
                    return decimalValue;
                case ValueKind.Boolean:
                    if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        return value.GetBoolean();
                    }
                    break;
                case ValueKind.Date:
                    if (value.ValueKind == JsonValueKind.String &&
                        DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var date))
                    {
                        return date.ToDateTime(TimeOnly.MinValue);
                    }
                    break;
            }
        }
        catch (FormatException)
        {
            // Report malformed JSON values using the same stable validation response.
        }

        throw new ReferenceDataValidationException(
            $"'{Camel(field.Name)}' must be a valid {field.Kind.ToString().ToLowerInvariant()} value.");
    }

    private static void CheckRange(FieldDefinition field, decimal value)
    {
        if ((field.Minimum is not null && value < field.Minimum) || (field.Maximum is not null && value > field.Maximum))
        {
            throw new ReferenceDataValidationException($"'{Camel(field.Name)}' is outside the allowed range.");
        }
    }

    private static void ValidateCrossFieldRules(
        ResourceDefinition definition, IReadOnlyDictionary<string, object?> values, long? resourceId)
    {
        object? Value(string name) => values.TryGetValue(name, out var value) ? value : null;

        if (definition.Table == "lon.InterestRatePlan" &&
            Value("EffectiveTo") is DateTime end && Value("EffectiveFrom") is DateTime start && end < start)
        {
            throw new ReferenceDataValidationException("effectiveTo must be on or after effectiveFrom.");
        }
        if (definition.Table == "lon.LoanProduct")
        {
            CheckOrderedRange(values, "MinAmount", "MaxAmount");
            CheckOrderedRange(values, "MinInstallments", "MaxInstallments");
            CheckOrderedRange(values, "MinRatePct", "MaxRatePct");
            CheckAgeRange(values);
        }
        if (definition.Table == "gl.Ledger" &&
            Value("IsCash") is true && Value("IsBank") is true)
        {
            throw new ReferenceDataValidationException("A ledger cannot be both cash and bank.");
        }
        if (definition.Table == "sec.EmployeeIntroducer")
        {
            if (Value("EmployeeId") is int employee && Value("IntroducerId") is int introducer && employee == introducer)
            {
                throw new ReferenceDataValidationException("An employee cannot introduce themselves.");
            }
            if (Value("EffectiveTo") is DateTime relationshipEnd &&
                Value("EffectiveFrom") is DateTime relationshipStart && relationshipEnd < relationshipStart)
            {
                throw new ReferenceDataValidationException("effectiveTo must be on or after effectiveFrom.");
            }
        }
        if (definition.Table is "gl.AccountGroup" or "gl.Ledger")
        {
            var parentField = definition.ParentColumn;
            var parentValue = parentField is null ? null : Value(parentField);
            if (resourceId is not null && parentValue is not null && Convert.ToInt64(parentValue, CultureInfo.InvariantCulture) == resourceId)
            {
                throw new ReferenceDataValidationException("A record cannot be its own parent.");
            }
        }
    }

    private static void CheckOrderedRange(IReadOnlyDictionary<string, object?> values, string lower, string upper)
    {
        if (values.TryGetValue(lower, out var lowerValue) && lowerValue is not null &&
            values.TryGetValue(upper, out var upperValue) && upperValue is not null &&
            Convert.ToDecimal(lowerValue, CultureInfo.InvariantCulture) > Convert.ToDecimal(upperValue, CultureInfo.InvariantCulture))
        {
            throw new ReferenceDataValidationException($"'{Camel(lower)}' must not exceed '{Camel(upper)}'.");
        }
    }

    private static void CheckAgeRange(IReadOnlyDictionary<string, object?> values)
    {
        CheckOrderedRange(values, "MinAge", "MaxAge");
    }

    private static string SelectSql(ResourceDefinition definition)
    {
        var columns = new List<string>();
        if (definition.KeyColumn is not null)
        {
            columns.Add($"[{definition.KeyColumn}] AS [id]");
        }
        else
        {
            foreach (var key in definition.CompositeKey!)
            {
                var keyField = definition.Fields.Single(field => field.Name == key);
                columns.Add(keyField.Kind == ValueKind.Date
                    ? $"CONVERT(char(10), [{key}], 23) AS [{Camel(key)}]"
                    : $"[{key}] AS [{Camel(key)}]");
            }
        }
        if (definition.Table == "ref.Lookup")
        {
            columns.Add("[Category] AS [category]");
        }
        columns.AddRange(definition.Fields
            .Where(field => definition.CompositeKey is null || !definition.CompositeKey.Contains(field.Name))
            .Select(field => field.Kind == ValueKind.Date
            ? $"CONVERT(char(10), [{field.Name}], 23) AS [{Camel(field.Name)}]"
            : $"[{field.Name}] AS [{Camel(field.Name)}]"));
        return $"SELECT {string.Join(", ", columns)} FROM {definition.Table}";
    }

    private static object ToApiRow(object row)
    {
        var values = (IDictionary<string, object>)row;
        return values.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static DynamicParameters ToParameters(IReadOnlyDictionary<string, object?> values)
    {
        var parameters = new DynamicParameters();
        foreach (var (name, value) in values)
        {
            parameters.Add(name, value);
        }
        return parameters;
    }

    private static string Quoted(string identifier) => $"[{identifier}]";
    private static string Camel(string value) => char.ToLowerInvariant(value[0]) + value[1..];

    private static FieldDefinition S(
        string name, bool required, int maxLength, bool isAnsi = false, bool nullable = false) =>
        new(name, ValueKind.String, required, maxLength, IsAnsi: isAnsi, Nullable: nullable);
    private static FieldDefinition I(
        string name, bool required, string? table = null, string? column = null,
        int? min = 1, int? max = null, bool nullable = false) =>
        new(name, ValueKind.Int, required, ForeignTable: table, ForeignColumn: column,
            Minimum: min, Maximum: max, Nullable: nullable);
    private static FieldDefinition L(
        string name, bool required, string? table = null, string? column = null, bool nullable = false) =>
        new(name, ValueKind.Long, required, ForeignTable: table, ForeignColumn: column,
            Minimum: table is null ? 0 : 1, Nullable: nullable);
    private static FieldDefinition D(string name, bool required, decimal? min = null, bool nullable = false) =>
        new(name, ValueKind.Decimal, required, Minimum: min, Nullable: nullable);
    private static FieldDefinition B(string name) => new(name, ValueKind.Boolean, false);
    private static FieldDefinition Dt(string name, bool required, bool nullable = false) =>
        new(name, ValueKind.Date, required, Nullable: nullable);
    private static FieldDefinition E(string name, bool required, params string[] allowed) =>
        new(name, ValueKind.String, required, 12, IsAnsi: true, AllowedValues: allowed);

    private static ResourceDefinition EmployeeResource() =>
        new("sec.Employee", "EmployeeId", "EmployeeId", "EmployeeName",
            [S("EmployeeCode", true, 20, isAnsi: true),
                I("UserId", false, "sec.AppUser", "UserId", nullable: true),
                S("EmployeeName", true, 120), I("BranchId", true, "ref.Branch", "BranchId"),
                I("DesignationId", false, "ref.Lookup", "LookupId", nullable: true), Dt("JoiningDate", true),
                S("IntroductoryRank", false, 40, nullable: true),
                S("InitialRank", false, 40, nullable: true), S("CurrentRank", false, 40, nullable: true),
                D("JoiningFee", false, 0), E("Status", false, "ACTIVE", "INACTIVE", "LEFT"), B("IsDeleted")]);

    private sealed record ResourceDefinition(
        string Table,
        string? KeyColumn,
        string? IdName,
        string? OrderColumn,
        IReadOnlyList<FieldDefinition> Fields,
        string? ParentFilterColumn = null,
        string? ParentColumn = null,
        IReadOnlyList<string>? CompositeKey = null);

    private sealed record FieldDefinition(
        string Name,
        ValueKind Kind,
        bool Required,
        int MaxLength = int.MaxValue,
        bool IsAnsi = false,
        bool Nullable = false,
        string? ForeignTable = null,
        string? ForeignColumn = null,
        decimal? Minimum = null,
        decimal? Maximum = null,
        IReadOnlyList<string>? AllowedValues = null);

    private enum ValueKind { String, Int, Long, Decimal, Boolean, Date }
}

internal sealed class ReferenceDataValidationException(string message) : Exception(message);
