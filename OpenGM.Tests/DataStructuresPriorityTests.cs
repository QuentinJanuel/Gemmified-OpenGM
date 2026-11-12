using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenGM.VirtualMachine;
using OpenGM.VirtualMachine.BuiltInFunctions;

namespace OpenGM.Tests;

/// <summary>
/// ds_priority, ds_list_sort, and the queue/stack copy+serialise gaps.
///
/// ds_priority was backed by .NET's <c>PriorityQueue</c>, which structurally cannot express half of
/// GameMaker's API: <c>ds_priority_find_priority</c>, <c>_change_priority</c> and
/// <c>_delete_value</c> all address an entry *by its value*, and a binary heap has no way to find
/// one. Those three, plus <c>_find_min</c>/<c>_find_max</c> and <c>_write</c>/<c>_read</c>, were
/// therefore left as bare <c>// name</c> comments -- and since an unresolved call aborts the whole
/// calling event, a game doing the textbook A* thing (<c>ds_priority_change_priority</c> when a
/// cheaper route to an open node turns up) lost its entire pathfinding step, not just that call.
///
/// The heap also got ties wrong in a way that is invisible until it isn't: two entries at the same
/// priority came back in whichever order the heap had sifted them into, where a list-backed store
/// returns the one added first.
/// </summary>
[TestClass]
public class DataStructuresPriorityTests
{
    private static int NewQueue() => DataStructuresFunctions.ds_priority_create([]).Conv<int>();

    private static void Add(int q, object? value, double priority)
        => DataStructuresFunctions.ds_priority_add([q, value, priority]);

    private static int Size(int q) => DataStructuresFunctions.ds_priority_size([q]).Conv<int>();

    [TestMethod]
    public void FindsTheExtremesWithoutRemovingThem()
    {
        var q = NewQueue();
        Add(q, "a", 5);
        Add(q, "b", 1);
        Add(q, "c", 9);

        Assert.AreEqual("b", DataStructuresFunctions.ds_priority_find_min([q]));
        Assert.AreEqual("c", DataStructuresFunctions.ds_priority_find_max([q]));
        Assert.AreEqual(3, Size(q), "find_* must not consume");
    }

    [TestMethod]
    public void TiesResolveToTheOldestEntry()
    {
        var q = NewQueue();
        Add(q, "first", 1);
        Add(q, "second", 1);
        Add(q, "third", 9);
        Add(q, "fourth", 9);

        Assert.AreEqual("first", DataStructuresFunctions.ds_priority_find_min([q]));
        Assert.AreEqual("third", DataStructuresFunctions.ds_priority_find_max([q]));

        // And deleting one leaves the *other* tied entry behind, rather than either both or neither.
        Assert.AreEqual("first", DataStructuresFunctions.ds_priority_delete_min([q]));
        Assert.AreEqual("second", DataStructuresFunctions.ds_priority_find_min([q]));
    }

    [TestMethod]
    public void AddressesEntriesByValue()
    {
        var q = NewQueue();
        Add(q, "a", 5);
        Add(q, "b", 1);

        Assert.AreEqual(5.0, DataStructuresFunctions.ds_priority_find_priority([q, "a"]).Conv<double>());

        DataStructuresFunctions.ds_priority_change_priority([q, "a", 0]);
        Assert.AreEqual(0.0, DataStructuresFunctions.ds_priority_find_priority([q, "a"]).Conv<double>());
        Assert.AreEqual("a", DataStructuresFunctions.ds_priority_find_min([q]), "a is now the cheapest");
        Assert.AreEqual(2, Size(q), "changing a priority must not add an entry");

        DataStructuresFunctions.ds_priority_delete_value([q, "a"]);
        Assert.AreEqual(1, Size(q));
        Assert.AreEqual("b", DataStructuresFunctions.ds_priority_find_min([q]));
    }

