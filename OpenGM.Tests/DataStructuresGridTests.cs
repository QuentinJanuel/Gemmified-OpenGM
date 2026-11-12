using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// ds_grid was the one collection with nothing behind it at all, so these cover the conventions that
/// aren't guessable: regions are inclusive on both corners, accept their corners in either order,
/// and clamp to the grid instead of erroring; disks are a centre and radius; sorting reorders whole
/// rows by one column.
/// </summary>
[TestClass]
public class DataStructuresGridTests
{
    private const int TypeGrid = (int)DSType.Grid;

    [TestInitialize]
    public void SetUp()
    {
        DataStructuresFunctions.Clear();
        GMRandom.InitRandom(12345);
    }

    [TestCleanup]
    public void TearDown() => DataStructuresFunctions.Clear();

    private static int Grid(int width, int height)
        => DataStructuresFunctions.ds_grid_create([width, height]).Conv<int>();

    private static int Get(int grid, int x, int y)
        => DataStructuresFunctions.ds_grid_get([grid, x, y]).Conv<int>();

    private static void Set(int grid, int x, int y, object value)
        => DataStructuresFunctions.ds_grid_set([grid, x, y, value]);

    [TestMethod]
    public void CreateSetGetAndSize()
    {
        var grid = Grid(3, 2);

        Assert.IsTrue(DataStructuresFunctions.ds_exists(grid, TypeGrid).Conv<bool>());
        Assert.AreEqual(3, DataStructuresFunctions.ds_grid_width([grid]).Conv<int>());
        Assert.AreEqual(2, DataStructuresFunctions.ds_grid_height([grid]).Conv<int>());
        Assert.AreEqual(0, Get(grid, 2, 1), "a new grid starts zeroed");

        Set(grid, 2, 1, 7);
        Assert.AreEqual(7, Get(grid, 2, 1));
        Assert.AreEqual(0, Get(grid, 1, 1), "setting one cell shouldn't touch its neighbours");

        DataStructuresFunctions.ds_grid_destroy([grid]);
        Assert.IsFalse(DataStructuresFunctions.ds_exists(grid, TypeGrid).Conv<bool>());
    }

    [TestMethod]
    public void OutOfBoundsAccessIsIgnoredRatherThanThrowing()
    {
        var grid = Grid(2, 2);

        Set(grid, 5, 5, 9);

        Assert.AreEqual(0, DataStructuresFunctions.ds_grid_get([grid, 5, 5]).Conv<int>());
        Assert.AreEqual(0, DataStructuresFunctions.ds_grid_get([grid, -1, 0]).Conv<int>());
    }

    [TestMethod]
    public void RegionsAreInclusiveAndTakeTheirCornersInEitherOrder()
    {
        var grid = Grid(4, 4);

        // Given backwards on both axes -- it should cover exactly the same cells either way.
        DataStructuresFunctions.ds_grid_set_region([grid, 2, 2, 1, 1, 5]);

        Assert.AreEqual(5, Get(grid, 1, 1));
        Assert.AreEqual(5, Get(grid, 2, 2), "the far corner is included");
        Assert.AreEqual(0, Get(grid, 0, 1), "outside the region");
        Assert.AreEqual(0, Get(grid, 3, 3), "outside the region");
    }

    [TestMethod]
    public void RegionsClampToTheGridInsteadOfErroring()
    {
        var grid = Grid(2, 2);

        DataStructuresFunctions.ds_grid_set_region([grid, -5, -5, 99, 99, 3]);

        Assert.AreEqual(3, Get(grid, 0, 0));
        Assert.AreEqual(3, Get(grid, 1, 1));
    }

    [TestMethod]
    public void AddAndMultiplyCombineWithWhatsAlreadyThere()
    {
        var grid = Grid(2, 1);
        Set(grid, 0, 0, 10);

        DataStructuresFunctions.ds_grid_add([grid, 0, 0, 5]);
        Assert.AreEqual(15, Get(grid, 0, 0));

        DataStructuresFunctions.ds_grid_multiply([grid, 0, 0, 2]);
        Assert.AreEqual(30, Get(grid, 0, 0));
    }

