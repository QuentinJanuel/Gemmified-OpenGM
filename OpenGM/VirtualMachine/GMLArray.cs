using System.Collections;

namespace OpenGM.VirtualMachine;

/// <summary>
/// a GML array.
///
/// Only distinguished from the plain <see cref="List{T}"/> of object that arrays are otherwise
/// represented by so that pre-2.3 copy-on-write has somewhere to keep its "is this still shared
/// with whoever I got it from?" bit.
///
/// Before GMS 2.3, GML arrays are *value* types: <c>b = a</c> gives b its own array, so writing
/// <c>b[0] = 1</c> must not be visible through a. Real GM implements that lazily -- the two
/// variables share one refcounted buffer until somebody writes through one of them, and only then
/// is it duplicated. We approximate the refcount with <see cref="Shared"/>: it gets set whenever a
/// second lasting reference to an array escapes (i.e. it's stored into another variable, see
/// <see cref="MarkShared"/>), and the next plain <c>arr[i] = v</c> through a variable copies the
/// array and rebinds the variable to the copy before writing (see
/// <see cref="VariableResolver.ArraySet"/>).
///
/// The <c>arr[@ i] = v</c> accessor -- which old bytecode compiles to a call to the array_set
/// builtin -- deliberately opts *out* of all this and always writes in place. That is how pre-2.3
/// code mutates records it keeps inside ds_lists/ds_maps, so the two paths have to stay
/// distinguishable: ArraySet treats "caller gave me a setter to rebind" as the plain-write case and
/// "no setter" as the in-place accessor case.
/// </summary>
public class GMLArray : List<object?>
{
    /// <summary>
    /// Whether some other variable may still be looking at this same array, meaning the next plain
    /// write through a variable has to copy first. Always false on a freshly copied/created array,
    /// which is what keeps a run of writes to one variable from copying more than once.
    /// </summary>
    public bool Shared;

    public GMLArray() { }

    public GMLArray(IEnumerable<object?> source) : base(source) { }

    /// <summary>
    /// Records that <paramref name="value"/> just got stored somewhere it can outlive the variable
    /// it came from, so pre-2.3 copy-on-write knows it can no longer be written to in place.
    /// </summary>
    public static void MarkShared(object? value)
    {
        if (CompatFlags.LegacyArrayCopyOnWrite && value is GMLArray array)
        {
            array.Shared = true;
        }
    }

    /// <summary>
    /// Whether a plain (non-<c>[@ ]</c>) write to <paramref name="array"/> has to copy it first.
    /// </summary>
    public static bool NeedsCopyOnWrite(IList array)
    {
        if (!CompatFlags.LegacyArrayCopyOnWrite)
        {
            return false;
        }

        return array switch
        {
            GMLArray gmlArray => gmlArray.Shared,
            // A GML array we didn't hand out ourselves -- an element of another array, something a
            // builtin returned, etc. Whatever produced it still has its own reference, so treat it
            // as shared. The copy makes it a GMLArray, so this only ever costs one copy.
            List<object?> => true,
            // Typed arrays (int[], List<int>, ...) are engine state exposed to GML, not GML arrays;
            // aliasing is the whole point of those, so they're never copied.
            _ => false
        };
    }
}