    [TestMethod]
    public void MatchesValuesAcrossBoxedNumericTypes()
    {
        // A GML literal small enough for a 16-bit immediate arrives as a short; the same number read
        // back out of a variable arrives as an int. Raw object equality says those differ. This is
        // the exact confusion that made ds_list_find_index report "not found" for values that were
        // plainly in the list.
        var q = NewQueue();
        Add(q, (short)7, 3);

        Assert.AreEqual(3.0, DataStructuresFunctions.ds_priority_find_priority([q, 7]).Conv<double>());

        DataStructuresFunctions.ds_priority_change_priority([q, 7L, 1]);
        Assert.AreEqual(1.0, DataStructuresFunctions.ds_priority_find_priority([q, 7.0]).Conv<double>());

        DataStructuresFunctions.ds_priority_delete_value([q, 7.0]);
        Assert.AreEqual(0, Size(q));
    }

    [TestMethod]
    public void DeleteDrainsInPriorityOrder()
    {
        var q = NewQueue();
        Add(q, "mid", 5);
        Add(q, "low", 1);
        Add(q, "high", 9);

        Assert.AreEqual("low", DataStructuresFunctions.ds_priority_delete_min([q]));
        Assert.AreEqual("high", DataStructuresFunctions.ds_priority_delete_max([q]));
        Assert.AreEqual("mid", DataStructuresFunctions.ds_priority_delete_min([q]));
        Assert.AreEqual(0, Size(q));

        // Draining an empty queue is 0, not an exception.
        Assert.AreEqual(0, DataStructuresFunctions.ds_priority_delete_min([q]).Conv<int>());
        Assert.AreEqual(0, DataStructuresFunctions.ds_priority_delete_max([q]).Conv<int>());
    }

    [TestMethod]
    public void CopyIsIndependentOfItsSource()
    {
        var source = NewQueue();
        Add(source, "a", 5);
        Add(source, "b", 1);

        var dest = NewQueue();
        DataStructuresFunctions.ds_priority_copy([dest, source]);

        Assert.AreEqual(2, Size(dest));
        Assert.AreEqual("b", DataStructuresFunctions.ds_priority_find_min([dest]));

        DataStructuresFunctions.ds_priority_delete_min([source]);
        Assert.AreEqual(1, Size(source));
        Assert.AreEqual(2, Size(dest), "the copy must not share storage with its source");
    }

    [TestMethod]
    public void SurvivesAWriteReadRoundTrip()
    {
        var q = NewQueue();
        Add(q, "a", 5);
        Add(q, 42, 1);
        Add(q, 1.5, 9);

        var written = DataStructuresFunctions.ds_priority_write([q]).Conv<string>();

        var restored = NewQueue();
        DataStructuresFunctions.ds_priority_read([restored, written]);

        Assert.AreEqual(3, Size(restored));
        Assert.AreEqual(42, DataStructuresFunctions.ds_priority_find_min([restored]).Conv<int>());
        Assert.AreEqual(1.5, DataStructuresFunctions.ds_priority_find_max([restored]).Conv<double>());
        Assert.AreEqual(5.0, DataStructuresFunctions.ds_priority_find_priority([restored, "a"]).Conv<double>());
    }

    [TestMethod]
    public void ReadReplacesRatherThanAppends()
    {
        var q = NewQueue();
        Add(q, "a", 1);
        var written = DataStructuresFunctions.ds_priority_write([q]).Conv<string>();

        Add(q, "b", 2);
        Assert.AreEqual(2, Size(q));

        DataStructuresFunctions.ds_priority_read([q, written]);
        Assert.AreEqual(1, Size(q));
    }
}

/// <summary>
/// ds_list_sort, and the copy/serialise gaps in ds_queue and ds_stack.
/// </summary>
[TestClass]
public class DataStructuresSortAndSequenceTests
{
    private static int NewList(params object?[] items)
    {
        var id = DataStructuresFunctions.ds_list_create().Conv<int>();
        foreach (var item in items)
        {
            DataStructuresFunctions.ds_list_add(id, item);
        }
        return id;
    }

    private static List<object?> Contents(int list)
    {
        var size = DataStructuresFunctions.ds_list_size([list]).Conv<int>();
        var result = new List<object?>();
        for (var i = 0; i < size; i++)
        {
            result.Add(DataStructuresFunctions.ds_list_find_value(list, i));
        }
        return result;
    }

