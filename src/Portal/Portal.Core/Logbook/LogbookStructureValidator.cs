namespace Portal.Core.Logbook;

/// <summary>What is wrong with a structure that is being saved.</summary>
public enum LogbookStructureError
{
    Unknown = 0,
    DistrictNameMissing = 1,
    DistrictNameNotUnique = 2,
    RegionNameMissing = 3,
    RegionNameNotUnique = 4,
    DuplicateId = 5,
    UnknownDistrict = 6,
    UnknownRegion = 7,
    RegionNotInDistrict = 8,
    StoreAssignedTwice = 9,
    LeaderListedTwice = 10,
}

/// <summary>One error with the id of the place it is about (district, region or store), so that the answer can say where.</summary>
public sealed record LogbookStructureProblem(LogbookStructureError Code, Guid Id);

/// <summary>
/// The rules for a structure that is saved as a whole: names filled and unique (districts overall, regions within their district), ids unique, every region
/// in an existing district, every store in one place only (a region must lie in the district the store is assigned to), a leader once per place. An
/// empty structure is fine (nothing set up yet). Whether people and stores exist is checked against the copies by the caller.
/// </summary>
public static class LogbookStructureValidator
{
    public static IReadOnlyList<LogbookStructureProblem> Validate(LogbookStructure structure)
    {
        var errors = new List<LogbookStructureProblem>();

        foreach (var duplicate in structure.Districts.GroupBy(d => d.Id).Where(g => g.Count() > 1))
        {
            errors.Add(new(LogbookStructureError.DuplicateId, duplicate.Key));
        }

        foreach (var duplicate in structure.Regions.GroupBy(r => r.Id).Where(g => g.Count() > 1))
        {
            errors.Add(new(LogbookStructureError.DuplicateId, duplicate.Key));
        }

        foreach (var district in structure.Districts)
        {
            if (string.IsNullOrWhiteSpace(district.Name))
            {
                errors.Add(new(LogbookStructureError.DistrictNameMissing, district.Id));
            }

            if (district.LeaderIds.Count != district.LeaderIds.Distinct().Count())
            {
                errors.Add(new(LogbookStructureError.LeaderListedTwice, district.Id));
            }
        }

        foreach (var group in structure.Districts.Where(d => !string.IsNullOrWhiteSpace(d.Name)).GroupBy(d => Normalize(d.Name)))
        {
            errors.AddRange(group.Skip(1).Select(d => new LogbookStructureProblem(LogbookStructureError.DistrictNameNotUnique, d.Id)));
        }

        var districtIds = structure.Districts.Select(d => d.Id).ToHashSet();
        foreach (var region in structure.Regions)
        {
            if (string.IsNullOrWhiteSpace(region.Name))
            {
                errors.Add(new(LogbookStructureError.RegionNameMissing, region.Id));
            }

            if (!districtIds.Contains(region.DistrictId))
            {
                errors.Add(new(LogbookStructureError.UnknownDistrict, region.Id));
            }

            if (region.LeaderIds.Count != region.LeaderIds.Distinct().Count())
            {
                errors.Add(new(LogbookStructureError.LeaderListedTwice, region.Id));
            }
        }

        foreach (var group in structure.Regions.Where(r => !string.IsNullOrWhiteSpace(r.Name)).GroupBy(r => (r.DistrictId, Name: Normalize(r.Name))))
        {
            errors.AddRange(group.Skip(1).Select(r => new LogbookStructureProblem(LogbookStructureError.RegionNameNotUnique, r.Id)));
        }

        var regionsById = structure.Regions.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var duplicate in structure.Stores.GroupBy(s => s.StoreId).Where(g => g.Count() > 1))
        {
            errors.Add(new(LogbookStructureError.StoreAssignedTwice, duplicate.Key));
        }

        foreach (var store in structure.Stores)
        {
            if (!districtIds.Contains(store.DistrictId))
            {
                errors.Add(new(LogbookStructureError.UnknownDistrict, store.StoreId));
            }

            if (store.RegionId is not { } regionId)
            {
                continue;
            }

            if (!regionsById.TryGetValue(regionId, out var region))
            {
                errors.Add(new(LogbookStructureError.UnknownRegion, store.StoreId));
            }
            else if (region.DistrictId != store.DistrictId)
            {
                errors.Add(new(LogbookStructureError.RegionNotInDistrict, store.StoreId));
            }
        }

        return errors;
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
