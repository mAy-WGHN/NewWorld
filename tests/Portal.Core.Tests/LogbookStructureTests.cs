using Portal.Core.Logbook;

namespace Portal.Core.Tests;

/// <summary>
/// The hierarchy of the Berichtsheft (district → region → stores) and who reaches which store: the regional leader (RL) the stores of the
/// region, the district leader (BL) everything in the district (all regions and the stores that belong to the district directly).
/// </summary>
public sealed class LogbookStructureTests
{
    private static readonly Guid DistrictSouth = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid DistrictNorth = Guid.Parse("00000000-0000-0000-0000-0000000000d2");
    private static readonly Guid RegionAugsburg = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid RegionMunich = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid RegionHamburg = Guid.Parse("00000000-0000-0000-0000-0000000000a3");

    private static readonly Guid Store17 = Guid.Parse("00000000-0000-0000-0000-000000000017");
    private static readonly Guid Store18 = Guid.Parse("00000000-0000-0000-0000-000000000018");
    private static readonly Guid Store19 = Guid.Parse("00000000-0000-0000-0000-000000000019");
    private static readonly Guid Store20 = Guid.Parse("00000000-0000-0000-0000-000000000020");
    private static readonly Guid Store21 = Guid.Parse("00000000-0000-0000-0000-000000000021");
    private static readonly Guid StoreWithoutAnybody = Guid.Parse("00000000-0000-0000-0000-000000000099");

    private static readonly Guid Rita = Guid.Parse("00000000-0000-0000-0000-0000000000e1"); // RL Augsburg
    private static readonly Guid Rolf = Guid.Parse("00000000-0000-0000-0000-0000000000e2"); // RL Munich
    private static readonly Guid Rosi = Guid.Parse("00000000-0000-0000-0000-0000000000e3"); // second RL Augsburg
    private static readonly Guid Bernd = Guid.Parse("00000000-0000-0000-0000-0000000000b1"); // BL South
    private static readonly Guid Berta = Guid.Parse("00000000-0000-0000-0000-0000000000b2"); // BL North
    private static readonly Guid Fiona = Guid.Parse("00000000-0000-0000-0000-0000000000f1"); // nothing

    /// <summary>
    /// South: region Augsburg (stores 17, 18; RL Rita and Rosi), region Munich (store 19; RL Rolf), store 20 directly in the district (no region); BL Bernd.
    /// North: region Hamburg (store 21, no RL), BL Berta.
    /// </summary>
    private static LogbookStructure Structure() => new(
        [new LogbookDistrict(DistrictSouth, "Süd", [Bernd]), new LogbookDistrict(DistrictNorth, "Nord", [Berta])],
        [
            new LogbookRegion(RegionAugsburg, DistrictSouth, "Augsburg", [Rita, Rosi]),
            new LogbookRegion(RegionMunich, DistrictSouth, "München", [Rolf]),
            new LogbookRegion(RegionHamburg, DistrictNorth, "Hamburg", []),
        ],
        [
            new LogbookStoreAssignment(Store17, DistrictSouth, RegionAugsburg),
            new LogbookStoreAssignment(Store18, DistrictSouth, RegionAugsburg),
            new LogbookStoreAssignment(Store19, DistrictSouth, RegionMunich),
            new LogbookStoreAssignment(Store20, DistrictSouth, null),
            new LogbookStoreAssignment(Store21, DistrictNorth, RegionHamburg),
        ]);

    // ---------------------------------------------------------------- who reaches which store

    [Fact]
    public void A_regional_leader_reaches_exactly_the_stores_of_the_own_region()
    {
        var reach = LogbookReach.Of(Structure(), Rita);

        reach.StoreIds.ShouldBe([Store17, Store18], ignoreOrder: true);
        reach.IsRegionalLeader.ShouldBeTrue();
        reach.IsDistrictLeader.ShouldBeFalse();
        LogbookReach.Of(Structure(), Rolf).StoreIds.ShouldBe([Store19]);
    }

    [Fact]
    public void A_district_leader_reaches_every_region_of_the_district_and_the_stores_without_a_region_but_nothing_of_other_districts()
    {
        var south = LogbookReach.Of(Structure(), Bernd);
        var north = LogbookReach.Of(Structure(), Berta);

        south.StoreIds.ShouldBe([Store17, Store18, Store19, Store20], ignoreOrder: true);
        south.IsDistrictLeader.ShouldBeTrue();
        south.IsRegionalLeader.ShouldBeFalse();
        north.StoreIds.ShouldBe([Store21]);
    }