    [TestMethod]
    public void AddConcatenatesStrings()
    {
        var grid = Grid(1, 1);
        Set(grid, 0, 0, "ab");

        DataStructuresFunctions.ds_grid_add([grid, 0, 0, "cd"]);

        Assert.AreEqual("abcd", DataStructuresFunctions.ds_grid_get([grid, 0, 0]).Conv<string>());
    }

    [TestMethod]
    public void SumMaxMinAndMeanOverARegion()
    {
        var grid = Grid(2, 2);
        Set(grid, 0, 0, 1);
        Set(grid, 1, 0, 2);
        Set(grid, 0, 1, 3);
        Set(grid, 1, 1, 10);

        Assert.AreEqual(16, DataStructuresFunctions.ds_grid_get_sum([grid, 0, 0, 1, 1]).Conv<int>());
        Assert.AreEqual(10, DataStructuresFunctions.ds_grid_get_max([grid, 0, 0, 1, 1]).Conv<int>());
        Assert.AreEqual(1, DataStructuresFunctions.ds_grid_get_min([grid, 0, 0, 1, 1]).Conv<int>());
        Assert.AreEqual(4, DataStructuresFunctions.ds_grid_get_mean([grid, 0, 0, 1, 1]).Conv<int>());
    }

    [TestMethod]
    public void DisksCoverCellsWithinTheRadiusOfTheirCentre()
    {
        var grid = Grid(5, 5);

        DataStructuresFunctions.ds_grid_set_disk([grid, 2, 2, 1, 4]);

        Assert.AreEqual(4, Get(grid, 2, 2), "the centre");
        Assert.AreEqual(4, Get(grid, 1, 2), "one cell out, on the radius");
        Assert.AreEqual(4, Get(grid, 2, 3), "one cell out, on the radius");
        Assert.AreEqual(0, Get(grid, 1, 1), "diagonal is sqrt(2) away, outside radius 1");
        Assert.AreEqual(0, Get(grid, 0, 2), "two cells out");
    }

    [TestMethod]
    public void ValueLookupsReportWhereTheValueIsOrMinusOne()
    {
        var grid = Grid(3, 3);
        Set(grid, 2, 1, 8);

        Assert.IsTrue(DataStructuresFunctions.ds_grid_value_exists([grid, 0, 0, 2, 2, 8]).Conv<bool>());
        Assert.AreEqual(2, DataStructuresFunctions.ds_grid_value_x([grid, 0, 0, 2, 2, 8]).Conv<int>());
        Assert.AreEqual(1, DataStructuresFunctions.ds_grid_value_y([grid, 0, 0, 2, 2, 8]).Conv<int>());

        Assert.IsFalse(DataStructuresFunctions.ds_grid_value_exists([grid, 0, 0, 2, 2, 99]).Conv<bool>());
        Assert.AreEqual(-1, DataStructuresFunctions.ds_grid_value_x([grid, 0, 0, 2, 2, 99]).Conv<int>());
    }

    [TestMethod]
    public void ResizeKeepsWhatStillFits()
    {
        var grid = Grid(3, 3);
        Set(grid, 0, 0, 1);
        Set(grid, 2, 2, 9);

        DataStructuresFunctions.ds_grid_resize([grid, 2, 2]);

        Assert.AreEqual(2, DataStructuresFunctions.ds_grid_width([grid]).Conv<int>());
        Assert.AreEqual(1, Get(grid, 0, 0), "cells inside the new bounds survive");

        DataStructuresFunctions.ds_grid_resize([grid, 4, 4]);
        Assert.AreEqual(1, Get(grid, 0, 0));
        Assert.AreEqual(0, Get(grid, 3, 3), "newly exposed cells start at 0");
    }

