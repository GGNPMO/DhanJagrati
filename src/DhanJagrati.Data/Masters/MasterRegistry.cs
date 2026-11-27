namespace DhanJagrati.Data.Masters;

/// <summary>All simple master screens (MASTER menu). Adding a master = adding one definition here.</summary>
public static class MasterRegistry
{
    private static readonly string[] EmiModes = ["D", "W", "F", "M"];
    private static readonly string[] EmiMethods = ["MANUAL", "FLAT", "REDUCING", "FACTOR"];
    private static readonly string[] RateBases = ["ANNUAL", "TENURE"];
    private static readonly string[] Roundings = ["NONE", "NEAREST", "UP", "DOWN"];
    private static readonly string[] Splits = ["FLAT", "REDUCING"];

    private static MasterField Name(string column, int length) => new(column, FieldKind.Text, Required: true, MaxLength: length, Upper: true);
    private static MasterField Active() => new("IsActive", FieldKind.Bool, Default: true);

    private static MasterDefinition Simple(string key, string table, string alias, string id, string name, int length, params MasterField[] extra) => new()
    {
        Key = key, Table = table, Alias = alias, IdColumn = id,
        SelectList = $"{alias}.{name}, {alias}.IsActive" + string.Concat(extra.Select(f => $", {alias}.{f.Column}")),
        From = $"{table} {alias}",
        OrderBy = $"{alias}.{name}",
        SearchColumn = $"{alias}.{name}",
        Fields = [Name(name, length), .. extra, Active()]
    };