    [Fact]
    public void A_person_who_leads_nothing_reaches_nothing_and_a_person_who_leads_both_gets_the_union()
    {
        LogbookReach.Of(Structure(), Fiona).StoreIds.ShouldBeEmpty();
        LogbookReach.Of(Structure(), Fiona).Level.ShouldBe(LogbookLevel.None);

        var both = Structure() with { Regions = [.. Structure().Regions, new LogbookRegion(Guid.NewGuid(), DistrictNorth, "Bremen", [Rita])] };
        var rita = LogbookReach.Of(both, Rita);
        rita.IsRegionalLeader.ShouldBeTrue();
        rita.StoreIds.ShouldBe([Store17, Store18], ignoreOrder: true); // Bremen has no stores yet

        var ritaAndBerta = Structure() with { Districts = [new LogbookDistrict(DistrictSouth, "Süd", [Bernd, Rita]), new LogbookDistrict(DistrictNorth, "Nord", [Berta])] };
        LogbookReach.Of(ritaAndBerta, Rita).StoreIds.ShouldBe([Store17, Store18, Store19, Store20], ignoreOrder: true);
        LogbookReach.Of(ritaAndBerta, Rita).Level.ShouldBe(LogbookLevel.District); // the higher level counts
    }

    [Fact]
    public void The_level_of_a_person_for_a_store_is_the_highest_one_that_reaches_it()
    {
        var s = Structure();

        LogbookReach.LevelFor(s, Rita, Store17).ShouldBe(LogbookLevel.Regional);
        LogbookReach.LevelFor(s, Rita, Store19).ShouldBe(LogbookLevel.None); // other region
        LogbookReach.LevelFor(s, Bernd, Store19).ShouldBe(LogbookLevel.District);
        LogbookReach.LevelFor(s, Bernd, Store20).ShouldBe(LogbookLevel.District); // no region
        LogbookReach.LevelFor(s, Bernd, Store21).ShouldBe(LogbookLevel.None); // other district
        LogbookReach.LevelFor(s, Fiona, Store17).ShouldBe(LogbookLevel.None);
        LogbookReach.LevelFor(s, Bernd, StoreWithoutAnybody).ShouldBe(LogbookLevel.None); // unknown store
    }

    // ---------------------------------------------------------------- who is told

    [Fact]
    public void A_report_goes_to_the_regional_leaders_of_the_region_and_escalation_to_the_district_leaders()
    {
        var s = Structure();

        LogbookReach.RecipientsOf(s, Store17).Regional.ShouldBe([Rita, Rosi], ignoreOrder: true);
        LogbookReach.RecipientsOf(s, Store17).District.ShouldBe([Bernd]);
        LogbookReach.RecipientsOf(s, Store19).Regional.ShouldBe([Rolf]);
        LogbookReach.RecipientsOf(s, Store19).District.ShouldBe([Bernd]);
    }

    [Fact]
    public void A_store_without_a_region_or_a_region_without_leaders_goes_directly_to_the_district_leaders()
    {
        var s = Structure();

        var direct = LogbookReach.RecipientsOf(s, Store20);
        direct.Regional.ShouldBeEmpty();
        direct.District.ShouldBe([Bernd]);
        direct.Primary.ShouldBe([Bernd]); // the ones to tell first

        var noLeader = LogbookReach.RecipientsOf(s, Store21); // region Hamburg has no RL
        noLeader.Regional.ShouldBeEmpty();
        noLeader.Primary.ShouldBe([Berta]);

        LogbookReach.RecipientsOf(s, Store17).Primary.ShouldBe([Rita, Rosi], ignoreOrder: true);
    }

    [Fact]
    public void A_store_that_is_not_in_the_structure_has_nobody_and_cannot_be_reported_for()
    {
        var nobody = LogbookReach.RecipientsOf(Structure(), StoreWithoutAnybody);

        nobody.Primary.ShouldBeEmpty();
        nobody.District.ShouldBeEmpty();
        LogbookReach.CanReceiveReports(Structure(), StoreWithoutAnybody).ShouldBeFalse();
        LogbookReach.CanReceiveReports(Structure(), Store17).ShouldBeTrue();
        LogbookReach.CanReceiveReports(Structure(), Store21).ShouldBeTrue(); // the district leader is told
    }

    [Fact]
    public void A_store_whose_district_has_no_leader_at_all_cannot_receive_reports()
    {
        var s = Structure() with { Districts = [new LogbookDistrict(DistrictSouth, "Süd", []), new LogbookDistrict(DistrictNorth, "Nord", [Berta])] };

        LogbookReach.CanReceiveReports(s, Store20).ShouldBeFalse(); // direct store, nobody to tell
        LogbookReach.CanReceiveReports(s, Store17).ShouldBeTrue(); // Rita and Rosi are told
        LogbookReach.CanReceiveReports(s, Store19).ShouldBeTrue();
    }