    [TestMethod]
    public void SortsRealsNumericallyNotAsText()
    {
        // The trap: sorting these as strings puts 10 before 9.
        var list = NewList(9, 10, 1, 100, 2);
        DataStructuresFunctions.ds_list_sort([list, true]);

        CollectionAssert.AreEqual(
            new object?[] { 1.0, 2.0, 9.0, 10.0, 100.0 },
            Contents(list).Select(x => (object?)x.Conv<double>()).ToArray());
    }

    [TestMethod]
    public void SortsDescendingToo()
    {
        var list = NewList(2, 9, 1);
        DataStructuresFunctions.ds_list_sort([list, false]);

        CollectionAssert.AreEqual(
            new object?[] { 9.0, 2.0, 1.0 },
            Contents(list).Select(x => (object?)x.Conv<double>()).ToArray());
    }

    [TestMethod]
    public void SortsAcrossBoxedNumericWidths()
    {
        // Same mixed-boxing hazard as everywhere else: these are all "3" and "1" to GML.
        var list = NewList((short)3, 1L, 2.0);
        DataStructuresFunctions.ds_list_sort([list, true]);

        CollectionAssert.AreEqual(
            new[] { 1.0, 2.0, 3.0 },
            Contents(list).Select(x => x.Conv<double>()).ToArray());
    }

    [TestMethod]
    public void RealsSortBeforeStrings()
    {
        // Measured on GMS 1.4.9999: a list of the real 9 and the string "10", sorted ascending,
        // comes back as [9]["10"] with is_real reporting 1,0 -- so reals sort before strings and
        // sorting does not retype anything.
        //
        // This is the case that separates the two candidate rules. The alternative ("as soon as
        // either side is a string, compare both as text") puts "10" first, because as text "10"
        // precedes "9". An earlier capture used -2.5/1/5/Apple/apple/banana, which both rules
        // order identically -- so it looked like confirmation and confirmed nothing.
        var mixed = NewList(9, "10");
        DataStructuresFunctions.ds_list_sort([mixed, true]);

        var contents = Contents(mixed);
        Assert.AreEqual(9.0, contents[0].Conv<double>());
        Assert.AreEqual("10", contents[1]);
        Assert.IsFalse(contents[0] is string, "sorting must not convert the real to a string");

        // The easy case both rules agree on, kept because it is the shape real games actually sort.
        var list = NewList("banana", 5, "apple", 1);
        DataStructuresFunctions.ds_list_sort([list, true]);
        CollectionAssert.AreEqual(
            new object?[] { "1", "5", "apple", "banana" },
            Contents(list).Select(x => (object?)x.Conv<string>()).ToArray());
    }

    [TestMethod]
    public void GridSortAndListSortAgree()
    {
        // These used to be two independent comparison functions with different rules for mixed
        // values, so the same four values sorted two different ways depending on which structure
        // held them.
        var list = NewList(9, "10", 2, "apple");
        DataStructuresFunctions.ds_list_sort([list, true]);
        var viaList = Contents(list).Select(x => x.Conv<string>()).ToArray();

        var grid = DataStructuresFunctions.ds_grid_create([1, 4]).Conv<int>();
        object?[] values = [9, "10", 2, "apple"];
        for (var y = 0; y < 4; y++)
        {
            DataStructuresFunctions.ds_grid_set([grid, 0, y, values[y]]);
        }
        DataStructuresFunctions.ds_grid_sort([grid, 0, true]);

        var viaGrid = new string[4];
        for (var y = 0; y < 4; y++)
        {
            viaGrid[y] = DataStructuresFunctions.ds_grid_get([grid, 0, y]).Conv<string>();
        }

        CollectionAssert.AreEqual(viaList, viaGrid);
    }

    [TestMethod]
    public void SortIsStableAcrossEqualKeys()
    {
        // Equal elements must keep insertion order -- List.Sort (introsort) would not guarantee it,
        // and for a list sorted by score with names attached that reordering is visible.
        var list = NewList("b", "a", "a", "c");
        DataStructuresFunctions.ds_list_sort([list, true]);
        CollectionAssert.AreEqual(new object?[] { "a", "a", "b", "c" }, Contents(list));
    }