    [TestMethod]
    public void CopyIsIndependentOfItsSource()
    {
        var source = Grid(2, 2);
        Set(source, 0, 0, 5);

        var destination = Grid(1, 1);
        DataStructuresFunctions.ds_grid_copy([destination, source]);

        Assert.AreEqual(2, DataStructuresFunctions.ds_grid_width([destination]).Conv<int>());
        Assert.AreEqual(5, Get(destination, 0, 0));

        Set(source, 0, 0, 6);
        Assert.AreEqual(5, Get(destination, 0, 0), "the copy shouldn't alias the source");
    }

    [TestMethod]
    public void GridRegionCopiesIntoAnOffsetPosition()
    {
        var source = Grid(2, 2);
        Set(source, 0, 0, 1);
        Set(source, 1, 1, 4);

        var destination = Grid(4, 4);
        DataStructuresFunctions.ds_grid_set_grid_region([destination, source, 0, 0, 1, 1, 2, 2]);

        Assert.AreEqual(1, Get(destination, 2, 2));
        Assert.AreEqual(4, Get(destination, 3, 3));
        Assert.AreEqual(0, Get(destination, 0, 0), "nothing outside the target position");
    }

    [TestMethod]
    public void SortReordersWholeRowsByOneColumn()
    {
        // Column 0 is the key, column 1 rides along -- if the rows weren't moved together the
        // labels would end up attached to the wrong numbers.
        var grid = Grid(2, 3);
        Set(grid, 0, 0, 3); Set(grid, 1, 0, "three");
        Set(grid, 0, 1, 1); Set(grid, 1, 1, "one");
        Set(grid, 0, 2, 2); Set(grid, 1, 2, "two");

        DataStructuresFunctions.ds_grid_sort([grid, 0, true]);

        Assert.AreEqual(1, Get(grid, 0, 0));
        Assert.AreEqual(2, Get(grid, 0, 1));
        Assert.AreEqual(3, Get(grid, 0, 2));
        Assert.AreEqual("one", DataStructuresFunctions.ds_grid_get([grid, 1, 0]).Conv<string>());
        Assert.AreEqual("three", DataStructuresFunctions.ds_grid_get([grid, 1, 2]).Conv<string>());

        DataStructuresFunctions.ds_grid_sort([grid, 0, false]);
        Assert.AreEqual(3, Get(grid, 0, 0));
        Assert.AreEqual("three", DataStructuresFunctions.ds_grid_get([grid, 1, 0]).Conv<string>());
    }

    [TestMethod]
    public void WriteAndReadRoundTrip()
    {
        var grid = Grid(3, 2);
        Set(grid, 0, 0, 1);
        Set(grid, 2, 1, "end");

        var written = DataStructuresFunctions.ds_grid_write([grid]).Conv<string>();

        var restored = Grid(1, 1);
        DataStructuresFunctions.ds_grid_read([restored, written]);

        Assert.AreEqual(3, DataStructuresFunctions.ds_grid_width([restored]).Conv<int>());
        Assert.AreEqual(2, DataStructuresFunctions.ds_grid_height([restored]).Conv<int>());
        Assert.AreEqual(1, Get(restored, 0, 0));
        Assert.AreEqual("end", DataStructuresFunctions.ds_grid_get([restored, 2, 1]).Conv<string>());
    }

    [TestMethod]
    public void ShuffleKeepsEveryValueAndTheGridsShape()
    {
        var grid = Grid(3, 3);
        for (var i = 0; i < 9; i++)
        {
            Set(grid, i % 3, i / 3, i);
        }

        DataStructuresFunctions.ds_grid_shuffle([grid]);

        var seen = new List<int>();
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                seen.Add(Get(grid, x, y));
            }
        }

        seen.Sort();
        CollectionAssert.AreEqual(Enumerable.Range(0, 9).ToList(), seen);
        Assert.AreEqual(3, DataStructuresFunctions.ds_grid_width([grid]).Conv<int>());
    }
}