    // ---------------------------------------------------------------- validation of what is maintained

    [Fact]
    public void A_correct_structure_has_no_errors()
    {
        LogbookStructureValidator.Validate(Structure()).ShouldBeEmpty();
        LogbookStructureValidator.Validate(new LogbookStructure([], [], [])).ShouldBeEmpty(); // an empty one is allowed (nothing set up yet)
    }

    [Fact]
    public void Names_must_be_filled_and_unique_without_regard_to_case_within_their_level()
    {
        var s = Structure() with
        {
            Districts = [new LogbookDistrict(DistrictSouth, " ", []), new LogbookDistrict(DistrictNorth, "NORD", []), new LogbookDistrict(Guid.NewGuid(), "nord", [])],
            Regions = [new LogbookRegion(RegionAugsburg, DistrictSouth, "Augsburg", []), new LogbookRegion(RegionMunich, DistrictSouth, "augsburg", [])],
            Stores = [],
        };

        var errors = LogbookStructureValidator.Validate(s).Select(e => e.Code).ToList();

        errors.ShouldContain(LogbookStructureError.DistrictNameMissing);
        errors.ShouldContain(LogbookStructureError.DistrictNameNotUnique);
        errors.ShouldContain(LogbookStructureError.RegionNameNotUnique); // within one district
    }

    [Fact]
    public void The_same_region_name_may_exist_in_different_districts()
    {
        var s = Structure() with { Regions = [.. Structure().Regions, new LogbookRegion(Guid.NewGuid(), DistrictNorth, "Augsburg", [])] };

        LogbookStructureValidator.Validate(s).ShouldBeEmpty();
    }

    [Fact]
    public void A_store_belongs_to_one_place_only_and_a_region_must_lie_in_the_district_of_the_store()
    {
        var twice = Structure() with { Stores = [.. Structure().Stores, new LogbookStoreAssignment(Store17, DistrictNorth, null)] };
        var wrongDistrict = Structure() with { Stores = [new LogbookStoreAssignment(Store17, DistrictNorth, RegionAugsburg)] }; // Augsburg is in the South
        var unknownRegion = Structure() with { Stores = [new LogbookStoreAssignment(Store17, DistrictSouth, Guid.NewGuid())] };
        var unknownDistrict = Structure() with { Stores = [new LogbookStoreAssignment(Store17, Guid.NewGuid(), null)] };

        LogbookStructureValidator.Validate(twice).Select(e => e.Code).ShouldContain(LogbookStructureError.StoreAssignedTwice);
        LogbookStructureValidator.Validate(wrongDistrict).Select(e => e.Code).ShouldContain(LogbookStructureError.RegionNotInDistrict);
        LogbookStructureValidator.Validate(unknownRegion).Select(e => e.Code).ShouldContain(LogbookStructureError.UnknownRegion);
        LogbookStructureValidator.Validate(unknownDistrict).Select(e => e.Code).ShouldContain(LogbookStructureError.UnknownDistrict);
    }

    [Fact]
    public void A_region_needs_an_existing_district_and_ids_must_be_unique()
    {
        var orphan = Structure() with { Regions = [new LogbookRegion(RegionAugsburg, Guid.NewGuid(), "Augsburg", [])], Stores = [] };
        var duplicateIds = Structure() with { Regions = [.. Structure().Regions, new LogbookRegion(RegionAugsburg, DistrictNorth, "Zwilling", [])] };

        LogbookStructureValidator.Validate(orphan).Select(e => e.Code).ShouldContain(LogbookStructureError.UnknownDistrict);
        LogbookStructureValidator.Validate(duplicateIds).Select(e => e.Code).ShouldContain(LogbookStructureError.DuplicateId);
    }

    [Fact]
    public void A_leader_is_listed_once_per_place_but_may_lead_several_places()
    {
        var twice = Structure() with { Regions = [new LogbookRegion(RegionAugsburg, DistrictSouth, "Augsburg", [Rita, Rita]), .. Structure().Regions.Skip(1)] };
        var several = Structure() with { Regions = [.. Structure().Regions.Select(r => r with { LeaderIds = [Rita] })] };

        LogbookStructureValidator.Validate(twice).Select(e => e.Code).ShouldContain(LogbookStructureError.LeaderListedTwice);
        LogbookStructureValidator.Validate(several).ShouldBeEmpty();
    }

    [Fact]
    public void The_errors_name_the_place_so_the_message_can_say_where()
    {
        var s = Structure() with { Regions = [new LogbookRegion(RegionAugsburg, DistrictSouth, "", [])] };

        var error = LogbookStructureValidator.Validate(s).Single(e => e.Code == LogbookStructureError.RegionNameMissing);

        error.Id.ShouldBe(RegionAugsburg);
    }
}