    [TestMethod]
    public void QueueCopyAndRoundTripPreserveOrder()
    {
        var q = DataStructuresFunctions.ds_queue_create().Conv<int>();
        DataStructuresFunctions.ds_queue_enqueue([q, "first"]);
        DataStructuresFunctions.ds_queue_enqueue([q, "second"]);
        DataStructuresFunctions.ds_queue_enqueue([q, 3]);

        var copy = DataStructuresFunctions.ds_queue_create().Conv<int>();
        DataStructuresFunctions.ds_queue_copy([copy, q]);
        Assert.AreEqual("first", DataStructuresFunctions.ds_queue_head([copy]));
        Assert.AreEqual(3, DataStructuresFunctions.ds_queue_tail([copy]).Conv<int>());

        var written = DataStructuresFunctions.ds_queue_write([q]).Conv<string>();
        var restored = DataStructuresFunctions.ds_queue_create().Conv<int>();
        DataStructuresFunctions.ds_queue_read([restored, written]);

        Assert.AreEqual("first", DataStructuresFunctions.ds_queue_dequeue([restored]));
        Assert.AreEqual("second", DataStructuresFunctions.ds_queue_dequeue([restored]));
        Assert.AreEqual(3.0, DataStructuresFunctions.ds_queue_dequeue([restored]).Conv<double>());
        Assert.AreEqual(0, DataStructuresFunctions.ds_queue_size([restored]).Conv<int>());
    }

    [TestMethod]
    public void StackCopyAndRoundTripKeepTheSameElementOnTop()
    {
        // The easy mistake here is inverting the stack: Stack's enumerator runs top-to-bottom, so
        // copying by pushing in enumeration order turns it upside down.
        var s = DataStructuresFunctions.ds_stack_create().Conv<int>();
        DataStructuresFunctions.ds_stack_push([s, "bottom"]);
        DataStructuresFunctions.ds_stack_push([s, "middle"]);
        DataStructuresFunctions.ds_stack_push([s, "top"]);

        var copy = DataStructuresFunctions.ds_stack_create().Conv<int>();
        DataStructuresFunctions.ds_stack_copy([copy, s]);
        Assert.AreEqual("top", DataStructuresFunctions.ds_stack_top([copy]));

        var written = DataStructuresFunctions.ds_stack_write([s]).Conv<string>();
        var restored = DataStructuresFunctions.ds_stack_create().Conv<int>();
        DataStructuresFunctions.ds_stack_read([restored, written]);

        Assert.AreEqual("top", DataStructuresFunctions.ds_stack_pop([restored]));
        Assert.AreEqual("middle", DataStructuresFunctions.ds_stack_pop([restored]));
        Assert.AreEqual("bottom", DataStructuresFunctions.ds_stack_pop([restored]));
    }

    [TestMethod]
    public void ShuffleIsReproducibleFromASeed()
    {
        // ds_list_shuffle used to call an extension method backed by Random.Shared, so a game that
        // seeded its RNG for a reproducible run still got a different shuffle every time.
        var first = NewList(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        GMRandom.InitRandom(12345);
        DataStructuresFunctions.ds_list_shuffle([first]);
        var a = Contents(first).Select(x => x.Conv<double>()).ToArray();

        var second = NewList(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        GMRandom.InitRandom(12345);
        DataStructuresFunctions.ds_list_shuffle([second]);
        var b = Contents(second).Select(x => x.Conv<double>()).ToArray();

        CollectionAssert.AreEqual(a, b, "same seed must give the same shuffle");
        CollectionAssert.AreEquivalent(new[] { 1.0, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, a, "shuffle must permute, not lose or duplicate");

        GMRandom.InitRandom(999);
        var third = NewList(1, 2, 3, 4, 5, 6, 7, 8, 9, 10);
        DataStructuresFunctions.ds_list_shuffle([third]);
        var c = Contents(third).Select(x => x.Conv<double>()).ToArray();
        CollectionAssert.AreNotEqual(a, c, "a different seed should give a different shuffle");
    }
}