    public static readonly IReadOnlyDictionary<string, MasterDefinition> All = new[]
    {
        Simple("salutations", "mst.Salutation", "s", "SalutationId", "SalutationName", 20, new MasterField("IsRelation", FieldKind.Bool, Default: false)),
        Simple("occupations", "mst.Occupation", "o", "OccupationId", "OccupationName", 80),
        Simple("religions", "mst.Religion", "r", "ReligionId", "ReligionName", 50),
        Simple("relations", "mst.Relation", "r", "RelationId", "RelationName", 40),
        Simple("designations", "mst.Designation", "d", "DesignationId", "DesignationName", 60),
        Simple("banks", "mst.Bank", "b", "BankId", "BankName", 120),
        Simple("countries", "mst.Country", "c", "CountryId", "CountryName", 80),

        new MasterDefinition
        {
            Key = "advisor-ranks", Table = "emp.AdvisorRank", Alias = "ar", IdColumn = "RankId",
            SelectList = "ar.RankNo, ar.RankCode, ar.RankName, ar.IsActive",
            From = "emp.AdvisorRank ar", OrderBy = "ar.RankNo", SearchColumn = "ar.RankName",
            Fields =
            [
                new("RankNo", FieldKind.Int, Required: true, Min: 1, Max: 999),
                Name("RankCode", 20),
                Name("RankName", 60),
                Active()
            ]
        },

        new MasterDefinition
        {
            Key = "states", Table = "mst.State", Alias = "st", IdColumn = "StateId",
            SelectList = "st.StateName, st.CountryId, c.CountryName, st.IsActive",
            From = "mst.State st JOIN mst.Country c ON c.CountryId = st.CountryId",
            OrderBy = "st.StateName", SearchColumn = "st.StateName", ParentColumn = "CountryId",
            Fields = [new("CountryId", FieldKind.Int, Required: true), Name("StateName", 80), Active()]
        },
        new MasterDefinition
        {
            Key = "cities", Table = "mst.City", Alias = "ci", IdColumn = "CityId",
            SelectList = "ci.CityName, ci.StateId, st.StateName, ci.IsActive",
            From = "mst.City ci LEFT JOIN mst.State st ON st.StateId = ci.StateId",
            OrderBy = "ci.CityName", SearchColumn = "ci.CityName", ParentColumn = "StateId",
            Fields = [new("StateId", FieldKind.Int), Name("CityName", 80), Active()]
        },
        new MasterDefinition
        {
            Key = "districts", Table = "mst.District", Alias = "d", IdColumn = "DistrictId",
            SelectList = "d.DistrictName, d.StateId, st.StateName, d.IsActive",
            From = "mst.District d JOIN mst.State st ON st.StateId = d.StateId",
            OrderBy = "d.DistrictName", SearchColumn = "d.DistrictName", ParentColumn = "StateId",
            Fields = [new("StateId", FieldKind.Int, Required: true), Name("DistrictName", 80), Active()]
        },
        new MasterDefinition
        {
            Key = "tehsils", Table = "mst.Tehsil", Alias = "t", IdColumn = "TehsilId",
            SelectList = "t.TehsilName, t.DistrictId, d.DistrictName, d.StateId, st.StateName, t.IsActive",
            From = "mst.Tehsil t JOIN mst.District d ON d.DistrictId = t.DistrictId JOIN mst.State st ON st.StateId = d.StateId",
            OrderBy = "t.TehsilName", SearchColumn = "t.TehsilName", ParentColumn = "DistrictId",
            Fields = [new("DistrictId", FieldKind.Int, Required: true), Name("TehsilName", 80), Active()]
        },
        new MasterDefinition
        {
            Key = "villages", Table = "mst.Village", Alias = "v", IdColumn = "VillageId",
            SelectList = "v.VillageName, v.TehsilId, t.TehsilName, t.DistrictId, d.DistrictName, d.StateId, st.StateName, v.IsActive",
            From = "mst.Village v JOIN mst.Tehsil t ON t.TehsilId = v.TehsilId JOIN mst.District d ON d.DistrictId = t.DistrictId JOIN mst.State st ON st.StateId = d.StateId",
            OrderBy = "v.VillageName", SearchColumn = "v.VillageName", ParentColumn = "TehsilId",
            Fields = [new("TehsilId", FieldKind.Int, Required: true), Name("VillageName", 100), Active()]
        },
        new MasterDefinition
        {
            Key = "khasras", Table = "mst.Khasra", Alias = "k", IdColumn = "KhasraId",
            SelectList = "k.KhasraNo, k.TotalArea, k.AreaUnit, k.VillageId, v.VillageName, v.TehsilId, t.TehsilName, t.DistrictId, d.DistrictName, d.StateId, st.StateName, k.IsActive",
            From = "mst.Khasra k JOIN mst.Village v ON v.VillageId = k.VillageId JOIN mst.Tehsil t ON t.TehsilId = v.TehsilId JOIN mst.District d ON d.DistrictId = t.DistrictId JOIN mst.State st ON st.StateId = d.StateId",
            OrderBy = "k.KhasraNo", SearchColumn = "k.KhasraNo", ParentColumn = "VillageId",
            Fields =
            [
                new("VillageId", FieldKind.Int, Required: true),
                Name("KhasraNo", 30),
                new("TotalArea", FieldKind.Decimal, Min: 0),
                new("AreaUnit", FieldKind.Text, MaxLength: 15, Upper: true),
                Active()
            ]
        },
        new MasterDefinition
        {
            Key = "centers", Table = "mst.Center", Alias = "ce", IdColumn = "CenterId", BranchScoped = true,
            SelectList = "ce.CenterCode, ce.CenterName, ce.VillageId, v.VillageName, ce.MeetingDay, ce.IsActive",
            From = "mst.Center ce LEFT JOIN mst.Village v ON v.VillageId = ce.VillageId",
            OrderBy = "ce.CenterName", SearchColumn = "ce.CenterName",
            Fields =
            [
                Name("CenterCode", 20), Name("CenterName", 100),
                new("VillageId", FieldKind.Int),
                new("MeetingDay", FieldKind.Int, Min: 1, Max: 31),
                Active()
            ]
        },
        new MasterDefinition
        {
            Key = "groups", Table = "mst.MemberGroup", Alias = "g", IdColumn = "GroupId", BranchScoped = true,
            SelectList = "g.GroupCode, g.GroupName, g.CenterId, ce.CenterName, g.IsActive",
            From = "mst.MemberGroup g LEFT JOIN mst.Center ce ON ce.CenterId = g.CenterId",
            OrderBy = "g.GroupName", SearchColumn = "g.GroupName", ParentColumn = "CenterId",
            Fields = [Name("GroupCode", 20), Name("GroupName", 100), new("CenterId", FieldKind.Int), Active()]
        },
        new MasterDefinition
        {
            Key = "vehicles", Table = "mst.Vehicle", Alias = "ve", IdColumn = "VehicleId",
            SelectList = "ve.VehicleName, ve.Manufacturer, ve.Model, ve.IsActive",
            From = "mst.Vehicle ve", OrderBy = "ve.VehicleName", SearchColumn = "ve.VehicleName",
            Fields =
            [
                Name("VehicleName", 100),
                new("Manufacturer", FieldKind.Text, MaxLength: 100, Upper: true),
                new("Model", FieldKind.Text, MaxLength: 60, Upper: true),
                Active()
            ]
        },
        new MasterDefinition
        {
            Key = "dealers", Table = "mst.Dealer", Alias = "de", IdColumn = "DealerId",
            SelectList = "de.DealerName, de.ContactPerson, de.MobileNo, de.AddressLine, de.CityId, ci.CityName, de.GstNo, de.IsActive",
            From = "mst.Dealer de LEFT JOIN mst.City ci ON ci.CityId = de.CityId",
            OrderBy = "de.DealerName", SearchColumn = "de.DealerName",
            Fields =
            [
                Name("DealerName", 120),
                new("ContactPerson", FieldKind.Text, MaxLength: 100, Upper: true),
                new("MobileNo", FieldKind.Text, MaxLength: 15),
                new("AddressLine", FieldKind.Text, MaxLength: 300, Upper: true),
                new("CityId", FieldKind.Int),
                new("GstNo", FieldKind.Text, MaxLength: 20, Upper: true),
                Active()
            ]
        },
        new MasterDefinition
        {
            Key = "loan-plans", Table = "mst.LoanPlan", Alias = "p", IdColumn = "PlanId",
            SelectList = """
                p.PlanCode, p.PlanName, p.EmiMode, p.TenureMonths, p.NoOfInstallments, p.ProcessingFeePct,
                p.LateFeePerInstallment, p.PreCloseChargesPct, p.EmiCalcMethod, p.EmiFactor, p.RateBasis,
                p.EmiRounding, p.EmiRoundTo, p.InterestSplit, p.AllowEmiOverride, p.IsActive
                """,
            From = "mst.LoanPlan p", OrderBy = "p.PlanCode", SearchColumn = "p.PlanCode",
            Fields =
            [
                Name("PlanCode", 20),
                new("PlanName", FieldKind.Text, MaxLength: 100, Upper: true),
                new("EmiMode", FieldKind.Choice, Required: true, Choices: EmiModes),
                new("TenureMonths", FieldKind.Int, Required: true, Min: 1, Max: 600),
                new("NoOfInstallments", FieldKind.Int, Required: true, Min: 1, Max: 10000),
                new("ProcessingFeePct", FieldKind.Decimal, Required: true, Min: 0, Max: 100, Default: 0m),
                new("LateFeePerInstallment", FieldKind.Decimal, Required: true, Min: 0, Default: 0m),
                new("PreCloseChargesPct", FieldKind.Decimal, Required: true, Min: 0, Max: 100, Default: 0m),
                new("EmiCalcMethod", FieldKind.Choice, Required: true, Choices: EmiMethods, Default: "MANUAL"),
                new("EmiFactor", FieldKind.Decimal, Min: 0),
                new("RateBasis", FieldKind.Choice, Required: true, Choices: RateBases, Default: "ANNUAL"),
                new("EmiRounding", FieldKind.Choice, Required: true, Choices: Roundings, Default: "NEAREST"),
                new("EmiRoundTo", FieldKind.Decimal, Required: true, Min: 0.01m, Default: 1m),
                new("InterestSplit", FieldKind.Choice, Required: true, Choices: Splits, Default: "FLAT"),
                new("AllowEmiOverride", FieldKind.Bool, Default: true),
                Active()
            ],
            Validate = v =>
                Equals(v["EmiCalcMethod"], "FACTOR") && !(v["EmiFactor"] is decimal factor && factor > 0)
                    ? [("EmiFactor", "EMI factor is required (and must be > 0) for the FACTOR method.")]
                    : []
        },
        new MasterDefinition
        {
            Key = "interest-rates", Table = "mst.InterestRate", Alias = "ir", IdColumn = "InterestRateId", HasIsActive = false,
            SelectList = "ir.PlanId, p.PlanCode, ir.PlanAmount, ir.RateMale, ir.RateFemale, ir.EffectiveFrom, ir.EffectiveTo",
            From = "mst.InterestRate ir JOIN mst.LoanPlan p ON p.PlanId = ir.PlanId",
            OrderBy = "p.PlanCode, ir.EffectiveFrom DESC", SearchColumn = "p.PlanCode", ParentColumn = "PlanId",
            Fields =
            [
                new("PlanId", FieldKind.Int, Required: true),
                new("PlanAmount", FieldKind.Decimal, Required: true, Min: 1),
                new("RateMale", FieldKind.Decimal, Required: true, Min: 0, Max: 100),
                new("RateFemale", FieldKind.Decimal, Required: true, Min: 0, Max: 100),
                new("EffectiveFrom", FieldKind.Date, Required: true),
                new("EffectiveTo", FieldKind.Date)
            ],
            Validate = v =>
                v["EffectiveTo"] is DateOnly to && v["EffectiveFrom"] is DateOnly from && to < from
                    ? [("EffectiveTo", "'To' date cannot be before 'From' date.")]
                    : []
        }
    }.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
}
